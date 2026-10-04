using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// M15a: funding per budget line (BudgetSystem), the funded sources the spatial systems see, and the
// ledger that Tick charges.
public sealed class BudgetTests
{
    private readonly List<Object> m_Created = new();
    private BalanceConfig m_Config;
    private GridData m_Grid;

    [SetUp]
    public void SetUp()
    {
        m_Config = Make<BalanceConfig>();
        m_Grid = new GridData(24, 24);
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

    private SimulationSystem Empty(params ServiceSource[] sources)
    {
        var sim = new SimulationSystem(m_Grid, new RoadNetwork(m_Grid), m_Config);
        sim.Sources = sources;
        return sim;
    }

    private static ServiceSource Civic(Vector2Int at, ServiceKind kind, int radius, float strength, float upkeep = 0f,
        float research = 0f) =>
        new ServiceSource(at, Vector2Int.one, 0, 0, civicKind: kind, civicRadius: radius, civicStrength: strength,
            upkeepPerDay: upkeep, researchPerDay: research);

    [Test]
    public void Factors_FollowTheCurves()
    {
        Assert.AreEqual(0.5f, BudgetSystem.EffectFactorAt(m_Config, 0.5f), 1e-6f);
        Assert.AreEqual(1f, BudgetSystem.EffectFactorAt(m_Config, 1f), 1e-6f);
        Assert.AreEqual(1.25f, BudgetSystem.EffectFactorAt(m_Config, 1.5f), 1e-6f);
        Assert.AreEqual(0.75f, BudgetSystem.ReachFactorAt(m_Config, 0.5f), 1e-6f);
        Assert.AreEqual(1f, BudgetSystem.ReachFactorAt(m_Config, 1f), 1e-6f);
        Assert.AreEqual(1.125f, BudgetSystem.ReachFactorAt(m_Config, 1.5f), 1e-6f);
    }

    [Test]
    public void SetFunding_ClampsAndSnapsToTheStep()
    {
        var budget = new BudgetSystem(m_Config);
        Assert.IsTrue(budget.IsDefault);
        budget.SetFunding(BudgetLine.Fire, 0.2f);
        Assert.AreEqual(0.5f, budget.GetFunding(BudgetLine.Fire), 1e-6f);
        budget.SetFunding(BudgetLine.Fire, 2f);
        Assert.AreEqual(1.5f, budget.GetFunding(BudgetLine.Fire), 1e-6f);
        budget.SetFunding(BudgetLine.Fire, 0.84f);
        Assert.AreEqual(0.8f, budget.GetFunding(BudgetLine.Fire), 1e-6f);
        Assert.IsFalse(budget.IsDefault);
        Assert.AreEqual(1f, budget.GetFunding(BudgetLine.Order), "other lines untouched");
    }

    [Test]
    public void DefaultFunding_FeedsThePlacedSourcesUnchanged()
    {
        SimulationSystem sim = SeededCity.Build(m_Grid, m_Config, parks: true);
        Assert.IsTrue(sim.Budget.IsDefault);
        Assert.AreEqual(SeededCity.PlantSupply, sim.Power.Supply);
        Assert.AreEqual(1f, sim.Budget.EffectFactor(BudgetLine.Parks));
    }

    [Test]
    public void Fund_ScalesCivicStrengthAndReach_StrengthCappedAtOne()
    {
        var budget = new BudgetSystem(m_Config);
        ServiceSource police = Civic(new Vector2Int(4, 4), ServiceKind.Order, 8, 1f);
        ServiceSource apothecary = Civic(new Vector2Int(8, 8), ServiceKind.Health, 4, 0.5f);

        budget.SetFunding(BudgetLine.Order, 0.5f);
        budget.SetFunding(BudgetLine.Health, 0.5f);
        Assert.AreEqual(0.5f, budget.Fund(police).CivicStrength, 1e-6f);
        Assert.AreEqual(6, budget.Fund(police).CivicRadius);
        Assert.AreEqual(0.25f, budget.Fund(apothecary).CivicStrength, 1e-6f);
        Assert.AreEqual(3, budget.Fund(apothecary).CivicRadius);

        budget.SetFunding(BudgetLine.Order, 1.5f);
        Assert.AreEqual(1f, budget.Fund(police).CivicStrength, 1e-6f, "a full-strength tier only gains reach");
        Assert.AreEqual(9, budget.Fund(police).CivicRadius);
    }

    [Test]
    public void Fund_ReachNeverDropsBelowOne()
    {
        var budget = new BudgetSystem(m_Config);
        budget.SetFunding(BudgetLine.Parks, 0.5f);
        var park = new ServiceSource(Vector2Int.zero, Vector2Int.one, 1, 0);
        Assert.AreEqual(1, budget.Fund(park).CoverageRadius);
        Assert.AreEqual(0, budget.Fund(new ServiceSource(Vector2Int.zero, Vector2Int.one, 0, 0)).CoverageRadius, "no reach stays none");
    }

    [Test]
    public void CivicCover_FollowsFundingThroughTheSim()
    {
        Vector2Int cell = new Vector2Int(10, 4);
        SimulationSystem sim = Empty(Civic(new Vector2Int(4, 4), ServiceKind.Order, 8, 1f));
        Assert.AreEqual(1f, sim.Civic.Cover.GetStrength(ServiceKind.Order, cell), 1e-6f);

        sim.Budget.SetFunding(BudgetLine.Order, 0.5f);
        Assert.AreEqual(0.5f, sim.Civic.Cover.GetStrength(ServiceKind.Order, cell), 1e-6f);
        Assert.AreEqual(0f, sim.Civic.Cover.GetStrength(ServiceKind.Order, new Vector2Int(11, 4)), "reach 6 now stops at x = 10");

        sim.Budget.SetFunding(BudgetLine.Order, 1f);
        Assert.AreEqual(1f, sim.Civic.Cover.GetStrength(ServiceKind.Order, new Vector2Int(11, 4)), 1e-6f);
    }

    [Test]
    public void Power_SupplyFollowsPowerFunding_AndUnderfundingBrownsOut()
    {
        SimulationSystem sim = SeededCity.Run(m_Grid, m_Config, 120);
        Assert.AreEqual(600, sim.Power.Supply);
        Assert.Greater(sim.Power.Demand, 300, "the seeded city has outgrown half a plant");
        Assert.AreEqual(0, sim.Power.UnpoweredCells, "a full plant: nothing dark");

        sim.Budget.SetFunding(BudgetLine.Power, 0.5f);
        Assert.AreEqual(300, sim.Power.Supply);
        Assert.Greater(sim.Power.UnpoweredCells, 0, "half a plant leaves the furthest blocks dark");

        sim.Budget.SetFunding(BudgetLine.Power, 1.5f);
        Assert.AreEqual(750, sim.Power.Supply);
        Assert.AreEqual(0, sim.Power.UnpoweredCells);
    }

    [Test]
    public void Water_WellReachAndTowerSupplyFollowWaterFunding()
    {
        Vector2Int near = new Vector2Int(8 + 7, 8);
        var well = new ServiceSource(new Vector2Int(8, 8), Vector2Int.one, 0, 0, waterRadius: 8);
        SimulationSystem sim = Empty(well);
        Assert.AreEqual(1, sim.Water.Coverage.GetCoverage(near));
        sim.Budget.SetFunding(BudgetLine.Water, 0.5f);
        Assert.AreEqual(0, sim.Water.Coverage.GetCoverage(near), "reach 8 -> 6");
        Assert.AreEqual(1, sim.Water.Coverage.GetCoverage(new Vector2Int(8 + 6, 8)));

        var tower = new ServiceSource(new Vector2Int(2, 2), Vector2Int.one, 0, 0, waterSupply: 100);
        Assert.AreEqual(50, sim.Budget.Fund(tower).WaterSupply, "tower supply 100 x 0.5");
    }

    [Test]
    public void Fountain_CheerFollowsParks_UpkeepIsChargedToWater()
    {
        // A fountain is a park (CoverageRadius) and a well (WaterRadius).
        var fountain = new ServiceSource(new Vector2Int(8, 8), Vector2Int.one, 8, 0, waterRadius: 8, upkeepPerDay: 10f);
        SimulationSystem sim = Empty(fountain);
        sim.Modifiers = new CityModifiers { UpkeepPerDay = 10f };
        sim.Budget.SetFunding(BudgetLine.Parks, 0.5f);

        Assert.AreEqual(0, sim.Coverage.GetCoverage(new Vector2Int(8 + 7, 8)), "park reach 8 -> 6");
        Assert.AreEqual(1, sim.Water.Coverage.GetCoverage(new Vector2Int(8 + 7, 8)), "water reach untouched");
        BudgetBreakdown ledger = sim.Ledger();
        Assert.AreEqual(10f, ledger.UpkeepByLine[(int)BudgetLine.Water], 1e-4f);
        Assert.AreEqual(0f, ledger.UpkeepByLine[(int)BudgetLine.Parks], 1e-4f);
        Assert.AreEqual(10f, ledger.Expense, 1e-4f, "parks funding does not touch the fountain's upkeep");
    }

    [Test]
    public void SchoolResearch_FollowsEducationFunding()
    {
        var budget = new BudgetSystem(m_Config);
        ServiceSource school = Civic(new Vector2Int(4, 4), ServiceKind.Education, 5, 0.4f, 10f, 4f);
        var sources = new[] { school };
        Assert.AreEqual(0f, budget.ResearchDelta(sources));
        budget.SetFunding(BudgetLine.Education, 1.5f);
        Assert.AreEqual(1f, budget.ResearchDelta(sources), 1e-5f, "4 RP x (1.25 - 1)");
        budget.SetFunding(BudgetLine.Education, 0.5f);
        Assert.AreEqual(-2f, budget.ResearchDelta(sources), 1e-5f, "4 RP x (0.5 - 1)");
    }

    [Test]
    public void ParkCheer_ScalesWithParksFunding()
    {
        SimulationSystem sim = SeededCity.Run(m_Grid, m_Config, 60, parks: true);
        float at100 = sim.MeasureServices().ServiceBonus;
        sim.Budget.SetFunding(BudgetLine.Parks, 0.5f);
        float at50 = sim.MeasureServices().ServiceBonus;
        sim.Budget.SetFunding(BudgetLine.Parks, 1.5f);
        float at150 = sim.MeasureServices().ServiceBonus;
        Assert.Greater(at100, 0f);
        Assert.Less(at50, at100);
        Assert.GreaterOrEqual(at150, at100);
    }

    [Test]
    public void Ledger_UpkeepFollowsEachLinesFunding()
    {
        SimulationSystem sim = Empty(
            Civic(new Vector2Int(4, 4), ServiceKind.Order, 8, 1f, 30f),
            new ServiceSource(new Vector2Int(10, 10), new Vector2Int(3, 3), 0, 600, upkeepPerDay: 100f));
        sim.Modifiers = new CityModifiers { UpkeepPerDay = 150f };   // 130 on lines + 20 on no line
        sim.Budget.SetFunding(BudgetLine.Order, 0.5f);
        sim.Budget.SetFunding(BudgetLine.Power, 1.5f);

        BudgetBreakdown ledger = sim.Ledger();
        Assert.AreEqual(15f, ledger.UpkeepByLine[(int)BudgetLine.Order], 1e-4f);
        Assert.AreEqual(150f, ledger.UpkeepByLine[(int)BudgetLine.Power], 1e-4f);
        Assert.AreEqual(20f, ledger.OtherUpkeep, 1e-4f);
        Assert.AreEqual(150f - 15f + 50f, ledger.Expense, 1e-3f);
        Assert.AreEqual(ledger.Expense, ledger.UpkeepByLine[(int)BudgetLine.Order] + ledger.UpkeepByLine[(int)BudgetLine.Power]
            + ledger.OtherUpkeep + ledger.Roads + ledger.Pipes, 1e-3f, "the parts add up");
    }

    [Test]
    public void Ledger_IsWhatTickCharges()
    {
        SimulationSystem sim = SeededCity.Build(m_Grid, m_Config, parks: true);
        sim.Budget.SetFunding(BudgetLine.Parks, 0.7f);
        for (int day = 0; day < 40; day++) sim.Tick();

        BudgetBreakdown ledger = sim.Ledger();
        Assert.AreEqual(sim.Economy.ExpensePerDay, ledger.Expense, 1e-3f);
        Assert.AreEqual(sim.Economy.IncomePerDay, ledger.Income, 1e-3f);
        Assert.Greater(ledger.IncomeResidential, 0f);
    }

    [Test]
    public void Funding_SurvivesResize()
    {
        var park = new ServiceSource(new Vector2Int(8, 8), Vector2Int.one, 8, 0);
        SimulationSystem sim = Empty(park);
        sim.Budget.SetFunding(BudgetLine.Parks, 0.5f);
        m_Grid.Resize(32, 32);

        Assert.AreEqual(0.5f, sim.Budget.GetFunding(BudgetLine.Parks), 1e-6f);
        Assert.AreEqual(1, sim.Coverage.GetCoverage(new Vector2Int(8 + 6, 8)), "funded reach 6 after the resize");
        Assert.AreEqual(0, sim.Coverage.GetCoverage(new Vector2Int(8 + 7, 8)));
    }
}
