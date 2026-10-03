using System;
using System.Collections.Generic;
using UnityEngine;

// Grows undeveloped zoned cells to level 1, then upgrades the lowest-level cells, scanning in
// row-major order so results are deterministic. Level 1 needs only road access. Upgrades are capped
// by the current age's MaxLevel (a historic cell by its own built age's); homes and shops need
// LandValueForLevel3 to reach level 3 (M12, industry exempt); and in ages whose
// upgrades need power they also need power with headroom for the extra draw (reserved during the
// scan). With ages, a separate pass then redevelops up to RedevelopPerDay outdated cells (built in
// an older age, not kept historic) into the current age at the same level. Writes levels and built
// ages into GridData; visuals react via GridData.OnCellChanged.
public sealed class GrowthSystem
{
    private static readonly ZoneType[] s_Zones = { ZoneType.Residential, ZoneType.Commercial, ZoneType.Industrial };

    private readonly GridData m_Grid;
    private readonly RoadNetwork m_Roads;
    private readonly PowerSystem m_Power;
    private readonly BalanceConfig m_Config;
    private readonly CapacityModel m_Capacity;
    private readonly TechSystem m_Tech;
    private readonly LandValueSystem m_LandValue;
    private readonly List<Vector2Int> m_Changed = new();
    private readonly HashSet<Vector2Int> m_ChangedSet = new();
    private readonly List<Vector2Int> m_Redeveloped = new();

    public GrowthSystem(GridData grid, RoadNetwork roads, PowerSystem power, BalanceConfig config)
        : this(grid, roads, power, config, null, null)
    {
    }

    // tech null = no ages (AgeRules.Legacy, nothing is ever outdated); landValue null = no level-3 gate.
    public GrowthSystem(GridData grid, RoadNetwork roads, PowerSystem power, BalanceConfig config,
        CapacityModel capacity, TechSystem tech, LandValueSystem landValue = null)
    {
        m_LandValue = landValue;
        m_Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        m_Roads = roads ?? throw new ArgumentNullException(nameof(roads));
        m_Power = power ?? throw new ArgumentNullException(nameof(power));
        m_Config = config ?? throw new ArgumentNullException(nameof(config));
        m_Capacity = capacity ?? new CapacityModel(config);
        m_Tech = tech;
    }

    private AgeRules Rules => m_Tech != null ? m_Tech.Rules : AgeRules.Legacy;
    private int CurrentAge => m_Tech != null ? m_Tech.CurrentAge : 0;

    // Cells redeveloped into the current age on the last Apply.
    public IReadOnlyList<Vector2Int> Redeveloped => m_Redeveloped;

    // Built in an older age and not kept: growth will rebuild it in the current age's style.
    public bool IsOutdated(Vector2Int cell)
    {
        return m_Tech != null
            && m_Grid.GetBuildingLevel(cell) > 0
            && !m_Grid.IsHistoric(cell)
            && m_Grid.GetBuiltAge(cell) < m_Tech.CurrentAge;
    }

    // The level this cell can grow to: the current age's cap, or its own age's if kept historic.
    public int MaxLevelFor(Vector2Int cell)
    {
        if (m_Grid.IsHistoric(cell)) return m_Capacity.MaxLevelOf(m_Grid.GetBuiltAge(cell));
        return Mathf.Min(Rules.MaxLevel, m_Config.MaxLevel);
    }

    // A grown home or shop that could go up a level but its land value is too low (M12).
    public bool IsHeldByLandValue(Vector2Int cell)
    {
        if (m_LandValue == null) return false;
        int level = m_Grid.GetBuildingLevel(cell);
        return level > 0 && level < MaxLevelFor(cell) && !m_LandValue.AllowsLevel(cell, level + 1);
    }

    // Returns the cells whose level rose this tick (redeveloped cells are in Redeveloped).
    public IReadOnlyList<Vector2Int> Apply(DemandSnapshot demand)
    {
        m_Changed.Clear();
        m_ChangedSet.Clear();
        m_Redeveloped.Clear();
        AgeRules rules = Rules;

        foreach (ZoneType zone in s_Zones)
        {
            float d = demand.Get(zone);
            if (d <= m_Config.GrowthDemandThreshold) continue;

            int budget = Mathf.RoundToInt(d * m_Config.MaxGrowthPerDay);
            for (int level = 0; level < m_Config.MaxLevel && budget > 0; level++)
            {
                budget = CollectAtLevel(zone, level, budget, rules);
            }
        }

        if (m_Tech != null) CollectRedevelopment(rules);

        // Applied after the scan so each write doesn't dirty RoadNetwork mid-scan. New cells are
        // stamped with the current age before their level is set.
        byte age = (byte)CurrentAge;
        foreach (Vector2Int cell in m_Changed)
        {
            byte level = m_Grid.GetBuildingLevel(cell);
            if (level == 0) m_Grid.SetBuiltAge(cell, age);
            m_Grid.SetBuildingLevel(cell, (byte)(level + 1));
        }
        foreach (Vector2Int cell in m_Redeveloped)
        {
            m_Grid.SetBuiltAge(cell, age);
        }
        return m_Changed;
    }

    // Same rules as Apply, for explaining a cell to the player. None still depends on the daily budget.
    public GrowthBlocker GetBlocker(Vector2Int cell, DemandSnapshot demand)
    {
        ZoneType zone = m_Grid.GetZone(cell);
        if (zone == ZoneType.None || m_Grid.IsRoad(cell)) return GrowthBlocker.NotZoned;
        if (m_Grid.IsOccupied(cell)) return GrowthBlocker.Occupied;

        AgeRules rules = Rules;
        int level = m_Grid.GetBuildingLevel(cell);
        if (level >= MaxLevelFor(cell))
        {
            if (IsOutdated(cell))
            {
                if (!m_Roads.HasRoadAccess(cell)) return GrowthBlocker.NoRoadAccess;
                if (rules.UpgradesNeedPower)
                {
                    if (!m_Power.IsPowered(cell)) return GrowthBlocker.NoPower;
                    if (!m_Power.HasHeadroom(cell, m_Capacity.RedevelopDraw(m_Grid, cell, CurrentAge))) return GrowthBlocker.PowerAtCapacity;
                }
                return GrowthBlocker.Outdated;
            }
            if (m_Grid.IsHistoric(cell)) return GrowthBlocker.KeptHistoric;
            return level >= m_Config.MaxLevel ? GrowthBlocker.MaxLevel : GrowthBlocker.AgeMaxLevel;
        }
        if (!m_Roads.HasRoadAccess(cell)) return GrowthBlocker.NoRoadAccess;
        if (m_LandValue != null && !m_LandValue.AllowsLevel(cell, level + 1)) return GrowthBlocker.LowLandValue;
        if (level > 0 && rules.UpgradesNeedPower)
        {
            if (!m_Power.IsPowered(cell)) return GrowthBlocker.NoPower;
            if (!m_Power.HasHeadroom(cell, m_Capacity.UpgradeDraw(m_Grid, cell))) return GrowthBlocker.PowerAtCapacity;
        }
        if (demand.Get(zone) <= m_Config.GrowthDemandThreshold) return GrowthBlocker.LowDemand;
        return GrowthBlocker.None;
    }

    private int CollectAtLevel(ZoneType zone, int level, int budget, AgeRules rules)
    {
        for (int y = 0; y < m_Grid.Height && budget > 0; y++)
        {
            for (int x = 0; x < m_Grid.Width && budget > 0; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (!IsEligible(cell, zone, level)) continue;
                if (level > 0)
                {
                    if (level >= MaxLevelFor(cell)) continue;
                    if (m_LandValue != null && !m_LandValue.AllowsLevel(cell, level + 1)) continue;
                    if (rules.UpgradesNeedPower && !m_Power.TryReserve(cell, m_Capacity.UpgradeDraw(m_Grid, cell))) continue;
                }

                m_Changed.Add(cell);
                m_ChangedSet.Add(cell);
                budget--;
            }
        }
        return budget;
    }

    // Row-major over the whole map, not demand-gated; skips cells that upgraded this tick.
    private void CollectRedevelopment(AgeRules rules)
    {
        int budget = m_Config.RedevelopPerDay;
        int age = CurrentAge;
        for (int y = 0; y < m_Grid.Height && budget > 0; y++)
        {
            for (int x = 0; x < m_Grid.Width && budget > 0; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (!IsOutdated(cell) || m_ChangedSet.Contains(cell)) continue;
                if (m_Grid.GetZone(cell) == ZoneType.None || m_Grid.IsRoad(cell) || m_Grid.IsOccupied(cell)) continue;
                if (!m_Roads.HasRoadAccess(cell)) continue;
                if (rules.UpgradesNeedPower && !m_Power.TryReserve(cell, m_Capacity.RedevelopDraw(m_Grid, cell, age))) continue;

                m_Redeveloped.Add(cell);
                budget--;
            }
        }
    }

    private bool IsEligible(Vector2Int cell, ZoneType zone, int level)
    {
        return m_Grid.GetZone(cell) == zone
            && m_Grid.GetBuildingLevel(cell) == level
            && !m_Grid.IsRoad(cell)
            && !m_Grid.IsOccupied(cell)
            && m_Roads.HasRoadAccess(cell);
    }
}
