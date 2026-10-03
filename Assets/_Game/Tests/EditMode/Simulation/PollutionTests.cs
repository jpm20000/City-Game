using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// M12a: local pollution (PollutionSystem) and the per-home Pollution happiness term.
public sealed class PollutionTests
{
    private readonly List<Object> m_Created = new();
    private BalanceConfig m_Config;
    private GridData m_Grid;

    [SetUp]
    public void SetUp()
    {
        m_Config = Make<BalanceConfig>();
        m_Grid = new GridData(16, 16);
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

    private void Grow(Vector2Int cell, ZoneType zone, byte level, byte age = 0)
    {
        m_Grid.SetZone(cell, zone);
        m_Grid.SetBuildingLevel(cell, level);
        m_Grid.SetBuiltAge(cell, age);
    }

    // Two ages: a gentle one (x0.3, radius 1) and the default (x1, config radius).
    private AgeDatabase TwoAges()
    {
        AgeDefinition workshops = Make<AgeDefinition>();
        workshops.Init("workshops", 750, pollutionScale: 0.3f, pollutionRadius: 1);
        AgeDefinition factories = Make<AgeDefinition>();
        factories.Init("factories", 1760);
        AgeDatabase ages = Make<AgeDatabase>();
        ages.Init(workshops, factories);
        return ages;
    }

    [Test]
    public void IndustrialCell_SpreadsWithLinearFalloff()
    {
        var pollution = new PollutionSystem(m_Grid, m_Config);
        Vector2Int source = new Vector2Int(8, 8);
        Grow(source, ZoneType.Industrial, 1);

        // Level 1 = 4 capacity x 0.25 = 1 point; radius 3 -> 1, 0.75, 0.5, 0.25, then nothing.
        Assert.AreEqual(1f, pollution.EmissionOf(source), 1e-5f);
        Assert.AreEqual(1f, pollution.GetPollution(source), 1e-5f);
        Assert.AreEqual(0.75f, pollution.GetPollution(new Vector2Int(9, 9)), 1e-5f);
        Assert.AreEqual(0.5f, pollution.GetPollution(new Vector2Int(6, 9)), 1e-5f);
        Assert.AreEqual(0.25f, pollution.GetPollution(new Vector2Int(8, 11)), 1e-5f);
        Assert.AreEqual(0f, pollution.GetPollution(new Vector2Int(8, 12)), 1e-5f);
    }

    [Test]
    public void Emission_ScalesWithLevel_OnlyIndustryPollutes()
    {
        var pollution = new PollutionSystem(m_Grid, m_Config);
        Grow(new Vector2Int(2, 2), ZoneType.Industrial, 3);
        Grow(new Vector2Int(12, 12), ZoneType.Commercial, 3);
        Grow(new Vector2Int(12, 2), ZoneType.Residential, 3);
        m_Grid.SetZone(new Vector2Int(2, 12), ZoneType.Industrial);   // zoned, not grown

        Assert.AreEqual(4f, pollution.EmissionOf(new Vector2Int(2, 2)), 1e-5f);
        Assert.AreEqual(0f, pollution.GetPollution(new Vector2Int(12, 12)), 1e-5f);
        Assert.AreEqual(0f, pollution.GetPollution(new Vector2Int(12, 2)), 1e-5f);
        Assert.AreEqual(0f, pollution.GetPollution(new Vector2Int(2, 12)), 1e-5f);
    }

    [Test]
    public void BuiltAge_SetsScaleAndRadius()
    {
        AgeDatabase ages = TwoAges();
        var pollution = new PollutionSystem(m_Grid, m_Config, new CapacityModel(m_Config, ages), ages);
        Grow(new Vector2Int(3, 8), ZoneType.Industrial, 3, 0);    // workshop: 4 x 0.3, radius 1
        Grow(new Vector2Int(12, 8), ZoneType.Industrial, 3, 1);   // factory: 4, radius 3

        Assert.AreEqual(1.2f, pollution.EmissionOf(new Vector2Int(3, 8)), 1e-5f);
        Assert.AreEqual(1, pollution.RadiusOf(new Vector2Int(3, 8)));
        Assert.AreEqual(0.6f, pollution.GetPollution(new Vector2Int(4, 8)), 1e-5f);
        Assert.AreEqual(0f, pollution.GetPollution(new Vector2Int(5, 8)), 1e-5f);
        Assert.AreEqual(3, pollution.RadiusOf(new Vector2Int(12, 8)));
        Assert.AreEqual(1f, pollution.GetPollution(new Vector2Int(9, 8)), 1e-5f);
    }

    [Test]
    public void PlacedSource_SpreadsFromItsFootprint()
    {
        var pollution = new PollutionSystem(m_Grid, m_Config);
        pollution.SetSources(new[] { new ServiceSource(new Vector2Int(4, 4), new Vector2Int(3, 3), 0, 600, 6f, 5) });

        Assert.AreEqual(6f, pollution.GetPollution(new Vector2Int(6, 6)), 1e-5f);    // inside
        Assert.AreEqual(5f, pollution.GetPollution(new Vector2Int(7, 4)), 1e-5f);    // d = 1
        Assert.AreEqual(1f, pollution.GetPollution(new Vector2Int(11, 11)), 1e-5f);  // d = 5
        Assert.AreEqual(0f, pollution.GetPollution(new Vector2Int(12, 6)), 1e-5f);   // d = 6

        pollution.SetSources(null);
        Assert.AreEqual(0f, pollution.GetPollution(new Vector2Int(6, 6)), 1e-5f);
    }

    [Test]
    public void Sources_Add_AndRecomputeAfterGridChanges()
    {
        var pollution = new PollutionSystem(m_Grid, m_Config);
        Vector2Int a = new Vector2Int(5, 5);
        Vector2Int b = new Vector2Int(6, 5);
        Grow(a, ZoneType.Industrial, 1);
        Grow(b, ZoneType.Industrial, 1);
        Assert.AreEqual(1.75f, pollution.GetPollution(a), 1e-5f);

        m_Grid.SetBuildingLevel(b, 2);   // 8 x 0.25 = 2 points
        Assert.AreEqual(1f + 1.5f, pollution.GetPollution(a), 1e-5f);

        m_Grid.SetBuildingLevel(b, 0);
        Assert.AreEqual(1f, pollution.GetPollution(a), 1e-5f);

        m_Grid.Resize(8, 8);
        Assert.AreEqual(0f, pollution.GetPollution(a), 1e-5f);
        Grow(new Vector2Int(7, 7), ZoneType.Industrial, 1);
        Assert.AreEqual(1f, pollution.GetPollution(new Vector2Int(7, 7)), 1e-5f);
    }

    [Test]
    public void TechMultiplier_ScalesEveryEmission()
    {
        TechDefinition renewables = Make<TechDefinition>();
        renewables.Init("renewables", 0, 1f, effects: new[] { new TechEffect(TechEffectType.PollutionMultiplier, "", 0.5f) });
        TechModifiers current = TechModifiers.None;
        var pollution = new PollutionSystem(m_Grid, m_Config, tech: () => current);
        Vector2Int cell = new Vector2Int(5, 5);
        Grow(cell, ZoneType.Industrial, 3);
        pollution.SetSources(new[] { new ServiceSource(new Vector2Int(10, 10), Vector2Int.one, 0, 0, 2f, 0) });
        Assert.AreEqual(4f, pollution.GetPollution(cell), 1e-5f);

        current = TechModifiers.Fold(new[] { renewables });   // a new modifiers object marks it dirty
        Assert.AreEqual(2f, pollution.GetPollution(cell), 1e-5f);
        Assert.AreEqual(1f, pollution.GetPollution(new Vector2Int(10, 10)), 1e-5f);
    }

    [Test]
    public void ServiceStats_PollutionPenalty_IsPerHome()
    {
        var sim = new SimulationSystem(m_Grid, new RoadNetwork(m_Grid), m_Config);
        Grow(new Vector2Int(2, 2), ZoneType.Industrial, 3);       // 4 points at the source
        Grow(new Vector2Int(3, 2), ZoneType.Residential, 1);      // d = 1: 3 points -> 0.06
        Grow(new Vector2Int(12, 12), ZoneType.Residential, 1);    // far away: 0

        ServiceStats stats = sim.MeasureServices();
        Assert.AreEqual(3f * m_Config.PollutionPenaltyPerPoint / 2f, stats.PollutionPenalty, 1e-5f);

        // A home on top of heavy pollution pays at most the cap.
        Assert.AreEqual(m_Config.PollutionPenaltyCap, ServiceStats.PollutionPenaltyAt(m_Config, 1000f), 1e-5f);
    }

    [Test]
    public void SeededCity_HomesNearIndustryArePolluted_OthersClean()
    {
        SimulationSystem sim = SeededCity.Run(m_Grid = new GridData(24, 24), m_Config, 60);

        // East homes face industry across the road; homes in the west middle are out of reach of
        // both the industry (x >= 13) and the plant at (0..2, 9..11).
        Assert.AreEqual(ZoneType.Residential, m_Grid.GetZone(new Vector2Int(20, 13)));
        Assert.Greater(sim.Pollution.GetPollution(new Vector2Int(20, 13)), 1f);
        Assert.AreEqual(ZoneType.Residential, m_Grid.GetZone(new Vector2Int(8, 13)));
        Assert.AreEqual(0f, sim.Pollution.GetPollution(new Vector2Int(8, 13)), 1e-5f);
        Assert.Greater(sim.Pollution.GetPollution(new Vector2Int(3, 13)), 0f);   // the plant
        Assert.Less(sim.Population.Happiness.Pollution, 0f);
    }
}
