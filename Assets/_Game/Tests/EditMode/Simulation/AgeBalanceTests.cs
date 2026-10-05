using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// M11g: the shipped content played by EngagedCity (see there for the player model). Targets
// (GamePlan §12): with engaged play 120-260 in-game days in the Medieval age and 100-320 in each later one (M21e: every
// tech costs 12x its 1.0 price, and Electricity and Waterworks are Renaissance techs the Industrial age requires), no age where the city stalls, and an economy that pays for itself. Measured, from a
// Medieval start: Medieval 199 days, Renaissance 168, Industrial 192, Modern reached on day 559 (1.0: 86 / 62 / 63, day 211).
public sealed class AgeBalanceTests
{
    private BalanceConfig m_Config;

    [SetUp]
    public void SetUp()
    {
        m_Config = ScriptableObject.CreateInstance<BalanceConfig>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(m_Config);
    }

    // M15e: also with the budget player (ordinances and a loan when saving): the same targets.
    [TestCase(false)]
    [TestCase(true)]
    public void FromMedieval_EachAgeTakes45To90Days_AndReachesModern(bool useBudget)
    {
        var city = new EngagedCity(m_Config, 0) { UseBudget = useBudget };
        for (int day = 0; day < 1000 && city.Sim.Tech.CurrentAge < 3; day++) city.RunDay();
        TestContext.WriteLine(city.Report());
        TestContext.WriteLine(city.BudgetReport());

        Assert.AreEqual(3, city.Sim.Tech.CurrentAge, "reaches the Modern age within 1000 days");
        for (int i = 1; i < city.AgeEntries.Count; i++)
        {
            int span = city.AgeEntries[i].day - city.AgeEntries[i - 1].day;
            Assert.That(span, i == 1 ? Is.InRange(120, 260) : Is.InRange(100, 320), $"days spent in {city.Ages[city.AgeEntries[i - 1].age].Id}");
        }
        // 0.40, not 0.55 (M21e): techs cost 12x, so the city outgrows its services while the next tech is being researched
        // (Renaissance: crime / fire / health / traffic; measured low 0.41).
        Assert.GreaterOrEqual(city.MinHappiness, 0.40f);
        Assert.GreaterOrEqual(city.MinMoney, 0f, "never in debt");
    }

    // M23e: the same targets with the density player. Gated = what the toolbar allows (Low in the Medieval age, Medium
    // from the Renaissance, repainted when it opens); Mixed = also High homes and shops in alternate blocks once High
    // opens. Measured: Gated 236 / 143 / 210 days, Modern on day 589 (2516 pop); Mixed 236 / 143 / 203, day 582 (2624 pop);
    // the Off run is the baseline above (199 / 181 / 253, day 633).
    [TestCase(1)]
    [TestCase(2)]
    public void FromMedieval_WithDensity_EachAgeTakes45To90Days_AndReachesModern(int mode)
    {
        var city = new EngagedCity(m_Config, 0) { DensityUse = (EngagedCity.DensityMode)mode };
        for (int day = 0; day < 1000 && city.Sim.Tech.CurrentAge < 3; day++) city.RunDay();
        TestContext.WriteLine(city.Report());

        Assert.AreEqual(3, city.Sim.Tech.CurrentAge, "reaches the Modern age within 1000 days");
        for (int i = 1; i < city.AgeEntries.Count; i++)
        {
            int span = city.AgeEntries[i].day - city.AgeEntries[i - 1].day;
            Assert.That(span, i == 1 ? Is.InRange(120, 260) : Is.InRange(100, 320), $"days spent in {city.Ages[city.AgeEntries[i - 1].age].Id}");
        }
        Assert.GreaterOrEqual(city.MinHappiness, 0.40f);
        Assert.GreaterOrEqual(city.MinMoney, 0f, "never in debt");
    }

    // Later starts with High blocks in the mix: still growing and solvent (Industrial 244 -> 753 pop, happiness 0.64;
    // Modern 314 -> 630, 0.78).
    [TestCase(2)]
    [TestCase(3)]
    public void LaterStart_WithMixedDensity_KeepsGrowing(int startAge)
    {
        var city = new EngagedCity(m_Config, startAge) { DensityUse = EngagedCity.DensityMode.Mixed };
        city.RunDays(60);
        int at60 = city.Sim.Population.Population;
        city.RunDays(60);
        TestContext.WriteLine(city.Report());

        Assert.Greater(at60, 150);
        Assert.Greater(city.Sim.Population.Population, at60 * 1.9f, "still growing between day 60 and 120");
        Assert.GreaterOrEqual(city.MinHappiness, 0.55f);
        Assert.GreaterOrEqual(city.MinMoney, 0f, "never in debt");
    }

    // M17f: the same targets with disasters on (fires, plague, breakdowns and events from fixed seeds), the engaged
    // player rebuilding, repairing and answering events. The off runs above stay the regression baseline.
    [TestCase(1ul, false)]
    [TestCase(2ul, false)]
    [TestCase(3ul, false)]
    [TestCase(1ul, true)]
    [TestCase(2ul, true)]
    [TestCase(3ul, true)]
    public void FromMedieval_WithDisasters_EachAgeTakes45To90Days_AndReachesModern(ulong seed, bool useBudget)
    {
        var city = new EngagedCity(m_Config, 0, 64, true, seed) { UseBudget = useBudget };
        for (int day = 0; day < 1000 && city.Sim.Tech.CurrentAge < 3; day++) city.RunDay();
        TestContext.WriteLine(city.Report());

        Assert.AreEqual(3, city.Sim.Tech.CurrentAge, "reaches the Modern age within 1000 days");
        for (int i = 1; i < city.AgeEntries.Count; i++)
        {
            int span = city.AgeEntries[i].day - city.AgeEntries[i - 1].day;
            Assert.That(span, i == 1 ? Is.InRange(120, 260) : Is.InRange(100, 320), $"days spent in {city.Ages[city.AgeEntries[i - 1].age].Id}");
        }
        // 0.52, not 0.55: events and hazards add trajectory noise (seed 2 without the budget player dipped to 0.545 on a
        // pollution / power-capacity stall, not on a hazard; see Docs/milestones/v1.0/M17.md 17f; M21a: seed 3 with the
        // budget player dips to 0.528 now that research runs slower and the Industrial stretch is longer).
        // M21e: 0.38 (the six seeds reach 0.41-0.44); see the first test for why it is low with 12x tech costs.
        Assert.GreaterOrEqual(city.MinHappiness, 0.38f);
        Assert.GreaterOrEqual(city.MinMoney, 0f, "never in debt");
    }

    // A city started in any later age keeps growing and stays solvent.
    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(3, false)]
    [TestCase(2, true)]
    [TestCase(3, true)]
    public void LaterStart_KeepsGrowing(int startAge, bool useBudget)
    {
        var city = new EngagedCity(m_Config, startAge) { UseBudget = useBudget };
        city.RunDays(60);
        int at60 = city.Sim.Population.Population;
        city.RunDays(60);
        TestContext.WriteLine(city.Report());
        TestContext.WriteLine(city.BudgetReport());

        Assert.Greater(at60, 150);
        // 1.9, not 2 (M21a): the Modern techs cost twice what they did, so a Modern start gets its demand techs later.
        Assert.Greater(city.Sim.Population.Population, at60 * 1.9f, "still growing between day 60 and 120");
        Assert.GreaterOrEqual(city.MinHappiness, 0.55f);
        Assert.GreaterOrEqual(city.MinMoney, 0f, "never in debt");
    }

    // M24d death-spiral probe: factories that make little or nothing. A player who ignores goods lives on imports (capped at
    // half of what the city uses): shops at about 70% income, homes held at level 2 (supply 0.5 < 0.7), growth slower but
    // without a debt spiral. (Industrial start with factories that make nothing: -867 at day 120, its taxes barely cover the
    // 100 a day of imports; a game state a player leaves by building industry.)
    [TestCase(2, 0.1f)]
    [TestCase(3, 0.1f)]
    [TestCase(3, 0f)]
    public void LaterStart_WithLittleFactoryOutput_StaysSolvent(int startAge, float perJob)
    {
        typeof(BalanceConfig).GetField("m_GoodsPerIndustrialJob", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .SetValue(m_Config, perJob);
        var city = new EngagedCity(m_Config, startAge);
        city.RunDays(60);
        int at60 = city.Sim.Population.Population;
        city.RunDays(60);
        TestContext.WriteLine(city.Report());

        Assert.Greater(at60, 120);
        Assert.Greater(city.Sim.Population.Population, at60 * 1.3f, "still growing between day 60 and 120");
        if (perJob == 0f) Assert.AreEqual(0.5f, city.Sim.Goods.Last.Supply, 1e-3f, "imports are capped at half of the demand");
        Assert.GreaterOrEqual(city.MinHappiness, 0.45f);
        // A thin overdraft at most (measured -752 and -867 at day 120 in two cases): imports are most of the gap between income
        // and expense for a young city, but nothing spirals.
        Assert.GreaterOrEqual(city.MinMoney, -1500f, "no debt spiral");
    }

    // Industrial start with the real content = today's game plus the bonuses of every Medieval and
    // Renaissance tech it starts with (accepted in M11g). Same layout as SimulationTests.RunSeededCity
    // (212 pop at day 60 without ages). Re-recorded in M12a for local pollution (was 0.647) and in
    // M14a for crime (was 0.660; crime -0.007 at 220 pop) and M14b for fire risk and sickness (was 0.653;
    // fire -0.007, health -0.016) and in M24d for goods (was 0.631; goods term -0.004 at 220 pop: the seeded city imports
    // about half of what it uses, supply 0.96; population unchanged).
    [Test]
    public void IndustrialStart_RealContent_Baseline()
    {
        var ages = AssetDatabase.LoadAssetAtPath<AgeDatabase>(EngagedCity.AgeDatabasePath);
        var techs = AssetDatabase.LoadAssetAtPath<TechDatabase>(EngagedCity.TechDatabasePath);
        SimulationSystem sim = SeededCity.Run(new GridData(24, 24), m_Config, 60, ages: ages, techs: techs, startAge: ages.Legacy);

        Assert.AreEqual(220, sim.Population.Population);
        Assert.AreEqual(0.627f, sim.Population.AverageHappiness, 0.003f);
        Assert.AreEqual(-0.004f, sim.Population.Happiness.Goods, 0.002f);
        Assert.AreEqual(0.05f, sim.Population.Happiness.Technology, 1e-5f);
    }

    // Tuning aid (not part of the normal run): the same Medieval run against the BalanceConfig
    // asset, so values can be tried in the Editor without a recompile.
    [Test, Explicit("Balance tuning report")]
    public void Report_FromMedievalWithAsset()
    {
        var asset = AssetDatabase.LoadAssetAtPath<BalanceConfig>("Assets/_Game/Scriptables/Balance/BalanceConfig.asset");
        var city = new EngagedCity(asset, 0);
        for (int day = 0; day < 1000 && city.Sim.Tech.CurrentAge < 3; day++) city.RunDay();
        city.RunDays(30);
        TestContext.WriteLine(city.Report());
    }
}
