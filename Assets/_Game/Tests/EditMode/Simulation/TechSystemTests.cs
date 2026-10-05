using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class TechSystemTests
{
    private readonly List<Object> m_Created = new();
    private BalanceConfig m_Config;
    private AgeDatabase m_Ages;
    private TechDatabase m_Techs;

    // Age 0: MedA, MedB (needs MedA), MedC. Age 1: RenA (needs MedB). Age 2: Elec, Steam (needs RenA).
    private TechDefinition m_MedA, m_MedB, m_MedC, m_RenA, m_Elec, m_Steam;
    private AgeDefinition m_Medieval, m_Renaissance, m_Industrial;

    [SetUp]
    public void SetUp()
    {
        m_Config = Make<BalanceConfig>();
        GoodsOff.Apply(m_Config);   // compares with the age-less sim (M24d)
        m_MedA = Tech("med_a", 0, 100f);
        m_MedB = Tech("med_b", 0, 100f, m_MedA);
        m_MedC = Tech("med_c", 0, 50f);
        m_RenA = Tech("ren_a", 1, 100f, m_MedB);
        m_Elec = Tech("elec", 2, 50f);
        m_Steam = Tech("steam", 2, 100f, m_RenA);

        m_Medieval = Make<AgeDefinition>();
        m_Medieval.Init("medieval", 750, maxLevel: 2, capacityScale: 0.5f, upgradesNeedPower: false);
        m_Renaissance = Make<AgeDefinition>();
        m_Renaissance.Init("renaissance", 1450, maxLevel: 3, capacityScale: 0.75f, upgradesNeedPower: false,
            techsToAdvance: 2, populationToEnter: 100, advanceCost: 50f);
        m_Industrial = Make<AgeDefinition>();
        m_Industrial.Init("industrial", 1760, techsToAdvance: 1, populationToEnter: 200, advanceCost: 100f,
            startingTechs: new[] { m_Elec });

        m_Ages = Make<AgeDatabase>();
        m_Ages.Init(m_Medieval, m_Renaissance, m_Industrial);
        m_Techs = Make<TechDatabase>();
        m_Techs.Init(m_MedA, m_MedB, m_MedC, m_RenA, m_Elec, m_Steam);
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

    private TechDefinition Tech(string id, int age, float cost, params TechDefinition[] prerequisites)
    {
        TechDefinition tech = Make<TechDefinition>();
        tech.Init(id, age, cost, prerequisites);
        return tech;
    }

    private TechSystem MedievalStart()
    {
        var tech = new TechSystem(m_Ages, m_Techs, m_Config);
        tech.StartNew(0);
        return tech;
    }

    private List<string> Errors()
    {
        var errors = new List<string>();
        m_Ages.Validate(errors);
        m_Techs.Validate(m_Ages, errors);
        return errors;
    }

    // --- Validation ---

    [Test]
    public void Validate_TestTree_HasNoErrors()
    {
        CollectionAssert.IsEmpty(Errors());
    }

    [Test]
    public void Validate_Cycle_IsReported()
    {
        TechDefinition x = Tech("x", 0, 10f);
        TechDefinition y = Tech("y", 0, 10f, x);
        x.Init("x", 0, 10f, new[] { y });
        m_Techs.Init(m_MedA, m_MedB, m_MedC, m_RenA, m_Elec, m_Steam, x, y);

        Assert.IsTrue(Errors().Exists(e => e.Contains("cycle")));
    }

    [Test]
    public void Validate_PrerequisiteFromLaterAge_IsReported()
    {
        m_MedC.Init("med_c", 0, 50f, new[] { m_RenA });
        Assert.IsTrue(Errors().Exists(e => e.Contains("later age")));
    }

    [Test]
    public void Validate_DuplicateId_IsReported()
    {
        TechDefinition copy = Tech("med_a", 0, 10f);
        m_Techs.Init(m_MedA, m_MedB, m_MedC, m_RenA, m_Elec, m_Steam, copy);
        Assert.IsTrue(Errors().Exists(e => e.Contains("Duplicate tech Id 'med_a'")));
    }

    [Test]
    public void Validate_TooFewTechsToAdvance_IsReported()
    {
        // Renaissance asks for 4 Medieval techs; there are 3.
        m_Renaissance.Init("renaissance", 1450, techsToAdvance: 4, populationToEnter: 100, advanceCost: 50f);
        Assert.IsTrue(Errors().Exists(e => e.Contains("'renaissance' needs 4")));
    }

    [Test]
    public void Validate_AgesOutOfOrder_IsReported()
    {
        m_Ages.Init(m_Renaissance, m_Medieval, m_Industrial);
        var errors = new List<string>();
        Assert.IsFalse(m_Ages.Validate(errors));
    }

    [Test]
    public void DepthInAge_CountsOnlySameAgePrerequisites()
    {
        Assert.AreEqual(0, m_Techs.DepthInAge(m_MedA));
        Assert.AreEqual(1, m_Techs.DepthInAge(m_MedB));     // MedA -> MedB
        Assert.AreEqual(0, m_Techs.DepthInAge(m_RenA));     // its prerequisite MedB is from an earlier age
        Assert.AreEqual(0, m_Techs.DepthInAge(m_Steam));
        TechDefinition deep = Tech("deep", 0, 10f, m_MedB, m_MedC);
        Assert.AreEqual(2, m_Techs.DepthInAge(deep));
    }

    // --- Starting ---

    [Test]
    public void StartNew_GrantsEarlierAgesAndStartingTechs()
    {
        var tech = new TechSystem(m_Ages, m_Techs, m_Config);
        tech.StartNew(2);

        Assert.AreEqual(2, tech.CurrentAge);
        foreach (TechDefinition t in new[] { m_MedA, m_MedB, m_MedC, m_RenA, m_Elec }) Assert.IsTrue(tech.IsResearched(t), t.Id);
        Assert.IsFalse(tech.IsResearched(m_Steam));
        Assert.IsNull(tech.Active);
        Assert.AreEqual(0f, tech.Progress);
    }

    [Test]
    public void NewTechSystem_StartsInTheLegacyAge()
    {
        var tech = new TechSystem(m_Ages, m_Techs, m_Config);
        Assert.AreEqual(m_Ages.Legacy, tech.CurrentAge);
        Assert.IsTrue(tech.IsResearched(m_Elec));
        Assert.IsTrue(tech.Rules.UpgradesNeedPower);
    }

    [Test]
    public void Rules_FollowTheCurrentAge()
    {
        AgeRules rules = MedievalStart().Rules;
        Assert.AreEqual(2, rules.MaxLevel);
        Assert.AreEqual(0.5f, rules.CapacityScale);
        Assert.IsFalse(rules.UpgradesNeedPower);
    }

    // --- CanResearch / queue ---

    [Test]
    public void CanResearch_NeedsPrerequisitesAndAge()
    {
        TechSystem tech = MedievalStart();
        Assert.IsTrue(tech.CanResearch(m_MedA));
        Assert.IsFalse(tech.CanResearch(m_MedB), "prerequisite not researched");
        Assert.IsFalse(tech.CanResearch(m_Elec), "later age");

        tech.Enqueue(m_MedA);
        Assert.IsFalse(tech.CanResearch(m_MedA), "already planned");
        Assert.IsTrue(tech.CanEnqueue(m_MedB), "prerequisite planned ahead");
        Assert.IsFalse(tech.CanResearch(m_MedB));

        tech.Step(100f);
        Assert.IsTrue(tech.IsResearched(m_MedA));
        Assert.IsFalse(tech.CanResearch(m_MedA), "already researched");
        Assert.IsTrue(tech.CanResearch(m_MedB));
    }

    [Test]
    public void Queue_RunsInOrder_AndIsCapped()
    {
        var techs = new List<TechDefinition>();
        for (int i = 0; i < 8; i++) techs.Add(Tech("q" + i, 0, 10f));
        m_Techs.Init(techs.ToArray());
        TechSystem tech = MedievalStart();

        for (int i = 0; i < 6; i++) Assert.IsTrue(tech.Enqueue(techs[i]), "q" + i);
        Assert.IsFalse(tech.Enqueue(techs[6]), "active + 5 queued is the cap");
        Assert.AreEqual(techs[0], tech.Active.Tech);
        Assert.AreEqual(m_Config.ResearchQueueMax, tech.Queue.Count);

        var completed = new List<TechDefinition>();
        tech.TechCompleted += completed.Add;
        for (int day = 0; day < 6; day++) tech.Step(10f);
        CollectionAssert.AreEqual(techs.GetRange(0, 6), completed);
        Assert.IsNull(tech.Active);
    }

    [Test]
    public void SetActive_PutsReplacedProjectBackAtFront_AndKeepsProgress()
    {
        TechSystem tech = MedievalStart();
        tech.Enqueue(m_MedA);
        tech.Enqueue(m_MedC);
        tech.Step(30f);

        Assert.IsTrue(tech.SetActive(m_MedC));
        Assert.AreEqual(m_MedC, tech.Active.Tech);
        Assert.AreEqual(m_MedA, tech.Queue[0].Tech);
        Assert.AreEqual(30f, tech.Progress);

        tech.Step(20f);     // 50 = MedC's cost
        Assert.IsTrue(tech.IsResearched(m_MedC));
        Assert.AreEqual(m_MedA, tech.Active.Tech);
    }

    [Test]
    public void Remove_DropsQueuedDependents()
    {
        TechSystem tech = MedievalStart();
        tech.Enqueue(m_MedC);
        tech.Enqueue(m_MedA);
        tech.Enqueue(m_MedB);   // needs MedA, planned ahead

        Assert.IsTrue(tech.Remove(m_MedA));
        Assert.AreEqual(m_MedC, tech.Active.Tech);
        Assert.AreEqual(0, tech.Queue.Count);
    }

    // --- RP ---

    [Test]
    public void Progress_CarriesLeftoverIntoNextProject()
    {
        TechSystem tech = MedievalStart();
        tech.Enqueue(m_MedA);
        tech.Enqueue(m_MedC);

        tech.Step(70f);
        tech.Step(70f);     // 140: MedA (100) done, 40 toward MedC
        Assert.IsTrue(tech.IsResearched(m_MedA));
        Assert.AreEqual(m_MedC, tech.Active.Tech);
        Assert.AreEqual(40f, tech.Progress, 1e-4f);

        Assert.AreEqual(1, tech.Step(10f));
        Assert.IsTrue(tech.IsResearched(m_MedC));
        Assert.AreEqual(0f, tech.Progress, 1e-4f);
    }

    [Test]
    public void Bank_WithNothingPlanned_IsCappedAtBankDays()
    {
        TechSystem tech = MedievalStart();
        for (int day = 0; day < 40; day++) tech.Step(10f);
        Assert.AreEqual(m_Config.ResearchBankDays * 10f, tech.Progress, 1e-3f);

        // A day with no income keeps the bank.
        tech.Step(0f);
        Assert.AreEqual(m_Config.ResearchBankDays * 10f, tech.Progress, 1e-3f);

        // The bank pays for projects as soon as they're planned: 300 + 10 covers MedA + MedB + MedC (250).
        tech.Enqueue(m_MedA);
        tech.Enqueue(m_MedB);
        tech.Enqueue(m_MedC);
        Assert.AreEqual(3, tech.Step(10f));
        Assert.AreEqual(60f, tech.Progress, 1e-3f);
    }

    // --- Advancing ---

    [Test]
    public void Advance_EachConditionAloneBlocks()
    {
        TechSystem tech = MedievalStart();
        Assert.IsFalse(tech.GetAdvanceStatus(1000).Ready, "no techs");

        tech.Enqueue(m_MedA);
        tech.Enqueue(m_MedC);
        tech.Step(150f);    // 2 Medieval techs = TechsToAdvance

        AdvanceStatus enough = tech.GetAdvanceStatus(100);
        Assert.IsTrue(enough.Ready);
        Assert.AreEqual(1, enough.NextAge);
        Assert.AreEqual(50f, enough.RpCost);
        Assert.AreEqual(2, enough.TechsDone);

        AdvanceStatus lowPop = tech.GetAdvanceStatus(99);
        Assert.IsTrue(lowPop.TechsMet);
        Assert.IsFalse(lowPop.PopulationMet);
        Assert.IsFalse(lowPop.Ready);
        Assert.IsFalse(tech.EnqueueAdvance(99));

        // A required tech blocks even with the count and population met.
        m_Renaissance.Init("renaissance", 1450, techsToAdvance: 2, populationToEnter: 100, advanceCost: 50f,
            requiredTechs: new[] { m_MedB });
        AdvanceStatus missing = tech.GetAdvanceStatus(100);
        Assert.AreEqual(1, missing.RequiredTechsMissing);
        Assert.IsFalse(missing.Ready);
        Assert.IsFalse(tech.EnqueueAdvance(100));

        // The RP cost: queued, but not paid yet, so the age doesn't change.
        m_Renaissance.Init("renaissance", 1450, techsToAdvance: 2, populationToEnter: 100, advanceCost: 50f);
        Assert.IsTrue(tech.EnqueueAdvance(100));
        tech.Step(49f);
        Assert.AreEqual(0, tech.CurrentAge);
        Assert.IsFalse(tech.EnqueueAdvance(100), "already planned");
    }

    [Test]
    public void Advance_Completing_RaisesAgeAdvanced_AndOpensTheAge()
    {
        TechSystem tech = MedievalStart();
        tech.Enqueue(m_MedA);
        tech.Enqueue(m_MedB);
        tech.Step(200f);

        int advancedTo = -1;
        tech.AgeAdvanced += age => advancedTo = age;
        Assert.IsFalse(tech.CanResearch(m_RenA), "Renaissance tech before the age");
        Assert.IsTrue(tech.EnqueueAdvance(150));
        tech.Step(50f);

        Assert.AreEqual(1, advancedTo);
        Assert.AreEqual(1, tech.CurrentAge);
        Assert.AreEqual(0.75f, tech.Rules.CapacityScale);
        Assert.IsTrue(tech.CanResearch(m_RenA));
        Assert.AreEqual(1450, tech.CurrentAgeDefinition.YearOnEntering(760));
        Assert.AreEqual(1500, tech.CurrentAgeDefinition.YearOnEntering(1500), "the year never goes back");
    }

    [Test]
    public void Advance_InTheLastAge_IsUnavailable()
    {
        var tech = new TechSystem(m_Ages, m_Techs, m_Config);
        tech.StartNew(2);
        Assert.IsFalse(tech.GetAdvanceStatus(100000).HasNextAge);
        Assert.IsNull(tech.NextAdvance());
        Assert.IsFalse(tech.EnqueueAdvance(100000));
    }

    // --- Modifiers ---

    [Test]
    public void Modifiers_FoldResearchedEffects()
    {
        m_MedA.Init("med_a", 0, 100f, null, new[]
        {
            new TechEffect(TechEffectType.DemandMultiplier, "Residential", 1.1f),
            new TechEffect(TechEffectType.ResearchMultiplier, "", 1.25f),
            new TechEffect(TechEffectType.UnlockBuilding, "park", 0f),
        });
        m_MedC.Init("med_c", 0, 50f, null, new[]
        {
            new TechEffect(TechEffectType.DemandMultiplier, "", 1.2f),
            new TechEffect(TechEffectType.DemandMultiplier, "commercial", 1.5f),
            new TechEffect(TechEffectType.HappinessBonus, "", 0.02f),
            new TechEffect(TechEffectType.UpkeepMultiplier, "", 0.9f),
        });
        TechSystem tech = MedievalStart();
        Assert.AreEqual(1f, tech.Modifiers.ResearchMultiplier);
        Assert.IsFalse(tech.Modifiers.IsUnlocked("park"));

        tech.Enqueue(m_MedA);
        tech.Enqueue(m_MedC);
        tech.Step(150f);

        TechModifiers m = tech.Modifiers;
        Assert.AreEqual(1.1f * 1.2f, m.DemandMultiplier(ZoneType.Residential), 1e-5f);
        Assert.AreEqual(1.2f * 1.5f, m.DemandMultiplier(ZoneType.Commercial), 1e-5f);
        Assert.AreEqual(1.2f, m.DemandMultiplier(ZoneType.Industrial), 1e-5f);
        Assert.AreEqual(1.25f, m.ResearchMultiplier, 1e-5f);
        Assert.AreEqual(0.02f, m.HappinessBonus, 1e-5f);
        Assert.AreEqual(0.9f, m.UpkeepMultiplier, 1e-5f);
        Assert.IsTrue(m.IsUnlocked("park"));
        Assert.IsFalse(m.IsUnlocked("power_plant"));
    }

    // --- Restore ---

    [Test]
    public void Restore_DropsUnknownIds()
    {
        var tech = new TechSystem(m_Ages, m_Techs, m_Config);
        int dropped = tech.Restore(1, new[] { "med_a", "med_b", "gone" }, "ren_a", 12f,
            new[] { "missing", "advance:industrial", "med_c" });

        Assert.AreEqual(2, dropped);
        Assert.AreEqual(1, tech.CurrentAge);
        Assert.IsTrue(tech.IsResearched(m_MedB));
        Assert.AreEqual(m_RenA, tech.Active.Tech);
        Assert.AreEqual(2, tech.Queue.Count);
        Assert.IsTrue(tech.Queue[0].IsAdvance);
        Assert.AreEqual(m_MedC, tech.Queue[1].Tech);
        Assert.AreEqual(12f, tech.Progress);
    }

    // --- In the simulation ---

    [Test]
    public void Simulation_WithoutAges_HasNoResearch_AndLegacyRules()
    {
        var grid = new GridData(8, 8);
        var sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config);
        Assert.IsNull(sim.Tech);
        Assert.AreEqual(AgeRules.Legacy.MaxLevel, sim.Rules.MaxLevel);
        Assert.IsTrue(sim.Rules.UpgradesNeedPower);
        Assert.AreEqual(0f, sim.ResearchIncome());
    }

    // A small city run without ages and started in the Industrial age (today's rules): research
    // accrues, nothing else changes, and two runs with ages are identical.
    [Test]
    public void Simulation_ResearchStep_IsDeterministic_AndChangesNothingElse()
    {
        SimulationSystem plain = RunCity(false, 60);
        SimulationSystem a = RunCity(true, 60);
        SimulationSystem b = RunCity(true, 60);

        Assert.AreEqual(plain.Population.Population, a.Population.Population);
        Assert.AreEqual(plain.Economy.Money, a.Economy.Money);
        Assert.Greater(a.Population.CommercialJobs, 0);

        Assert.IsTrue(a.Tech.Progress > 0f || a.Tech.ResearchedCount > 0, "research accrued");
        Assert.AreEqual(a.Tech.Progress, b.Tech.Progress);
        Assert.AreEqual(a.Tech.ResearchPerDay, b.Tech.ResearchPerDay);
        Assert.AreEqual(a.Tech.ResearchedCount, b.Tech.ResearchedCount);
        Assert.Greater(a.Tech.ResearchPerDay, 0f);
    }

    private SimulationSystem RunCity(bool withAges, int days)
    {
        var grid = new GridData(24, 24);
        var roads = new RoadNetwork(grid);
        for (int x = 0; x < 24; x++) grid.SetRoad(new Vector2Int(x, 12), true);
        for (int x = 0; x < 24; x++)
        {
            grid.SetZone(new Vector2Int(x, 13), ZoneType.Residential);
            grid.SetZone(new Vector2Int(x, 11), x < 12 ? ZoneType.Commercial : ZoneType.Industrial);
        }
        SimulationSystem sim = withAges
            ? new SimulationSystem(grid, roads, m_Config, m_Ages, m_Techs)
            : new SimulationSystem(grid, roads, m_Config);
        if (withAges)
        {
            sim.Tech.StartNew(2);
            sim.Tech.Enqueue(m_Steam);
        }
        for (int day = 0; day < days; day++) sim.Tick();
        return sim;
    }
}
