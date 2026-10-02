using System;
using System.Collections.Generic;
using UnityEngine;

// Grows undeveloped zoned cells to level 1, then upgrades the lowest-level cells, scanning in
// row-major order so results are deterministic. Level 1 needs only road access; upgrades also need
// power with headroom for the extra draw (reserved during the scan). Writes levels into GridData; visuals react
// via GridData.OnCellChanged.
public sealed class GrowthSystem
{
    private static readonly ZoneType[] s_Zones = { ZoneType.Residential, ZoneType.Commercial, ZoneType.Industrial };

    private readonly GridData m_Grid;
    private readonly RoadNetwork m_Roads;
    private readonly PowerSystem m_Power;
    private readonly BalanceConfig m_Config;
    private readonly List<Vector2Int> m_Changed = new();

    public GrowthSystem(GridData grid, RoadNetwork roads, PowerSystem power, BalanceConfig config)
    {
        m_Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        m_Roads = roads ?? throw new ArgumentNullException(nameof(roads));
        m_Power = power ?? throw new ArgumentNullException(nameof(power));
        m_Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    // Returns the cells whose level rose this tick.
    public IReadOnlyList<Vector2Int> Apply(DemandSnapshot demand)
    {
        m_Changed.Clear();

        foreach (ZoneType zone in s_Zones)
        {
            float d = demand.Get(zone);
            if (d <= m_Config.GrowthDemandThreshold) continue;

            int budget = Mathf.RoundToInt(d * m_Config.MaxGrowthPerDay);
            for (int level = 0; level < m_Config.MaxLevel && budget > 0; level++)
            {
                budget = CollectAtLevel(zone, level, budget);
            }
        }

        // Applied after the scan so each write doesn't dirty RoadNetwork mid-scan.
        foreach (Vector2Int cell in m_Changed)
        {
            m_Grid.SetBuildingLevel(cell, (byte)(m_Grid.GetBuildingLevel(cell) + 1));
        }
        return m_Changed;
    }

    // Same rules as Apply, for explaining a cell to the player. None still depends on the daily budget.
    public GrowthBlocker GetBlocker(Vector2Int cell, DemandSnapshot demand)
    {
        ZoneType zone = m_Grid.GetZone(cell);
        if (zone == ZoneType.None || m_Grid.IsRoad(cell)) return GrowthBlocker.NotZoned;
        if (m_Grid.IsOccupied(cell)) return GrowthBlocker.Occupied;
        if (m_Grid.GetBuildingLevel(cell) >= m_Config.MaxLevel) return GrowthBlocker.MaxLevel;
        if (!m_Roads.HasRoadAccess(cell)) return GrowthBlocker.NoRoadAccess;
        int level = m_Grid.GetBuildingLevel(cell);
        if (level > 0)
        {
            if (!m_Power.IsPowered(cell)) return GrowthBlocker.NoPower;
            if (!m_Power.HasHeadroom(cell, UpgradeDraw(level))) return GrowthBlocker.PowerAtCapacity;
        }
        if (demand.Get(zone) <= m_Config.GrowthDemandThreshold) return GrowthBlocker.LowDemand;
        return GrowthBlocker.None;
    }

    private int CollectAtLevel(ZoneType zone, int level, int budget)
    {
        for (int y = 0; y < m_Grid.Height && budget > 0; y++)
        {
            for (int x = 0; x < m_Grid.Width && budget > 0; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (!IsEligible(cell, zone, level)) continue;
                if (level > 0 && !m_Power.TryReserve(cell, UpgradeDraw(level))) continue;

                m_Changed.Add(cell);
                budget--;
            }
        }
        return budget;
    }

    private bool IsEligible(Vector2Int cell, ZoneType zone, int level)
    {
        return m_Grid.GetZone(cell) == zone
            && m_Grid.GetBuildingLevel(cell) == level
            && !m_Grid.IsRoad(cell)
            && !m_Grid.IsOccupied(cell)
            && m_Roads.HasRoadAccess(cell);
    }

    private int UpgradeDraw(int level)
    {
        return m_Config.CapacityForLevel(level + 1) - m_Config.CapacityForLevel(level);
    }
}
