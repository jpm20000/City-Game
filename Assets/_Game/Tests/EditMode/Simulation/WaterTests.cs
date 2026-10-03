using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// M13a: water — wells / fountains by coverage, the piped network (UtilityNetwork, like power), the
// growth gate with the both-or-neither reservation, the blockers and the Water happiness term.
public sealed class WaterTests
{
    private static readonly DemandSnapshot High = new DemandSnapshot(1f, 1f, 1f);

    private readonly List<Object> m_Created = new();
    private BalanceConfig m_Config;

    [SetUp]
    public void SetUp()
    {
        m_Config = Make<BalanceConfig>();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object o in m_Created) Object.DestroyImmediate(o);
        m_Created.Clear();
    }

    private T Make<T>() where T : ScriptableObject
    {
        T instance = ScriptableObject.CreateInstance<T>();
        m_Created.Add(instance);
        return instance;
    }

    // A road along y = 0 across the whole map.
    private static GridData Street(int width, int height)
    {
        var grid = new GridData(width, height);
        for (int x = 0; x < width; x++) grid.SetRoad(new Vector2Int(x, 0), true);
        return grid;
    }

    private static void Grow(GridData grid, Vector2Int cell, byte level, ZoneType zone = ZoneType.Residential)
    {
        grid.SetZone(cell, zone);
        grid.SetBuildingLevel(cell, level);
    }

    private static ServiceSource Tower(Vector2Int at, int supply) =>
        new ServiceSource(at, Vector2Int.one, 0, 0, waterSupply: supply);

    private static ServiceSource Well(Vector2Int at, int radius) =>
        new ServiceSource(at, Vector2Int.one, 0, 0, waterRadius: radius);

    private static ServiceSource Plant(Vector2Int at, int supply) => new ServiceSource(at, Vector2Int.one, 0, supply);

    private static int CountAtLevel(GridData grid, int level)
    {
        int count = 0;
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (grid.GetZone(cell) != ZoneType.None && grid.GetBuildingLevel(cell) == level) count++;
            }
        }
        return count;
    }

    // --- Coverage (wells and fountains) ---

    [Test]
    public void Coverage_ReachesTheChebyshevRadiusAroundTheFootprint()
    {
        var grid = new GridData(16, 16);
        var water = new WaterSystem(grid, m_Config, null, () => WaterRule.Coverage);
        water.SetSources(new[] { Well(new Vector2Int(5, 5), 2), Well(new Vector2Int(6, 5), 1) });

        Assert.AreEqual(1, water.Coverage.GetCoverage(new Vector2Int(3, 3)));
        Assert.AreEqual(1, water.Coverage.GetCoverage(new Vector2Int(7, 7)));
        Assert.AreEqual(2, water.Coverage.GetCoverage(new Vector2Int(6, 6)));
        Assert.AreEqual(0, water.Coverage.GetCoverage(new Vector2Int(8, 5)));
        Assert.IsTrue(water.HasWater(new Vector2Int(3, 3)));
        Assert.IsFalse(water.HasWater(new Vector2Int(2, 5)));
    }

    [Test]
    public void Coverage_CountsOnlyWaterRadius_NotParks()
    {
        var grid = new GridData(16, 16);
        var water = new WaterSystem(grid, m_Config, null, () => WaterRule.Coverage);
        water.SetSources(new[] { new ServiceSource(new Vector2Int(5, 5), new Vector2Int(2, 2), 4, 0) });   // a park

        Assert.IsFalse(water.HasWater(new Vector2Int(5, 5)));
    }

    [Test]
    public void NoWaterRule_AlwaysHasWater()
    {
        var grid = Street(8, 3);
        Grow(grid, new Vector2Int(1, 1), 1);
        var water = new WaterSystem(grid, m_Config, null, () => WaterRule.None);

        Assert.IsTrue(water.HasWater(new Vector2Int(1, 1)));
        Assert.IsTrue(water.HasHeadroom(new Vector2Int(1, 1), 4, 8));
        Assert.AreEqual(0, water.DryCells);
    }

    [Test]
    public void CoverageMode_DryCells_AreGrownCellsOutOfReach()
    {
        var grid = Street(12, 3);
        for (int x = 0; x < 5; x++) Grow(grid, new Vector2Int(x, 1), 1);
        var water = new WaterSystem(grid, m_Config, null, () => WaterRule.Coverage);
        water.SetSources(new[] { Well(new Vector2Int(0, 2), 2) });

        Assert.AreEqual(2, water.DryCells);     // x = 3, 4
    }

    // --- The piped network ---

    [Test]
    public void Piped_FeedsCellsBesideRoads_FurthestGoDryFirst()
    {
        var grid = Street(12, 3);
        for (int x = 0; x < 5; x++) Grow(grid, new Vector2Int(x, 1), 1);    // 5 homes drawing 4 each
        var water = new WaterSystem(grid, m_Config);
        water.SetSources(new[] { Tower(new Vector2Int(10, 1), 12) });

        Assert.AreEqual(WaterRule.Piped, water.Mode, "the age-less rule");
        Assert.AreEqual(12, water.Network.Supply);
        Assert.AreEqual(20, water.Network.Demand);
        Assert.AreEqual(12, water.Network.Load);
        Assert.AreEqual(2, water.DryCells);
        Assert.IsTrue(water.HasWater(new Vector2Int(4, 1)));
        Assert.IsTrue(water.HasWater(new Vector2Int(2, 1)));
        Assert.IsFalse(water.HasWater(new Vector2Int(1, 1)));
        Assert.IsFalse(water.HasWater(new Vector2Int(0, 1)));
        Assert.IsTrue(water.Network.IsCarrying(new Vector2Int(0, 0)));
    }

    [Test]
    public void Piped_SourcesOnOneNetwork_PoolSupply()
    {
        var grid = Street(12, 3);
        for (int x = 0; x < 5; x++) Grow(grid, new Vector2Int(x, 1), 1);
        var water = new WaterSystem(grid, m_Config);
        water.SetSources(new[] { Tower(new Vector2Int(10, 1), 12), Tower(new Vector2Int(11, 1), 8) });

        Assert.AreEqual(20, water.Network.Supply);
        Assert.AreEqual(0, water.DryCells);
    }

    [Test]
    public void Piped_SourceAwayFromRoads_FeedsNothing()
    {
        var grid = Street(12, 4);
        Grow(grid, new Vector2Int(1, 1), 1);
        var water = new WaterSystem(grid, m_Config);
        water.SetSources(new[] { Tower(new Vector2Int(6, 3), 100) });

        Assert.AreEqual(0, water.Network.Supply);
        Assert.IsFalse(water.HasWater(new Vector2Int(1, 1)));
    }

    [Test]
    public void Piped_IgnoresPowerAndWellsInItsSupply()
    {
        var grid = Street(12, 3);
        Grow(grid, new Vector2Int(1, 1), 1);
        var water = new WaterSystem(grid, m_Config);
        water.SetSources(new[] { Plant(new Vector2Int(10, 1), 100), Well(new Vector2Int(2, 1), 5) });

        Assert.AreEqual(0, water.Network.Supply);
        Assert.IsFalse(water.HasWater(new Vector2Int(1, 1)));
    }

    [Test]
    public void DrawFor_ScalesWithWaterPerCapacity_AtLeastOne()
    {
        var so = new SerializedObject(m_Config);
        so.FindProperty("m_WaterPerCapacity").floatValue = 0.5f;
        so.ApplyModifiedPropertiesWithoutUndo();
        var network = new WaterNetwork(new GridData(4, 4), m_Config);

        Assert.AreEqual(0, network.DrawFor(0));
        Assert.AreEqual(1, network.DrawFor(1));
        Assert.AreEqual(2, network.DrawFor(4));
        Assert.AreEqual(8, network.DrawFor(16));
        Assert.AreEqual(2, network.ExtraDraw(4, 8));
    }

    [Test]
    public void Resize_KeepsSources_AndReallocates()
    {
        var grid = Street(12, 3);
        var water = new WaterSystem(grid, m_Config, null, () => WaterRule.Coverage);
        water.SetSources(new[] { Well(new Vector2Int(10, 1), 2), Tower(new Vector2Int(10, 1), 50) });

        grid.Resize(20, 20);
        Assert.AreEqual(1, water.Coverage.GetCoverage(new Vector2Int(12, 3)), "coverage recomputed at the new size");
        Assert.AreEqual(0, water.Coverage.GetCoverage(new Vector2Int(18, 18)));

        for (int x = 0; x < 20; x++) grid.SetRoad(new Vector2Int(x, 0), true);
        Grow(grid, new Vector2Int(15, 1), 1);
        Assert.IsTrue(water.Network.IsServed(new Vector2Int(15, 1)), "the network works at the new size");
    }

    [Test]
    public void Status_ReportsTheRuleSupplyAndDryShare()
    {
        var grid = Street(12, 3);
        for (int x = 0; x < 5; x++) Grow(grid, new Vector2Int(x, 1), 1);
        WaterRule mode = WaterRule.Piped;
        var water = new WaterSystem(grid, m_Config, null, () => mode);
        water.SetSources(new[] { Tower(new Vector2Int(10, 1), 12), Well(new Vector2Int(0, 2), 2) });

        WaterStatus piped = water.Status;
        Assert.AreEqual(WaterRule.Piped, piped.Mode);
        Assert.AreEqual(12, piped.Supply);
        Assert.AreEqual(20, piped.Demand);
        Assert.AreEqual(2, piped.DryCells);
        Assert.AreEqual(5, piped.GrownCells);
        Assert.AreEqual(0.6f, piped.WateredShare, 1e-5f);

        mode = WaterRule.Coverage;
        WaterStatus wells = water.Status;
        Assert.AreEqual(0, wells.Supply, "no piped numbers in the well ages");
        Assert.AreEqual(2, wells.DryCells);     // x = 3, 4 are out of the well's reach
        Assert.AreEqual(1f, new WaterStatus(WaterRule.Coverage, 0, 0, 0, 0).WateredShare, "nothing grown yet");
    }

    // --- The growth gate ---

    [Test]
    public void AgelessSim_UpgradesNeedPipedWater_WithHeadroom()
    {
        var grid = Street(12, 3);
        for (int x = 1; x <= 4; x++) Grow(grid, new Vector2Int(x, 1), 1);
        var sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config);
        sim.Sources = new[] { Plant(new Vector2Int(10, 1), 1000) };

        sim.Growth.Apply(High);
        Assert.AreEqual(4, CountAtLevel(grid, 1), "powered but dry: no upgrades");
        Assert.AreEqual(GrowthBlocker.NoWater, sim.Growth.GetBlocker(new Vector2Int(1, 1), High));

        // 16 drawn + 4 spare = one upgrade (+4), taken row-major.
        sim.Sources = new[] { Plant(new Vector2Int(10, 1), 1000), Tower(new Vector2Int(11, 1), 20) };
        sim.Growth.Apply(High);

        Assert.AreEqual(2, grid.GetBuildingLevel(new Vector2Int(1, 1)));
        Assert.AreEqual(3, CountAtLevel(grid, 1));
        Assert.AreEqual(20, sim.Water.Network.Load);
        Assert.AreEqual(GrowthBlocker.WaterAtCapacity, sim.Growth.GetBlocker(new Vector2Int(2, 1), High));
    }

    [Test]
    public void PowerIsCheckedBeforeWater()
    {
        var grid = Street(12, 3);
        Grow(grid, new Vector2Int(1, 1), 1);
        var sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config);

        Assert.AreEqual(GrowthBlocker.NoPower, sim.Growth.GetBlocker(new Vector2Int(1, 1), High));
    }

    [Test]
    public void Reservation_IsBothOrNeither_SoADryCellHoldsNoPower()
    {
        // An age with the power gate and wells: A (first row-major) has power but no well; B has both.
        // Power has headroom for exactly one upgrade, which must go to B.
        AgeDefinition age = Make<AgeDefinition>();
        age.Init("test", 750, 3, 1f, true, water: WaterRule.Coverage);
        AgeDatabase ages = Make<AgeDatabase>();
        ages.Init(age);
        TechDatabase techs = Make<TechDatabase>();
        techs.Init();

        var grid = Street(12, 4);
        Vector2Int a = new Vector2Int(1, 1), b = new Vector2Int(8, 1);
        Grow(grid, a, 1);
        Grow(grid, b, 1);
        var sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config, ages, techs);
        sim.Tech.StartNew(0);
        sim.Sources = new[] { Plant(new Vector2Int(11, 1), 12), Well(new Vector2Int(9, 2), 1) };

        Assert.AreEqual(GrowthBlocker.NoWater, sim.Growth.GetBlocker(a, High));
        sim.Growth.Apply(High);

        Assert.AreEqual(1, grid.GetBuildingLevel(a));
        Assert.AreEqual(2, grid.GetBuildingLevel(b));
        Assert.AreEqual(GrowthBlocker.PowerAtCapacity, sim.Growth.GetBlocker(a, High), "B took the power");
        Assert.AreEqual(12, sim.Power.Load);
    }

    [Test]
    public void Medieval_HomesNeedAWellToReachLevel2()
    {
        using var test = new TestAges();
        var grid = Street(12, 4);
        for (int x = 1; x <= 3; x++) Grow(grid, new Vector2Int(x, 1), 1);
        var sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config, test.Ages, test.Techs);
        sim.Tech.StartNew(TestAges.Medieval);

        sim.Growth.Apply(High);
        Assert.AreEqual(WaterRule.Coverage, sim.Water.Mode);
        Assert.AreEqual(3, CountAtLevel(grid, 1));
        Assert.AreEqual(GrowthBlocker.NoWater, sim.Growth.GetBlocker(new Vector2Int(1, 1), High));

        sim.Sources = new[] { Well(new Vector2Int(6, 2), 5) };
        sim.Growth.Apply(High);
        Assert.AreEqual(3, CountAtLevel(grid, 2));
        Assert.AreEqual(GrowthBlocker.AgeMaxLevel, sim.Growth.GetBlocker(new Vector2Int(1, 1), High));
    }

    [Test]
    public void AdvancingIntoAPipedAge_WellsStopCounting()
    {
        using var test = new TestAges();
        var grid = Street(12, 4);
        for (int x = 1; x <= 3; x++) Grow(grid, new Vector2Int(x, 1), 1);
        var sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config, test.Ages, test.Techs);
        sim.Tech.StartNew(TestAges.Renaissance);
        ServiceSource well = Well(new Vector2Int(6, 2), 12), plant = Plant(new Vector2Int(10, 1), 1000);
        sim.Sources = new[] { well, plant };

        sim.Growth.Apply(High);
        Assert.AreEqual(3, CountAtLevel(grid, 2), "wells water the Renaissance town");

        TestAges.Advance(sim);
        Assert.AreEqual(WaterRule.Piped, sim.Water.Mode);
        Assert.AreEqual(GrowthBlocker.NoWater, sim.Growth.GetBlocker(new Vector2Int(1, 1), High));
        sim.Growth.Apply(High);
        Assert.AreEqual(0, CountAtLevel(grid, 3));
        Assert.AreEqual(0, sim.Growth.Redeveloped.Count, "redevelopment needs water too");
        Assert.AreEqual(3, sim.Water.DryCells);

        sim.Sources = new[] { well, plant, Tower(new Vector2Int(11, 1), 1000) };
        sim.Growth.Apply(High);
        Assert.AreEqual(3, CountAtLevel(grid, 3));
        Assert.AreEqual(0, sim.Water.DryCells);
    }

    // --- Pipes (13d) ---

    [Test]
    public void Pipes_NeverUnderRoads_RoadsReplaceThem_ResizeClears()
    {
        var grid = Street(8, 4);
        grid.SetPipe(new Vector2Int(2, 0), true);
        Assert.IsFalse(grid.IsPipe(new Vector2Int(2, 0)), "a road carries water already");

        grid.SetPipe(new Vector2Int(2, 2), true);
        Assert.IsTrue(grid.IsPipe(new Vector2Int(2, 2)));
        Assert.AreEqual(1, grid.CountPipes());
        grid.SetRoad(new Vector2Int(2, 2), true);
        Assert.IsFalse(grid.IsPipe(new Vector2Int(2, 2)), "a road laid on a pipe replaces it");

        grid.SetPipe(new Vector2Int(3, 3), true);
        byte[] pipes = grid.ExportPipes();
        var copy = new GridData(8, 4);
        copy.Import(grid.ExportZones(), grid.ExportRoads(), grid.ExportLevels(), null, null, pipes);
        Assert.IsTrue(copy.IsPipe(new Vector2Int(3, 3)));
        Assert.AreEqual(1, copy.CountPipes());

        grid.Resize(10, 10);
        Assert.AreEqual(0, grid.CountPipes());
    }

    [Test]
    public void PumpAwayFromRoads_FeedsTheRoadsThroughAPipe()
    {
        var grid = Street(12, 6);
        for (int x = 0; x < 5; x++) Grow(grid, new Vector2Int(x, 1), 1);
        var water = new WaterSystem(grid, m_Config);
        water.SetSources(new[] { Tower(new Vector2Int(10, 4), 100) });
        Assert.AreEqual(0, water.Network.Supply, "no road beside the pump");

        for (int y = 1; y <= 3; y++) grid.SetPipe(new Vector2Int(10, y), true);   // (10,1)..(10,3) up to the road
        Assert.AreEqual(100, water.Network.Supply);
        Assert.IsTrue(water.Network.IsCarrying(new Vector2Int(10, 2)));
        Assert.IsTrue(water.Network.IsCarrying(new Vector2Int(0, 0)), "the road behind the pipe carries it");
        Assert.AreEqual(0, water.DryCells);
    }

    [Test]
    public void PipeJoiningTwoRoadNetworks_PoolsTheirSupply()
    {
        var grid = new GridData(12, 4);
        for (int x = 0; x < 5; x++) grid.SetRoad(new Vector2Int(x, 0), true);
        for (int x = 7; x < 12; x++) grid.SetRoad(new Vector2Int(x, 0), true);
        for (int x = 0; x < 5; x++) Grow(grid, new Vector2Int(x, 1), 1);      // 20 drawn on the west road
        var water = new WaterSystem(grid, m_Config);
        water.SetSources(new[] { Tower(new Vector2Int(11, 1), 20) });         // the tower is on the east road
        Assert.AreEqual(5, water.DryCells);

        grid.SetPipe(new Vector2Int(5, 0), true);
        grid.SetPipe(new Vector2Int(6, 0), true);
        Assert.AreEqual(0, water.DryCells);
        Assert.AreEqual(20, water.Network.Load);
    }

    [Test]
    public void PipeUnderAGrownCell_FeedsIt_AndCarriesOn()
    {
        var grid = Street(8, 6);
        Vector2Int deep = new Vector2Int(3, 3), deeper = new Vector2Int(3, 4);   // no road beside either
        Grow(grid, deep, 1);
        Grow(grid, deeper, 1);
        var water = new WaterSystem(grid, m_Config);
        water.SetSources(new[] { Tower(new Vector2Int(7, 1), 100) });
        Assert.IsFalse(water.HasWater(deep));

        grid.SetPipe(new Vector2Int(3, 1), true);
        grid.SetPipe(new Vector2Int(3, 2), true);
        grid.SetPipe(deep, true);
        Assert.IsTrue(water.HasWater(deep), "a pipe under it");
        Assert.IsTrue(water.HasWater(deeper), "beside a pipe that runs through a grown cell");
    }

    [Test]
    public void Pipes_DontCarryPower()
    {
        var grid = new GridData(12, 4);
        for (int x = 0; x < 5; x++) grid.SetRoad(new Vector2Int(x, 0), true);
        for (int x = 7; x < 12; x++) grid.SetRoad(new Vector2Int(x, 0), true);
        grid.SetPipe(new Vector2Int(5, 0), true);
        grid.SetPipe(new Vector2Int(6, 0), true);
        var power = new PowerSystem(grid, m_Config);
        power.SetSources(new[] { Plant(new Vector2Int(11, 1), 100) });

        Assert.IsTrue(power.IsEnergisedRoad(new Vector2Int(7, 0)));
        Assert.IsFalse(power.IsEnergisedRoad(new Vector2Int(4, 0)));
    }

    [Test]
    public void Pipes_CostUpkeep()
    {
        var grid = new GridData(12, 4);
        for (int x = 0; x < 10; x++) grid.SetPipe(new Vector2Int(x, 2), true);
        var sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config);
        sim.Tick();
        Assert.AreEqual(10 * m_Config.PipeUpkeepPerDay, sim.Economy.ExpensePerDay, 1e-5f);
    }

    // --- Happiness ---

    [Test]
    public void WaterTerm_IsZeroInAWateredCity_AndCostsHappinessWhenDry()
    {
        SimulationSystem watered = SeededCity.Run(new GridData(24, 24), m_Config, 60);
        Assert.AreEqual(0f, watered.Population.Happiness.Water);
        Assert.AreEqual(0f, watered.MeasureServices().UnwateredHousingShare);

        var dryGrid = new GridData(24, 24);
        SimulationSystem dry = SeededCity.Run(dryGrid, m_Config, 60, waterSupply: 0);
        Assert.AreEqual(1f, dry.MeasureServices().UnwateredHousingShare);
        Assert.Less(dry.Population.Happiness.Water, 0f);
        Assert.GreaterOrEqual(dry.Population.Happiness.Water, -m_Config.WaterPenalty - 1e-5f);
        Assert.AreEqual(0, CountAtLevel(dryGrid, 2), "a dry city never upgrades");
        Assert.Less(dry.Population.Population, watered.Population.Population);
    }

    [Test]
    public void WaterTerm_IsZeroInAnAgeWithoutWaterRule()
    {
        AgeDefinition age = Make<AgeDefinition>();
        age.Init("dry", 750, 3, 1f, false);
        AgeDatabase ages = Make<AgeDatabase>();
        ages.Init(age);
        TechDatabase techs = Make<TechDatabase>();
        techs.Init();

        var grid = Street(12, 3);
        Grow(grid, new Vector2Int(1, 1), 1);
        var sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config, ages, techs);
        sim.Tech.StartNew(0);

        Assert.AreEqual(0f, sim.MeasureServices().UnwateredHousingShare);
        sim.Growth.Apply(High);
        Assert.AreEqual(2, grid.GetBuildingLevel(new Vector2Int(1, 1)));
    }
}
