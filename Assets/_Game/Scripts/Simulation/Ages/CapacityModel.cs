using System;
using UnityEngine;

// The one place grown-cell capacity is computed: CapacityForLevel(level) x the CapacityScale of the
// age the cell was built in, rounded, at least 1. Without an AgeDatabase every cell uses scale 1
// (today's numbers). Residents/jobs and power draw both use it.
public sealed class CapacityModel
{
    private readonly BalanceConfig m_Config;
    private readonly AgeDatabase m_Ages;

    public CapacityModel(BalanceConfig config, AgeDatabase ages = null)
    {
        m_Config = config ?? throw new ArgumentNullException(nameof(config));
        m_Ages = ages;
    }

    public int Capacity(int level, int builtAge)
    {
        int capacity = m_Config.CapacityForLevel(level);
        if (capacity == 0 || m_Ages == null || !m_Ages.IsValidIndex(builtAge)) return capacity;
        return Math.Max(1, (int)Math.Floor(capacity * m_Ages[builtAge].CapacityScale + 0.5f));
    }

    public int CapacityOf(GridData grid, Vector2Int cell)
    {
        return Capacity(grid.GetBuildingLevel(cell), grid.GetBuiltAge(cell));
    }

    // Extra capacity (and power draw) of upgrading the cell one level within its built age.
    public int UpgradeDraw(GridData grid, Vector2Int cell)
    {
        int level = grid.GetBuildingLevel(cell);
        int age = grid.GetBuiltAge(cell);
        return Capacity(level + 1, age) - Capacity(level, age);
    }

    // Extra capacity (and power draw) of rebuilding the cell at its level in another age (never negative).
    public int RedevelopDraw(GridData grid, Vector2Int cell, int newAge)
    {
        int level = grid.GetBuildingLevel(cell);
        return Math.Max(0, Capacity(level, newAge) - Capacity(level, grid.GetBuiltAge(cell)));
    }

    // Highest level a block built in the given age can reach (the config's max without ages).
    public int MaxLevelOf(int builtAge)
    {
        if (m_Ages == null || !m_Ages.IsValidIndex(builtAge)) return m_Config.MaxLevel;
        return Math.Min(m_Ages[builtAge].MaxLevel, m_Config.MaxLevel);
    }
}
