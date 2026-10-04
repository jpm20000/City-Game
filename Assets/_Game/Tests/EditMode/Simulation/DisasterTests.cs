using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// M17a: the seeded RNG, the Disasters & events switch, save v6 (and V5ToV6) and the pieces later steps read
// (HazardMultiplier effects, plague risk per age, ServiceSource cost / tech age). The hazards themselves
// arrive in 17b-17d with their own tests.
public sealed class DisasterTests
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

    private AgeDatabase Ages => m_TestAges.Ages;
    private TechDatabase Techs => m_TestAges.Techs;

    private SimulationSystem NewSim(GridData grid, bool withAges = true)
    {
        return withAges
            ? new SimulationSystem(grid, new RoadNetwork(grid), m_Config, Ages, Techs)
            : new SimulationSystem(grid, new RoadNetwork(grid), m_Config);
    }

    // --- SimRandom ---

    [Test]
    public void SimRandom_SameSeedSameStream_DifferentSeedsDiffer()
    {
        var a = new SimRandom(7);
        var b = new SimRandom(7);
        var c = new SimRandom(8);
        bool differs = false;
        for (int i = 0; i < 1000; i++)
        {
            ulong x = a.NextULong();
            Assert.AreEqual(x, b.NextULong());
            if (x != c.NextULong()) differs = true;
        }
        Assert.IsTrue(differs);
    }

    [Test]
    public void SimRandom_SmallSeedsStillGiveWellSpreadValues()
    {
        // Seeds 0, 1, 2 ... must not start with a near-zero state (xorshift would then print a run of tiny numbers).
        for (ulong seed = 0; seed < 4; seed++)
        {
            var random = new SimRandom(seed);
            float sum = 0f;
            for (int i = 0; i < 2000; i++)
            {
                float value = random.NextFloat();
                Assert.IsTrue(value >= 0f && value < 1f);
                sum += value;
            }
            Assert.AreEqual(0.5f, sum / 2000f, 0.05f, $"mean of seed {seed}");
        }
    }

    [Test]
    public void SimRandom_RestoreMidStream_ContinuesTheStream()
    {
        var original = new SimRandom(42);
        for (int i = 0; i < 123; i++) original.NextFloat();
        string state = original.StateString;
        Assert.AreEqual(16, state.Length);

        var restored = new SimRandom(1);
        Assert.IsTrue(restored.TryRestore(state));
        for (int i = 0; i < 500; i++) Assert.AreEqual(original.NextULong(), restored.NextULong());
    }

    [Test]
    public void SimRandom_RejectsUnreadableOrZeroState()
    {
        var random = new SimRandom(3);
        string before = random.StateString;
        Assert.IsFalse(random.TryRestore(""));
        Assert.IsFalse(random.TryRestore(null));
        Assert.IsFalse(random.TryRestore("not hex"));
        Assert.IsFalse(random.TryRestore("0000000000000000"));
        Assert.AreEqual(before, random.StateString, "a rejected state changes nothing");
    }

    [Test]
    public void SimRandom_BoundedDrawsStayInRange_AndAlwaysDrawOnce()
    {
        var a = new SimRandom(5);
        var b = new SimRandom(5);
        for (int i = 0; i < 500; i++)
        {
            int value = a.NextInt(7);
            Assert.IsTrue(value >= 0 && value < 7);
            b.NextULong();
        }
        Assert.AreEqual(a.StateString, b.StateString);

        // max <= 0 and Chance(0 / 1) still draw once, so the draw count never depends on the arguments.
        a.NextInt(0);
        Assert.AreEqual(0, new SimRandom(9).NextInt(0));
        a.Chance(0f);
        a.Chance(1f);
        for (int i = 0; i < 3; i++) b.NextULong();
        Assert.AreEqual(b.StateString, a.StateString);
        Assert.IsFalse(new SimRandom(9).Chance(0f));
        Assert.IsTrue(new SimRandom(9).Chance(1.01f));
    }

    // --- The switch ---

    [Test]
    public void Enabled_IsNeverOnWithoutAges()
    {
        var grid = new GridData(8, 8);
        SimulationSystem ageless = NewSim(grid, withAges: false);
        ageless.Disasters.Enabled = true;
        Assert.IsFalse(ageless.Disasters.Enabled);

        SimulationSystem aged = NewSim(new GridData(8, 8));
        Assert.IsFalse(aged.Disasters.Enabled, "off by default");
        aged.Disasters.Enabled = true;
        Assert.IsTrue(aged.Disasters.Enabled);
    }

    [Test]
    public void SwitchOff_DrawsNothingAndThePlayIsUnchanged()
    {
        var gridA = new GridData(24, 24);
        var gridB = new GridData(24, 24);
        SimulationSystem a = SeededCity.Build(gridA, m_Config, ages: Ages, techs: Techs, startAge: TestAges.Industrial);
        SimulationSystem b = SeededCity.Build(gridB, m_Config, ages: Ages, techs: Techs, startAge: TestAges.Industrial);
        b.Disasters.Enabled = false;
        string state = a.Disasters.Random.StateString;

        for (int day = 0; day < 60; day++)
        {
            a.Tick();
            b.Tick();
        }

        Assert.AreEqual(a.Population.Population, b.Population.Population);
        Assert.AreEqual(a.Economy.Money, b.Economy.Money);
        Assert.AreEqual(state, a.Disasters.Random.StateString, "the RNG is untouched while the switch is off");
    }

    // --- Save v6 ---

    private static SaveData V5Save(int age = SaveData.NoAge)
    {
        return new SaveData
        {
            Version = 5,
            Width = 4,
            Height = 4,
            Age = age,
            Zones = new byte[16],
            Roads = new byte[16],
            Levels = new byte[16],
            BuiltAges = new byte[16],
            Historic = new byte[16],
            Pipes = new byte[16],
        };
    }

    [Test]
    public void V5ToV6_StartsWithDisastersOffAndNothingUnderWay()
    {
        SaveData data = V5Save(TestAges.Industrial);
        Assert.IsTrue(SaveMigrations.TryMigrate(data, Ages, Techs, out string error), error);

        Assert.AreEqual(6, data.Version);
        Assert.AreEqual(SaveData.CurrentVersion, data.Version);
        Assert.IsFalse(data.Disasters);
        Assert.AreEqual(16, data.Fires.Length);
        Assert.AreEqual(16, data.Rubble.Length);
        Assert.AreEqual(16, data.Plague.Length);
        Assert.IsTrue(System.Array.TrueForAll(data.Fires, b => b == 0));
        Assert.AreEqual(0, data.Broken.Count);
        Assert.AreEqual("", data.PendingEvent);
        Assert.AreEqual(0, data.ActiveEvents.Count);
        Assert.AreEqual(0, data.RecentEvents.Count);
    }

    [Test]
    public void V5Save_LoadsIntoACityWithTheSwitchOff()
    {
        SaveData data = V5Save(TestAges.Industrial);
        Assert.IsTrue(SaveSystem.TryFromJson(SaveSystem.ToJson(data), out SaveData loaded, out string error, Ages, Techs), error);

        var grid = new GridData(4, 4);
        SimulationSystem sim = NewSim(grid);
        sim.Disasters.Enabled = true;   // a previous city's switch must not leak into the loaded one
        SaveSystem.ApplyGrid(loaded, grid);
        SaveSystem.ApplySimulation(loaded, sim);
        Assert.IsFalse(sim.Disasters.Enabled);
    }

    [Test]
    public void V1Fixture_MigratesToV6WithDisastersOff()
    {
        string path = System.IO.Path.Combine(Application.dataPath, "_Game/Tests/EditMode/Simulation/Fixtures/city_v1_24x24.json");
        Assert.IsTrue(SaveSystem.TryFromJson(System.IO.File.ReadAllText(path), out SaveData data, out string error, Ages, Techs), error);
        Assert.AreEqual(SaveData.CurrentVersion, data.Version);
        Assert.IsFalse(data.Disasters);
        Assert.AreEqual(24 * 24, data.Fires.Length);
    }

    [Test]
    public void TryFromJson_RejectsAMismatchedHazardLayer()
    {
        SaveData data = SaveSystem.CreateNew(4, 4, m_Config, Ages, Techs);
        data.Rubble = new byte[3];
        Assert.IsFalse(SaveSystem.TryFromJson(SaveSystem.ToJson(data), out _, out string error, Ages, Techs));
        StringAssert.Contains("mismatched", error);
    }

    [Test]
    public void CreateNew_CarriesTheSwitchAndSeed_OnlyWithAges()
    {
        SaveData on = SaveSystem.CreateNew(8, 8, m_Config, Ages, Techs, TestAges.Medieval, disasters: true, seed: 5);
        Assert.IsTrue(on.Disasters);
        Assert.AreEqual(new SimRandom(5).StateString, on.RandomState);
        Assert.AreEqual(64, on.Fires.Length);

        SaveData off = SaveSystem.CreateNew(8, 8, m_Config, Ages, Techs, TestAges.Medieval);
        Assert.IsFalse(off.Disasters);

        SaveData ageless = SaveSystem.CreateNew(8, 8, m_Config, disasters: true);
        Assert.IsFalse(ageless.Disasters, "no hazards without age data");
    }

    [Test]
    public void SaveLoad_KeepsEveryHazardField()
    {
        // The pending and running events must exist in the game's databases to survive a load.
        EventDefinition fair = TwoChoiceEvent("fair");
        EventDefinition harvest = TwoChoiceEvent("harvest");
        Techs.InitEvents(fair, harvest);
        var grid = new GridData(6, 6);
        SimulationSystem sim = NewSim(grid);
        DisasterSystem d = sim.Disasters;
        d.Enabled = true;
        d.Random.Seed(99);
        d.Random.NextFloat();
        d.Fires[7] = 2;
        d.Rubble[8] = 9;
        d.Plague[9] = 4;
        d.Plague[10] = DisasterSystem.PlagueRecovered;
        d.PlagueCooldown = 120;
        d.PlagueRemainder = 0.25f;
        d.Broken.Add(new BrokenRecord(1, 2, 6));
        var plant = new ServiceSource(new Vector2Int(1, 2), Vector2Int.one, 0, 600);
        sim.Sources = new[] { plant };
        d.PendingEvent = "fair";
        d.PendingDays = 3;
        d.DaysToNextEvent = 40;
        d.ActiveEvents.Add(new EventRecord("harvest", 1, 12));
        d.RecentEvents.Add(new EventRecord("fair", -1, 300));
        string state = d.Random.StateString;

        string json = SaveSystem.ToJson(SaveSystem.Capture(grid, sim));
        Assert.IsTrue(SaveSystem.TryFromJson(json, out SaveData loaded, out string error, Ages, Techs), error);

        var grid2 = new GridData(6, 6);
        SimulationSystem sim2 = NewSim(grid2);
        SaveSystem.ApplyGrid(loaded, grid2);
        sim2.Sources = new[] { plant };     // a broken source's record needs the source to still stand
        SaveSystem.ApplySimulation(loaded, sim2);
        DisasterSystem r = sim2.Disasters;

        Assert.IsTrue(r.Enabled);
        Assert.AreEqual(state, r.Random.StateString);
        Assert.AreEqual(d.Random.NextULong(), r.Random.NextULong());
        CollectionAssert.AreEqual(d.Fires, r.Fires);
        CollectionAssert.AreEqual(d.Rubble, r.Rubble);
        CollectionAssert.AreEqual(d.Plague, r.Plague);
        Assert.AreEqual(120, r.PlagueCooldown);
        Assert.AreEqual(0.25f, r.PlagueRemainder, 1e-6f);
        Assert.AreEqual(1, r.Broken.Count);
        Assert.AreEqual(6, r.Broken[0].DaysLeft);
        Assert.AreEqual("fair", r.PendingEvent);
        Assert.AreEqual(3, r.PendingDays);
        Assert.AreEqual(40, r.DaysToNextEvent);
        Assert.AreEqual("harvest", r.ActiveEvents[0].Id);
        Assert.AreEqual(1, r.ActiveEvents[0].Choice);
        Assert.AreEqual(300, r.RecentEvents[0].DaysLeft);
    }

    [Test]
    public void Restore_DropsBrokenEntriesOffTheMap_AndSeedsABadRngState()
    {
        SaveData data = SaveSystem.CreateNew(4, 4, m_Config, Ages, Techs, TestAges.Industrial, disasters: true);
        data.Broken.Add(new BrokenRecord(9, 9, 5));
        data.Broken.Add(new BrokenRecord(1, 1, 0));
        data.Broken.Add(new BrokenRecord(2, 2, 5));
        data.RandomState = "garbage";
        data.PlagueRemainder = 7f;

        var grid = new GridData(4, 4);
        SimulationSystem sim = NewSim(grid);
        SaveSystem.ApplyGrid(data, grid);
        sim.Sources = new[] { new ServiceSource(new Vector2Int(2, 2), Vector2Int.one, 0, 600) };
        SaveSystem.ApplySimulation(data, sim);

        Assert.AreEqual(1, sim.Disasters.Broken.Count);
        Assert.AreEqual(2, sim.Disasters.Broken[0].X);
        Assert.AreEqual(new SimRandom(1).StateString, sim.Disasters.Random.StateString);
        Assert.AreEqual(0f, sim.Disasters.PlagueRemainder);
    }

    [Test]
    public void Layers_AreReallocatedEmptyOnResize_AndKeepTheSwitchAndRng()
    {
        var grid = new GridData(6, 6);
        SimulationSystem sim = NewSim(grid);
        sim.Disasters.Enabled = true;
        sim.Disasters.Fires[3] = 1;
        sim.Disasters.Rubble[4] = 5;
        sim.Disasters.Plague[5] = 2;
        sim.Disasters.Broken.Add(new BrokenRecord(1, 1, 3));
        string state = sim.Disasters.Random.StateString;

        grid.Resize(10, 8);

        Assert.AreEqual(80, sim.Disasters.Fires.Length);
        Assert.AreEqual(80, sim.Disasters.Rubble.Length);
        Assert.AreEqual(80, sim.Disasters.Plague.Length);
        Assert.IsTrue(System.Array.TrueForAll(sim.Disasters.Fires, b => b == 0));
        Assert.IsTrue(System.Array.TrueForAll(sim.Disasters.Rubble, b => b == 0));
        Assert.AreEqual(0, sim.Disasters.Broken.Count);
        Assert.IsTrue(sim.Disasters.Enabled);
        Assert.AreEqual(state, sim.Disasters.Random.StateString);
    }

    [Test]
    public void SaveLoadContinue_WithTheSwitchOn_EqualsAnUninterruptedRun()
    {
        var grid = new GridData(24, 24);
        SimulationSystem sim = SeededCity.Build(grid, m_Config, plantSupply: 0, waterSupply: 0,
            ages: Ages, techs: Techs, startAge: TestAges.Industrial);
        sim.Disasters.Enabled = true;
        sim.Disasters.Random.Seed(12);
        for (int day = 0; day < 30; day++) sim.Tick();

        string json = SaveSystem.ToJson(SaveSystem.Capture(grid, sim));
        Assert.IsTrue(SaveSystem.TryFromJson(json, out SaveData loaded, out string error, Ages, Techs), error);
        var grid2 = new GridData(24, 24);
        var sim2 = NewSim(grid2);
        SaveSystem.ApplyGrid(loaded, grid2);
        SaveSystem.ApplySimulation(loaded, sim2);

        for (int day = 0; day < 30; day++)
        {
            sim.Tick();
            sim2.Tick();
        }

        Assert.AreEqual(sim.Population.Population, sim2.Population.Population);
        Assert.AreEqual(sim.Economy.Money, sim2.Economy.Money, 1e-2f);
        CollectionAssert.AreEqual(grid.ExportLevels(), grid2.ExportLevels());
        Assert.AreEqual(sim.Disasters.Random.StateString, sim2.Disasters.Random.StateString);
    }

    private EventDefinition TwoChoiceEvent(string id)
    {
        EventDefinition definition = ScriptableObject.CreateInstance<EventDefinition>();
        var choice = new EventChoice { Label = "x", Description = "x", Effects = new TechEffect[0] };
        definition.Init(id, 0, 3, 0, null, 1f, choice, choice);
        m_Events.Add(definition);
        return definition;
    }

    private readonly List<Object> m_Events = new();

    [OneTimeTearDown]
    public void DestroyEvents()
    {
        foreach (Object o in m_Events) Object.DestroyImmediate(o);
    }

    // --- What later steps read ---

    [Test]
    public void HazardMultiplier_FoldsByTargetAndIgnoresUnknownOnes()
    {
        TechDefinition steel = Tech("steel", new TechEffect(TechEffectType.HazardMultiplier, "FireSpread", 0.5f));
        TechDefinition grid = Tech("grid", new TechEffect(TechEffectType.HazardMultiplier, "breakdown", 0.25f),
            new TechEffect(TechEffectType.HazardMultiplier, "Earthquake", 0.1f));
        TechDefinition both = Tech("more", new TechEffect(TechEffectType.HazardMultiplier, "FireSpread", 0.8f));

        TechModifiers folded = TechModifiers.Fold(new[] { steel, grid, both });
        Assert.AreEqual(0.4f, folded.Hazard(HazardKind.FireSpread), 1e-6f);
        Assert.AreEqual(0.25f, folded.Hazard(HazardKind.Breakdown), 1e-6f);
        Assert.AreEqual(1f, folded.Hazard(HazardKind.PlagueSpread));
        Assert.AreEqual(1f, TechModifiers.None.Hazard(HazardKind.FireSpread));
    }

    [Test]
    public void AgeDefinition_PlagueRisk_DefaultsToNone_AndTheTestAgesMirrorTheContent()
    {
        AgeDefinition age = ScriptableObject.CreateInstance<AgeDefinition>();
        age.Init("test", 750, plagueRisk: 0.6f);
        Assert.AreEqual(0.6f, age.PlagueRisk);
        Object.DestroyImmediate(age);
        Assert.AreEqual(1f, Ages[TestAges.Medieval].PlagueRisk);
        Assert.AreEqual(0.6f, Ages[TestAges.Renaissance].PlagueRisk);
        Assert.AreEqual(0f, Ages[TestAges.Industrial].PlagueRisk);
        Assert.AreEqual(0f, Ages[TestAges.Modern].PlagueRisk);
    }

    [Test]
    public void ServiceSource_CarriesCostAndTechAge_WithDefaultsForOlderCallers()
    {
        var plain = new ServiceSource(Vector2Int.zero, Vector2Int.one, 0, 0);
        Assert.AreEqual(0f, plain.Cost);
        Assert.AreEqual(-1, plain.TechAge);

        var plant = new ServiceSource(Vector2Int.zero, Vector2Int.one, 0, 600, cost: 10000f, techAge: 2);
        Assert.AreEqual(10000f, plant.Cost);
        Assert.AreEqual(2, plant.TechAge);
    }

    private TechDefinition Tech(string id, params TechEffect[] effects)
    {
        TechDefinition tech = ScriptableObject.CreateInstance<TechDefinition>();
        tech.Init(id, 0, 1f, null, effects);
        m_Created.Add(tech);
        return tech;
    }

    private readonly List<Object> m_Created = new();

    [OneTimeTearDown]
    public void DestroyTechs()
    {
        foreach (Object o in m_Created) Object.DestroyImmediate(o);
    }
}
