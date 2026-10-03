using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// M11b: ages in the simulation — scaled capacity, per-age level caps and power gate,
// redevelopment of outdated cells and "Keep historical building".
public sealed class AgesSimulationTests
{
    private const int Medieval = TestAges.Medieval, Renaissance = TestAges.Renaissance,
        Industrial = TestAges.Industrial, Modern = TestAges.Modern;

    private readonly List<Object> m_Created = new();
    private BalanceConfig m_Config;
    private TestAges m_TestAges;
    private AgeDatabase m_Ages;
    private TechDatabase m_Techs;
    private TechDefinition m_Electricity;

    [SetUp]
    public void SetUp()
    {
        m_Config = Make<BalanceConfig>();
        m_TestAges = new TestAges();
        m_Ages = m_TestAges.Ages;
        m_Techs = m_TestAges.Techs;
        m_Electricity = m_TestAges["electricity"];
    }

    [TearDown]
    public void TearDown()
    {
        m_TestAges.Dispose();
        foreach (Object o in m_Created) Object.DestroyImmediate(o);
        m_Created.Clear();
    }

    private T Make<T>() where T : ScriptableObject
    {
        T instance = ScriptableObject.CreateInstance<T>();
        m_Created.Add(instance);
        return instance;
    }

    private AgeDefinition Age(string id, int year, int maxLevel, float scale, bool power, TechDefinition[] starting = null)
    {
        AgeDefinition age = Make<AgeDefinition>();
        age.Init(id, year, maxLevel, scale, power, advanceCost: 1f, startingTechs: starting);
        return age;
    }

    private static void SetTaxes(SimulationSystem sim, float tax)
    {
        sim.Economy.TaxResidential = tax;
        sim.Economy.TaxCommercial = tax;
        sim.Economy.TaxIndustrial = tax;
    }

    private static List<Vector2Int> GrownCells(GridData grid)
    {
        var cells = new List<Vector2Int>();
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                if (grid.GetBuildingLevel(new Vector2Int(x, y)) > 0) cells.Add(new Vector2Int(x, y));
        return cells;
    }

    // --- Capacity ---

    [Test]
    public void CapacityModel_ScalesByBuiltAge_RoundedAndAtLeastOne()
    {
        var model = new CapacityModel(m_Config, m_Ages);
        Assert.AreEqual(0, model.Capacity(0, Modern));
        Assert.AreEqual(2, model.Capacity(1, Medieval));
        Assert.AreEqual(4, model.Capacity(2, Medieval));
        Assert.AreEqual(12, model.Capacity(3, Renaissance));
        Assert.AreEqual(16, model.Capacity(3, Industrial));
        Assert.AreEqual(5, model.Capacity(1, Modern));
        Assert.AreEqual(20, model.Capacity(3, Modern));

        var noAges = new CapacityModel(m_Config);
        Assert.AreEqual(8, noAges.Capacity(2, Medieval));

        AgeDefinition tiny = Make<AgeDefinition>();
        tiny.Init("tiny", 1, capacityScale: 0.1f);
        AgeDatabase tinyAges = Make<AgeDatabase>();
        tinyAges.Init(tiny);
        Assert.AreEqual(1, new CapacityModel(m_Config, tinyAges).Capacity(1, 0));
    }

    [Test]
    public void AgeDatabase_Validate_RejectsFallingMaxLevel()
    {
        m_Ages.Init(Age("a", 1, 3, 1f, false), Age("b", 2, 2, 1f, false));
        var errors = new List<string>();
        Assert.IsFalse(m_Ages.Validate(errors));
        Assert.IsTrue(errors.Exists(e => e.Contains("lower MaxLevel")));
    }

    // --- Industrial start = today's game ---

    [TestCase(SeededCity.PlantSupply, false)]
    [TestCase(0, false)]
    [TestCase(SeededCity.PlantSupply, true)]
    public void IndustrialStart_PlaysExactlyLikeTheNoAgeSim(int plantSupply, bool parks)
    {
        var plainGrid = new GridData(24, 24);
        var agedGrid = new GridData(24, 24);
        SimulationSystem plain = SeededCity.Run(plainGrid, m_Config, 90, plantSupply, parks);
        SimulationSystem aged = SeededCity.Run(agedGrid, m_Config, 90, plantSupply, parks, ages: m_Ages, techs: m_Techs,
            startAge: Industrial);

        Assert.AreEqual(plain.Population.Population, aged.Population.Population);
        Assert.AreEqual(plain.Population.Housing, aged.Population.Housing);
        Assert.AreEqual(plain.Population.Jobs, aged.Population.Jobs);
        Assert.AreEqual(plain.Population.AverageHappiness, aged.Population.AverageHappiness);
        Assert.AreEqual(plain.Economy.Money, aged.Economy.Money);
        Assert.AreEqual(plain.Power.Load, aged.Power.Load);
        CollectionAssert.AreEqual(plainGrid.ExportLevels(), agedGrid.ExportLevels());
        foreach (Vector2Int cell in GrownCells(agedGrid)) Assert.AreEqual(Industrial, agedGrid.GetBuiltAge(cell));
    }

    // --- Medieval ---

    [Test]
    public void Medieval_CapsAtLevel2_WithHalfCapacity_AndNeedsNoPower()
    {
        var grid = new GridData(24, 24);
        SimulationSystem sim = SeededCity.Run(grid, m_Config, 120, plantSupply: 0, ages: m_Ages, techs: m_Techs,
            startAge: Medieval);

        int housing = 0, levelTwo = 0;
        foreach (Vector2Int cell in GrownCells(grid))
        {
            int level = grid.GetBuildingLevel(cell);
            Assert.LessOrEqual(level, 2, cell.ToString());
            Assert.AreEqual(Medieval, grid.GetBuiltAge(cell));
            if (level == 2)
            {
                levelTwo++;
                Assert.AreEqual(GrowthBlocker.AgeMaxLevel, sim.Growth.GetBlocker(cell, new DemandSnapshot(1f, 1f, 1f)));
            }
            if (grid.GetZone(cell) == ZoneType.Residential) housing += level == 1 ? 2 : 4;
        }
        Assert.Greater(levelTwo, 0, "upgrades happen without power in the Medieval age");
        Assert.AreEqual(housing, sim.Population.Housing);
        Assert.AreEqual(0f, sim.Population.Happiness.Power, "no outage penalty before power exists");
        Assert.Greater(sim.Population.Population, 0);
    }

    // --- Redevelopment ---

    [Test]
    public void Advancing_RedevelopsOutdatedCells_AtRedevelopPerDay_RowMajor()
    {
        var grid = new GridData(24, 24);
        SimulationSystem sim = SeededCity.Run(grid, m_Config, 60, plantSupply: 0, ages: m_Ages, techs: m_Techs,
            startAge: Medieval);
        List<Vector2Int> outdated = GrownCells(grid);   // row-major
        Assert.Greater(outdated.Count, 2 * m_Config.RedevelopPerDay);
        var levels = new Dictionary<Vector2Int, byte>();
        foreach (Vector2Int cell in outdated) levels[cell] = grid.GetBuildingLevel(cell);

        TestAges.Advance(sim);
        Assert.AreEqual(Renaissance, sim.Tech.CurrentAge);
        Assert.IsTrue(sim.Growth.IsOutdated(outdated[0]));
        SetTaxes(sim, 0.5f);    // no demand, so nothing grows: only redevelopment changes cells

        for (int day = 1; day <= 2; day++)
        {
            sim.Tick();
            int done = day * m_Config.RedevelopPerDay;
            for (int i = 0; i < outdated.Count; i++)
            {
                Vector2Int cell = outdated[i];
                Assert.AreEqual(levels[cell], grid.GetBuildingLevel(cell), "redevelopment keeps the level");
                Assert.AreEqual(i < done ? Renaissance : Medieval, grid.GetBuiltAge(cell), $"day {day}, cell {cell}");
            }
        }

        Vector2Int rebuilt = outdated[0];
        Assert.AreEqual(sim.Capacity.Capacity(levels[rebuilt], Renaissance), sim.Capacity.CapacityOf(grid, rebuilt));
        Vector2Int waiting = outdated[outdated.Count - 1];
        Assert.IsTrue(sim.Growth.IsOutdated(waiting));
        grid.SetBuildingLevel(waiting, 3);      // at the cap, its only change left is the rebuild
        Assert.AreEqual(GrowthBlocker.Outdated, sim.Growth.GetBlocker(waiting, sim.Demand.Snapshot));
    }

    [Test]
    public void HistoricCells_NeverRedevelop_AndCapAtTheirOwnAge()
    {
        var grid = new GridData(12, 4);
        var sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config, m_Ages, m_Techs);
        sim.Tech.StartNew(Renaissance);
        for (int x = 0; x < 12; x++) grid.SetRoad(new Vector2Int(x, 0), true);

        Vector2Int keptTop = new Vector2Int(1, 1), keptLow = new Vector2Int(2, 1), plain = new Vector2Int(3, 1);
        foreach (Vector2Int cell in new[] { keptTop, keptLow, plain })
        {
            grid.SetZone(cell, ZoneType.Residential);
            grid.SetBuildingLevel(cell, cell == keptLow ? (byte)1 : (byte)2);
            grid.SetBuiltAge(cell, Medieval);
        }
        grid.SetHistoric(keptTop, true);
        grid.SetHistoric(keptLow, true);
        Assert.IsTrue(grid.Occupy(new Vector2Int(11, 3), Vector2Int.one, 0, 1));
        sim.Sources = new[] { new ServiceSource(new Vector2Int(11, 3), Vector2Int.one, 0, 0, waterRadius: 12) };   // a well (M13)

        var high = new DemandSnapshot(1f, 1f, 1f);
        for (int day = 0; day < 5; day++) sim.Growth.Apply(high);

        Assert.AreEqual(2, grid.GetBuildingLevel(keptTop));
        Assert.AreEqual(Medieval, grid.GetBuiltAge(keptTop));
        Assert.AreEqual(GrowthBlocker.KeptHistoric, sim.Growth.GetBlocker(keptTop, high));

        Assert.AreEqual(2, grid.GetBuildingLevel(keptLow), "upgrades within its own age's cap");
        Assert.AreEqual(Medieval, grid.GetBuiltAge(keptLow));

        Assert.AreEqual(3, grid.GetBuildingLevel(plain), "the current age allows level 3");
        Assert.AreEqual(Renaissance, grid.GetBuiltAge(plain));
        Assert.AreEqual(GrowthBlocker.MaxLevel, sim.Growth.GetBlocker(plain, high));
    }

    [Test]
    public void Redevelopment_ReservesPowerHeadroom_InPoweredAges()
    {
        var grid = new GridData(12, 4);
        var sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config, m_Ages, m_Techs);
        sim.Tech.StartNew(Modern);
        for (int x = 0; x < 11; x++) grid.SetRoad(new Vector2Int(x, 0), true);
        for (int x = 1; x <= 4; x++)
        {
            Vector2Int cell = new Vector2Int(x, 1);
            grid.SetZone(cell, ZoneType.Residential);
            grid.SetBuildingLevel(cell, 1);
            grid.SetBuiltAge(cell, Industrial);     // draws 4; rebuilt in Modern it draws 5
        }
        var none = new DemandSnapshot(0f, 0f, 0f);

        // No plant: nothing can be rebuilt in a powered age.
        sim.Growth.Apply(none);
        Assert.AreEqual(0, sim.Growth.Redeveloped.Count);
        Assert.AreEqual(GrowthBlocker.NoPower, sim.Growth.GetBlocker(new Vector2Int(1, 1), none));

        // 18 units for 16 drawn: headroom for two +1 rebuilds, taken row-major (water as plentiful, M13).
        Assert.IsTrue(grid.Occupy(new Vector2Int(10, 1), Vector2Int.one, 0, 1));
        sim.Sources = new[] { new ServiceSource(new Vector2Int(10, 1), Vector2Int.one, 0, 18, waterSupply: 100) };
        sim.Growth.Apply(none);

        Assert.AreEqual(Modern, grid.GetBuiltAge(new Vector2Int(1, 1)));
        Assert.AreEqual(Modern, grid.GetBuiltAge(new Vector2Int(2, 1)));
        Assert.AreEqual(Industrial, grid.GetBuiltAge(new Vector2Int(3, 1)));
        Assert.AreEqual(Industrial, grid.GetBuiltAge(new Vector2Int(4, 1)));
        Assert.AreEqual(18, sim.Power.Load);
        Assert.AreEqual(0, sim.Power.UnpoweredCells);
        Assert.AreEqual(GrowthBlocker.PowerAtCapacity, sim.Growth.GetBlocker(new Vector2Int(3, 1), none));
    }

    [Test]
    public void NewCells_AreStampedWithTheCurrentAge()
    {
        var grid = new GridData(8, 4);
        var sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config, m_Ages, m_Techs);
        sim.Tech.StartNew(Renaissance);
        for (int x = 0; x < 8; x++) grid.SetRoad(new Vector2Int(x, 0), true);
        grid.SetZone(new Vector2Int(1, 1), ZoneType.Commercial);

        sim.Growth.Apply(new DemandSnapshot(0f, 1f, 0f));

        Assert.AreEqual(1, grid.GetBuildingLevel(new Vector2Int(1, 1)));
        Assert.AreEqual(Renaissance, grid.GetBuiltAge(new Vector2Int(1, 1)));
    }

    // --- Grid flags ---

    [Test]
    public void DemolishAndRezone_ClearBuiltAgeAndHistoric()
    {
        var grid = new GridData(4, 4);
        Vector2Int cell = new Vector2Int(1, 1);
        grid.SetZone(cell, ZoneType.Residential);

        grid.SetHistoric(cell, true);
        Assert.IsFalse(grid.IsHistoric(cell), "undeveloped cells can't be kept");

        grid.SetBuildingLevel(cell, 2);
        grid.SetBuiltAge(cell, 3);
        grid.SetHistoric(cell, true);
        Assert.IsTrue(grid.IsHistoric(cell));

        // Demolish (PlacementController.DemolishAt) resets the level.
        grid.SetBuildingLevel(cell, 0);
        Assert.AreEqual(0, grid.GetBuiltAge(cell));
        Assert.IsFalse(grid.IsHistoric(cell));

        // Rezone (PlacementController.TryZone / Unzone) resets the level, then changes the zone.
        grid.SetBuildingLevel(cell, 1);
        grid.SetBuiltAge(cell, 2);
        grid.SetHistoric(cell, true);
        grid.SetBuildingLevel(cell, 0);
        grid.SetZone(cell, ZoneType.Commercial);
        Assert.AreEqual(0, grid.GetBuiltAge(cell));
        Assert.IsFalse(grid.IsHistoric(cell));
    }

    [Test]
    public void ExportImport_RoundTripsBuiltAgesAndHistoric()
    {
        var grid = new GridData(4, 4);
        Vector2Int cell = new Vector2Int(2, 3);
        grid.SetZone(cell, ZoneType.Industrial);
        grid.SetBuildingLevel(cell, 2);
        grid.SetBuiltAge(cell, 1);
        grid.SetHistoric(cell, true);

        var copy = new GridData(4, 4);
        copy.Import(grid.ExportZones(), grid.ExportRoads(), grid.ExportLevels(), grid.ExportBuiltAges(), grid.ExportHistoric());
        Assert.AreEqual(1, copy.GetBuiltAge(cell));
        Assert.IsTrue(copy.IsHistoric(cell));

        // Without the new arrays (v1 data) everything is age 0 and not kept.
        copy.Import(grid.ExportZones(), grid.ExportRoads(), grid.ExportLevels());
        Assert.AreEqual(0, copy.GetBuiltAge(cell));
        Assert.IsFalse(copy.IsHistoric(cell));
    }

    // --- Tech modifiers ---

    [Test]
    public void TechModifiers_ScaleDemandUpkeepAndAddHappiness()
    {
        var plainGrid = new GridData(24, 24);
        SimulationSystem plain = SeededCity.Run(plainGrid, m_Config, 1, ages: m_Ages, techs: m_Techs, startAge: Industrial);

        m_Electricity.Init("electricity", Industrial, 10f, null, new[]
        {
            new TechEffect(TechEffectType.DemandMultiplier, "Residential", 2f),
            new TechEffect(TechEffectType.UpkeepMultiplier, "", 0.5f),
            new TechEffect(TechEffectType.HappinessBonus, "", 0.05f),
        });
        var techGrid = new GridData(24, 24);
        SimulationSystem tech = SeededCity.Run(techGrid, m_Config, 1, ages: m_Ages, techs: m_Techs, startAge: Industrial);

        Assert.AreEqual(plain.Economy.ExpensePerDay * 0.5f, tech.Economy.ExpensePerDay, 1e-4f);
        Assert.AreEqual(0.05f, tech.Population.Happiness.Technology, 1e-6f);
        Assert.AreEqual(0f, plain.Population.Happiness.Technology);
        Assert.Greater(GrownCells(techGrid).Count, GrownCells(plainGrid).Count, "doubled residential demand grows more on day 1");
    }
}
