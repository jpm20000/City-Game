using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

// M16a: road tiers in the grid, the tier table (legacy and from content), frontage, the per-tier Roads
// ledger line, save v5 and the v4 -> v5 migration.
public sealed class RoadTierTests
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

    private TechDatabase Techs => m_TestAges.Techs;

    // The five tiers on the test techs: dirt (none), cobble (architecture), paved (electricity, the
    // Industrial starting tech), avenue (steam), highway (computing).
    private TechDatabase WithTiers()
    {
        RoadTierDefinition Tier(byte tier, string id, string tech, int cost, float upkeep, float capacity, int travel, bool frontage, byte obsolete)
        {
            var def = ScriptableObject.CreateInstance<RoadTierDefinition>();
            m_Created.Add(def);
            def.Init(tier, id, tech != null ? m_TestAges[tech] : null, cost, upkeep, capacity, travel, frontage, obsolete);
            return def;
        }
        Techs.InitRoadTiers(
            Tier(1, "dirt", null, 20, 0.4f, 60, 6, true, 2),
            Tier(2, "cobble", "architecture", 35, 0.7f, 100, 5, true, 3),
            Tier(3, "paved", "electricity", 50, 1f, 160, 4, true, 0),
            Tier(4, "avenue", "steam", 150, 3f, 400, 3, true, 0),
            Tier(5, "highway", "computing", 400, 6f, 1200, 1, false, 0));
        return Techs;
    }

    private SimulationSystem NewSim(GridData grid, int startAge)
    {
        var sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config, m_TestAges.Ages, WithTiers());
        sim.Tech.StartNew(startAge);
        return sim;
    }

    // --- GridData ---

    [Test]
    public void SetRoad_LaysPavedAndRemoves()
    {
        var grid = new GridData(8, 8);
        var cell = new Vector2Int(2, 3);
        grid.SetRoad(cell, true);
        Assert.IsTrue(grid.IsRoad(cell));
        Assert.AreEqual(3, grid.GetRoadTier(cell));

        grid.SetRoadTier(cell, 4);
        grid.SetRoad(cell, true);
        Assert.AreEqual(4, grid.GetRoadTier(cell), "laying a road on a road keeps its tier");

        grid.SetRoad(cell, false);
        Assert.IsFalse(grid.IsRoad(cell));
        Assert.AreEqual(0, grid.GetRoadTier(cell));
    }

    [Test]
    public void TierChange_RaisesCellChangedOnce_AndClearsPipe()
    {
        var grid = new GridData(8, 8);
        var cell = new Vector2Int(1, 1);
        grid.SetPipe(cell, true);
        int changes = 0;
        grid.OnCellChanged += _ => changes++;

        grid.SetRoadTier(cell, 2);
        Assert.AreEqual(1, changes);
        Assert.IsFalse(grid.IsPipe(cell));

        grid.SetRoadTier(cell, 2);
        Assert.AreEqual(1, changes, "same tier: no event");
        grid.SetRoadTier(cell, 5);
        Assert.AreEqual(2, changes);
    }

    [Test]
    public void Tiers_SurviveExportImport_AndResizeEmpties()
    {
        var grid = new GridData(6, 6);
        grid.SetRoadTier(new Vector2Int(0, 0), 1);
        grid.SetRoadTier(new Vector2Int(1, 0), 4);
        grid.SetRoadTier(new Vector2Int(2, 0), 5);
        byte[] roads = grid.ExportRoads();
        Assert.AreEqual(4, roads[1]);

        var copy = new GridData(6, 6);
        copy.Import(grid.ExportZones(), roads, grid.ExportLevels());
        Assert.AreEqual(1, copy.GetRoadTier(new Vector2Int(0, 0)));
        Assert.AreEqual(4, copy.GetRoadTier(new Vector2Int(1, 0)));
        Assert.AreEqual(5, copy.GetRoadTier(new Vector2Int(2, 0)));

        var counts = new int[GridData.MaxRoadTier + 1];
        copy.CountRoadsByTier(counts);
        CollectionAssert.AreEqual(new[] { 0, 1, 0, 0, 1, 1 }, counts);
        Assert.AreEqual(3, copy.CountRoads());

        copy.Resize(9, 9);
        Assert.AreEqual(0, copy.CountRoads());
    }

    // --- The tier table ---

    [Test]
    public void Legacy_EveryTierIsTodaysRoad()
    {
        var tiers = new RoadTiers(m_Config);
        Assert.IsFalse(tiers.HasContent);
        Assert.AreEqual(RoadTiers.Paved, tiers.BestStreetTier);
        for (int t = 1; t <= RoadTiers.Count; t++)
        {
            Assert.AreEqual(m_Config.RoadCost, tiers.Cost(t));
            Assert.AreEqual(m_Config.RoadUpkeepPerDay, tiers.UpkeepPerDay(t));
            Assert.IsTrue(tiers.Frontage(t));
            Assert.IsTrue(tiers.IsUnlocked(t));
        }
        Assert.AreEqual(50, m_Config.RoadCost);
        Assert.AreEqual(1f, m_Config.RoadUpkeepPerDay);
    }

    [Test]
    public void AgelessSim_LaysAndChargesPavedRoads()
    {
        var grid = new GridData(8, 8);
        var sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config);
        for (int i = 0; i < 5; i++) grid.SetRoad(new Vector2Int(i, 0), true);

        Assert.AreEqual(RoadTiers.Paved, sim.RoadTiers.BestStreetTier);
        Assert.AreEqual(5f, sim.Ledger().Roads, 1e-4f);
    }

    [Test]
    public void BestStreetTier_FollowsTheStartingAge()
    {
        var grid = new GridData(8, 8);
        Assert.AreEqual(RoadTiers.Dirt, NewSim(grid, TestAges.Medieval).RoadTiers.BestStreetTier);
        Assert.AreEqual(RoadTiers.Dirt, NewSim(new GridData(8, 8), TestAges.Renaissance).RoadTiers.BestStreetTier,
            "architecture is not a starting tech here");
        Assert.AreEqual(RoadTiers.Paved, NewSim(new GridData(8, 8), TestAges.Industrial).RoadTiers.BestStreetTier);
        Assert.AreEqual(RoadTiers.Paved, NewSim(new GridData(8, 8), TestAges.Modern).RoadTiers.BestStreetTier,
            "avenues and highways have their own tools");
    }

    [Test]
    public void Cobble_ReplacesDirtOnceResearched()
    {
        SimulationSystem sim = NewSim(new GridData(8, 8), TestAges.Medieval);
        Assert.IsTrue(sim.RoadTiers.IsUnlocked(RoadTiers.Dirt));
        Assert.IsFalse(sim.RoadTiers.IsUnlocked(RoadTiers.Cobble));

        sim.Tech.Restore(TestAges.Renaissance, new[] { "commons", "masonry", "architecture" }, "", 0f, new string[0]);
        Assert.IsTrue(sim.RoadTiers.IsUnlocked(RoadTiers.Cobble));
        Assert.AreEqual(RoadTiers.Cobble, sim.RoadTiers.BestStreetTier);
    }

    [Test]
    public void UpgradeCost_IsTheDifference_NeverNegative()
    {
        SimulationSystem sim = NewSim(new GridData(8, 8), TestAges.Modern);
        Assert.AreEqual(100, sim.RoadTiers.UpgradeCost(RoadTiers.Paved, RoadTiers.Avenue));
        Assert.AreEqual(15, sim.RoadTiers.UpgradeCost(RoadTiers.Dirt, RoadTiers.Cobble));
        Assert.AreEqual(0, sim.RoadTiers.UpgradeCost(RoadTiers.Avenue, RoadTiers.Dirt));
    }

    [Test]
    public void Ledger_RoadsIsTheSumOfEachTiersUpkeep()
    {
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid, TestAges.Modern);
        for (int i = 0; i < 4; i++) grid.SetRoadTier(new Vector2Int(i, 0), RoadTiers.Paved);
        for (int i = 0; i < 2; i++) grid.SetRoadTier(new Vector2Int(i, 2), RoadTiers.Avenue);
        grid.SetRoadTier(new Vector2Int(0, 4), RoadTiers.Highway);
        grid.SetRoadTier(new Vector2Int(0, 6), RoadTiers.Dirt);

        Assert.AreEqual(4 * 1f + 2 * 3f + 6f + 0.4f, sim.Ledger().Roads, 1e-4f);
    }

    [Test]
    public void TechDatabase_Validate_CatchesBadTiers()
    {
        WithTiers();
        var errors = new List<string>();
        Assert.IsTrue(Techs.Validate(m_TestAges.Ages, errors), string.Join("\n", errors));

        var dup = ScriptableObject.CreateInstance<RoadTierDefinition>();
        m_Created.Add(dup);
        dup.Init(3, "again", null, 10, 1f, 0f, 0, true);
        var list = new List<RoadTierDefinition>(Techs.RoadTiers) { dup };
        Techs.InitRoadTiers(list.ToArray());
        errors.Clear();
        Assert.IsFalse(Techs.Validate(m_TestAges.Ages, errors));
        StringAssert.Contains("Duplicate road tier", string.Join("\n", errors));
        StringAssert.Contains("capacity", string.Join("\n", errors));
        StringAssert.Contains("travel cost", string.Join("\n", errors));
    }

    // --- Frontage ---

    [Test]
    public void Highway_GivesNoAccess_ButConnectsToTheEdge()
    {
        var grid = new GridData(12, 12);
        var roads = new RoadNetwork(grid);
        var sim = new SimulationSystem(grid, roads, m_Config, m_TestAges.Ages, WithTiers());
        sim.Tech.StartNew(TestAges.Modern);

        // A highway from the edge, with a paved street leaving it.
        for (int x = 0; x < 6; x++) grid.SetRoadTier(new Vector2Int(x, 5), RoadTiers.Highway);
        for (int y = 6; y < 9; y++) grid.SetRoadTier(new Vector2Int(5, y), RoadTiers.Paved);

        Assert.IsTrue(roads.IsConnectedToEntry(new Vector2Int(5, 5)));
        Assert.IsTrue(roads.IsConnectedToEntry(new Vector2Int(5, 8)), "the street is connected through the highway");
        Assert.IsFalse(roads.HasRoadAccess(new Vector2Int(3, 4)), "beside only a highway");
        Assert.IsFalse(roads.HasRoadAccess(new Vector2Int(3, 6)));
        Assert.IsTrue(roads.HasRoadAccess(new Vector2Int(6, 7)), "beside the paved street");
        Assert.IsTrue(roads.HasRoadAccess(new Vector2Int(4, 6)), "beside the street's first cell, which also touches the highway");
    }

    [Test]
    public void CellBesideHighwayAndStreet_Grows()
    {
        var grid = new GridData(12, 12);
        var roads = new RoadNetwork(grid);
        var sim = new SimulationSystem(grid, roads, m_Config, m_TestAges.Ages, WithTiers());
        sim.Tech.StartNew(TestAges.Modern);
        for (int x = 0; x < 12; x++) grid.SetRoadTier(new Vector2Int(x, 5), RoadTiers.Highway);
        for (int x = 0; x < 12; x++) grid.SetRoadTier(new Vector2Int(x, 7), RoadTiers.Paved);
        grid.SetZone(new Vector2Int(4, 6), ZoneType.Residential);   // between the two
        grid.SetZone(new Vector2Int(4, 4), ZoneType.Residential);   // beside only the highway

        Assert.AreNotEqual(GrowthBlocker.NoRoadAccess, sim.Growth.GetBlocker(new Vector2Int(4, 6), sim.Demand.Snapshot));
        Assert.AreEqual(GrowthBlocker.NoRoadAccess, sim.Growth.GetBlocker(new Vector2Int(4, 4), sim.Demand.Snapshot));
    }

    // --- Save v5 ---

    private SaveData V4Save(int age, string[] researched, params byte[] roads)
    {
        return new SaveData
        {
            Version = 4,
            Width = 4,
            Height = 4,
            Age = age,
            Researched = new List<string>(researched),
            Zones = new byte[16],
            Roads = roads.Length == 16 ? roads : Pad(roads),
            Levels = new byte[16],
            BuiltAges = new byte[16],
            Historic = new byte[16],
            Pipes = new byte[16],
        };
    }

    private static byte[] Pad(byte[] roads)
    {
        var all = new byte[16];
        roads.CopyTo(all, 0);
        return all;
    }

    [Test]
    public void V4ToV5_RoadsBecomeTheBestStreetTierResearched()
    {
        WithTiers();

        SaveData medieval = V4Save(TestAges.Medieval, new string[0], 1, 0, 1);
        Assert.IsTrue(SaveMigrations.TryMigrate(medieval, m_TestAges.Ages, Techs, out string error), error);
        Assert.AreEqual(5, medieval.Version);
        CollectionAssert.AreEqual(new byte[] { 1, 0, 1 }, new[] { medieval.Roads[0], medieval.Roads[1], medieval.Roads[2] }, "dirt");

        SaveData renaissance = V4Save(TestAges.Renaissance, new[] { "commons", "masonry", "architecture" }, 1, 1);
        Assert.IsTrue(SaveMigrations.TryMigrate(renaissance, m_TestAges.Ages, Techs, out error), error);
        Assert.AreEqual(2, renaissance.Roads[0], "cobble");

        SaveData industrial = V4Save(TestAges.Industrial, new[] { "electricity" }, 1);
        Assert.IsTrue(SaveMigrations.TryMigrate(industrial, m_TestAges.Ages, Techs, out error), error);
        Assert.AreEqual(3, industrial.Roads[0], "paved");
    }

    [Test]
    public void V4ToV5_WithoutAges_RoadsArePaved()
    {
        SaveData data = V4Save(SaveData.NoAge, new string[0], 1, 1, 0, 1);
        Assert.IsTrue(SaveMigrations.TryMigrate(data, null, null, out string error), error);
        CollectionAssert.AreEqual(new byte[] { 3, 3, 0, 3 }, new[] { data.Roads[0], data.Roads[1], data.Roads[2], data.Roads[3] });
    }

    [Test]
    public void TryFromJson_RejectsAnInvalidRoadTier()
    {
        var grid = new GridData(4, 4);
        var sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config);
        SaveData data = SaveSystem.Capture(grid, sim);
        data.Roads[3] = 9;

        Assert.IsFalse(SaveSystem.TryFromJson(SaveSystem.ToJson(data), out _, out string error));
        StringAssert.Contains("road tier", error);
    }

    [Test]
    public void SaveLoad_KeepsMixedTiers_AndTheLedger()
    {
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid, TestAges.Modern);
        grid.SetRoadTier(new Vector2Int(0, 0), RoadTiers.Paved);
        grid.SetRoadTier(new Vector2Int(1, 0), RoadTiers.Avenue);
        grid.SetRoadTier(new Vector2Int(2, 0), RoadTiers.Highway);
        float ledger = sim.Ledger().Roads;

        string json = SaveSystem.ToJson(SaveSystem.Capture(grid, sim));
        Assert.IsTrue(SaveSystem.TryFromJson(json, out SaveData loaded, out string error, m_TestAges.Ages, Techs), error);
        Assert.AreEqual(SaveData.CurrentVersion, loaded.Version);

        var grid2 = new GridData(12, 12);
        var sim2 = new SimulationSystem(grid2, new RoadNetwork(grid2), m_Config, m_TestAges.Ages, Techs);
        SaveSystem.ApplyGrid(loaded, grid2);
        SaveSystem.ApplySimulation(loaded, sim2);

        CollectionAssert.AreEqual(grid.ExportRoads(), grid2.ExportRoads());
        Assert.AreEqual(ledger, sim2.Ledger().Roads, 1e-4f);
    }
}
