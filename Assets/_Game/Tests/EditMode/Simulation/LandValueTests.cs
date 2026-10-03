using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// M12b: land value (LandValueSystem), the level-3 gate and the heritage bonus.
public sealed class LandValueTests
{
    private static readonly DemandSnapshot Full = new DemandSnapshot(1f, 1f, 1f);
    private static readonly ServiceSource Plant = new ServiceSource(new Vector2Int(15, 1), Vector2Int.one, 0, 1000);

    private readonly List<Object> m_Created = new();
    private BalanceConfig m_Config;
    private GridData m_Grid;
    private SimulationSystem m_Sim;

    [SetUp]
    public void SetUp()
    {
        m_Config = Make<BalanceConfig>();
        m_Grid = new GridData(16, 16);
        for (int x = 0; x < 16; x++) m_Grid.SetRoad(new Vector2Int(x, 0), true);
        m_Sim = new SimulationSystem(m_Grid, new RoadNetwork(m_Grid), m_Config);
        m_Sim.Sources = new[] { Plant };
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

    private void Grow(Vector2Int cell, ZoneType zone, byte level)
    {
        m_Grid.SetZone(cell, zone);
        m_Grid.SetBuildingLevel(cell, level);
    }

    private static ServiceSource Polluter(Vector2Int at, float points, int radius) =>
        new ServiceSource(at, Vector2Int.one, 0, 0, points, radius);

    private static ServiceSource Park(Vector2Int at) => new ServiceSource(at, new Vector2Int(2, 2), 4, 0);

    [Test]
    public void Contributors_AddUp_AndClamp()
    {
        Vector2Int cell = new Vector2Int(8, 8);
        Assert.AreEqual(m_Config.LandValueBase, m_Sim.LandValue.GetLandValue(cell), 1e-5f);

        // Three parks reach the cell: capped at +0.20.
        m_Sim.Sources = new[] { Plant, Park(new Vector2Int(6, 6)), Park(new Vector2Int(9, 9)), Park(new Vector2Int(5, 10)) };
        LandValueBreakdown parks = m_Sim.LandValue.Explain(cell);
        Assert.AreEqual(m_Config.LandValueServiceCap, parks.Services, 1e-5f);
        Assert.AreEqual(m_Config.LandValueBase + m_Config.LandValueServiceCap, parks.Total, 1e-5f);

        // 5 points of pollution at the source: -0.20.
        m_Sim.Sources = new[] { Plant, Polluter(cell, 5f, 0) };
        LandValueBreakdown dirty = m_Sim.LandValue.Explain(cell);
        Assert.AreEqual(-5f * m_Config.LandValuePerPollution, dirty.Pollution, 1e-5f);
        Assert.AreEqual(m_Config.LandValueBase - 0.2f, dirty.Total, 1e-5f);

        // Never below 0.
        m_Sim.Sources = new[] { Plant, Polluter(cell, 1000f, 0) };
        Assert.AreEqual(0f, m_Sim.LandValue.GetLandValue(cell), 1e-5f);
    }

    [Test]
    public void TechBonus_RaisesEveryCell()
    {
        TechDefinition architecture = Make<TechDefinition>();
        architecture.Init("architecture_plus", 0, 1f, effects: new[] { new TechEffect(TechEffectType.LandValueBonus, "", 0.05f) });
        TechModifiers mods = TechModifiers.Fold(new[] { architecture });
        var pollution = new PollutionSystem(m_Grid, m_Config);
        var coverage = new CoverageSystem(16, 16);
        var landValue = new LandValueSystem(m_Grid, m_Config, coverage, pollution, () => mods);

        Assert.AreEqual(0.05f, landValue.Explain(new Vector2Int(3, 3)).Technology, 1e-5f);
        Assert.AreEqual(m_Config.LandValueBase + 0.05f, landValue.GetLandValue(new Vector2Int(3, 3)), 1e-5f);
    }

    [Test]
    public void Heritage_KeptBlocksRaiseValueAround_UntilDemolished()
    {
        Vector2Int kept = new Vector2Int(8, 8);
        Grow(kept, ZoneType.Residential, 1);
        Assert.AreEqual(0, m_Sim.LandValue.HeritageCount(kept));   // not kept yet

        m_Grid.SetHistoric(kept, true);
        Grow(new Vector2Int(9, 8), ZoneType.Residential, 1);
        m_Grid.SetHistoric(new Vector2Int(9, 8), true);

        Assert.AreEqual(2, m_Sim.LandValue.HeritageCount(kept));
        Assert.AreEqual(2, m_Sim.LandValue.HeritageCount(new Vector2Int(11, 11)));
        Assert.AreEqual(1, m_Sim.LandValue.HeritageCount(new Vector2Int(12, 8)));   // only (9,8) reaches
        Assert.AreEqual(0, m_Sim.LandValue.HeritageCount(new Vector2Int(13, 8)));
        Assert.AreEqual(2 * m_Config.HeritageLandValueEach, m_Sim.LandValue.Explain(new Vector2Int(10, 10)).Heritage, 1e-5f);

        m_Grid.SetBuildingLevel(new Vector2Int(9, 8), 0);   // demolished: the flag goes with it
        Assert.AreEqual(1, m_Sim.LandValue.HeritageCount(new Vector2Int(10, 10)));
    }

    [Test]
    public void Heritage_RaisesHappinessOfHomesNearby()
    {
        Vector2Int kept = new Vector2Int(8, 8);
        Grow(kept, ZoneType.Residential, 1);
        m_Grid.SetHistoric(kept, true);
        Grow(new Vector2Int(9, 9), ZoneType.Residential, 1);     // 1 kept block nearby (and kept counts itself)
        Grow(new Vector2Int(1, 14), ZoneType.Residential, 1);    // none

        ServiceStats stats = m_Sim.MeasureServices();
        Assert.AreEqual(2f * m_Config.HeritageHappinessEach / 3f, stats.HeritageBonus, 1e-5f);
        Assert.AreEqual(m_Config.HeritageHappinessCap, ServiceStats.HeritageBonusAt(m_Config, 100), 1e-5f);

        m_Sim.Population.RecountCapacity(m_Grid, default);
        m_Sim.Population.Step(0.1f, 0.1f, 0.1f, stats);
        Assert.AreEqual(stats.HeritageBonus, m_Sim.Population.Happiness.Heritage, 1e-5f);
    }

    [Test]
    public void Level3_NeedsLandValue_ParkLetsAPollutedHomeThrough()
    {
        Vector2Int home = new Vector2Int(2, 1);
        Vector2Int shed = new Vector2Int(4, 1);
        Grow(home, ZoneType.Residential, 2);
        Grow(shed, ZoneType.Industrial, 2);
        // 6 points two cells away (6 x (1 - 2/3) = 2) plus the level-2 shed's 1 -> land value 0.38 at
        // the home; after the shed reaches level 3 it is 4 points, and a park (+0.10) makes it 0.44.
        ServiceSource smoke = Polluter(new Vector2Int(3, 3), 6f, 2);
        m_Sim.Sources = new[] { Plant, smoke };
        Assert.Less(m_Sim.LandValue.GetLandValue(home), m_Config.LandValueForLevel3);

        Assert.AreEqual(GrowthBlocker.LowLandValue, m_Sim.Growth.GetBlocker(home, Full));
        Assert.AreEqual(GrowthBlocker.None, m_Sim.Growth.GetBlocker(shed, Full));   // industry is exempt
        m_Sim.Growth.Apply(Full);
        Assert.AreEqual(2, m_Grid.GetBuildingLevel(home));
        Assert.AreEqual(3, m_Grid.GetBuildingLevel(shed));

        m_Sim.Sources = new[] { Plant, smoke, Park(new Vector2Int(5, 2)) };
        Assert.GreaterOrEqual(m_Sim.LandValue.GetLandValue(home), m_Config.LandValueForLevel3);
        Assert.AreEqual(GrowthBlocker.None, m_Sim.Growth.GetBlocker(home, Full));
        m_Sim.Growth.Apply(Full);
        Assert.AreEqual(3, m_Grid.GetBuildingLevel(home));

        // Once there, losing the land value doesn't demolish anything.
        m_Sim.Sources = new[] { Plant, smoke };
        m_Sim.Growth.Apply(Full);
        Assert.AreEqual(3, m_Grid.GetBuildingLevel(home));
    }

    [Test]
    public void LowerLevels_AreNeverGated()
    {
        Vector2Int home = new Vector2Int(2, 1);
        m_Grid.SetZone(home, ZoneType.Residential);
        m_Sim.Sources = new[] { Plant, Polluter(home, 1000f, 0) };
        Assert.AreEqual(0f, m_Sim.LandValue.GetLandValue(home), 1e-5f);

        m_Sim.Growth.Apply(Full);
        m_Sim.Growth.Apply(Full);
        Assert.AreEqual(2, m_Grid.GetBuildingLevel(home));
        Assert.IsTrue(m_Sim.LandValue.AllowsLevel(home, 2));
        Assert.IsFalse(m_Sim.LandValue.AllowsLevel(home, 3));
    }
}
