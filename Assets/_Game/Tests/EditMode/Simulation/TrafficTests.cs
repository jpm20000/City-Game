using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

// M16b: the commute flow (TrafficSystem), the Traffic happiness term, the land-value line and the
// traffic multiplier. Small hand-built grids with legacy tiers (every road is Paved: capacity 160,
// travel cost 4, upkeep 1) unless a test asks for the tier content.
public sealed class TrafficTests
{
    private BalanceConfig m_Config;
    private TestAges m_TestAges;
    private readonly List<Object> m_Created = new();

    [SetUp]
    public void SetUp()
    {
        m_Config = ScriptableObject.CreateInstance<BalanceConfig>();
        m_TestAges = new TestAges();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object o in m_Created) Object.DestroyImmediate(o);
        m_Created.Clear();
        m_TestAges.Dispose();
        Object.DestroyImmediate(m_Config);
    }

    private static Vector2Int V(int x, int y) => new Vector2Int(x, y);

    private static void Row(GridData grid, int y, int x0, int x1, byte tier = 3)
    {
        for (int x = x0; x <= x1; x++) grid.SetRoadTier(V(x, y), tier);
    }

    private static void Grown(GridData grid, int x, int y, ZoneType zone, byte level = 1)
    {
        grid.SetZone(V(x, y), zone);
        grid.SetBuildingLevel(V(x, y), level);
    }

    private TrafficSystem Legacy(GridData grid) =>
        new TrafficSystem(grid, m_Config, new CapacityModel(m_Config), new RoadTiers(m_Config));

    // Dirt..Highway on the test techs (as RoadTierTests): avenue 400 / travel 3, highway 1200 / travel 1 / no frontage.
    private RoadTiers WithTiers()
    {
        RoadTierDefinition Tier(byte tier, string id, string tech, int cost, float upkeep, float capacity, int travel, bool frontage, byte obsolete)
        {
            var def = ScriptableObject.CreateInstance<RoadTierDefinition>();
            m_Created.Add(def);
            def.Init(tier, id, tech != null ? m_TestAges[tech] : null, cost, upkeep, capacity, travel, frontage, obsolete);
            return def;
        }
        m_TestAges.Techs.InitRoadTiers(
            Tier(1, "dirt", null, 20, 0.4f, 60, 6, true, 2),
            Tier(2, "cobble", "architecture", 35, 0.7f, 100, 5, true, 3),
            Tier(3, "paved", "electricity", 50, 1f, 160, 4, true, 0),
            Tier(4, "avenue", "steam", 150, 3f, 400, 3, true, 0),
            Tier(5, "highway", "computing", 400, 6f, 1200, 1, false, 0));
        return new RoadTiers(m_Config, m_TestAges.Techs);
    }

    // Homes at x = 10..12 (y 11), a shop at (14, 9): its road cell (14, 10) is the sink.
    private GridData StraightStreet()
    {
        var grid = new GridData(20, 20);
        Row(grid, 10, 0, 19);
        for (int x = 10; x <= 12; x++) Grown(grid, x, 11, ZoneType.Residential);
        Grown(grid, 14, 9, ZoneType.Commercial);
        return grid;
    }

    [Test]
    public void StraightStreet_LoadGrowsTowardsTheJobs()
    {
        GridData grid = StraightStreet();
        TrafficSystem traffic = Legacy(grid);
        traffic.Update(1f, 1f, 1f);

        Assert.AreEqual(12f, traffic.Trips, 1e-4f);
        Assert.AreEqual(4f, traffic.Load(V(10, 10)), 1e-4f);
        Assert.AreEqual(8f, traffic.Load(V(11, 10)), 1e-4f);
        Assert.AreEqual(12f, traffic.Load(V(12, 10)), 1e-4f);
        Assert.AreEqual(12f, traffic.Load(V(14, 10)), 1e-4f, "the sink carries everything");
        Assert.AreEqual(0f, traffic.Load(V(9, 10)), 1e-4f, "behind the homes");
        Assert.AreEqual(0f, traffic.Load(V(16, 10)), 1e-4f, "past the shop");
        Assert.AreEqual(12f / 160f, traffic.Congestion(V(14, 10)), 1e-5f);
        Assert.AreEqual(0, traffic.JammedRoads);
    }

    [Test]
    public void Trips_AreResidentsTimesEmploymentTimesMultiplier()
    {
        GridData grid = StraightStreet();
        TrafficSystem traffic = Legacy(grid);
        traffic.Update(0.5f, 0.8f, 1.2f);

        float perHome = 4f * 0.5f * 0.8f * 1.2f;
        Assert.AreEqual(perHome, traffic.HomeTrips(V(10, 11)), 1e-4f);
        Assert.AreEqual(3f * perHome, traffic.Trips, 1e-4f);
        Assert.AreEqual(3f * perHome, traffic.Load(V(14, 10)), 1e-4f);
    }

    [Test]
    public void NoJobs_CommuteOutOfTownThroughTheNearestEdge()
    {
        var grid = new GridData(20, 20);
        Row(grid, 10, 0, 19);
        Grown(grid, 10, 11, ZoneType.Residential);
        TrafficSystem traffic = Legacy(grid);
        traffic.Update(1f, 1f, 1f);

        Assert.AreEqual(4f, traffic.Trips, 1e-4f);
        Assert.AreEqual(4f, traffic.Load(V(19, 10)), 1e-4f, "east edge is nearer (9 cells) than west (10)");
        Assert.AreEqual(0f, traffic.Load(V(0, 10)), 1e-4f);
        Assert.AreEqual(4f / 160f, traffic.CommuteCongestion(V(10, 11)), 1e-5f);
    }

    [Test]
    public void AJobBeatsTheEdge()
    {
        GridData grid = StraightStreet();
        TrafficSystem traffic = Legacy(grid);
        traffic.Update(1f, 1f, 1f);

        Assert.AreEqual(0f, traffic.Load(V(19, 10)), 1e-4f);
        Assert.AreEqual(0f, traffic.Load(V(0, 10)), 1e-4f);
    }

    [Test]
    public void FasterParallelAvenue_DrawsTheFlow()
    {
        RoadTiers tiers = WithTiers();
        var grid = new GridData(20, 20);
        Row(grid, 10, 0, 19, RoadTiers.Paved);
        Row(grid, 12, 0, 19, RoadTiers.Avenue);
        Grown(grid, 3, 11, ZoneType.Residential);      // between the paved street and the avenue
        Grown(grid, 15, 11, ZoneType.Commercial);
        var traffic = new TrafficSystem(grid, m_Config, new CapacityModel(m_Config), tiers);
        traffic.Update(1f, 1f, 1f);

        Assert.AreEqual(4f, traffic.Load(V(3, 12)), 1e-4f, "the home loads the avenue");
        Assert.AreEqual(0f, traffic.Load(V(3, 10)), 1e-4f);
        Assert.AreEqual(4f / 400f, traffic.Congestion(V(10, 12)), 1e-5f, "capacity is the avenue's");
    }

    [Test]
    public void Ties_ResolveTheSameWayEveryRun()
    {
        var grid = new GridData(20, 20);
        Row(grid, 9, 0, 19);
        Row(grid, 11, 0, 19);
        for (int x = 4; x <= 14; x++) Grown(grid, x, 10, ZoneType.Residential);   // equidistant from both streets
        Grown(grid, 17, 8, ZoneType.Commercial);
        Grown(grid, 17, 12, ZoneType.Industrial);

        TrafficSystem a = Legacy(grid);
        TrafficSystem b = Legacy(grid);
        a.Update(1f, 1f, 1f);
        b.Update(1f, 1f, 1f);
        a.Update(1f, 1f, 1f);       // re-running on the same instance gives the same loads too

        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                Assert.AreEqual(b.Load(V(x, y)), a.Load(V(x, y)), 1e-5f, $"({x},{y})");
            }
        }
        Assert.AreEqual(11 * 4f, a.Trips, 1e-4f);
    }

    [Test]
    public void Highway_CarriesFlow_ButGivesNoFrontage()
    {
        RoadTiers tiers = WithTiers();
        var grid = new GridData(20, 20);
        Row(grid, 10, 0, 19, RoadTiers.Highway);
        grid.SetRoadTier(V(10, 11), RoadTiers.Paved);     // a street joins the highway
        Grown(grid, 11, 11, ZoneType.Residential);        // beside the street
        Grown(grid, 5, 9, ZoneType.Residential);          // beside only the highway
        Grown(grid, 14, 9, ZoneType.Commercial);          // a shop beside only the highway: not a job
        var traffic = new TrafficSystem(grid, m_Config, new CapacityModel(m_Config), tiers);
        traffic.Update(1f, 1f, 1f);

        Assert.AreEqual(0f, traffic.HomeTrips(V(5, 9)), "no frontage: the home loads nothing");
        Assert.AreEqual(4f, traffic.HomeTrips(V(11, 11)), 1e-4f);
        Assert.AreEqual(4f, traffic.Trips, 1e-4f);
        Assert.AreEqual(4f, traffic.Load(V(10, 10)), 1e-4f, "the highway carries the street's trips");
        Assert.AreEqual(4f, traffic.Load(V(15, 10)), 1e-4f, "on to the east edge (the shop is not a sink)");
        Assert.AreEqual(4f / 1200f, traffic.Congestion(V(15, 10)), 1e-6f);
    }

    [Test]
    public void CommuteCongestion_IsTheWorstRatioOnTheWay()
    {
        GridData grid = StraightStreet();
        TrafficSystem traffic = Legacy(grid);
        traffic.Update(1f, 1f, 20f);          // 80 trips a home: 240 on the last stretch (150%)

        Assert.AreEqual(80f / 160f, traffic.Congestion(V(10, 10)), 1e-5f);
        Assert.AreEqual(240f / 160f, traffic.Congestion(V(13, 10)), 1e-5f);
        Assert.AreEqual(1.5f, traffic.CommuteCongestion(V(10, 11)), 1e-5f, "the far home is stuck in the jam ahead");
        Assert.AreEqual(1.5f, traffic.CommuteCongestion(V(12, 11)), 1e-5f);
        Assert.AreEqual(1.5f, traffic.WorstCongestion, 1e-5f);
        Assert.AreEqual(3, traffic.JammedRoads, "(12..14, 10); (11, 10) is exactly full");
    }

    [Test]
    public void PenaltyAndLandValueFormulas()
    {
        Assert.AreEqual(0f, TrafficSystem.TrafficPenaltyAt(m_Config, 0.8f), 1e-6f, "free up to 80%");
        Assert.AreEqual(0.04f, TrafficSystem.TrafficPenaltyAt(m_Config, 1.2f), 1e-6f);
        Assert.AreEqual(0.08f, TrafficSystem.TrafficPenaltyAt(m_Config, 1.6f), 1e-6f);
        Assert.AreEqual(0.08f, TrafficSystem.TrafficPenaltyAt(m_Config, 9f), 1e-6f, "capped");
        Assert.AreEqual(0.01f, TrafficSystem.LandValueLossAt(m_Config, 0.9f), 1e-6f);
        Assert.AreEqual(0.10f, TrafficSystem.LandValueLossAt(m_Config, 9f), 1e-6f, "capped");
    }

    [Test]
    public void TrafficPenalty_IsWeightedByHousing()
    {
        var grid = StraightStreet();                           // three homes stuck at 150%
        Row(grid, 3, 0, 19);
        Grown(grid, 5, 4, ZoneType.Residential);               // one home on a quiet street out of town
        var sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config);
        sim.Traffic.Update(1f, 1f, 20f);

        ServiceStats stats = ServiceStats.Measure(grid, m_Config, sim.Coverage, sim.Power, sim.Capacity, false, traffic: sim.Traffic);

        Assert.AreEqual(0.5f, sim.Traffic.CommuteCongestion(V(5, 4)), 1e-5f);
        Assert.AreEqual(3f * 0.07f / 4f, stats.TrafficPenalty, 1e-5f);
        Assert.AreEqual(0f, ServiceStats.Measure(grid, m_Config, sim.Coverage, sim.Power, sim.Capacity, false).TrafficPenalty, "no traffic system: no term");
    }

    [Test]
    public void LandValue_LosesFromTheRoadsBesideTheCell()
    {
        GridData grid = StraightStreet();
        var sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config);
        sim.Traffic.Update(1f, 1f, 20f);

        LandValueBreakdown beside = sim.LandValue.Explain(V(13, 11));
        LandValueBreakdown away = sim.LandValue.Explain(V(13, 13));
        Assert.AreEqual(-0.07f, beside.Traffic, 1e-5f, "(1.5 - 0.8) x 0.10");
        Assert.AreEqual(0f, away.Traffic, 1e-6f, "two cells from the road");
        Assert.AreEqual(away.Total - 0.07f, beside.Total, 1e-5f);
    }

    [Test]
    public void ResizeClearsTheFlow()
    {
        GridData grid = StraightStreet();
        TrafficSystem traffic = Legacy(grid);
        traffic.Update(1f, 1f, 1f);
        Assert.Greater(traffic.Trips, 0f);

        grid.Resize(12, 12);
        Assert.AreEqual(0f, traffic.Trips);
        Assert.AreEqual(0f, traffic.Load(V(3, 3)));
        traffic.Update(1f, 1f, 1f);
        Assert.AreEqual(0f, traffic.Trips);
    }

    [Test]
    public void TrafficMultiplier_ComesFromTheTechs()
    {
        var techs = AssetDatabaseTechs();
        TechModifiers none = TechModifiers.Fold(new TechDefinition[0]);
        Assert.AreEqual(1f, none.TrafficMultiplier);
        var folded = TechModifiers.Fold(new[] { techs.GetById("railways"), techs.GetById("electric_trams"), techs.GetById("automobiles") });
        Assert.AreEqual(0.85f * 0.9f * 1.2f, folded.TrafficMultiplier, 1e-5f);
        var withSundays = TechModifiers.Fold(new[] { techs.GetById("railways") }, new[] { techs.GetOrdinanceById("car_free_sundays") });
        Assert.AreEqual(0.85f * 0.9f, withSundays.TrafficMultiplier, 1e-5f);
    }

    private static TechDatabase AssetDatabaseTechs() =>
        UnityEditor.AssetDatabase.LoadAssetAtPath<TechDatabase>("Assets/_Game/Scriptables/Techs/TechDatabase.asset");

    [Test]
    public void SaveThenLoad_RecomputesTheSameFlow_AndContinuesIdentically()
    {
        var gridA = new GridData(24, 24);
        SeededCity.Seed(gridA);
        var a = new SimulationSystem(gridA, new RoadNetwork(gridA), m_Config);
        for (int i = 0; i < 40; i++) a.Tick();
        Assert.Greater(a.Traffic.Trips, 0f);

        var gridB = new GridData(24, 24);
        var b = new SimulationSystem(gridB, new RoadNetwork(gridB), m_Config);
        SaveData saved = SaveSystem.Capture(gridA, a);
        SaveSystem.ApplyGrid(saved, gridB);
        SaveSystem.ApplySimulation(saved, b);

        AssertSameFlow(gridA, a, b);
        for (int i = 0; i < 30; i++)
        {
            a.Tick();
            b.Tick();
        }
        AssertSameFlow(gridA, a, b);
        Assert.AreEqual(a.Population.AverageHappiness, b.Population.AverageHappiness);
        Assert.AreEqual(a.Population.Population, b.Population.Population);
    }

    private static void AssertSameFlow(GridData grid, SimulationSystem a, SimulationSystem b)
    {
        Assert.AreEqual(a.Traffic.Trips, b.Traffic.Trips, 1e-3f);
        Assert.AreEqual(a.Traffic.WorstCongestion, b.Traffic.WorstCongestion, 1e-5f);
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                Assert.AreEqual(a.Traffic.Load(V(x, y)), b.Traffic.Load(V(x, y)), 1e-3f, $"load ({x},{y})");
            }
        }
    }
}
