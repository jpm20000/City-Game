using System.Reflection;
using NUnit.Framework;
using UnityEngine;

// M24a: the goods pool — stock / import / export arithmetic, the identity when goods are off, the tech multiplier, the
// ledger lines and the shortage happiness term.
public sealed class GoodsTests
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

    private static void SetField(BalanceConfig config, string name, object value)
    {
        typeof(BalanceConfig).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(config, value);
    }

    // --- the pool ---

    [Test]
    public void Defaults_GoodsAreOffUntilTheirAge()
    {
        Assert.AreEqual(99, m_Config.GoodsMinAge, "24a-24c ship dark; 24d turns goods on");
    }

    [Test]
    public void Inactive_IsTheIdentity()
    {
        var goods = new GoodsSystem(m_Config);
        GoodsReport report = goods.Step(0f, 100f, false);
        Assert.AreEqual(1f, report.Supply);
        Assert.AreEqual(0f, report.Imported);
        Assert.AreEqual(0f, report.Exported);
        Assert.AreEqual(0f, goods.Stock);
        Assert.AreEqual(0f, goods.ImportCost(report));
        Assert.AreEqual(0f, goods.ExportIncome(report));
    }

    [Test]
    public void Production_CoveringDemand_FillsTheStockAndSellsEverything()
    {
        var goods = new GoodsSystem(m_Config);
        GoodsReport report = goods.Step(30f, 20f, true);
        Assert.AreEqual(20f, report.Delivered, 1e-4f);
        Assert.AreEqual(1f, report.Supply);
        Assert.AreEqual(0f, report.Imported);
        Assert.AreEqual(10f, goods.Stock, 1e-4f, "the surplus is kept");
        Assert.AreEqual(0f, report.Exported);
    }

    [Test]
    public void StockCoversADryDay_ThenImportsThenRealShortage()
    {
        var goods = new GoodsSystem(m_Config);
        goods.Step(100f, 10f, true);                      // stock 90
        GoodsReport fromStock = goods.Step(0f, 10f, true);
        Assert.AreEqual(1f, fromStock.Supply);
        Assert.AreEqual(0f, fromStock.Imported);
        Assert.AreEqual(80f, goods.Stock, 1e-4f);

        // Empty the stock, then no production at all: imports cover up to half of demand, the rest is shortage.
        var dry = new GoodsSystem(m_Config);
        GoodsReport report = dry.Step(0f, 10f, true);
        Assert.AreEqual(5f, report.Imported, 1e-4f, "GoodsMaxImportShare 0.5");
        Assert.AreEqual(0.5f, report.Supply, 1e-4f);
        Assert.AreEqual(0.5f, report.Shortage, 1e-4f);
        Assert.AreEqual(15f, dry.ImportCost(report), 1e-4f, "5 units x 3");
        Assert.AreEqual(0f, dry.Stock);
    }

    [Test]
    public void PartialProduction_ImportsOnlyTheGap()
    {
        var goods = new GoodsSystem(m_Config);
        GoodsReport report = goods.Step(8f, 10f, true);
        Assert.AreEqual(2f, report.Imported, 1e-4f);
        Assert.AreEqual(1f, report.Supply, 1e-4f);
    }

    [Test]
    public void SurplusAboveTheStockCap_IsExportedForIncome()
    {
        var goods = new GoodsSystem(m_Config);
        // demand 10, cap 10 days = 100; produce 130 -> sell 10, keep 100, export 20.
        GoodsReport report = goods.Step(130f, 10f, true);
        Assert.AreEqual(100f, goods.Stock, 1e-3f);
        Assert.AreEqual(20f, report.Exported, 1e-3f);
        Assert.AreEqual(30f, goods.ExportIncome(report), 1e-3f, "20 x 1.5");
    }

    [Test]
    public void Evaluate_ChangesNothing_AndStepAppliesExactlyIt()
    {
        var goods = new GoodsSystem(m_Config);
        goods.Step(50f, 10f, true);
        float stock = goods.Stock;
        GoodsReport preview = goods.Evaluate(3f, 12f, true);
        Assert.AreEqual(stock, goods.Stock);
        GoodsReport applied = goods.Step(3f, 12f, true);
        Assert.AreEqual(preview.Supply, applied.Supply);
        Assert.AreEqual(preview.StockAfter, goods.Stock, 1e-4f);
    }

    [Test]
    public void NoDemand_IsFullySupplied()
    {
        var goods = new GoodsSystem(m_Config);
        Assert.AreEqual(1f, goods.Step(0f, 0f, true).Supply);
    }

    [Test]
    public void GoingInactive_ClearsTheStock()
    {
        var goods = new GoodsSystem(m_Config);
        goods.Step(100f, 10f, true);
        goods.Step(0f, 10f, false);
        Assert.AreEqual(0f, goods.Stock);
    }

    [Test]
    public void Restore_KeepsTheStock_AndRejectsGarbage()
    {
        var goods = new GoodsSystem(m_Config);
        goods.Restore(42f, 0f, 10f, true);
        Assert.AreEqual(42f, goods.Stock);
        Assert.AreEqual(1f, goods.Last.Supply, "the stock covers the day");
        goods.Restore(float.NaN, 0f, 10f, true);
        Assert.AreEqual(0f, goods.Stock);
        goods.Restore(-5f, 0f, 10f, true);
        Assert.AreEqual(0f, goods.Stock);
        goods.Restore(42f, 0f, 10f, false);
        Assert.AreEqual(0f, goods.Stock, "no stock while goods are off");
    }

    // --- techs ---

    [Test]
    public void GoodsMultiplier_FoldsFromTechs_AndDefaultsToOne()
    {
        Assert.AreEqual(1f, TechModifiers.None.GoodsMultiplier);
        var a = ScriptableObject.CreateInstance<TechDefinition>();
        var b = ScriptableObject.CreateInstance<TechDefinition>();
        try
        {
            a.Init("a", 2, 1f, null, new[] { new TechEffect(TechEffectType.GoodsMultiplier, "", 1.25f) });
            b.Init("b", 2, 1f, null, new[] { new TechEffect(TechEffectType.GoodsMultiplier, "", 1.2f) });
            Assert.AreEqual(1.5f, TechModifiers.Fold(new[] { a, b }).GoodsMultiplier, 1e-5f);
        }
        finally
        {
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }
    }

    // --- in the sim ---

    private SimulationSystem AgedCity(int startAge, int goodsMinAge, int days)
    {
        SetField(m_Config, "m_GoodsMinAge", goodsMinAge);
        var grid = new GridData(24, 24);
        SimulationSystem sim = SeededCity.Build(grid, m_Config, ages: m_TestAges.Ages, techs: m_TestAges.Techs, startAge: startAge);
        for (int day = 0; day < days; day++) sim.Tick();
        return sim;
    }

    [Test]
    public void WithoutAges_GoodsNeverRun_EvenWhenTheAgeIsZero()
    {
        SetField(m_Config, "m_GoodsMinAge", 0);
        var grid = new GridData(24, 24);
        SimulationSystem sim = SeededCity.Run(grid, m_Config, 120);
        Assert.IsFalse(sim.GoodsActive);
        Assert.AreEqual(1f, sim.GoodsSupply());
        Assert.AreEqual(0f, sim.Goods.Stock);
        BudgetBreakdown ledger = sim.Ledger();
        Assert.AreEqual(0f, ledger.Imports);
        Assert.AreEqual(0f, ledger.Exports);
    }

    [Test]
    public void BeforeTheirAge_TheCityPlaysExactlyAsWithGoodsOff()
    {
        // Renaissance with goods starting in Industrial = the same numbers as goods never configured.
        SimulationSystem off = AgedCity(TestAges.Renaissance, 99, 150);
        float offMoney = off.Economy.Money;
        int offPop = off.Population.Population;
        float offHappy = off.Population.AverageHappiness;

        SimulationSystem on = AgedCity(TestAges.Renaissance, 2, 150);
        Assert.IsFalse(on.GoodsActive);
        Assert.AreEqual(offMoney, on.Economy.Money);
        Assert.AreEqual(offPop, on.Population.Population);
        Assert.AreEqual(offHappy, on.Population.AverageHappiness);
    }

    [Test]
    public void InTheirAge_FlowsAreChargedInTheLedger_AndShortageCostsHappiness()
    {
        SimulationSystem sim = AgedCity(TestAges.Industrial, 2, 60);
        Assert.IsTrue(sim.GoodsActive);
        Assert.Greater(sim.Population.Population, 0);

        GoodsReport last = sim.Goods.Last;
        Assert.Greater(last.Demanded, 0f);
        Assert.AreEqual(sim.GoodsDemand(), last.Demanded, last.Demanded * 0.5f, "same order as today's demand");

        BudgetBreakdown ledger = sim.Ledger();
        Assert.AreEqual(sim.Goods.ImportCost(last), ledger.Imports, 1e-4f);
        Assert.AreEqual(sim.Goods.ExportIncome(last), ledger.Exports, 1e-4f);
        Assert.AreEqual(ledger.IncomeResidential + ledger.IncomeCommercial + ledger.IncomeIndustrial + ledger.Exports, ledger.Income, 1e-3f);
        Assert.GreaterOrEqual(ledger.Expense, ledger.Imports);

        float goodsTerm = sim.Population.Happiness.Goods;
        Assert.LessOrEqual(goodsTerm, 0f);
    }

    [Test]
    public void NoFactories_AShortageHurtsHomes_AndFactoriesFixIt()
    {
        // Same city, goods per factory job made generous vs zero: with none made the supply drops below 1 and
        // happiness carries the term; with plenty it stays 1.
        SetField(m_Config, "m_GoodsPerIndustrialJob", 0f);
        SimulationSystem starved = AgedCity(TestAges.Industrial, 2, 80);
        Assert.Less(starved.GoodsSupply(), 1f);
        Assert.Less(starved.Population.Happiness.Goods, 0f);
        Assert.Greater(starved.Ledger().Imports, 0f, "imports bridge what they can");

        SetField(m_Config, "m_GoodsPerIndustrialJob", 50f);
        SimulationSystem fed = AgedCity(TestAges.Industrial, 2, 80);
        Assert.AreEqual(1f, fed.GoodsSupply());
        Assert.AreEqual(0f, fed.Population.Happiness.Goods);
        Assert.Greater(fed.Ledger().Exports, 0f, "a big surplus is exported");
    }
}
