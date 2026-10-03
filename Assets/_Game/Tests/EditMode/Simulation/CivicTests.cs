using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// M14a: civic cover (CivicCoverage), crime (CivicSystem), the Crime happiness term and crime in land value.
public sealed class CivicTests
{
    private readonly List<Object> m_Created = new();
    private BalanceConfig m_Config;
    private GridData m_Grid;
    private int m_Population;

    [SetUp]
    public void SetUp()
    {
        m_Config = Make<BalanceConfig>();
        m_Grid = new GridData(16, 16);
        m_Population = 10000;   // fully ramped unless a test says otherwise
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

    private CivicSystem Civic(params ServiceSource[] sources)
    {
        var civic = new CivicSystem(m_Grid, m_Config, population: () => m_Population);
        civic.SetSources(sources);
        return civic;
    }

    private void Grow(Vector2Int cell, ZoneType zone, byte level)
    {
        m_Grid.SetZone(cell, zone);
        m_Grid.SetBuildingLevel(cell, level);
    }

    private static ServiceSource Station(Vector2Int at, ServiceKind kind, int radius, float strength, Vector2Int? size = null) =>
        new ServiceSource(at, size ?? Vector2Int.one, 0, 0, civicKind: kind, civicRadius: radius, civicStrength: strength);

    [Test]
    public void Cover_IsTheBestStrengthInReach_NotASum()
    {
        Vector2Int cell = new Vector2Int(8, 8);
        Assert.AreEqual(0.5f, Civic(Station(new Vector2Int(6, 8), ServiceKind.Order, 3, 0.5f),
            Station(new Vector2Int(10, 8), ServiceKind.Order, 3, 0.5f)).Cover.GetStrength(ServiceKind.Order, cell), 1e-6f);

        CivicCoverage cover = Civic(Station(new Vector2Int(6, 8), ServiceKind.Order, 3, 0.5f),
            Station(new Vector2Int(10, 8), ServiceKind.Order, 3, 0.75f)).Cover;
        Assert.AreEqual(0.75f, cover.GetStrength(ServiceKind.Order, cell), 1e-6f);
        Assert.AreEqual(0f, cover.GetStrength(ServiceKind.Fire, cell), "other lines get nothing");
        Assert.AreEqual(0f, cover.GetStrength(ServiceKind.None, cell));
    }

    [Test]
    public void Cover_ReachesRadiusCellsFromTheFootprint()
    {
        // A 2x2 station at (4,4)-(5,5) with reach 2 covers x 2..7.
        CivicCoverage cover = Civic(Station(new Vector2Int(4, 4), ServiceKind.Health, 2, 1f, new Vector2Int(2, 2))).Cover;
        Assert.AreEqual(1f, cover.GetStrength(ServiceKind.Health, new Vector2Int(7, 7)));
        Assert.AreEqual(1f, cover.GetStrength(ServiceKind.Health, new Vector2Int(2, 2)));
        Assert.AreEqual(0f, cover.GetStrength(ServiceKind.Health, new Vector2Int(8, 5)));
        Assert.AreEqual(0f, cover.GetStrength(ServiceKind.Health, new Vector2Int(1, 4)));
        Assert.AreEqual(0f, cover.GetStrength(ServiceKind.Health, new Vector2Int(-1, 4)), "off-map");
    }

    [Test]
    public void Ramp_IsZeroUpToFreePopulation_AndFullAtFullPopulation()
    {
        Assert.AreEqual(0f, CivicSystem.RampAt(m_Config, 0));
        Assert.AreEqual(0f, CivicSystem.RampAt(m_Config, m_Config.CivicFreePopulation));
        Assert.AreEqual(0.5f, CivicSystem.RampAt(m_Config, (m_Config.CivicFreePopulation + m_Config.CivicFullPopulation) / 2), 1e-6f);
        Assert.AreEqual(1f, CivicSystem.RampAt(m_Config, m_Config.CivicFullPopulation));
        Assert.AreEqual(1f, CivicSystem.RampAt(m_Config, 100000));
    }

    [Test]
    public void Crime_ScalesWithCapacity_RampAndOrder()
    {
        Vector2Int home1 = new Vector2Int(2, 2), home3 = new Vector2Int(3, 2), shop2 = new Vector2Int(4, 2);
        Grow(home1, ZoneType.Residential, 1);
        Grow(home3, ZoneType.Residential, 3);
        Grow(shop2, ZoneType.Commercial, 2);
        CivicSystem civic = Civic();

        // Capacity 4 / 16 / 8 x 0.04.
        Assert.AreEqual(0.16f, civic.GetCrime(home1), 1e-5f);
        Assert.AreEqual(0.64f, civic.GetCrime(home3), 1e-5f);
        Assert.AreEqual(0.32f, civic.GetCrime(shop2), 1e-5f);

        m_Population = (m_Config.CivicFreePopulation + m_Config.CivicFullPopulation) / 2;
        Assert.AreEqual(0.32f, civic.GetCrime(home3), 1e-5f, "half ramped");

        civic.SetSources(new[] { Station(new Vector2Int(3, 4), ServiceKind.Order, 3, 0.75f) });
        CivicBreakdown explained = civic.Explain(home3);
        Assert.AreEqual(0.64f, explained.CrimePotential, 1e-5f);
        Assert.AreEqual(0.5f, explained.Ramp, 1e-5f);
        Assert.AreEqual(0.75f, explained.Order, 1e-6f);
        Assert.AreEqual(0.64f * 0.5f * 0.25f, explained.Crime, 1e-5f);
    }

    [Test]
    public void Crime_IsZeroForIndustryEmptyLand_AndSmallTowns()
    {
        Vector2Int factory = new Vector2Int(2, 2), home = new Vector2Int(3, 2), zoned = new Vector2Int(4, 2);
        Grow(factory, ZoneType.Industrial, 3);
        Grow(home, ZoneType.Residential, 3);
        m_Grid.SetZone(zoned, ZoneType.Residential);
        CivicSystem civic = Civic();

        Assert.AreEqual(0f, civic.GetCrime(factory));
        Assert.AreEqual(0f, civic.GetCrime(zoned));
        Assert.AreEqual(0f, civic.GetCrime(new Vector2Int(9, 9)));
        Assert.Greater(civic.GetCrime(home), 0f);

        m_Population = m_Config.CivicFreePopulation;
        Assert.AreEqual(0f, civic.GetCrime(home));
    }

    [Test]
    public void CrimePenalty_IsHousingWeighted_AndCappedPerHome()
    {
        Assert.AreEqual(m_Config.CrimePenaltyCap, ServiceStats.CrimePenaltyAt(m_Config, 1f), 1e-6f);
        Assert.AreEqual(0.64f * m_Config.CrimePenalty, ServiceStats.CrimePenaltyAt(m_Config, 0.64f), 1e-6f);

        // An uncovered level-3 home (16) and a fully policed level-1 home (4).
        Grow(new Vector2Int(2, 2), ZoneType.Residential, 3);
        Grow(new Vector2Int(12, 12), ZoneType.Residential, 1);
        Grow(new Vector2Int(3, 2), ZoneType.Commercial, 3);
        CivicSystem civic = Civic(Station(new Vector2Int(12, 10), ServiceKind.Order, 3, 1f));
        var coverage = new CoverageSystem(16, 16);
        ServiceStats stats = ServiceStats.Measure(m_Grid, m_Config, coverage, new PowerSystem(m_Grid, m_Config), countPower: false,
            civic: civic);

        Assert.AreEqual(16f * 0.64f * m_Config.CrimePenalty / 20f, stats.CrimePenalty, 1e-5f, "shops don't count");
        Assert.AreEqual(0f, ServiceStats.Measure(m_Grid, m_Config, coverage, new PowerSystem(m_Grid, m_Config), countPower: false)
            .CrimePenalty, "no civic system = no crime term");
    }

    [Test]
    public void Crime_LowersLandValue_AndCanHoldALevel2Home_UntilPoliced()
    {
        // Pollution alone leaves the home just above the level-3 gate; its crime pushes it under.
        Vector2Int home = new Vector2Int(8, 8);
        Grow(home, ZoneType.Residential, 2);
        var coverage = new CoverageSystem(16, 16);
        var pollution = new PollutionSystem(m_Grid, m_Config);
        var polluter = new ServiceSource(home, Vector2Int.one, 0, 0, 1.5f, 0);
        pollution.SetSources(new[] { polluter });
        CivicSystem civic = Civic();
        var landValue = new LandValueSystem(m_Grid, m_Config, coverage, pollution, civic: civic);

        LandValueBreakdown value = landValue.Explain(home);
        Assert.AreEqual(-0.32f * m_Config.LandValuePerCrime, value.Crime, 1e-5f);
        Assert.Greater(value.Total - value.Crime, m_Config.LandValueForLevel3, "without crime it would pass");
        Assert.IsFalse(landValue.AllowsLevel(home, 3));

        civic.SetSources(new[] { Station(new Vector2Int(10, 8), ServiceKind.Order, 4, 1f) });
        Assert.AreEqual(0f, landValue.Explain(home).Crime, 1e-6f);
        Assert.IsTrue(landValue.AllowsLevel(home, 3));
    }

    [Test]
    public void CivicBuildings_AreNotParks()
    {
        var sim = new SimulationSystem(m_Grid, new RoadNetwork(m_Grid), m_Config);
        sim.Sources = new[] { Station(new Vector2Int(4, 4), ServiceKind.Order, 4, 1f) };
        Vector2Int cell = new Vector2Int(5, 5);

        Assert.AreEqual(0, sim.Coverage.GetCoverage(cell));
        Assert.AreEqual(0f, sim.LandValue.Explain(cell).Services);
        Assert.AreEqual(1f, sim.Civic.Cover.GetStrength(ServiceKind.Order, cell));
    }

    [Test]
    public void Resize_KeepsSources_AndReallocates()
    {
        CivicSystem civic = Civic(Station(new Vector2Int(2, 2), ServiceKind.Fire, 2, 0.5f));
        m_Grid.Resize(24, 24);

        Assert.AreEqual(0.5f, civic.Cover.GetStrength(ServiceKind.Fire, new Vector2Int(4, 4)), 1e-6f);
        Assert.AreEqual(0f, civic.Cover.GetStrength(ServiceKind.Fire, new Vector2Int(20, 20)));
    }

    [Test]
    public void Crime_FollowsGrowth_WithNoRecompute()
    {
        Vector2Int home = new Vector2Int(5, 5);
        Grow(home, ZoneType.Residential, 1);
        CivicSystem civic = Civic();
        Assert.AreEqual(0.16f, civic.GetCrime(home), 1e-5f);

        m_Grid.SetBuildingLevel(home, 2);
        Assert.AreEqual(0.32f, civic.GetCrime(home), 1e-5f);
        m_Grid.SetBuildingLevel(home, 0);
        Assert.AreEqual(0f, civic.GetCrime(home));
    }

    // The seeded 24x24 city stays small (384 people at day 120, ramp 0.47), so crime only partly
    // ramps in: -0.027 (M14a), and the term is what PopulationSystem uses.
    [Test]
    public void SeededCity_PaysALittleForCrime()
    {
        SimulationSystem sim = SeededCity.Run(new GridData(24, 24), m_Config, 120);
        ServiceStats stats = sim.MeasureServices();

        Assert.Less(sim.Population.Happiness.Crime, 0f);
        Assert.Greater(sim.Population.Happiness.Crime, -0.04f);
        Assert.AreEqual(-stats.CrimePenalty, sim.Population.Happiness.Crime, 0.01f);
    }
}
