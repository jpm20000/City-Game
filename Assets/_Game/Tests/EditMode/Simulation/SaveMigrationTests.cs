using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

// M11c: save v2 (ages, research, built ages, historic flags) and the v1 -> v2 migration.
public sealed class SaveMigrationTests
{
    private const string FixturePath = "_Game/Tests/EditMode/Simulation/Fixtures/city_v1_24x24.json";

    // The fixture's one placed building (a real save from the M8-M10 game).
    private static readonly ServiceSource s_FixturePark = new ServiceSource(new Vector2Int(14, 14), new Vector2Int(2, 2), 4, 0);

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

    private AgeDatabase Ages => m_TestAges.Ages;
    private TechDatabase Techs => m_TestAges.Techs;

    private static string FixtureJson() => File.ReadAllText(Path.Combine(Application.dataPath, FixturePath));

    private static void Run(SimulationSystem sim, int days)
    {
        for (int day = 0; day < days; day++) sim.Tick();
    }

    private static List<string> Ids(IEnumerable<TechDefinition> techs)
    {
        var ids = new List<string>();
        foreach (TechDefinition tech in techs) ids.Add(tech.Id);
        return ids;
    }

    private static List<string> Ids(IEnumerable<ResearchProject> projects)
    {
        var ids = new List<string>();
        foreach (ResearchProject project in projects) ids.Add(project.Id);
        return ids;
    }

    // The steps SaveGameController takes: grid, re-placed buildings, then the sim.
    private SimulationSystem Load(SaveData data, bool withAges, out GridData grid, out int dropped,
        ServiceSource[] sources = null, CityModifiers modifiers = default)
    {
        grid = new GridData(data.Width, data.Height);
        var sim = withAges
            ? new SimulationSystem(grid, new RoadNetwork(grid), m_Config, Ages, Techs)
            : new SimulationSystem(grid, new RoadNetwork(grid), m_Config);
        SaveSystem.ApplyGrid(data, grid);
        if (sources != null)
        {
            foreach (ServiceSource source in sources)
            {
                Assert.IsTrue(grid.Occupy(source.Origin, source.Size, 0, 100));
            }
            sim.Sources = sources;
        }
        sim.Modifiers = modifiers;
        dropped = SaveSystem.ApplySimulation(data, sim);
        return sim;
    }

    private SaveData Read(string json, bool withAges)
    {
        Assert.IsTrue(withAges
            ? SaveSystem.TryFromJson(json, out SaveData data, out string error, Ages, Techs)
            : SaveSystem.TryFromJson(json, out data, out error), error);
        return data;
    }

    private static void AssertSameCity(GridData expectedGrid, SimulationSystem expected, GridData actualGrid, SimulationSystem actual)
    {
        CollectionAssert.AreEqual(expectedGrid.ExportZones(), actualGrid.ExportZones(), "zones");
        CollectionAssert.AreEqual(expectedGrid.ExportLevels(), actualGrid.ExportLevels(), "levels");
        Assert.AreEqual(expected.Population.Population, actual.Population.Population, "population");
        Assert.AreEqual(expected.Population.AverageHappiness, actual.Population.AverageHappiness, "happiness");
        Assert.AreEqual(expected.Economy.Money, actual.Economy.Money, "money");
    }

    // --- v1 -> v2 ---

    [Test]
    public void V1Fixture_MigratesToAnIndustrialCity()
    {
        SaveData data = Read(FixtureJson(), withAges: true);

        Assert.AreEqual(SaveData.CurrentVersion, data.Version);
        Assert.AreEqual(TestAges.Industrial, data.Age);
        Assert.AreEqual(1760, data.Year, "v1 year 1 is the Industrial age's first year");
        Assert.AreEqual(2, data.Month);
        CollectionAssert.AreEqual(new[] { "commons", "masonry", "monasticism", "printing", "architecture", "electricity" },
            data.Researched);
        Assert.AreEqual("", data.ActiveResearch);
        Assert.AreEqual(0, data.ResearchQueue.Count);

        int grown = 0;
        for (int i = 0; i < data.Levels.Length; i++)
        {
            Assert.AreEqual(data.Levels[i] > 0 ? TestAges.Industrial : 0, data.BuiltAges[i], $"cell {i}");
            Assert.AreEqual(0, data.Historic[i]);
            if (data.Levels[i] > 0) grown++;
        }
        Assert.AreEqual(40, grown);
        Assert.AreEqual(24 * 24, data.Pipes.Length, "v2 -> v3 adds an empty pipe layer");
        Assert.IsTrue(System.Array.TrueForAll(data.Pipes, p => p == 0));
        Assert.AreEqual(1, data.Buildings.Count);
        Assert.AreEqual("park", data.Buildings[0].Id);
    }

    [Test]
    public void V1Fixture_WithoutAges_StaysAgeless()
    {
        SaveData data = Read(FixtureJson(), withAges: false);

        Assert.AreEqual(SaveData.CurrentVersion, data.Version);
        Assert.AreEqual(SaveData.NoAge, data.Age);
        Assert.AreEqual(1, data.Year);
        Assert.AreEqual(0, data.Researched.Count);
        Assert.AreEqual(24 * 24, data.BuiltAges.Length);
        Assert.AreEqual(24 * 24, data.Historic.Length);
    }

    [Test]
    public void V1Fixture_Migrated_RunsIdenticallyToTheGameItCameFrom()
    {
        var parkUpkeep = new CityModifiers { UpkeepPerDay = 5f };
        string json = FixtureJson();
        SimulationSystem before = Load(Read(json, false), false, out GridData beforeGrid, out _, new[] { s_FixturePark }, parkUpkeep);
        SimulationSystem after = Load(Read(json, true), true, out GridData afterGrid, out int dropped, new[] { s_FixturePark }, parkUpkeep);

        Assert.AreEqual(0, dropped);
        Assert.AreEqual(TestAges.Industrial, after.Tech.CurrentAge);
        Assert.IsTrue(after.Tech.IsResearched(m_TestAges["electricity"]));
        Assert.AreEqual(92, after.Population.Population);
        AssertSameCity(beforeGrid, before, afterGrid, after);

        Run(before, 90);
        Run(after, 90);

        AssertSameCity(beforeGrid, before, afterGrid, after);
        Assert.Greater(after.Population.Population, 92);
        for (int y = 0; y < 24; y++)
        {
            for (int x = 0; x < 24; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (afterGrid.GetBuildingLevel(cell) > 0) Assert.AreEqual(TestAges.Industrial, afterGrid.GetBuiltAge(cell), cell.ToString());
            }
        }
    }

    [Test]
    public void V2SaveWithoutAges_LoadedWithAges_BecomesIndustrial()
    {
        var grid = new GridData(24, 24);
        SimulationSystem sim = SeededCity.Run(grid, m_Config, 20);
        SaveData saved = SaveSystem.Capture(grid, sim);
        Assert.AreEqual(SaveData.NoAge, saved.Age);

        SaveData data = Read(SaveSystem.ToJson(saved), withAges: true);

        Assert.AreEqual(TestAges.Industrial, data.Age);
        Assert.AreEqual(1760, data.Year);
        Assert.Contains("electricity", data.Researched);
        for (int i = 0; i < data.Levels.Length; i++)
        {
            Assert.AreEqual(data.Levels[i] > 0 ? TestAges.Industrial : 0, data.BuiltAges[i]);
        }
    }

    // --- v2 -> v3 (M13) ---

    [Test]
    public void V2Save_MigratesToV3_WithNoPipes()
    {
        var grid = new GridData(24, 24);
        SimulationSystem sim = SeededCity.Run(grid, m_Config, 20);
        SaveData saved = SaveSystem.Capture(grid, sim);
        saved.Version = 2;
        saved.Pipes = null;

        SaveData data = Read(SaveSystem.ToJson(saved), withAges: false);

        Assert.AreEqual(SaveData.CurrentVersion, data.Version);
        Assert.AreEqual(24 * 24, data.Pipes.Length);
        Assert.IsTrue(System.Array.TrueForAll(data.Pipes, p => p == 0));
        CollectionAssert.AreEqual(saved.Levels, data.Levels, "nothing else changes");
    }

    // --- v2 round trip ---

    [Test]
    public void V2RoundTrip_MidResearchAndMidRedevelopment_MatchesUninterruptedRun()
    {
        var gridA = new GridData(24, 24);
        SimulationSystem a = SeededCity.Build(gridA, m_Config, plantSupply: 0, ages: Ages, techs: Techs,
            startAge: TestAges.Medieval);
        Assert.IsTrue(a.Tech.Enqueue(m_TestAges["commons"]));
        Assert.IsTrue(a.Tech.Enqueue(m_TestAges["masonry"]));
        Assert.IsTrue(a.Tech.Enqueue(m_TestAges["monasticism"]));
        Run(a, 40);

        TestAges.Advance(a);
        Run(a, 2);
        Assert.IsTrue(a.Tech.Enqueue(m_TestAges["printing"]));

        // Keep the last outdated cell (row-major, so redevelopment would reach it last).
        Vector2Int kept = new Vector2Int(-1, -1);
        int outdated = 0;
        for (int y = 0; y < 24; y++)
        {
            for (int x = 0; x < 24; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (!a.Growth.IsOutdated(cell)) continue;
                outdated++;
                kept = cell;
            }
        }
        gridA.SetHistoric(kept, true);

        Assert.AreEqual(TestAges.Renaissance, a.Tech.CurrentAge);
        Assert.IsNotNull(a.Tech.Active, "mid-research");
        Assert.Greater(a.Tech.Progress, 0f, "mid-research");
        Assert.Greater(a.Tech.Queue.Count, 0);
        Assert.Greater(outdated, m_Config.RedevelopPerDay, "mid-redevelopment");

        SaveData saved = SaveSystem.Capture(gridA, a);
        ServiceSource[] buildings = new List<ServiceSource>(a.Sources).ToArray();   // the seeded water source
        SimulationSystem b = Load(Read(SaveSystem.ToJson(saved), true), true, out GridData gridB, out int dropped, buildings);

        Assert.AreEqual(0, dropped);
        Assert.AreEqual(a.Tech.CurrentAge, b.Tech.CurrentAge);
        Assert.AreEqual(a.Tech.Active.Id, b.Tech.Active.Id);
        Assert.AreEqual(a.Tech.Progress, b.Tech.Progress);
        CollectionAssert.AreEqual(Ids(a.Tech.Queue), Ids(b.Tech.Queue));
        CollectionAssert.AreEqual(Ids(a.Tech.Researched), Ids(b.Tech.Researched));
        CollectionAssert.AreEqual(gridA.ExportBuiltAges(), gridB.ExportBuiltAges());
        Assert.IsTrue(gridB.IsHistoric(kept));

        Run(a, 30);
        Run(b, 30);

        AssertSameCity(gridA, a, gridB, b);
        CollectionAssert.AreEqual(gridA.ExportBuiltAges(), gridB.ExportBuiltAges(), "built ages");
        CollectionAssert.AreEqual(gridA.ExportHistoric(), gridB.ExportHistoric(), "historic");
        Assert.AreEqual(TestAges.Medieval, gridB.GetBuiltAge(kept), "kept cells are never rebuilt");
        Assert.AreEqual(a.Tech.Progress, b.Tech.Progress);
        CollectionAssert.AreEqual(Ids(a.Tech.Researched), Ids(b.Tech.Researched));
        Assert.AreEqual(a.Tech.Active?.Id, b.Tech.Active?.Id);
    }

    // --- New city / bad data ---

    [Test]
    public void CreateNew_WithAges_StartsInTheChosenAge()
    {
        SaveData renaissance = SaveSystem.CreateNew(16, 16, m_Config, Ages, Techs, TestAges.Renaissance);
        Assert.AreEqual(TestAges.Renaissance, renaissance.Age);
        Assert.AreEqual(1450, renaissance.Year);
        Assert.AreEqual(Ages[TestAges.Renaissance].StartingMoney, renaissance.Money);
        CollectionAssert.AreEqual(new[] { "commons", "masonry", "monasticism" }, renaissance.Researched);

        SimulationSystem sim = Load(Read(SaveSystem.ToJson(renaissance), true), true, out _, out int dropped);
        Assert.AreEqual(0, dropped);
        Assert.AreEqual(TestAges.Renaissance, sim.Tech.CurrentAge);
        Assert.IsTrue(sim.Tech.IsResearched(m_TestAges["monasticism"]));
        Assert.IsFalse(sim.Tech.IsResearched(m_TestAges["printing"]));

        SaveData standard = SaveSystem.CreateNew(16, 16, m_Config, Ages, Techs);
        Assert.AreEqual(TestAges.Industrial, standard.Age);
        Assert.AreEqual(1760, standard.Year);
        Assert.Contains("electricity", standard.Researched);

        SaveData ageless = SaveSystem.CreateNew(16, 16, m_Config);
        Assert.AreEqual(SaveData.NoAge, ageless.Age);
        Assert.AreEqual(1, ageless.Year);
        Assert.AreEqual(m_Config.StartingMoney, ageless.Money);
    }

    [Test]
    public void UnknownTechIds_AreDropped_NotAFailure()
    {
        SaveData data = SaveSystem.CreateNew(8, 8, m_Config, Ages, Techs, TestAges.Medieval);
        data.Researched.Add("gone");
        data.ActiveResearch = "vanished";
        data.ResearchQueue.Add("commons");

        SimulationSystem sim = Load(Read(SaveSystem.ToJson(data), true), true, out _, out int dropped);

        Assert.AreEqual(2, dropped);
        Assert.AreEqual(0, sim.Tech.ResearchedCount);
        Assert.AreEqual("commons", sim.Tech.Active.Id);
    }

    [Test]
    public void TryFromJson_RejectsUnsupportedVersionsAndUnknownAges()
    {
        SaveData ancient = SaveSystem.CreateNew(4, 4, m_Config);
        ancient.Version = 0;
        Assert.IsFalse(SaveSystem.TryFromJson(SaveSystem.ToJson(ancient), out _, out _));

        SaveData future = SaveSystem.CreateNew(4, 4, m_Config);
        future.Version = SaveData.CurrentVersion + 1;
        Assert.IsFalse(SaveSystem.TryFromJson(SaveSystem.ToJson(future), out _, out _, Ages, Techs));

        SaveData unknownAge = SaveSystem.CreateNew(4, 4, m_Config, Ages, Techs);
        unknownAge.Age = 9;
        Assert.IsFalse(SaveSystem.TryFromJson(SaveSystem.ToJson(unknownAge), out _, out string error, Ages, Techs));
        StringAssert.Contains("age", error);

        SaveData missingAges = SaveSystem.CreateNew(4, 4, m_Config);
        missingAges.BuiltAges = new byte[3];
        Assert.IsFalse(SaveSystem.TryFromJson(SaveSystem.ToJson(missingAges), out _, out _));
    }

    [Test]
    public void TryFromJson_ClampsBuiltAgesToTheCitysAge()
    {
        SaveData data = SaveSystem.CreateNew(4, 4, m_Config, Ages, Techs, TestAges.Renaissance);
        data.Levels[5] = 1;
        data.BuiltAges[5] = TestAges.Modern;

        SaveData loaded = Read(SaveSystem.ToJson(data), true);

        Assert.AreEqual(TestAges.Renaissance, loaded.BuiltAges[5]);
    }

    // --- v3 -> v4 (M15) ---

    [Test]
    public void V3Save_MigratesToV4_WithFullFundingAndNoLoans()
    {
        var grid = new GridData(24, 24);
        SimulationSystem sim = SeededCity.Run(grid, m_Config, 20);
        SaveData saved = SaveSystem.Capture(grid, sim);
        saved.Version = 3;
        saved.Funding = null;
        saved.Loans = null;
        saved.Ordinances = null;

        SaveData data = Read(SaveSystem.ToJson(saved), withAges: false);

        Assert.AreEqual(SaveData.CurrentVersion, data.Version);
        Assert.AreEqual(BudgetSystem.Lines, data.Funding.Length);
        Assert.IsTrue(System.Array.TrueForAll(data.Funding, f => f == 1f));
        Assert.AreEqual(0, data.Loans.Count);
        Assert.AreEqual(0, data.Ordinances.Count);
        CollectionAssert.AreEqual(saved.Levels, data.Levels, "nothing else changes");
    }
}
