using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// M15c: ordinances (OrdinanceDefinition, TechSystem enact / repeal), their folded effects, daily cost,
// their reach into crime / fire risk / sickness, the separate happiness term, the Banking loan
// effect and the save round trip. Plays the shipped content.
public sealed class OrdinanceTests
{
    private const string AgeDatabasePath = "Assets/_Game/Scriptables/Ages/AgeDatabase.asset";
    private const string TechDatabasePath = "Assets/_Game/Scriptables/Techs/TechDatabase.asset";

    private BalanceConfig m_Config;
    private AgeDatabase m_Ages;
    private TechDatabase m_Techs;

    [SetUp]
    public void SetUp()
    {
        m_Config = ScriptableObject.CreateInstance<BalanceConfig>();
        m_Ages = AssetDatabase.LoadAssetAtPath<AgeDatabase>(AgeDatabasePath);
        m_Techs = AssetDatabase.LoadAssetAtPath<TechDatabase>(TechDatabasePath);
        Assert.IsNotNull(m_Ages);
        Assert.IsNotNull(m_Techs);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(m_Config);
    }

    private SimulationSystem Industrial(out GridData grid)
    {
        grid = new GridData(24, 24);
        return SeededCity.Build(grid, m_Config, ages: m_Ages, techs: m_Techs, startAge: m_Ages.Legacy);
    }

    private OrdinanceDefinition Get(string id)
    {
        OrdinanceDefinition ordinance = m_Techs.GetOrdinanceById(id);
        Assert.IsNotNull(ordinance, id);
        return ordinance;
    }

    private static void Run(SimulationSystem sim, int days)
    {
        for (int day = 0; day < days; day++) sim.Tick();
    }

    [Test]
    public void Enact_NeedsTheTech_AndOnlyOnce()
    {
        var grid = new GridData(24, 24);
        SimulationSystem medieval = SeededCity.Build(grid, m_Config, ages: m_Ages, techs: m_Techs, startAge: 0);
        OrdinanceDefinition curfew = Get("curfew");
        Assert.IsFalse(medieval.Tech.IsUnlocked(curfew), "Town Watch is not researched at the start of the Medieval age");
        Assert.IsFalse(medieval.Tech.Enact(curfew));

        SimulationSystem industrial = Industrial(out _);
        Assert.IsTrue(industrial.Tech.IsUnlocked(curfew));
        Assert.IsTrue(industrial.Tech.Enact(curfew));
        Assert.IsTrue(industrial.Tech.IsEnacted(curfew));
        Assert.IsFalse(industrial.Tech.Enact(curfew), "already enacted");
        Assert.IsTrue(industrial.Tech.Repeal(curfew));
        Assert.IsFalse(industrial.Tech.Repeal(curfew), "not enacted any more");
    }

    [Test]
    public void EnactAndRepeal_FoldAndUnfoldTheEffects()
    {
        SimulationSystem sim = Industrial(out _);
        TechModifiers before = sim.TechModifiers;
        float demandBefore = before.DemandMultiplier(ZoneType.Commercial);

        sim.Tech.Enact(Get("curfew"));
        TechModifiers during = sim.TechModifiers;
        Assert.AreEqual(0.7f, during.CivicNeed(ServiceKind.Order), 1e-6f);
        Assert.AreEqual(1f, during.CivicNeed(ServiceKind.Fire), 1e-6f);
        Assert.AreEqual(-0.01f, during.OrdinanceHappiness, 1e-6f);
        Assert.AreEqual(before.HappinessBonus, during.HappinessBonus, 1e-6f, "techs' happiness is a separate term");
        Assert.AreEqual(demandBefore * 0.9f, during.DemandMultiplier(ZoneType.Commercial), 1e-5f);

        sim.Tech.Repeal(Get("curfew"));
        Assert.AreEqual(1f, sim.TechModifiers.CivicNeed(ServiceKind.Order), 1e-6f);
        Assert.AreEqual(0f, sim.TechModifiers.OrdinanceHappiness, 1e-6f);
        Assert.AreEqual(demandBefore, sim.TechModifiers.DemandMultiplier(ZoneType.Commercial), 1e-5f);
    }

    [Test]
    public void DailyCost_IsFlatPlusPerResident_AndShowsInTheLedger()
    {
        SimulationSystem sim = Industrial(out _);
        sim.Tech.Enact(Get("curfew"));        // $3 flat
        sim.Tech.Enact(Get("feast_days"));    // $0.03 per resident
        Assert.AreEqual(3f + 30f, sim.Tech.OrdinanceCostPerDay(1000), 1e-3f);

        BudgetBreakdown ledger = sim.Ledger();
        float population = sim.Population.Population;
        Assert.AreEqual(3f + 0.03f * population, ledger.Ordinances, 1e-3f);
        float upkeep = 0f;
        foreach (float line in ledger.UpkeepByLine) upkeep += line;
        Assert.AreEqual((upkeep + ledger.OtherUpkeep + ledger.Roads + ledger.Pipes) * ledger.TechUpkeepMultiplier
            + ledger.Loans + ledger.Ordinances, ledger.Expense, 1e-3f, "the parts add up");
    }

    [Test]
    public void Ordinances_ReduceCrimeFireRiskAndSickness_ByTheirMultipliers()
    {
        SimulationSystem sim = Industrial(out GridData grid);
        Run(sim, 90);
        Assert.Greater(sim.Population.Population, 150, "past the civic ramp");

        Vector2Int home = default;
        bool found = false;
        for (int y = 0; y < grid.Height && !found; y++)
        {
            for (int x = 0; x < grid.Width && !found; x++)
            {
                var cell = new Vector2Int(x, y);
                if (grid.GetZone(cell) == ZoneType.Residential && grid.GetBuildingLevel(cell) > 0 && sim.Civic.GetCrime(cell) > 0f)
                {
                    home = cell;
                    found = true;
                }
            }
        }
        Assert.IsTrue(found, "a grown home with crime");

        float crime = sim.Civic.GetCrime(home);
        float fire = sim.Civic.GetFireRisk(home);
        float sickness = sim.Civic.GetSickness(home);   // herb gardens: free clinics need an Industrial tech
        float penalty = sim.MeasureServices().CrimePenalty;
        Assert.Greater(fire, 0f);
        Assert.Greater(sickness, 0f);

        sim.Tech.Enact(Get("curfew"));
        sim.Tech.Enact(Get("fire_code"));
        sim.Tech.Enact(Get("herb_gardens"));

        Assert.AreEqual(crime * 0.7f, sim.Civic.GetCrime(home), 1e-6f);
        Assert.AreEqual(fire * 0.7f, sim.Civic.GetFireRisk(home), 1e-6f);
        Assert.AreEqual(sickness * 0.85f, sim.Civic.GetSickness(home), 1e-6f);
        Assert.AreEqual(sim.Civic.GetCrime(home), sim.Civic.Explain(home).Crime, 1e-6f, "the fast path and Explain agree");
        Assert.Less(sim.MeasureServices().CrimePenalty, penalty);
    }

    [Test]
    public void OrdinanceHappiness_IsItsOwnTerm_AndTechnologyIsUntouched()
    {
        SimulationSystem sim = Industrial(out _);
        Run(sim, 1);
        float technology = sim.Population.Happiness.Technology;
        Assert.AreEqual(0f, sim.Population.Happiness.Ordinances, 1e-6f);

        sim.Tech.Enact(Get("feast_days"));
        Run(sim, 1);
        Assert.AreEqual(0.03f, sim.Population.Happiness.Ordinances, 1e-6f);
        Assert.AreEqual(technology, sim.Population.Happiness.Technology, 1e-6f);
    }

    [Test]
    public void Banking_HalvesLoanInterest()
    {
        var grid = new GridData(24, 24);
        SimulationSystem medieval = SeededCity.Build(grid, m_Config, ages: m_Ages, techs: m_Techs, startAge: 0);
        Assert.IsTrue(medieval.TakeLoan());
        Assert.AreEqual(10000f * 1.10f / 360f, medieval.Budget.Loans[0].DailyPayment, 1e-3f, "no Banking yet: 10%");

        SimulationSystem industrial = Industrial(out _);
        Assert.IsTrue(industrial.TakeLoan());
        Assert.AreEqual(25000f * 1.05f / 360f, industrial.Budget.Loans[0].DailyPayment, 1e-3f, "Banking researched: 5%");
    }

    [Test]
    public void Save_KeepsEnactedOrdinances_AndDropsUnknownOnes()
    {
        SimulationSystem sim = Industrial(out GridData grid);
        sim.Tech.Enact(Get("curfew"));
        sim.Tech.Enact(Get("street_lighting"));
        Run(sim, 10);

        SaveData saved = SaveSystem.Capture(grid, sim);
        CollectionAssert.AreEquivalent(new[] { "curfew", "street_lighting" }, saved.Ordinances);

        SimulationSystem loaded = Load(saved, out int dropped);
        Assert.AreEqual(0, dropped);
        Assert.IsTrue(loaded.Tech.IsEnacted(Get("curfew")));
        Assert.IsTrue(loaded.Tech.IsEnacted(Get("street_lighting")));
        Assert.IsFalse(loaded.Tech.IsEnacted(Get("feast_days")));
        Assert.AreEqual(0.7f * 0.8f, loaded.TechModifiers.CivicNeed(ServiceKind.Order), 1e-6f);

        saved.Ordinances.Add("no_such_ordinance");
        Load(saved, out dropped);
        Assert.AreEqual(1, dropped);
    }

    [Test]
    public void SaveWithOrdinances_ThenContinue_MatchesUninterruptedRun()
    {
        SimulationSystem a = Industrial(out GridData gridA);
        a.Tech.Enact(Get("feast_days"));
        a.Tech.Enact(Get("curfew"));
        Run(a, 40);

        SimulationSystem b = Load(SaveSystem.Capture(gridA, a), out _, a.Sources, a.Modifiers);
        Run(a, 30);
        Run(b, 30);

        Assert.AreEqual(a.Population.Population, b.Population.Population);
        Assert.AreEqual(a.Population.AverageHappiness, b.Population.AverageHappiness);
        Assert.AreEqual(a.Economy.Money, b.Economy.Money, 1e-2f);
    }

    // Save -> JSON -> load into a fresh sim with the shipped databases (the steps SaveGameController takes).
    private SimulationSystem Load(SaveData saved, out int dropped, IReadOnlyList<ServiceSource> sources = null,
        CityModifiers modifiers = default)
    {
        Assert.IsTrue(SaveSystem.TryFromJson(SaveSystem.ToJson(saved), out SaveData data, out string error, m_Ages, m_Techs), error);
        var grid = new GridData(data.Width, data.Height);
        var sim = new SimulationSystem(grid, new RoadNetwork(grid), m_Config, m_Ages, m_Techs);
        SaveSystem.ApplyGrid(data, grid);
        if (sources != null) sim.Sources = sources;
        sim.Modifiers = modifiers;
        dropped = SaveSystem.ApplySimulation(data, sim);
        return sim;
    }
}
