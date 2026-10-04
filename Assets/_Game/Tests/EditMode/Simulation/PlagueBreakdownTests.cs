using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// M17c: plague (outbreaks from sickness, spread, immunity, end + cooldown, deaths, the happiness term) and
// breakdowns (rate by tech age, funding and techs, a broken source feeds nothing, repair, saves).
public sealed class PlagueBreakdownTests
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

    private void Tune(string field, float value)
    {
        var so = new SerializedObject(m_Config);
        so.FindProperty(field).floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private void TuneInt(string field, int value)
    {
        var so = new SerializedObject(m_Config);
        so.FindProperty(field).intValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private SimulationSystem NewSim(GridData grid, int age = TestAges.Medieval, int population = 800)
    {
        var sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config, m_TestAges.Ages, m_TestAges.Techs);
        sim.Tech.StartNew(age);
        sim.Population.Restore(population, 0.7f);
        return sim;
    }

    private static void Home(GridData grid, int x, int y, int age = TestAges.Medieval)
    {
        var cell = new Vector2Int(x, y);
        grid.SetZone(cell, ZoneType.Residential);
        grid.SetBuildingLevel(cell, 1);
        grid.SetBuiltAge(cell, (byte)age);
    }

    private static ServiceSource Apothecary(Vector2Int origin, float strength)
    {
        return new ServiceSource(origin, Vector2Int.one, 0, 0, civicKind: ServiceKind.Health, civicRadius: 8, civicStrength: strength);
    }

    private static void ClearPlague(SimulationSystem sim)
    {
        Array.Clear(sim.Disasters.Plague, 0, sim.Disasters.Plague.Length);
        sim.Disasters.PlagueCooldown = 0;
        sim.Disasters.PlagueDeaths = 0;
        sim.Disasters.Epidemic.Recount();
    }

    // A block of homes: x 2..5, y 2..4 (12 homes).
    private static void Block(GridData grid, int age = TestAges.Medieval)
    {
        for (int x = 2; x <= 5; x++) for (int y = 2; y <= 4; y++) Home(grid, x, y, age);
    }

    // --- Outbreaks ---

    [Test]
    public void NoPlague_InIndustrialAndModern_OrInATownBelowTheMinimum()
    {
        Tune("m_PlagueOutbreakPerDay", 1f);
        foreach (int age in new[] { TestAges.Industrial, TestAges.Modern })
        {
            var grid = new GridData(12, 12);
            SimulationSystem sim = NewSim(grid, age);
            Block(grid, age);
            for (int i = 0; i < 200; i++) sim.Disasters.Epidemic.Step();
            Assert.IsFalse(sim.Disasters.Epidemic.Active, $"age {age}");
        }

        var smallGrid = new GridData(12, 12);
        SimulationSystem small = NewSim(smallGrid, population: 100);
        Block(smallGrid);
        string state = small.Disasters.Random.StateString;
        for (int i = 0; i < 200; i++) small.Disasters.Epidemic.Step();
        Assert.IsFalse(small.Disasters.Epidemic.Active);
        Assert.AreEqual(state, small.Disasters.Random.StateString, "nothing drawn below the minimum");
    }

    private float OutbreakRate(SimulationSystem sim, int trials)
    {
        int started = 0;
        for (int i = 0; i < trials; i++)
        {
            sim.Disasters.Epidemic.Step();
            if (sim.Disasters.Epidemic.Started) started++;
            ClearPlague(sim);
        }
        return started / (float)trials;
    }

    [Test]
    public void OutbreakRate_FollowsTheMeanSickness_SoHealthCoverLowersIt()
    {
        Tune("m_PlagueOutbreakPerDay", 0.05f);
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid);
        Block(grid);

        float open = OutbreakRate(sim, 3000);
        sim.Sources = new[] { Apothecary(new Vector2Int(3, 5), 0.5f) };
        float covered = OutbreakRate(sim, 3000);

        Assert.AreEqual(0.05f, open, 0.012f, "sickness 1 at every home, Medieval risk 1");
        Assert.AreEqual(0.025f, covered, 0.01f, "half the sickness, half the rate");
    }

    [Test]
    public void RenaissanceHasLessPlagueThanMedieval()
    {
        Tune("m_PlagueOutbreakPerDay", 0.05f);
        var gridA = new GridData(12, 12);
        var gridB = new GridData(12, 12);
        SimulationSystem medieval = NewSim(gridA, TestAges.Medieval);
        SimulationSystem renaissance = NewSim(gridB, TestAges.Renaissance);
        Block(gridA);
        Block(gridB, TestAges.Renaissance);
        Assert.AreEqual(0.05f, OutbreakRate(medieval, 3000), 0.012f);
        Assert.AreEqual(0.03f, OutbreakRate(renaissance, 3000), 0.01f);
    }

    [Test]
    public void SickMultiplierAndHealthCover_ReachHomeSickness()
    {
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid);
        Home(grid, 3, 3);
        Home(grid, 8, 8);
        sim.Sources = new[] { Apothecary(new Vector2Int(3, 4), 0.4f) };
        foreach (Vector2Int cell in new[] { new Vector2Int(3, 3), new Vector2Int(8, 8), new Vector2Int(0, 0) })
        {
            Assert.AreEqual(sim.Civic.Explain(cell).Sickness, sim.Civic.HomeSickness(cell, sim.Civic.Ramp), 1e-6f, cell.ToString());
        }
    }

    // --- Spread, immunity, the end ---

    [Test]
    public void ItSpreadsWithinReachByTheTargetsSickness_AndNotToCoveredHomes()
    {
        Tune("m_PlagueSpread", 1f);
        var grid = new GridData(14, 14);
        SimulationSystem sim = NewSim(grid);
        Home(grid, 2, 2);
        Home(grid, 4, 4);       // distance 2: in reach
        Home(grid, 5, 2);       // distance 3: out of reach
        PlagueSystem plague = sim.Disasters.Epidemic;
        Assert.IsTrue(plague.Infect(new Vector2Int(2, 2)));
        plague.Step();
        Assert.IsTrue(plague.IsInfected(new Vector2Int(4, 4)));
        Assert.IsFalse(plague.IsInfected(new Vector2Int(5, 2)));

        // Full health cover: nothing to catch.
        ClearPlague(sim);
        sim.Sources = new[] { Apothecary(new Vector2Int(3, 3), 1f) };
        plague.Infect(new Vector2Int(2, 2));
        plague.Step();
        Assert.IsFalse(plague.IsInfected(new Vector2Int(4, 4)));
    }

    [Test]
    public void AHomeIsInfectedForPlagueDays_ThenImmuneUntilTheOutbreakEnds_AndTheCooldownFollows()
    {
        Tune("m_PlagueSpread", 1f);
        Tune("m_PlagueDeathRate", 0f);
        Tune("m_PlagueOutbreakPerDay", 1f);
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid);
        Home(grid, 3, 3);
        Home(grid, 4, 3);
        PlagueSystem plague = sim.Disasters.Epidemic;
        var a = new Vector2Int(3, 3);
        var b = new Vector2Int(4, 3);
        plague.Infect(a);

        plague.Step();                                    // a infects b; a has been ill 1 day
        Assert.IsTrue(plague.IsInfected(b));
        for (int day = 2; day <= 9; day++) plague.Step();
        Assert.IsTrue(plague.IsInfected(a));
        plague.Step();                                    // day 10: a recovers (immune), b still ill
        Assert.IsTrue(plague.IsRecovered(a));
        Assert.IsTrue(plague.IsInfected(b));
        Assert.IsFalse(plague.Ended);

        plague.Step();                                    // day 11: b recovers: no one is ill, the outbreak ends
        Assert.IsTrue(plague.Ended);
        Assert.IsFalse(plague.IsRecovered(a), "the immunity marks are cleared");
        Assert.AreEqual(m_Config.PlagueCooldownDays, sim.Disasters.PlagueCooldown);

        for (int day = 0; day < m_Config.PlagueCooldownDays; day++)
        {
            plague.Step();
            Assert.IsFalse(plague.Started, $"cooling down, day {day}");
        }
        plague.Step();
        Assert.IsTrue(plague.Started, "the first day after the cooldown (outbreak chance 1)");
    }

    [Test]
    public void ABurntHomeCarriesNoInfection_AndAnOutbreakWithNoSickLeftEnds()
    {
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid);
        Home(grid, 3, 3);
        PlagueSystem plague = sim.Disasters.Epidemic;
        plague.Infect(new Vector2Int(3, 3));
        grid.SetBuildingLevel(new Vector2Int(3, 3), 0);
        plague.Step();
        Assert.IsFalse(plague.Active);
        Assert.IsFalse(plague.IsInfected(new Vector2Int(3, 3)));
    }

    // --- Deaths and happiness ---

    private SimulationSystem InfectedBlock(out GridData grid, float spread = 0f)
    {
        Tune("m_PlagueSpread", spread);
        TuneInt("m_PlagueDays", 100);
        grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid, TestAges.Industrial);
        Block(grid, TestAges.Industrial);     // 12 homes, capacity 4 each
        sim.Population.RecountCapacity(grid, default);
        sim.Population.Restore(800, 0.7f);
        for (int x = 2; x <= 5; x++) for (int y = 2; y <= 3; y++) sim.Disasters.Epidemic.Infect(new Vector2Int(x, y));    // 8 homes
        return sim;
    }

    [Test]
    public void InfectedHomesLoseTheirResidentsAtTheDeathRate_WithTheRemainderCarried()
    {
        Tune("m_PlagueDeathRate", 0.05f);
        SimulationSystem sim = InfectedBlock(out GridData grid);
        int housing = sim.Population.Housing;
        Assert.AreEqual(48, housing);
        int before = sim.Population.Population;     // 800, far above the housing: occupancy is capped at 1
        sim.Disasters.Epidemic.Step();
        Assert.AreEqual(8 * 4 * 0.05f, 1.6f, 1e-5f);
        Assert.AreEqual(1, sim.Disasters.Epidemic.Died, "1.6 -> 1, carrying 0.6");
        sim.Disasters.Epidemic.Step();
        Assert.AreEqual(2, sim.Disasters.Epidemic.Died, "1.6 + 0.6 = 2.2 -> 2, carrying 0.2");
        Assert.AreEqual(before - 3, sim.Population.Population);
        Assert.AreEqual(3, sim.Disasters.PlagueDeaths);
    }

    [Test]
    public void ThePlagueTermIsTheInfectedShareTimesThePenalty_Capped()
    {
        Tune("m_PlagueDeathRate", 0f);
        SimulationSystem sim = InfectedBlock(out GridData grid);
        sim.Disasters.Enabled = true;
        int housing = sim.Population.Housing;
        float share = sim.Disasters.Epidemic.InfectedHousingShare(housing);
        Assert.AreEqual(32f / 48f, share, 1e-5f);
        Assert.AreEqual(-m_Config.PlaguePenaltyCap, sim.Disasters.Epidemic.HappinessTerm(housing), 1e-6f, "capped at 0.15");
        Assert.AreEqual(0.1f, PlagueSystem.PenaltyAt(m_Config, 0.2f), 1e-6f);
        Assert.AreEqual(0.15f, PlagueSystem.PenaltyAt(m_Config, 0.9f), 1e-6f);

        sim.Tick();
        Assert.AreEqual(-m_Config.PlaguePenaltyCap, sim.Population.Happiness.Plague, 1e-6f);
        float others = sim.Population.Happiness.Total - sim.Population.Happiness.Plague;
        Assert.Less(sim.Population.Happiness.Total, Mathf.Clamp01(others), "it lowers the total");
    }

    [Test]
    public void AnOutbreakCostsNothingWhileTheSwitchIsOff()
    {
        Tune("m_PlagueDeathRate", 0f);
        SimulationSystem sim = InfectedBlock(out GridData grid);
        sim.Tick();
        Assert.AreEqual(0f, sim.Population.Happiness.Plague);
    }

    // --- Breakdowns ---

    private ServiceSource Plant(int x, int y, float cost = 10000f, int techAge = TestAges.Industrial)
    {
        return new ServiceSource(new Vector2Int(x, y), new Vector2Int(3, 3), 0, 600, cost: cost, techAge: techAge);
    }

    private BreakdownSystem Breakdowns(SimulationSystem sim) => sim.Disasters.Breakdowns;

    [Test]
    public void BreakdownRate_RisesWithAgesBehind_FallsWithFunding_AndSmartGridCutsIt()
    {
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid, TestAges.Modern);
        BreakdownSystem breakdowns = Breakdowns(sim);
        ServiceSource current = Plant(0, 0, techAge: TestAges.Modern);
        ServiceSource old = Plant(0, 0, techAge: TestAges.Industrial);
        ServiceSource ancient = Plant(0, 0, techAge: TestAges.Medieval);

        float basic = breakdowns.Weight(current);
        Assert.AreEqual(m_Config.BreakdownPerDay, basic, 1e-7f);
        Assert.AreEqual(basic * 1.5f, breakdowns.Weight(old), 1e-7f, "one age behind: x1.5");
        Assert.AreEqual(basic * 2.5f, breakdowns.Weight(ancient), 1e-7f, "three behind: x2.5");

        sim.Budget.SetFunding(BudgetLine.Power, 1.5f);
        Assert.AreEqual(basic / 2.25f, breakdowns.Weight(current), 1e-7f);
        sim.Budget.SetFunding(BudgetLine.Power, 0.5f);
        Assert.AreEqual(basic * 4f, breakdowns.Weight(current), 1e-7f);

        sim.Budget.SetFunding(BudgetLine.Power, 1f);
        TechDefinition grid2 = ScriptableObject.CreateInstance<TechDefinition>();
        grid2.Init("smart", 0, 1f, null, new[] { new TechEffect(TechEffectType.HazardMultiplier, "Breakdown", 0.25f) });
        m_Created.Add(grid2);
        TechModifiers folded = TechModifiers.Fold(new[] { grid2 });
        var smart = new DisasterSystem(grid, m_Config, true, sim.Civic, sim.Water, () => folded, () => TestAges.Modern,
            sim.Population, sim.Capacity, sim.Budget, () => sim.Sources, () => 0f);
        Assert.AreEqual(basic * 0.25f, smart.Breakdowns.Weight(current), 1e-7f);
    }

    [Test]
    public void AtMostOneSourceBreaksADay_AndABrokenOneIsNotPickedAgain()
    {
        Tune("m_BreakdownPerDay", 10f);
        var grid = new GridData(14, 14);
        SimulationSystem sim = NewSim(grid, TestAges.Industrial);
        sim.Sources = new[] { Plant(0, 0), Plant(4, 0), Plant(8, 0) };
        BreakdownSystem breakdowns = Breakdowns(sim);

        breakdowns.Step();
        Assert.IsTrue(breakdowns.Broke);
        Assert.AreEqual(1, breakdowns.Count);
        breakdowns.Step();
        Assert.AreEqual(2, breakdowns.Count);
        breakdowns.Step();
        Assert.AreEqual(3, breakdowns.Count);
        breakdowns.Step();
        Assert.IsFalse(breakdowns.Broke, "everything is already down");
        Assert.AreEqual(3, breakdowns.Count);
    }

    [Test]
    public void ABrokenSourceFeedsNothing_UntilItIsRepairedOrTheDaysPass()
    {
        Tune("m_BreakdownPerDay", 0f);
        var grid = new GridData(14, 14);
        SimulationSystem sim = NewSim(grid, TestAges.Industrial);
        for (int x = 0; x < 14; x++) grid.SetRoad(new Vector2Int(x, 5), true);
        Home(grid, 6, 6, TestAges.Industrial);
        Assert.IsTrue(grid.Occupy(new Vector2Int(0, 6), new Vector2Int(3, 3), 0, 1));
        ServiceSource plant = Plant(0, 6);
        sim.Sources = new[] { plant };
        var home = new Vector2Int(6, 6);
        Assert.IsTrue(sim.Power.IsPowered(home));

        Assert.IsTrue(Breakdowns(sim).Break(plant.Origin));
        Assert.IsFalse(sim.Power.IsPowered(home), "the broken plant gives 0");
        Assert.IsTrue(Breakdowns(sim).IsBroken(plant.Origin));
        Assert.AreEqual(m_Config.BreakdownDays, Breakdowns(sim).DaysLeft(plant.Origin));
        Assert.AreEqual(2000f, sim.RepairCost(plant.Origin), 1e-3f, "20% of $10,000");

        // Not enough money: nothing happens.
        sim.Economy.Restore(100f, 0f, 0f, 0.1f, 0.1f, 0.1f);
        Assert.IsFalse(sim.Repair(plant.Origin));
        Assert.IsTrue(Breakdowns(sim).IsBroken(plant.Origin));

        sim.Economy.Restore(5000f, 0f, 0f, 0.1f, 0.1f, 0.1f);
        Assert.IsTrue(sim.Repair(plant.Origin));
        Assert.AreEqual(3000f, sim.Economy.Money, 1e-3f);
        Assert.IsTrue(sim.Power.IsPowered(home));
        Assert.IsFalse(Breakdowns(sim).IsBroken(plant.Origin));

        // The days passing repair it for free.
        Breakdowns(sim).Break(plant.Origin);
        for (int day = 0; day < m_Config.BreakdownDays - 1; day++) Breakdowns(sim).Step();
        Assert.IsTrue(Breakdowns(sim).IsBroken(plant.Origin));
        Breakdowns(sim).Step();
        Assert.IsFalse(Breakdowns(sim).IsBroken(plant.Origin));
        Assert.AreEqual(1, Breakdowns(sim).Recovered);
        Assert.IsTrue(sim.Power.IsPowered(home));
    }

    [Test]
    public void WellsAndFountainsNeverBreak()
    {
        Tune("m_BreakdownPerDay", 10f);
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid, TestAges.Medieval);
        sim.Sources = new[] { new ServiceSource(new Vector2Int(2, 2), Vector2Int.one, 0, 0, waterRadius: 4) };
        for (int i = 0; i < 20; i++) Breakdowns(sim).Step();
        Assert.AreEqual(0, Breakdowns(sim).Count);
    }

    [Test]
    public void ABrokenRecordForADemolishedSourceIsDropped()
    {
        Tune("m_BreakdownPerDay", 0f);
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid, TestAges.Industrial);
        sim.Sources = new[] { Plant(0, 0) };
        Breakdowns(sim).Break(new Vector2Int(0, 0));
        sim.Sources = new ServiceSource[0];
        Breakdowns(sim).Step();
        Assert.AreEqual(0, Breakdowns(sim).Count);
    }

    // --- Saves ---

    [Test]
    public void SaveLoad_KeepsABrokenPlantDown_AndDropsOneWithNoSourceThere()
    {
        Tune("m_BreakdownPerDay", 0f);
        var grid = new GridData(14, 14);
        SimulationSystem sim = NewSim(grid, TestAges.Industrial);
        for (int x = 0; x < 14; x++) grid.SetRoad(new Vector2Int(x, 5), true);
        Home(grid, 6, 6, TestAges.Industrial);
        Assert.IsTrue(grid.Occupy(new Vector2Int(0, 6), new Vector2Int(3, 3), 0, 1));
        ServiceSource plant = Plant(0, 6);
        sim.Sources = new[] { plant };
        sim.Disasters.Enabled = true;
        Breakdowns(sim).Break(plant.Origin);
        string json = SaveSystem.ToJson(SaveSystem.Capture(grid, sim));
        Assert.IsTrue(SaveSystem.TryFromJson(json, out SaveData loaded, out string error, m_TestAges.Ages, m_TestAges.Techs), error);

        // The same plant: still down, and the home loses its power.
        var grid2 = new GridData(14, 14);
        var sim2 = new SimulationSystem(grid2, new RoadNetwork(grid2), m_Config, m_TestAges.Ages, m_TestAges.Techs);
        SaveSystem.ApplyGrid(loaded, grid2);
        Assert.IsTrue(grid2.Occupy(new Vector2Int(0, 6), new Vector2Int(3, 3), 0, 1));
        sim2.Sources = new[] { plant };
        SaveSystem.ApplySimulation(loaded, sim2);
        Assert.IsTrue(Breakdowns(sim2).IsBroken(plant.Origin));
        Assert.IsFalse(sim2.Power.IsPowered(new Vector2Int(6, 6)));

        // No plant in the loaded city: the record is dropped.
        var grid3 = new GridData(14, 14);
        var sim3 = new SimulationSystem(grid3, new RoadNetwork(grid3), m_Config, m_TestAges.Ages, m_TestAges.Techs);
        SaveSystem.ApplyGrid(loaded, grid3);
        SaveSystem.ApplySimulation(loaded, sim3);
        Assert.AreEqual(0, Breakdowns(sim3).Count);
    }

    [Test]
    public void SaveLoadContinue_WithAnOutbreakUnderWay_EqualsAnUninterruptedRun()
    {
        Tune("m_PlagueOutbreakPerDay", 0.5f);
        TuneInt("m_CivicFreePopulation", 0);
        TuneInt("m_CivicFullPopulation", 100);
        TuneInt("m_PlagueMinPopulation", 10);
        var grid = new GridData(24, 24);
        SimulationSystem sim = SeededCity.Build(grid, m_Config, plantSupply: 0, waterSupply: 0,
            ages: m_TestAges.Ages, techs: m_TestAges.Techs, startAge: TestAges.Medieval);
        sim.Disasters.Enabled = true;
        sim.Disasters.Random.Seed(4);
        int day = 0;
        while (!sim.Disasters.Epidemic.Active && day < 150)
        {
            sim.Tick();
            day++;
        }
        Assert.IsTrue(sim.Disasters.Epidemic.Active, "an outbreak started");
        sim.Tick();

        string json = SaveSystem.ToJson(SaveSystem.Capture(grid, sim));
        Assert.IsTrue(SaveSystem.TryFromJson(json, out SaveData loaded, out string error, m_TestAges.Ages, m_TestAges.Techs), error);
        var grid2 = new GridData(24, 24);
        var sim2 = new SimulationSystem(grid2, new RoadNetwork(grid2), m_Config, m_TestAges.Ages, m_TestAges.Techs);
        SaveSystem.ApplyGrid(loaded, grid2);
        SaveSystem.ApplySimulation(loaded, sim2);
        Assert.AreEqual(sim.Disasters.Epidemic.InfectedCount, sim2.Disasters.Epidemic.InfectedCount);

        for (int i = 0; i < 40; i++)
        {
            sim.Tick();
            sim2.Tick();
        }

        CollectionAssert.AreEqual(sim.Disasters.Plague, sim2.Disasters.Plague);
        Assert.AreEqual(sim.Population.Population, sim2.Population.Population);
        Assert.AreEqual(sim.Disasters.PlagueDeaths, sim2.Disasters.PlagueDeaths);
        Assert.AreEqual(sim.Disasters.PlagueCooldown, sim2.Disasters.PlagueCooldown);
        Assert.AreEqual(sim.Disasters.Random.StateString, sim2.Disasters.Random.StateString);
        Assert.AreEqual(sim.Economy.Money, sim2.Economy.Money, 1e-2f);
    }
}
