using NUnit.Framework;
using UnityEngine;

// M23b: density in the simulation — capacity bands, the High utilities gate, land value, pollution, and that the sim
// never changes a density.
public sealed class DensitySimTests
{
    private BalanceConfig m_Config;
    private TestAges m_TestAges;

    [SetUp]
    public void SetUp()
    {
        m_Config = ScriptableObject.CreateInstance<BalanceConfig>();
        m_TestAges = new TestAges();
    }

    [TearDown]
    public void TearDown()
    {
        m_TestAges.Dispose();
        Object.DestroyImmediate(m_Config);
    }

    // --- capacity ---

    [Test]
    public void Capacity_MediumIsTheOldNumber_LowAndHighAreTheirOwnBands()
    {
        var model = new CapacityModel(m_Config);
        foreach (ZoneType zone in new[] { ZoneType.Residential, ZoneType.Commercial, ZoneType.Industrial })
        {
            for (int level = 0; level <= 3; level++)
                Assert.AreEqual(model.Capacity(level, 0), model.Capacity(level, 0, zone, Density.Medium), $"{zone} L{level}");
        }
        Assert.AreEqual(8, model.Capacity(3, 0, ZoneType.Residential, Density.Low));
        Assert.AreEqual(2, model.Capacity(1, 0, ZoneType.Commercial, Density.Low));
        Assert.AreEqual(32, model.Capacity(3, 0, ZoneType.Residential, Density.High));
        Assert.AreEqual(26, model.Capacity(3, 0, ZoneType.Industrial, Density.High), "16 x 1.6 rounded");
        Assert.AreEqual(0, model.Capacity(0, 0, ZoneType.Residential, Density.High), "undeveloped stays 0");
    }

    [Test]
    public void CapacityOf_ReadsTheCellsDensity_AndUpgradeDrawFollowsIt()
    {
        var grid = new GridData(8, 4);
        var model = new CapacityModel(m_Config);
        var cell = new Vector2Int(2, 1);
        grid.SetZone(cell, ZoneType.Residential);
        grid.SetBuildingLevel(cell, 2);
        Assert.AreEqual(8, model.CapacityOf(grid, cell));
        Assert.AreEqual(8, model.UpgradeDraw(grid, cell));

        grid.SetDensity(cell, Density.High);
        Assert.AreEqual(16, model.CapacityOf(grid, cell));
        Assert.AreEqual(16, model.UpgradeDraw(grid, cell), "L3 32 minus L2 16");

        grid.SetDensity(cell, Density.Low);
        Assert.AreEqual(4, model.CapacityOf(grid, cell));
    }

    [Test]
    public void Capacity_ComposesWithTheBuiltAgeScale()
    {
        var model = new CapacityModel(m_Config, m_TestAges.Ages);
        float scale = m_TestAges.Ages[TestAges.Modern].CapacityScale;
        int expected = Mathf.Max(1, Mathf.FloorToInt(16 * 2f * scale + 0.5f));
        Assert.AreEqual(expected, model.Capacity(3, TestAges.Modern, ZoneType.Residential, Density.High));
    }

    // --- the sim never changes it, and it changes the city ---

    private static int CountDensity(GridData grid, Density density)
    {
        int n = 0;
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (grid.GetZone(new Vector2Int(x, y)) != ZoneType.None && grid.GetDensity(new Vector2Int(x, y)) == density) n++;
            }
        }
        return n;
    }

    private static void PaintResidential(GridData grid, Density density)
    {
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                var cell = new Vector2Int(x, y);
                if (grid.GetZone(cell) == ZoneType.Residential) grid.SetDensity(cell, density);
            }
        }
    }

    [Test]
    public void TheSimNeverChangesADensity()
    {
        var grid = new GridData(24, 24);
        SimulationSystem sim = SeededCity.Build(grid, m_Config);
        PaintResidential(grid, Density.High);
        int high = CountDensity(grid, Density.High);
        Assert.Greater(high, 0);
        for (int day = 0; day < 90; day++) sim.Tick();

        Assert.AreEqual(high, CountDensity(grid, Density.High));
        Assert.AreEqual(0, CountDensity(grid, Density.Low));
    }

    // Homes per grown residential cell after some days: jobs, not housing, limit a High-only city's growth (demand
    // follows jobs), so the per-cell figure is what density changes.
    private float HousingPerHome(Density residential, int days)
    {
        var grid = new GridData(24, 24);
        SimulationSystem sim = SeededCity.Build(grid, m_Config);
        PaintResidential(grid, residential);
        for (int day = 0; day < days; day++) sim.Tick();
        int homes = 0;
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                var cell = new Vector2Int(x, y);
                if (grid.GetZone(cell) == ZoneType.Residential && grid.GetBuildingLevel(cell) > 0) homes++;
            }
        }
        Assert.Greater(homes, 0, $"{residential} homes grew");
        return sim.Population.Housing / (float)homes;
    }

    [Test]
    public void HomesHoldMoreOrFewerPeopleByDensity()
    {
        float low = HousingPerHome(Density.Low, 20);
        float medium = HousingPerHome(Density.Medium, 20);
        float high = HousingPerHome(Density.High, 20);
        TestContext.WriteLine($"housing per grown home after 20 days: low {low}, medium {medium}, high {high}");
        Assert.Less(low, medium);
        Assert.Greater(high, medium);
    }

    [Test]
    public void Repainting_AGrownCell_RefitsTheCapacity()
    {
        var grid = new GridData(24, 24);
        SimulationSystem sim = SeededCity.Run(grid, m_Config, 60);
        Vector2Int home = default;
        bool found = false;
        for (int y = 0; y < grid.Height && !found; y++)
        {
            for (int x = 0; x < grid.Width && !found; x++)
            {
                var cell = new Vector2Int(x, y);
                if (grid.GetZone(cell) == ZoneType.Residential && grid.GetBuildingLevel(cell) > 0) { home = cell; found = true; }
            }
        }
        Assert.IsTrue(found);
        int before = sim.Capacity.CapacityOf(grid, home);

        grid.SetDensity(home, Density.Low);
        Assert.Less(sim.Capacity.CapacityOf(grid, home), before);
        sim.Tick();
        Assert.AreEqual(Density.Low, grid.GetDensity(home), "still Low after a tick");
    }

    // --- the High gate ---

    [Test]
    public void HighDensity_NeedsPowerAndWater_ToStartGrowing_InAPoweredAge()
    {
        var grid = new GridData(12, 4);
        var sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config, m_TestAges.Ages, m_TestAges.Techs);
        sim.Tech.StartNew(TestAges.Modern);
        for (int x = 0; x < 11; x++) grid.SetRoad(new Vector2Int(x, 0), true);
        var medium = new Vector2Int(1, 1);
        var high = new Vector2Int(2, 1);
        grid.SetZone(medium, ZoneType.Residential);
        grid.SetZone(high, ZoneType.Residential);
        grid.SetDensity(high, Density.High);
        var demand = new DemandSnapshot(1f, 1f, 1f);

        Assert.AreEqual(GrowthBlocker.None, sim.Growth.GetBlocker(medium, demand), "Medium starts without utilities");
        Assert.AreEqual(GrowthBlocker.NoPower, sim.Growth.GetBlocker(high, demand));

        sim.Growth.Apply(demand);
        Assert.AreEqual(1, grid.GetBuildingLevel(medium));
        Assert.AreEqual(0, grid.GetBuildingLevel(high), "the High cell did not start");

        Assert.IsTrue(grid.Occupy(new Vector2Int(10, 1), Vector2Int.one, 0, 1));
        sim.Sources = new[] { new ServiceSource(new Vector2Int(10, 1), Vector2Int.one, 0, 200, waterSupply: 200) };
        Assert.AreEqual(GrowthBlocker.None, sim.Growth.GetBlocker(high, demand));
        sim.Growth.Apply(demand);
        Assert.AreEqual(1, grid.GetBuildingLevel(high), "with power and water it starts");
    }

    // --- land value and pollution ---

    [Test]
    public void LandValue_RisesForLow_AndFallsForHigh()
    {
        var grid = new GridData(24, 24);
        SimulationSystem sim = SeededCity.Run(grid, m_Config, 5);
        var cell = new Vector2Int(2, 13);
        Assert.AreEqual(ZoneType.Residential, grid.GetZone(cell));
        float medium = sim.LandValue.GetLandValue(cell);

        grid.SetDensity(cell, Density.Low);
        float low = sim.LandValue.GetLandValue(cell);
        grid.SetDensity(cell, Density.High);
        float high = sim.LandValue.GetLandValue(cell);

        Assert.AreEqual(medium + m_Config.LowDensityLandValue, low, 1e-4f);
        Assert.AreEqual(medium - m_Config.HighDensityLandValuePenalty, high, 1e-4f);
    }

    [Test]
    public void Pollution_HighDensityFactoryEmitsMoreThanItsCapacityAlone()
    {
        var grid = new GridData(24, 24);
        SimulationSystem sim = SeededCity.Run(grid, m_Config, 60);
        Vector2Int factory = default;
        bool found = false;
        for (int y = 0; y < grid.Height && !found; y++)
        {
            for (int x = 0; x < grid.Width && !found; x++)
            {
                var cell = new Vector2Int(x, y);
                if (grid.GetZone(cell) == ZoneType.Industrial && grid.GetBuildingLevel(cell) > 0) { factory = cell; found = true; }
            }
        }
        Assert.IsTrue(found);
        float medium = sim.Pollution.EmissionOf(factory);
        int mediumCapacity = sim.Capacity.CapacityOf(grid, factory);

        grid.SetDensity(factory, Density.High);
        float expected = medium * sim.Capacity.CapacityOf(grid, factory) / mediumCapacity * m_Config.HighDensityPollution;
        Assert.AreEqual(expected, sim.Pollution.EmissionOf(factory), 1e-3f);
    }

    // --- save → load → continue with a mix ---

    [Test]
    public void SaveLoadContinue_WithAMixedDensityCity_EqualsAnUninterruptedRun()
    {
        SimulationSystem Mixed(GridData grid)
        {
            SimulationSystem sim = SeededCity.Build(grid, m_Config);
            int i = 0;
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var cell = new Vector2Int(x, y);
                    if (grid.GetZone(cell) == ZoneType.None) continue;
                    grid.SetDensity(cell, (Density)(i++ % 3));
                }
            }
            return sim;
        }

        var straightGrid = new GridData(24, 24);
        SimulationSystem straight = Mixed(straightGrid);
        for (int day = 0; day < 80; day++) straight.Tick();

        var firstGrid = new GridData(24, 24);
        SimulationSystem first = Mixed(firstGrid);
        for (int day = 0; day < 40; day++) first.Tick();
        SaveData saved = SaveSystem.Capture(firstGrid, first);
        Assert.IsTrue(SaveSystem.TryFromJson(SaveSystem.ToJson(saved), out SaveData loaded, out string error), error);

        var secondGrid = new GridData(24, 24);
        SimulationSystem second = SeededCity.Build(secondGrid, m_Config);
        SaveSystem.ApplyGrid(loaded, secondGrid);
        second.Sources = first.Sources;
        second.Modifiers = first.Modifiers;
        SaveSystem.ApplySimulation(loaded, second);
        for (int day = 0; day < 40; day++) second.Tick();

        CollectionAssert.AreEqual(straightGrid.ExportDensities(), secondGrid.ExportDensities());
        CollectionAssert.AreEqual(straightGrid.ExportLevels(), secondGrid.ExportLevels());
        Assert.AreEqual(straight.Population.Population, second.Population.Population);
    }
}
