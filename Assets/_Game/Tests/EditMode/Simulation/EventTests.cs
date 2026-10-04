using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// M17d: random events with choices: cadence, eligibility, choosing (price, reward, research, lasting effects), the
// automatic choice, the repeat window, the Events happiness term and saves.
public sealed class EventTests
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
        m_TestAges.Dispose();
        Object.DestroyImmediate(m_Config);
        foreach (Object o in m_Created) Object.DestroyImmediate(o);
        m_Created.Clear();
    }

    private void TuneInt(string field, int value)
    {
        var so = new SerializedObject(m_Config);
        so.FindProperty(field).intValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static EventChoice Choice(string label, float cost = 0f, float perResident = 0f, float reward = 0f, float rp = 0f,
        int days = 0, params TechEffect[] effects)
    {
        return new EventChoice
        {
            Label = label, Description = label, Cost = cost, CostPerResident = perResident, Reward = reward,
            ResearchPoints = rp, Days = days, Effects = effects,
        };
    }

    private EventDefinition Event(string id, int minAge, int maxAge, int minPopulation, TechDefinition tech, params EventChoice[] choices)
    {
        EventDefinition definition = ScriptableObject.CreateInstance<EventDefinition>();
        definition.Init(id, minAge, maxAge, minPopulation, tech, 1f, choices);
        m_Created.Add(definition);
        return definition;
    }

    private SimulationSystem NewSim(GridData grid, params EventDefinition[] events)
    {
        m_TestAges.Techs.InitEvents(events);
        var sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config, m_TestAges.Ages, m_TestAges.Techs);
        sim.Tech.StartNew(TestAges.Industrial);
        sim.Population.Restore(800, 0.7f);
        sim.Disasters.Enabled = true;
        return sim;
    }

    private static readonly TechEffect Happy5 = new TechEffect(TechEffectType.HappinessBonus, "", 0.05f);
    private static readonly TechEffect CommercialDemand = new TechEffect(TechEffectType.DemandMultiplier, "Commercial", 1.2f);

    private EventDefinition Fair(int minAge = 0, int maxAge = 3, int minPopulation = 0, TechDefinition tech = null)
    {
        return Event("fair", minAge, maxAge, minPopulation, tech,
            Choice("Host", cost: 200f, perResident: 0.5f, reward: 50f, days: 3, effects: new[] { Happy5, CommercialDemand }),
            Choice("Decline"));
    }

    private static void Steps(RandomEventSystem events, int count)
    {
        for (int i = 0; i < count; i++) events.Step();
    }

    // --- Cadence ---

    [Test]
    public void AnEventIsOfferedOnceTheScheduledDaysPass_CountedFromTheFirstStep()
    {
        TuneInt("m_EventIntervalMin", 10);
        TuneInt("m_EventIntervalMax", 10);
        SimulationSystem sim = NewSim(new GridData(8, 8), Fair());
        RandomEventSystem events = sim.Disasters.Events;

        Steps(events, 10);                         // schedules, then counts 9 down
        Assert.IsNull(events.Pending);
        Assert.AreEqual(1, sim.Disasters.DaysToNextEvent);
        events.Step();
        Assert.IsNotNull(events.Pending);
        Assert.AreEqual("fair", sim.Disasters.PendingEvent);
    }

    [Test]
    public void TheIntervalIsDrawnBetweenTheBounds_AndNothingIsOfferedWithTheSwitchOff()
    {
        TuneInt("m_EventIntervalMin", 5);
        TuneInt("m_EventIntervalMax", 8);
        SimulationSystem sim = NewSim(new GridData(8, 8), Fair());
        for (int i = 0; i < 60; i++)
        {
            sim.Disasters.DaysToNextEvent = 0;
            sim.Disasters.Events.Step();
            Assert.That(sim.Disasters.DaysToNextEvent, Is.InRange(5, 8));
        }

        sim.Disasters.Enabled = false;
        sim.Disasters.DaysToNextEvent = 1;
        for (int i = 0; i < 200; i++) sim.Tick();
        Assert.IsNull(sim.Disasters.Events.Pending);
    }

    [Test]
    public void AGameWithNoEventsDrawsNothing()
    {
        SimulationSystem sim = NewSim(new GridData(8, 8));
        string state = sim.Disasters.Random.StateString;
        Steps(sim.Disasters.Events, 200);
        Assert.AreEqual(state, sim.Disasters.Random.StateString);
    }

    // --- Eligibility ---

    [Test]
    public void EligibilityFollowsAgePopulationTechAndTheRepeatWindow()
    {
        TechDefinition steam = m_TestAges["steam"];
        EventDefinition early = Fair(0, 1);
        EventDefinition big = Event("big", 2, 3, 500, null, Choice("a", cost: 1f), Choice("b"));
        EventDefinition gated = Event("gated", 2, 3, 0, steam, Choice("a", cost: 1f), Choice("b"));
        SimulationSystem sim = NewSim(new GridData(8, 8), early, big, gated);
        RandomEventSystem events = sim.Disasters.Events;

        Assert.IsFalse(events.IsEligible(early, TestAges.Industrial, 800), "too late an age");
        Assert.IsTrue(events.IsEligible(early, TestAges.Renaissance, 0));
        Assert.IsTrue(events.IsEligible(big, TestAges.Industrial, 500));
        Assert.IsFalse(events.IsEligible(big, TestAges.Industrial, 499), "needs 500 residents");
        Assert.IsFalse(events.IsEligible(gated, TestAges.Industrial, 800), "steam is not researched");
        sim.Tech.SetActive(steam);
        sim.Tech.Step(1000f);
        Assert.IsTrue(sim.Tech.IsResearched(steam));
        Assert.IsTrue(events.IsEligible(gated, TestAges.Industrial, 800));

        sim.Disasters.RecentEvents.Add(new EventRecord("gated", -1, 100));
        Assert.IsFalse(events.IsEligible(gated, TestAges.Industrial, 800), "offered lately");
    }

    // --- Choosing ---

    [Test]
    public void ChoosingChargesThePriceAndPaysTheReward()
    {
        SimulationSystem sim = NewSim(new GridData(8, 8), Fair());
        sim.Economy.Restore(1000f, 0f, 0f, 0.1f, 0.1f, 0.1f);
        RandomEventSystem events = sim.Disasters.Events;

        Assert.IsTrue(events.OfferNow("fair"));
        Assert.AreEqual(600f, events.PriceOf(0), 1e-3f, "$200 + $0.5 x 800 residents");
        Assert.IsTrue(events.CanChoose(0));
        Assert.IsTrue(events.Choose(0));
        Assert.AreEqual(1000f - 600f + 50f, sim.Economy.Money, 1e-3f);
        Assert.IsNull(events.Pending);
        Assert.AreEqual(0, sim.Disasters.DaysToNextEvent, "the next one is scheduled by the next Step");
        Assert.AreEqual("fair", events.LastResolved.Id);
        Assert.AreEqual(0, events.LastChoice);
    }

    [Test]
    public void AnUnaffordableChoiceFails_ButTheLastChoiceIsAlwaysFree()
    {
        SimulationSystem sim = NewSim(new GridData(8, 8), Fair());
        sim.Economy.Restore(100f, 0f, 0f, 0.1f, 0.1f, 0.1f);
        RandomEventSystem events = sim.Disasters.Events;
        events.OfferNow("fair");

        Assert.IsFalse(events.CanChoose(0));
        Assert.IsFalse(events.Choose(0));
        Assert.IsNotNull(events.Pending, "still waiting");
        Assert.AreEqual(100f, sim.Economy.Money, 1e-3f);
        Assert.IsTrue(events.Choose(1));
        Assert.AreEqual(100f, sim.Economy.Money, 1e-3f);
        Assert.IsFalse(events.Choose(5), "no such choice");
    }

    [Test]
    public void ResearchPointsGoToTheActiveProject()
    {
        EventDefinition scholars = Event("scholars", 0, 3, 0, null, Choice("Lodge", rp: 20f), Choice("Send on"));
        SimulationSystem sim = NewSim(new GridData(8, 8), scholars);
        sim.Tech.SetActive(m_TestAges["steam"]);
        float before = sim.Tech.Progress;
        sim.Disasters.Events.OfferNow();
        Assert.IsTrue(sim.Disasters.Events.Choose(0));
        Assert.AreEqual(before + 20f, sim.Tech.Progress, 1e-3f);
    }

    // --- Lasting effects ---

    [Test]
    public void LastingEffectsFoldIntoTheModifiers_FeedTheEventsTerm_AndExpire()
    {
        SimulationSystem sim = NewSim(new GridData(8, 8), Fair());
        sim.Economy.Restore(5000f, 0f, 0f, 0.1f, 0.1f, 0.1f);
        RandomEventSystem events = sim.Disasters.Events;
        events.OfferNow("fair");
        events.Choose(0);

        Assert.AreEqual(0.05f, sim.TechModifiers.EventHappiness, 1e-6f);
        Assert.AreEqual(0f, sim.TechModifiers.HappinessBonus, "apart from the techs' own");
        Assert.AreEqual(1.2f, sim.TechModifiers.DemandMultiplier(ZoneType.Commercial), 1e-6f);
        Assert.AreEqual(3, events.DaysLeft("fair"));

        sim.Tick();
        Assert.AreEqual(0.05f, sim.Population.Happiness.Events, 1e-6f, "shown in the happiness breakdown");

        Steps(events, 1);
        Assert.AreEqual(1, events.DaysLeft("fair"));
        events.Step();
        Assert.AreEqual(0, events.DaysLeft("fair"));
        Assert.AreEqual(0f, sim.TechModifiers.EventHappiness);
        Assert.AreEqual(1f, sim.TechModifiers.DemandMultiplier(ZoneType.Commercial), 1e-6f);
    }

    [Test]
    public void TheRepeatWindowCountsDown_ThenTheEventCanComeAgain()
    {
        TuneInt("m_EventRepeatDays", 5);
        SimulationSystem sim = NewSim(new GridData(8, 8), Fair());
        sim.Economy.Restore(9000f, 0f, 0f, 0.1f, 0.1f, 0.1f);
        RandomEventSystem events = sim.Disasters.Events;
        events.OfferNow("fair");
        events.Choose(0);
        Assert.AreEqual(1, sim.Disasters.RecentEvents.Count);
        Steps(events, 5);
        Assert.AreEqual(0, sim.Disasters.RecentEvents.Count, "the repeat window passed");
    }

    // --- The automatic choice ---

    [Test]
    public void AnUnansweredEventTakesItsLastChoiceAfterEventAutoDays()
    {
        TuneInt("m_EventAutoDays", 3);
        SimulationSystem sim = NewSim(new GridData(8, 8), Fair());
        sim.Economy.Restore(5000f, 0f, 0f, 0.1f, 0.1f, 0.1f);
        RandomEventSystem events = sim.Disasters.Events;
        events.OfferNow("fair");

        Steps(events, 3);
        Assert.IsNotNull(events.Pending, "three days waiting");
        events.Step();
        Assert.IsNull(events.Pending);
        Assert.AreEqual(1, events.LastChoice, "the last choice");
        Assert.IsTrue(events.LastWasAutomatic);
        Assert.AreEqual(5000f, sim.Economy.Money, 1e-3f, "and it is free");
    }

    // --- Saves ---

    [Test]
    public void SaveKeepsThePendingEventAndRunningEffects_AndUnknownIdsAreDropped()
    {
        var grid = new GridData(8, 8);
        EventDefinition fair = Fair();
        SimulationSystem sim = NewSim(grid, fair);
        sim.Economy.Restore(5000f, 0f, 0f, 0.1f, 0.1f, 0.1f);
        sim.Disasters.Events.OfferNow("fair");
        sim.Disasters.Events.Choose(0);
        sim.Disasters.PendingEvent = "fair";
        sim.Disasters.PendingDays = 2;
        sim.Disasters.RecentEvents.Add(new EventRecord("ghost", -1, 50));
        sim.Disasters.ActiveEvents.Add(new EventRecord("ghost", 0, 10));

        string json = SaveSystem.ToJson(SaveSystem.Capture(grid, sim));
        Assert.IsTrue(SaveSystem.TryFromJson(json, out SaveData loaded, out string error, m_TestAges.Ages, m_TestAges.Techs), error);
        var grid2 = new GridData(8, 8);
        var sim2 = new SimulationSystem(grid2, new RoadNetwork(grid2), m_Config, m_TestAges.Ages, m_TestAges.Techs);
        SaveSystem.ApplyGrid(loaded, grid2);
        SaveSystem.ApplySimulation(loaded, sim2);

        Assert.AreEqual("fair", sim2.Disasters.Events.Pending.Id);
        Assert.AreEqual(2, sim2.Disasters.PendingDays);
        Assert.AreEqual(1, sim2.Disasters.ActiveEvents.Count, "the unknown event's effect was dropped");
        Assert.AreEqual(3, sim2.Disasters.Events.DaysLeft("fair"));
        Assert.AreEqual(0.05f, sim2.TechModifiers.EventHappiness, 1e-6f, "the running effect is folded again on load");
        Assert.AreEqual(1, sim2.Disasters.RecentEvents.Count);
    }

    [Test]
    public void SaveLoadContinue_WithEventsRunning_EqualsAnUninterruptedRun()
    {
        TuneInt("m_EventIntervalMin", 4);
        TuneInt("m_EventIntervalMax", 9);
        TuneInt("m_EventRepeatDays", 5);
        TuneInt("m_EventAutoDays", 2);
        var grid = new GridData(24, 24);
        m_TestAges.Techs.InitEvents(Fair(),
            Event("harvest", 0, 3, 0, null, Choice("Buy", perResident: 1f, days: 6, effects: new[] { Happy5 }), Choice("Belts", days: 4, effects: new[] { CommercialDemand })));
        SimulationSystem sim = SeededCity.Build(grid, m_Config, plantSupply: 0, waterSupply: 0,
            ages: m_TestAges.Ages, techs: m_TestAges.Techs, startAge: TestAges.Industrial);
        sim.Disasters.Enabled = true;
        sim.Disasters.Random.Seed(8);
        for (int day = 0; day < 40; day++)
        {
            Answer(sim);
            sim.Tick();
        }

        string json = SaveSystem.ToJson(SaveSystem.Capture(grid, sim));
        Assert.IsTrue(SaveSystem.TryFromJson(json, out SaveData loaded, out string error, m_TestAges.Ages, m_TestAges.Techs), error);
        var grid2 = new GridData(24, 24);
        var sim2 = new SimulationSystem(grid2, new RoadNetwork(grid2), m_Config, m_TestAges.Ages, m_TestAges.Techs);
        SaveSystem.ApplyGrid(loaded, grid2);
        SaveSystem.ApplySimulation(loaded, sim2);

        for (int day = 0; day < 60; day++)
        {
            Answer(sim);
            Answer(sim2);
            sim.Tick();
            sim2.Tick();
        }

        Assert.AreEqual(sim.Population.Population, sim2.Population.Population);
        Assert.AreEqual(sim.Economy.Money, sim2.Economy.Money, 1e-2f);
        Assert.AreEqual(sim.Disasters.Random.StateString, sim2.Disasters.Random.StateString);
        Assert.AreEqual(sim.Disasters.DaysToNextEvent, sim2.Disasters.DaysToNextEvent);
        Assert.AreEqual(sim.Disasters.ActiveEvents.Count, sim2.Disasters.ActiveEvents.Count);
        Assert.AreEqual(sim.Disasters.RecentEvents.Count, sim2.Disasters.RecentEvents.Count);
    }

    // The same simple player on both sides: take the first choice when affordable.
    private static void Answer(SimulationSystem sim)
    {
        RandomEventSystem events = sim.Disasters.Events;
        if (events.Pending == null) return;
        if (!events.Choose(0)) events.Choose(events.Pending.Choices.Length - 1);
    }
}
