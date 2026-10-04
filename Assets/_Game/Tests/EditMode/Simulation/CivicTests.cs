using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// M14: civic cover (CivicCoverage), crime, fire risk and sickness (CivicSystem), their happiness terms,
// crime in land value and research from educated residents.
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

    // --- M14b: fire risk, sickness, education ---

    private AgeDatabase AgesWithFireRisk(float medieval)
    {
        AgeDefinition a = Make<AgeDefinition>(), b = Make<AgeDefinition>();
        a.Init("timber", 750, fireRisk: medieval);
        b.Init("brick", 1450);   // 0 = BalanceConfig.FireRisk
        AgeDatabase ages = Make<AgeDatabase>();
        ages.Init(a, b);
        return ages;
    }

    [Test]
    public void FireRisk_FollowsBuiltAge_IndustryAndFireCover()
    {
        AgeDatabase ages = AgesWithFireRisk(0.6f);
        Vector2Int timberHome = new Vector2Int(2, 2), timberWorks = new Vector2Int(3, 2), brickHome = new Vector2Int(4, 2);
        Grow(timberHome, ZoneType.Residential, 1);
        Grow(timberWorks, ZoneType.Industrial, 1);
        Grow(brickHome, ZoneType.Residential, 1);
        m_Grid.SetBuiltAge(brickHome, 1);
        var civic = new CivicSystem(m_Grid, m_Config, population: () => m_Population, ages: ages);

        Assert.AreEqual(0.6f, civic.GetFireRisk(timberHome), 1e-5f);
        Assert.AreEqual(0.6f * m_Config.FireRiskIndustrialFactor, civic.GetFireRisk(timberWorks), 1e-5f);
        Assert.AreEqual(m_Config.FireRisk, civic.GetFireRisk(brickHome), 1e-5f, "an age without its own risk uses the config's");
        Assert.AreEqual(0f, civic.GetFireRisk(new Vector2Int(9, 9)), "empty land doesn't burn");

        civic.SetSources(new[] { Station(new Vector2Int(2, 4), ServiceKind.Fire, 3, 0.75f) });
        Assert.AreEqual(0.6f * 0.25f, civic.GetFireRisk(timberHome), 1e-5f);

        m_Population = m_Config.CivicFreePopulation;
        Assert.AreEqual(0f, civic.GetFireRisk(timberWorks), "small towns don't worry about fire");
    }

    [Test]
    public void Sickness_OnlyAtHomes_CutByHealthCover()
    {
        Vector2Int home = new Vector2Int(2, 2), shop = new Vector2Int(3, 2);
        Grow(home, ZoneType.Residential, 3);
        Grow(shop, ZoneType.Commercial, 3);
        CivicSystem civic = Civic();

        Assert.AreEqual(1f, civic.GetSickness(home), 1e-6f);
        Assert.AreEqual(0f, civic.GetSickness(shop));
        civic.SetSources(new[] { Station(new Vector2Int(2, 5), ServiceKind.Health, 4, 0.85f) });
        Assert.AreEqual(0.15f, civic.GetSickness(home), 1e-5f);
        m_Population = (m_Config.CivicFreePopulation + m_Config.CivicFullPopulation) / 2;
        Assert.AreEqual(0.075f, civic.GetSickness(home), 1e-5f);
    }

    [Test]
    public void FireAndHealthPenalties_AreHousingWeighted_FireCappedPerHome()
    {
        Assert.AreEqual(m_Config.FirePenaltyCap, ServiceStats.FirePenaltyAt(m_Config, 1f), 1e-6f);
        Assert.AreEqual(0.35f * m_Config.FirePenalty, ServiceStats.FirePenaltyAt(m_Config, 0.35f), 1e-6f);
        Assert.AreEqual(m_Config.HealthPenalty, ServiceStats.HealthPenaltyAt(m_Config, 1f), 1e-6f);

        // A level-3 home (16) with no care and a level-1 home (4) with full health and fire cover.
        Grow(new Vector2Int(2, 2), ZoneType.Residential, 3);
        Grow(new Vector2Int(12, 12), ZoneType.Residential, 1);
        CivicSystem civic = Civic(Station(new Vector2Int(12, 10), ServiceKind.Health, 3, 1f),
            Station(new Vector2Int(12, 10), ServiceKind.Fire, 3, 1f));
        ServiceStats stats = ServiceStats.Measure(m_Grid, m_Config, new CoverageSystem(16, 16), new PowerSystem(m_Grid, m_Config),
            countPower: false, civic: civic);

        Assert.AreEqual(16f * m_Config.HealthPenalty / 20f, stats.HealthPenalty, 1e-5f);
        Assert.AreEqual(16f * m_Config.FireRisk * m_Config.FirePenalty / 20f, stats.FirePenalty, 1e-5f);
    }

    [Test]
    public void EducatedResidents_EarnResearch_ThroughTheMultiplier()
    {
        using var ages = new TestAges();
        for (int x = 0; x < 8; x++)
        {
            m_Grid.SetRoad(new Vector2Int(x, 0), true);
            Grow(new Vector2Int(x, 1), ZoneType.Residential, 1);
            m_Grid.SetBuiltAge(new Vector2Int(x, 1), TestAges.Industrial);
        }
        var sim = new SimulationSystem(m_Grid, new RoadNetwork(m_Grid), m_Config, ages.Ages, ages.Techs);
        sim.Tech.StartNew(TestAges.Industrial);
        // Half-strength education reaching x 0..3: half of the housing at 0.8 -> share 0.4.
        sim.Sources = new[] { Station(new Vector2Int(1, 3), ServiceKind.Education, 2, 0.8f) };
        sim.Restore(0f, 0f, 0f, 0.1f, 0.1f, 0.1f, 300, 0.7f);

        ResearchBreakdown research = sim.ResearchBreakdown();
        Assert.AreEqual(300 * 0.4f * m_Config.ResearchPerEducatedResident, research.Education, 1e-4f);
        Assert.AreEqual((research.Commercial + research.Buildings + research.Education) * research.Multiplier, sim.ResearchIncome(), 1e-5f);

        var ageless = new SimulationSystem(new GridData(8, 8), new RoadNetwork(new GridData(8, 8)), m_Config);
        Assert.AreEqual(0f, ageless.ResearchIncome(), "no research without ages");
    }

    // 14d: the tick's fast paths (GetCrime for land value, HomeNeeds for ServiceStats) give exactly
    // Explain's numbers on every cell of a mixed city.
    [Test]
    public void FastPaths_MatchExplain()
    {
        m_Population = 400;   // part-way up the ramp
        var zones = new[] { ZoneType.None, ZoneType.Residential, ZoneType.Commercial, ZoneType.Industrial };
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                m_Grid.SetZone(cell, zones[(x * 7 + y * 3) % 4]);
                m_Grid.SetBuildingLevel(cell, (byte)((x + y * 5) % 4));
            }
        }
        CivicSystem civic = Civic(Station(new Vector2Int(3, 3), ServiceKind.Order, 4, 0.5f),
            Station(new Vector2Int(10, 9), ServiceKind.Fire, 3, 0.75f), Station(new Vector2Int(8, 2), ServiceKind.Health, 5, 0.85f),
            Station(new Vector2Int(2, 12), ServiceKind.Education, 4, 0.6f));
        var capacity = new CapacityModel(m_Config);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                CivicBreakdown e = civic.Explain(cell);
                Assert.AreEqual(e.Crime, civic.GetCrime(cell), 1e-6f, $"crime at {cell}");
                int c = capacity.CapacityOf(m_Grid, cell);
                if (m_Grid.GetZone(cell) != ZoneType.Residential || c == 0) continue;
                civic.HomeNeeds(cell, c, civic.Ramp, out float crime, out float fire, out float sick, out float education);
                Assert.AreEqual(e.Crime, crime, 1e-6f, $"home crime at {cell}");
                Assert.AreEqual(e.FireRisk, fire, 1e-6f, $"fire risk at {cell}");
                Assert.AreEqual(e.Sickness, sick, 1e-6f, $"sickness at {cell}");
                Assert.AreEqual(e.Education, education, 1e-6f, $"education at {cell}");
            }
        }
    }
}
