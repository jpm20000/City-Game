using System;
using UnityEngine;

// The one place grown-cell capacity is computed: CapacityForLevel(level) x the CapacityScale of the
// age the cell was built in, rounded, at least 1. Without an AgeDatabase every cell uses scale 1
// (today's numbers). Residents/jobs and power / water draw all use it. Called for every cell on
// every network recompute, so the per-age numbers are copied into plain arrays once (M13e: the
// UnityEngine.Object null check and indexers on the AgeDatabase dominated a 96x96 recompute).
public sealed class CapacityModel
{
    private readonly BalanceConfig m_Config;
    private readonly float[] m_Scales;      // per age index; null without ages
    private readonly int[] m_MaxLevels;

    public CapacityModel(BalanceConfig config, AgeDatabase ages = null)
    {
        m_Config = config ?? throw new ArgumentNullException(nameof(config));
        if (ages == null) return;
        m_Scales = new float[ages.Count];
        m_MaxLevels = new int[ages.Count];
        for (int i = 0; i < ages.Count; i++)
        {
            m_Scales[i] = ages[i].CapacityScale;
            m_MaxLevels[i] = ages[i].MaxLevel;
        }
    }

    private bool HasAge(int builtAge) => m_Scales != null && builtAge >= 0 && builtAge < m_Scales.Length;

    public int Capacity(int level, int builtAge)
    {
        int capacity = m_Config.CapacityForLevel(level);
        if (capacity == 0 || !HasAge(builtAge)) return capacity;
        return Math.Max(1, (int)Math.Floor(capacity * m_Scales[builtAge] + 0.5f));
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
        if (!HasAge(builtAge)) return m_Config.MaxLevel;
        return Math.Min(m_MaxLevels[builtAge], m_Config.MaxLevel);
    }
}
