using NUnit.Framework;
using UnityEngine;

public sealed class ServicesTests
{
    private BalanceConfig m_Config;
    private GridData m_Grid;
    private PowerSystem m_Power;
    private CoverageSystem m_Coverage;

    [SetUp]
    public void SetUp()
    {
        m_Config = ScriptableObject.CreateInstance<BalanceConfig>();
        m_Grid = new GridData(24, 24);
        m_Power = new PowerSystem(m_Grid, m_Config);
        m_Coverage = new CoverageSystem(24, 24);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(m_Config);
    }

    private void Road(int fromX, int toX, int y)
    {
        for (int x = fromX; x <= toX; x++) m_Grid.SetRoad(new Vector2Int(x, y), true);
    }

    private void Grown(Vector2Int cell, ZoneType zone, byte level)
    {
        m_Grid.SetZone(cell, zone);
        m_Grid.SetBuildingLevel(cell, level);
    }

    private static ServiceSource Plant(int x, int y, int supply)
    {
        return new ServiceSource(new Vector2Int(x, y), Vector2Int.one, 0, supply);
    }

    // --- Power ---

    [Test]
    public void Power_FlowsAlongRoadsToAdjacentGrownCells()
    {
        // Road on row 5 from x=2..10 (not touching the map edge: power doesn't need the entry).
        Road(2, 10, 5);
        Grown(new Vector2Int(10, 6), ZoneType.Residential, 1);
        Grown(new Vector2Int(6, 4), ZoneType.Industrial, 2);
        Grown(new Vector2Int(6, 7), ZoneType.Commercial, 1);    // two cells from the road
        m_Power.SetSources(new[] { Plant(2, 6, 100) });

        Assert.IsTrue(m_Power.IsEnergisedRoad(new Vector2Int(10, 5)));
        Assert.IsTrue(m_Power.IsPowered(new Vector2Int(10, 6)));
        Assert.IsTrue(m_Power.IsPowered(new Vector2Int(6, 4)));
        Assert.IsFalse(m_Power.IsPowered(new Vector2Int(6, 7)));
        Assert.AreEqual(100, m_Power.Supply);
        Assert.AreEqual(4 + 8, m_Power.Load);
        Assert.AreEqual(4 + 8 + 4, m_Power.Demand);
        Assert.AreEqual(1, m_Power.UnpoweredCells);
    }

    [Test]
    public void Power_PlantNotTouchingRoad_PowersNothing()
    {
        Road(0, 10, 5);
        Grown(new Vector2Int(3, 6), ZoneType.Residential, 1);
        // Diagonal to the road's end only: diagonals don't connect, like road access.
        m_Power.SetSources(new[] { Plant(11, 6, 100) });

        Assert.IsFalse(m_Power.IsEnergisedRoad(new Vector2Int(10, 5)));
        Assert.IsFalse(m_Power.IsPowered(new Vector2Int(3, 6)));
        Assert.AreEqual(0, m_Power.Supply);
    }

    [Test]
    public void Power_DisconnectedRoadIsland_StaysDark()
    {
        Road(0, 5, 5);
        Road(8, 12, 5);   // gap at x=6..7
        Grown(new Vector2Int(10, 6), ZoneType.Residential, 1);
        m_Power.SetSources(new[] { Plant(0, 6, 100) });

        Assert.IsTrue(m_Power.IsEnergisedRoad(new Vector2Int(5, 5)));
        Assert.IsFalse(m_Power.IsEnergisedRoad(new Vector2Int(8, 5)));
        Assert.IsFalse(m_Power.IsPowered(new Vector2Int(10, 6)));
    }

    [Test]
    public void Power_Shortage_FurthestCellsGoDarkFirst()
    {
        Road(0, 12, 5);
        for (int x = 2; x <= 12; x += 2) Grown(new Vector2Int(x, 6), ZoneType.Residential, 1);   // 6 homes x 4
        m_Power.SetSources(new[] { Plant(0, 6, 12) });

        Assert.IsTrue(m_Power.IsPowered(new Vector2Int(2, 6)));
        Assert.IsTrue(m_Power.IsPowered(new Vector2Int(6, 6)));
        Assert.IsFalse(m_Power.IsPowered(new Vector2Int(8, 6)));
        Assert.IsFalse(m_Power.IsPowered(new Vector2Int(12, 6)));
        Assert.AreEqual(12, m_Power.Load);
        Assert.AreEqual(3, m_Power.UnpoweredCells);
    }

    [Test]
    public void Power_PlantsOnOneNetworkPool_IndependentOfBuildOrder()
    {
        Road(0, 20, 5);
        for (int x = 1; x <= 19; x++) Grown(new Vector2Int(x, 4), ZoneType.Commercial, 1);
        ServiceSource west = Plant(0, 6, 20);
        ServiceSource east = Plant(20, 6, 20);

        m_Power.SetSources(new[] { west, east });
        bool[] first = new bool[24];
        for (int x = 0; x < 24; x++) first[x] = m_Power.IsPowered(new Vector2Int(x, 4));

        m_Power.SetSources(new[] { east, west });
        for (int x = 0; x < 24; x++) Assert.AreEqual(first[x], m_Power.IsPowered(new Vector2Int(x, 4)), $"x={x}");

        // 40 units pooled = 10 homes, five nearest each plant.
        Assert.AreEqual(40, m_Power.Load);
        Assert.IsTrue(m_Power.IsPowered(new Vector2Int(1, 4)));
        Assert.IsTrue(m_Power.IsPowered(new Vector2Int(19, 4)));
        Assert.IsFalse(m_Power.IsPowered(new Vector2Int(10, 4)));
    }

    [Test]
    public void Power_RecomputesAfterGridChange()
    {
        Road(0, 5, 5);
        Grown(new Vector2Int(5, 6), ZoneType.Residential, 1);
        m_Power.SetSources(new[] { Plant(0, 6, 100) });
        Assert.IsTrue(m_Power.IsPowered(new Vector2Int(5, 6)));

        m_Grid.SetRoad(new Vector2Int(3, 5), false);

        Assert.IsFalse(m_Power.IsPowered(new Vector2Int(5, 6)));
    }

    [Test]
    public void Growth_UpgradeNeedsPowerAndHeadroom()
    {
        RoadNetwork roads = new RoadNetwork(m_Grid);
        GrowthSystem growth = new GrowthSystem(m_Grid, roads, m_Power, m_Config);
        Road(0, 6, 0);
        Vector2Int a = new Vector2Int(1, 1);
        Vector2Int b = new Vector2Int(2, 1);
        Grown(a, ZoneType.Residential, 1);
        Grown(b, ZoneType.Residential, 1);
        DemandSnapshot high = new DemandSnapshot(1f, 0f, 0f);

        Assert.AreEqual(GrowthBlocker.NoPower, growth.GetBlocker(a, high));
        growth.Apply(high);
        Assert.AreEqual(1, m_Grid.GetBuildingLevel(a));

        // 12 units: both L1 homes (8) + one upgrade's extra 4. The second upgrade must wait.
        m_Power.SetSources(new[] { Plant(6, 1, 12) });
        Assert.AreEqual(GrowthBlocker.None, growth.GetBlocker(a, high));
        growth.Apply(high);

        Assert.AreEqual(2, m_Grid.GetBuildingLevel(a));
        Assert.AreEqual(1, m_Grid.GetBuildingLevel(b));
        Assert.AreEqual(GrowthBlocker.PowerAtCapacity, growth.GetBlocker(b, high));
        Assert.LessOrEqual(m_Power.Load, m_Power.Supply);
    }

    [Test]
    public void Growth_UnpoweredCellsStillGrowToLevel1()
    {
        RoadNetwork roads = new RoadNetwork(m_Grid);
        GrowthSystem growth = new GrowthSystem(m_Grid, roads, m_Power, m_Config);
        Road(0, 6, 0);
        Vector2Int cell = new Vector2Int(3, 1);
        m_Grid.SetZone(cell, ZoneType.Commercial);

        Assert.AreEqual(GrowthBlocker.None, growth.GetBlocker(cell, new DemandSnapshot(0f, 1f, 0f)));
        growth.Apply(new DemandSnapshot(0f, 1f, 0f));

        Assert.AreEqual(1, m_Grid.GetBuildingLevel(cell));
    }

    // --- Coverage ---

    [Test]
    public void Coverage_RadiusAroundRotatedFootprint()
    {
        // A 2x3 building rotated once stands 3 wide, 2 deep.
        Vector2Int size = CellUtils.EffectiveSize(new Vector2Int(2, 3), 1);
        m_Coverage.Recompute(new[] { new ServiceSource(new Vector2Int(10, 10), size, 2, 0) });

        Assert.AreEqual(1, m_Coverage.GetCoverage(new Vector2Int(8, 8)));     // corner, Chebyshev 2
        Assert.AreEqual(1, m_Coverage.GetCoverage(new Vector2Int(14, 13)));   // x 10..12 + 2, y 10..11 + 2
        Assert.AreEqual(0, m_Coverage.GetCoverage(new Vector2Int(15, 12)));
        Assert.AreEqual(0, m_Coverage.GetCoverage(new Vector2Int(12, 14)));
        Assert.AreEqual(0, m_Coverage.GetCoverage(new Vector2Int(7, 10)));
    }

    [Test]
    public void Coverage_ClipsAtMapEdgeAndStacks()
    {
        ServiceSource park = new ServiceSource(Vector2Int.zero, new Vector2Int(2, 2), 4, 0);
        m_Coverage.Recompute(new[] { park, park, park });

        Assert.AreEqual(3, m_Coverage.GetCoverage(new Vector2Int(5, 5)));
        Assert.AreEqual(0, m_Coverage.GetCoverage(new Vector2Int(6, 0)));
        Assert.AreEqual(0, m_Coverage.GetCoverage(new Vector2Int(-1, 0)));
    }

    [Test]
    public void ServiceStats_AverageOverHomes_CappedPerHome()
    {
        // Covered: a L3 home (16) under five parks (capped); uncovered: a L1 home (4) far away.
        Grown(new Vector2Int(2, 2), ZoneType.Residential, 3);
        Grown(new Vector2Int(20, 20), ZoneType.Residential, 1);
        Grown(new Vector2Int(3, 2), ZoneType.Commercial, 3);   // jobs don't count
        ServiceSource park = new ServiceSource(new Vector2Int(0, 0), Vector2Int.one, 4, 0);
        m_Coverage.Recompute(new[] { park, park, park, park, park });

        ServiceStats stats = ServiceStats.Measure(m_Grid, m_Config, m_Coverage, m_Power);

        Assert.AreEqual(m_Config.ServiceBonusCap * 16f / 20f, stats.ServiceBonus, 1e-5f);
        Assert.AreEqual(1f, stats.UnpoweredHousingShare, 1e-5f);
    }

    [Test]
    public void ServiceStats_NoHomes_IsZero()
    {
        ServiceStats stats = ServiceStats.Measure(m_Grid, m_Config, m_Coverage, m_Power);

        Assert.AreEqual(0f, stats.ServiceBonus);
        Assert.AreEqual(0f, stats.UnpoweredHousingShare);
    }
}
