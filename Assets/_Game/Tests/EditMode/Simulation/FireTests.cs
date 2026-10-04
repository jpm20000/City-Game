using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// M17b: fires. Ignition from fire risk, spread (roads as firebreaks, a one-cell street jumped at a reduced
// chance), fire cover and water putting fires out, burning down into rubble, placed buildings burning as a
// unit, determinism and save -> load -> continue. Rates are checked over many seeded trials with wide bounds.
public sealed class FireTests
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

    // Nothing random stands in the way of a spread test: certain spread, no self-extinguishing.
    private void CertainSpread()
    {
        Tune("m_FireSpreadChance", 1f);
        Tune("m_FireExtinguishBase", 0f);
        Tune("m_FireExtinguishPerCover", 0f);
    }

    private SimulationSystem NewSim(GridData grid, int age = TestAges.Industrial, int population = 800)
    {
        var sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config, m_TestAges.Ages, m_TestAges.Techs);
        sim.Tech.StartNew(age);
        sim.Population.Restore(population, 0.7f);     // the civic ramp reads the population
        return sim;
    }

    private static void Grow(GridData grid, int x, int y, ZoneType zone = ZoneType.Residential, int age = TestAges.Industrial)
    {
        var cell = new Vector2Int(x, y);
        grid.SetZone(cell, zone);
        grid.SetBuildingLevel(cell, 1);
        grid.SetBuiltAge(cell, (byte)age);
    }

    private static ServiceSource FireStation(Vector2Int origin, int radius = 6, float strength = 1f)
    {
        return new ServiceSource(origin, Vector2Int.one, 0, 0, civicKind: ServiceKind.Fire, civicRadius: radius, civicStrength: strength);
    }

    private static void Clear(SimulationSystem sim)
    {
        Array.Clear(sim.Disasters.Fires, 0, sim.Disasters.Fires.Length);
        Array.Clear(sim.Disasters.Rubble, 0, sim.Disasters.Rubble.Length);
    }

    // --- Ignition ---

    [Test]
    public void ATownBelowTheCivicFreePopulation_NeverBurns()
    {
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid, population: 40);
        for (int x = 0; x < 5; x++) Grow(grid, x, 2);
        string state = sim.Disasters.Random.StateString;

        for (int i = 0; i < 300; i++) sim.Disasters.Fire.Step();

        Assert.AreEqual(0, sim.Disasters.Fire.BurningCount);
        Assert.AreEqual(state, sim.Disasters.Random.StateString, "no risk, so nothing is drawn");
    }

    [Test]
    public void IgnitionRate_FollowsOneMinusExpOfTheSummedRisk()
    {
        Tune("m_FireIgnitionPerRisk", 0.05f);
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid);
        for (int x = 0; x < 10; x++) for (int y = 0; y < 2; y++) Grow(grid, x, y);

        float total = 0f;
        for (int x = 0; x < 10; x++) for (int y = 0; y < 2; y++) total += sim.Civic.GrownFireRisk(new Vector2Int(x, y), 1f);
        Assert.AreEqual(20 * 0.35f, total, 1e-4f);
        float expected = 1f - (float)Math.Exp(-0.05f * total);

        int trials = 3000;
        int fires = 0;
        for (int i = 0; i < trials; i++)
        {
            sim.Disasters.Fire.Step();
            fires += sim.Disasters.Fire.Ignitions;
            Clear(sim);
        }

        Assert.AreEqual(expected, fires / (float)trials, 0.035f);
    }

    [Test]
    public void TheBlockIsPickedInProportionToItsRisk()
    {
        Tune("m_FireIgnitionPerRisk", 1000f);
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid);
        Grow(grid, 2, 2, ZoneType.Residential);
        Grow(grid, 8, 8, ZoneType.Industrial);     // x1.5 fire risk

        int industrial = 0;
        int trials = 2000;
        for (int i = 0; i < trials; i++)
        {
            sim.Disasters.Fire.Step();
            if (sim.Disasters.Fire.IsBurning(new Vector2Int(8, 8))) industrial++;
            Clear(sim);
        }

        Assert.AreEqual(0.6f, industrial / (float)trials, 0.04f);
    }

    [Test]
    public void FireCoverLowersTheIgnitionRate()
    {
        Tune("m_FireIgnitionPerRisk", 0.05f);
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid);
        Grow(grid, 2, 2);
        float open = sim.Civic.GrownFireRisk(new Vector2Int(2, 2), 1f);
        sim.Sources = new[] { FireStation(new Vector2Int(2, 3), 4, 0.75f) };
        float covered = sim.Civic.GrownFireRisk(new Vector2Int(2, 2), 1f);
        Assert.AreEqual(open * 0.25f, covered, 1e-5f);
    }

    [Test]
    public void GrownFireRisk_MatchesExplain()
    {
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid);
        Grow(grid, 2, 2);
        Grow(grid, 3, 3, ZoneType.Industrial);
        sim.Sources = new[] { FireStation(new Vector2Int(2, 3), 4, 0.6f) };
        foreach (Vector2Int cell in new[] { new Vector2Int(2, 2), new Vector2Int(3, 3), new Vector2Int(9, 9) })
        {
            Assert.AreEqual(sim.Civic.Explain(cell).FireRisk, sim.Civic.GrownFireRisk(cell, sim.Civic.Ramp), 1e-6f, cell.ToString());
        }
    }

    // --- Spread ---

    private bool SpreadsFrom(GridData grid, SimulationSystem sim, Vector2Int from, Vector2Int to)
    {
        sim.Disasters.Fire.Ignite(from);
        sim.Disasters.Fire.Step();     // the day after: it acts now
        bool burning = sim.Disasters.Fire.IsBurning(to);
        Clear(sim);
        return burning;
    }

    [Test]
    public void AFireSetsItsNeighbourAlight_ButNotOnTheDayItWasLit()
    {
        CertainSpread();
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid, population: 0);     // no random ignition
        Grow(grid, 2, 2);
        Grow(grid, 3, 2);
        Grow(grid, 4, 2);

        sim.Disasters.Fire.Ignite(new Vector2Int(2, 2));
        Assert.IsFalse(sim.Disasters.Fire.IsBurning(new Vector2Int(3, 2)), "lit today: not yet spreading");
        sim.Disasters.Fire.Step();
        Assert.IsTrue(sim.Disasters.Fire.IsBurning(new Vector2Int(3, 2)));
        Assert.IsFalse(sim.Disasters.Fire.IsBurning(new Vector2Int(4, 2)), "a fire lit today does not chain in one day");
        Assert.AreEqual(2, sim.Disasters.Fire.FireDays(new Vector2Int(2, 2)));
        Assert.AreEqual(1, sim.Disasters.Fire.FireDays(new Vector2Int(3, 2)));
    }

    [Test]
    public void ARoadIsAFirebreak_ExceptAOneCellStreetAtTheJumpChance()
    {
        CertainSpread();
        Tune("m_FireJumpFactor", 1f);
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid, population: 0);
        Grow(grid, 2, 2);
        grid.SetRoad(new Vector2Int(3, 2), true);
        Grow(grid, 4, 2);                                     // across a 1-wide street
        grid.SetRoad(new Vector2Int(2, 6), true);
        grid.SetRoad(new Vector2Int(2, 7), true);
        Grow(grid, 2, 5);
        Grow(grid, 2, 8);                                     // across a 2-wide street from (2,5)

        Assert.IsTrue(SpreadsFrom(grid, sim, new Vector2Int(2, 2), new Vector2Int(4, 2)), "jumps a 1-wide street");
        for (int i = 0; i < 20; i++)
        {
            Assert.IsFalse(SpreadsFrom(grid, sim, new Vector2Int(2, 5), new Vector2Int(2, 8)), "a 2-wide street stops it");
        }

        Tune("m_FireJumpFactor", 0f);
        for (int i = 0; i < 20; i++)
        {
            Assert.IsFalse(SpreadsFrom(grid, sim, new Vector2Int(2, 2), new Vector2Int(4, 2)), "no jump at factor 0");
        }
    }

    [Test]
    public void IndustrialBlocksCatchFireOneAndAHalfTimesAsReadily()
    {
        Tune("m_FireSpreadChance", 0.4f);
        Tune("m_FireExtinguishBase", 0f);
        Tune("m_FireExtinguishPerCover", 0f);
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid, population: 0);
        Grow(grid, 2, 2);
        Grow(grid, 3, 2, ZoneType.Industrial);
        int trials = 1500;
        int spread = 0;
        for (int i = 0; i < trials; i++) if (SpreadsFrom(grid, sim, new Vector2Int(2, 2), new Vector2Int(3, 2))) spread++;
        Assert.AreEqual(0.6f, spread / (float)trials, 0.045f);
    }

    [Test]
    public void FireCoverShieldsABlockFromSpread()
    {
        CertainSpread();
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid, population: 0);
        Grow(grid, 2, 2);
        Grow(grid, 3, 2);
        Assert.IsTrue(SpreadsFrom(grid, sim, new Vector2Int(2, 2), new Vector2Int(3, 2)));

        sim.Sources = new[] { FireStation(new Vector2Int(3, 3), 3, 1f) };
        for (int i = 0; i < 30; i++)
        {
            Assert.IsFalse(SpreadsFrom(grid, sim, new Vector2Int(2, 2), new Vector2Int(3, 2)), "full cover leaves nothing to catch");
        }
    }

    // --- Putting fires out ---

    private int Extinguished(SimulationSystem sim, Vector2Int cell, int trials)
    {
        int out_ = 0;
        for (int i = 0; i < trials; i++)
        {
            sim.Disasters.Fire.Ignite(cell);
            sim.Disasters.Fire.Step();
            out_ += sim.Disasters.Fire.Extinguished;
            Clear(sim);
        }
        return out_;
    }

    [Test]
    public void FireCoverPutsFiresOut_AndTheBaseChanceDoesWithoutIt()
    {
        Tune("m_FireExtinguishBase", 0.1f);
        Tune("m_FireExtinguishPerCover", 0.7f);
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid, population: 0);
        Grow(grid, 2, 2);
        grid.SetRoad(new Vector2Int(0, 5), true);

        // The test age's water rule is Piped from Industrial on: give the block water so only cover differs.
        float uncovered = Extinguished(sim, new Vector2Int(2, 2), 1500) / 1500f;
        sim.Sources = new[] { FireStation(new Vector2Int(2, 3), 3, 1f) };
        float covered = Extinguished(sim, new Vector2Int(2, 2), 1500) / 1500f;

        Assert.Greater(covered, uncovered + 0.2f);
        Assert.AreEqual(0.4f, covered, 0.05f, "(0.1 + 0.7) with no piped water: x DryExtinguishFactor 0.5");
        Assert.AreEqual(0.05f, uncovered, 0.03f);
    }

    [Test]
    public void ABlockWithoutWaterIsHarderToSave_InAWaterAge()
    {
        Tune("m_FireExtinguishBase", 0.4f);
        Tune("m_FireExtinguishPerCover", 0f);
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid, TestAges.Medieval, population: 0);     // wells: a block needs one in reach
        Grow(grid, 2, 2, age: TestAges.Medieval);

        float dry = Extinguished(sim, new Vector2Int(2, 2), 1500) / 1500f;
        sim.Sources = new[] { new ServiceSource(new Vector2Int(2, 3), Vector2Int.one, 0, 0, waterRadius: 4) };
        float watered = Extinguished(sim, new Vector2Int(2, 2), 1500) / 1500f;

        Assert.AreEqual(0.2f, dry, 0.04f, "0.4 x DryExtinguishFactor 0.5");
        Assert.AreEqual(0.4f, watered, 0.045f);
    }

    // --- Burning down ---

    [Test]
    public void ABlockBurnsDownAfterFireBurnDays_AndLeavesRubbleThatClears()
    {
        Tune("m_FireExtinguishBase", 0f);
        Tune("m_FireExtinguishPerCover", 0f);
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid, population: 0);
        var cell = new Vector2Int(2, 2);
        for (int x = 0; x < 12; x++) grid.SetRoad(new Vector2Int(x, 0), true);
        Grow(grid, 2, 2);
        FireSystem fire = sim.Disasters.Fire;

        Assert.IsTrue(fire.Ignite(cell));
        fire.Step();
        fire.Step();
        Assert.IsTrue(fire.IsBurning(cell));
        Assert.AreEqual(1, grid.GetBuildingLevel(cell));
        fire.Step();      // the third day it survived: gone
        Assert.IsFalse(fire.IsBurning(cell));
        Assert.AreEqual(0, grid.GetBuildingLevel(cell));
        Assert.AreEqual(1, fire.LostBlocks);
        Assert.IsTrue(sim.Disasters.IsRubble(cell));
        Assert.AreEqual(ZoneType.Residential, grid.GetZone(cell), "the zone stays, so it regrows");

        Assert.AreEqual(GrowthBlocker.Rubble, sim.Growth.GetBlocker(cell, sim.Demand.Snapshot));
        for (int day = 0; day < m_Config.RubbleDays; day++) fire.Step();
        Assert.IsFalse(sim.Disasters.IsRubble(cell));
    }

    [Test]
    public void RubbleBlocksGrowth_UntilItClears()
    {
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid, population: 0);
        for (int x = 0; x < 12; x++) grid.SetRoad(new Vector2Int(x, 5), true);
        var cell = new Vector2Int(3, 4);
        grid.SetZone(cell, ZoneType.Residential);
        sim.Disasters.Rubble[4 * 12 + 3] = 5;

        var high = new DemandSnapshot(1f, 1f, 1f);
        Assert.AreEqual(GrowthBlocker.Rubble, sim.Growth.GetBlocker(cell, high));
        sim.Growth.Apply(high);
        Assert.AreEqual(0, grid.GetBuildingLevel(cell), "no growth on rubble");

        sim.Disasters.Rubble[4 * 12 + 3] = 0;
        Assert.AreNotEqual(GrowthBlocker.Rubble, sim.Growth.GetBlocker(cell, high));
        sim.Growth.Apply(high);
        Assert.AreEqual(1, grid.GetBuildingLevel(cell));
    }

    [Test]
    public void ADemolishedBuildingTakesItsFireWithIt()
    {
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid, population: 0);
        Grow(grid, 2, 2);
        sim.Disasters.Fire.Ignite(new Vector2Int(2, 2));
        grid.SetBuildingLevel(new Vector2Int(2, 2), 0);

        sim.Disasters.Fire.Step();

        Assert.AreEqual(0, sim.Disasters.Fire.BurningCount);
    }

    // --- Placed buildings ---

    [Test]
    public void APlacedBuildingBurnsAsOneAndIsReleasedAndDroppedFromTheSources()
    {
        CertainSpread();
        Tune("m_PlacedFlammability", 1f);
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid, population: 0);
        sim.Disasters.Enabled = true;
        Grow(grid, 1, 2);
        var origin = new Vector2Int(2, 2);
        Assert.IsTrue(grid.Occupy(origin, new Vector2Int(2, 2), 0, 7));
        ServiceSource park = new ServiceSource(origin, new Vector2Int(2, 2), 4, 0);
        sim.Sources = new[] { park };
        IReadOnlyList<int> reported = null;
        sim.BuildingsDestroyed += ids => reported = new List<int>(ids);

        sim.Disasters.Fire.Ignite(new Vector2Int(1, 2));      // the block beside the park
        sim.Tick();                                           // spreads into the park: all four cells at once
        foreach (Vector2Int c in new[] { new Vector2Int(2, 2), new Vector2Int(3, 2), new Vector2Int(2, 3), new Vector2Int(3, 3) })
        {
            Assert.IsTrue(sim.Disasters.Fire.IsBurning(c), c.ToString());
        }
        Assert.AreEqual(1, sim.Sources.Count);

        sim.Tick();
        sim.Tick();
        sim.Tick();
        sim.Tick();

        Assert.IsFalse(grid.IsOccupied(origin), "the footprint is free again");
        Assert.AreEqual(0, sim.Sources.Count, "the park stops counting at once");
        Assert.IsNotNull(reported);
        CollectionAssert.AreEqual(new[] { 7 }, reported);
        foreach (Vector2Int c in new[] { new Vector2Int(2, 2), new Vector2Int(3, 2), new Vector2Int(2, 3), new Vector2Int(3, 3) })
        {
            Assert.IsTrue(sim.Disasters.IsRubble(c), c.ToString());
        }
        Assert.AreEqual(0, sim.Coverage.GetCoverage(new Vector2Int(2, 2)), "its coverage is gone");
    }

    [Test]
    public void PlacedBuildingsCatchFireAtThePlacedFlammability()
    {
        Tune("m_FireSpreadChance", 0.8f);
        Tune("m_FireExtinguishBase", 0f);
        Tune("m_FireExtinguishPerCover", 0f);
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid, population: 0);
        Grow(grid, 1, 2);
        Assert.IsTrue(grid.Occupy(new Vector2Int(2, 2), Vector2Int.one, 0, 3));
        int trials = 1500;
        int caught = 0;
        for (int i = 0; i < trials; i++) if (SpreadsFrom(grid, sim, new Vector2Int(1, 2), new Vector2Int(2, 2))) caught++;
        Assert.AreEqual(0.8f * 0.5f, caught / (float)trials, 0.045f);
    }

    // --- Techs ---

    [Test]
    public void AHazardTechHalvesTheSpread()
    {
        TechDefinition steel = ScriptableObject.CreateInstance<TechDefinition>();
        steel.Init("steel_test", 0, 1f, null, new[] { new TechEffect(TechEffectType.HazardMultiplier, "FireSpread", 0.5f) });
        m_Created.Add(steel);
        TechModifiers folded = TechModifiers.Fold(new[] { steel });

        CertainSpread();
        var grid = new GridData(12, 12);
        SimulationSystem sim = NewSim(grid, population: 0);
        var halved = new DisasterSystem(grid, m_Config, true, sim.Civic, sim.Water, () => folded, () => TestAges.Industrial);
        Grow(grid, 2, 2);
        Grow(grid, 3, 2);

        int trials = 1500;
        int spread = 0;
        for (int i = 0; i < trials; i++)
        {
            halved.Fire.Ignite(new Vector2Int(2, 2));
            halved.Fire.Step();
            if (halved.Fire.IsBurning(new Vector2Int(3, 2))) spread++;
            Array.Clear(halved.Fires, 0, halved.Fires.Length);
        }
        Assert.AreEqual(0.5f, spread / (float)trials, 0.045f);
    }

    // --- Determinism, switch and saves ---

    [Test]
    public void TheSwitchOff_NothingBurnsAndNothingIsDrawn()
    {
        var grid = new GridData(24, 24);
        SimulationSystem sim = SeededCity.Build(grid, m_Config, ages: m_TestAges.Ages, techs: m_TestAges.Techs, startAge: TestAges.Industrial);
        string state = sim.Disasters.Random.StateString;
        for (int day = 0; day < 90; day++) sim.Tick();
        Assert.AreEqual(state, sim.Disasters.Random.StateString);
        Assert.AreEqual(0, sim.Disasters.Fire.BurningCount);
    }

    [Test]
    public void SameSeedSameBurning()
    {
        string a = Burn(5);
        string b = Burn(5);
        Assert.AreEqual(a, b);
        Assert.AreNotEqual(a, Burn(6));
    }

    private string Burn(ulong seed)
    {
        Tune("m_FireIgnitionPerRisk", 0.02f);
        var grid = new GridData(24, 24);
        SimulationSystem sim = SeededCity.Build(grid, m_Config, ages: m_TestAges.Ages, techs: m_TestAges.Techs, startAge: TestAges.Industrial);
        sim.Disasters.Enabled = true;
        sim.Disasters.Random.Seed(seed);
        for (int day = 0; day < 120; day++) sim.Tick();
        return Convert.ToBase64String(sim.Disasters.Fires) + "|" + Convert.ToBase64String(sim.Disasters.Rubble) + "|"
            + Convert.ToBase64String(grid.ExportLevels()) + "|" + sim.Disasters.Random.StateString;
    }

    [Test]
    public void SaveLoadContinue_WithAFireBurning_EqualsAnUninterruptedRun()
    {
        Tune("m_FireIgnitionPerRisk", 0.02f);
        var grid = new GridData(24, 24);
        SimulationSystem sim = SeededCity.Build(grid, m_Config, plantSupply: 0, waterSupply: 0,
            ages: m_TestAges.Ages, techs: m_TestAges.Techs, startAge: TestAges.Industrial);
        sim.Disasters.Enabled = true;
        sim.Disasters.Random.Seed(3);
        for (int day = 0; day < 50; day++) sim.Tick();

        // Light the first grown block so a fire is certainly under way when the city is saved.
        Vector2Int target = default;
        bool found = false;
        for (int i = 0; i < 24 * 24 && !found; i++)
        {
            var cell = new Vector2Int(i % 24, i / 24);
            if (grid.GetBuildingLevel(cell) > 0 && !sim.Disasters.Fire.IsBurning(cell)) { target = cell; found = true; }
        }
        Assert.IsTrue(found);
        Assert.IsTrue(sim.Disasters.Fire.Ignite(target));
        sim.Tick();

        string json = SaveSystem.ToJson(SaveSystem.Capture(grid, sim));
        Assert.IsTrue(SaveSystem.TryFromJson(json, out SaveData loaded, out string error, m_TestAges.Ages, m_TestAges.Techs), error);
        var grid2 = new GridData(24, 24);
        var sim2 = new SimulationSystem(grid2, new RoadNetwork(grid2), m_Config, m_TestAges.Ages, m_TestAges.Techs);
        SaveSystem.ApplyGrid(loaded, grid2);
        SaveSystem.ApplySimulation(loaded, sim2);
        Assert.IsTrue(sim2.Disasters.Fire.BurningCount > 0, "the fire was saved");

        for (int day = 0; day < 40; day++)
        {
            sim.Tick();
            sim2.Tick();
        }

        CollectionAssert.AreEqual(sim.Disasters.Fires, sim2.Disasters.Fires);
        CollectionAssert.AreEqual(sim.Disasters.Rubble, sim2.Disasters.Rubble);
        CollectionAssert.AreEqual(grid.ExportLevels(), grid2.ExportLevels());
        Assert.AreEqual(sim.Population.Population, sim2.Population.Population);
        Assert.AreEqual(sim.Disasters.Random.StateString, sim2.Disasters.Random.StateString);
    }
}
