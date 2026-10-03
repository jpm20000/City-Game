using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// M11g: the shipped content played by EngagedCity (see there for the player model). Targets
// (GamePlan §12): about 45-90 in-game days per age with engaged play, no age where the city
// stalls, and an economy that pays for itself. Tuned values, from a Medieval start: Medieval 86
// days, Renaissance 62, Industrial 63, Modern reached on day 211.
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

    [Test]
    public void FromMedieval_EachAgeTakes45To90Days_AndReachesModern()
    {
        var city = new EngagedCity(m_Config, 0);
        for (int day = 0; day < 300 && city.Sim.Tech.CurrentAge < 3; day++) city.RunDay();
        TestContext.WriteLine(city.Report());

        Assert.AreEqual(3, city.Sim.Tech.CurrentAge, "reaches the Modern age within 300 days");
        for (int i = 1; i < city.AgeEntries.Count; i++)
        {
            int span = city.AgeEntries[i].day - city.AgeEntries[i - 1].day;
            Assert.That(span, Is.InRange(45, 90), $"days spent in {city.Ages[city.AgeEntries[i - 1].age].Id}");
        }
        Assert.GreaterOrEqual(city.MinHappiness, 0.55f);
        Assert.GreaterOrEqual(city.MinMoney, 0f, "never in debt");
    }

    // A city started in any later age keeps growing and stays solvent.
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void LaterStart_KeepsGrowing(int startAge)
    {
        var city = new EngagedCity(m_Config, startAge);
        city.RunDays(60);
        int at60 = city.Sim.Population.Population;
        city.RunDays(60);
        TestContext.WriteLine(city.Report());

        Assert.Greater(at60, 150);
        Assert.Greater(city.Sim.Population.Population, at60 * 2, "still growing between day 60 and 120");
        Assert.GreaterOrEqual(city.MinHappiness, 0.55f);
        Assert.GreaterOrEqual(city.MinMoney, 0f, "never in debt");
    }

    // Industrial start with the real content = today's game plus the bonuses of every Medieval and
    // Renaissance tech it starts with (accepted in M11g). Same layout as SimulationTests.RunSeededCity
    // (212 pop at day 60 without ages). Re-recorded in M12a for local pollution (was 0.647).
    [Test]
    public void IndustrialStart_RealContent_Baseline()
    {
        var ages = AssetDatabase.LoadAssetAtPath<AgeDatabase>(EngagedCity.AgeDatabasePath);
        var techs = AssetDatabase.LoadAssetAtPath<TechDatabase>(EngagedCity.TechDatabasePath);
        SimulationSystem sim = SeededCity.Run(new GridData(24, 24), m_Config, 60, ages: ages, techs: techs, startAge: ages.Legacy);

        Assert.AreEqual(220, sim.Population.Population);
        Assert.AreEqual(0.660f, sim.Population.AverageHappiness, 0.005f);
        Assert.AreEqual(0.05f, sim.Population.Happiness.Technology, 1e-5f);
    }

    // Tuning aid (not part of the normal run): the same Medieval run against the BalanceConfig
    // asset, so values can be tried in the Editor without a recompile.
    [Test, Explicit("Balance tuning report")]
    public void Report_FromMedievalWithAsset()
    {
        var asset = AssetDatabase.LoadAssetAtPath<BalanceConfig>("Assets/_Game/Scriptables/Balance/BalanceConfig.asset");
        var city = new EngagedCity(asset, 0);
        for (int day = 0; day < 400 && city.Sim.Tech.CurrentAge < 3; day++) city.RunDay();
        city.RunDays(30);
        TestContext.WriteLine(city.Report());
    }
}
