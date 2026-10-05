using System;
using UnityEngine;

// The one place grown-cell capacity is computed: CapacityForLevel(level) x the CapacityScale of the
// age the cell was built in x the zone's density scale (M23; Medium = 1, skipped), rounded, at least 1. Without an AgeDatabase every cell uses scale 1
// (today's numbers). Residents/jobs and power / water draw all use it. Called for every cell on
// every network recompute, so the per-age numbers are copied into plain arrays once (M13e: the
// UnityEngine.Object null check and indexers on the AgeDatabase dominated a 96x96 recompute).
public sealed class CapacityModel
{
    private readonly BalanceConfig m_Config;
    private readonly float[] m_Scales;      // per age index; null without ages
    private readonly int[] m_MaxLevels;
    private readonly float[] m_DensityScales;    // [zone * 3 + density]

    public CapacityModel(BalanceConfig config, AgeDatabase ages = null)
    {
        m_Config = config ?? throw new ArgumentNullException(nameof(config));
        m_DensityScales = new float[4 * DensityUtils.Count];
        for (int zone = 0; zone < 4; zone++)
        {
            for (int density = 0; density < DensityUtils.Count; density++)
                m_DensityScales[zone * DensityUtils.Count + density] = config.DensityScale((ZoneType)zone, (Density)density);
        }
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

    // Medium density (today's numbers).
    public int Capacity(int level, int builtAge)
    {
        int capacity = m_Config.CapacityForLevel(level);
        if (capacity == 0 || !HasAge(builtAge)) return capacity;
        return Math.Max(1, (int)Math.Floor(capacity * m_Scales[builtAge] + 0.5f));
    }

    public int Capacity(int level, int builtAge, ZoneType zone, Density density)
    {
        if (density == Density.Medium) return Capacity(level, builtAge);
        int capacity = m_Config.CapacityForLevel(level);
        if (capacity == 0) return 0;
        float scale = m_DensityScales[(int)zone * DensityUtils.Count + (int)density];
        if (HasAge(builtAge)) scale *= m_Scales[builtAge];
        return Math.Max(1, (int)Math.Floor(capacity * scale + 0.5f));
    }

    public int CapacityOf(GridData grid, Vector2Int cell)
    {
        return Capacity(grid.GetBuildingLevel(cell), grid.GetBuiltAge(cell), grid.GetZone(cell), grid.GetDensity(cell));
    }

    // Capacity the cell would have at another level / built age, in its own zone and density.
    public int CapacityAt(GridData grid, Vector2Int cell, int level, int builtAge)
    {
        return Capacity(level, builtAge, grid.GetZone(cell), grid.GetDensity(cell));
    }

    // Extra capacity (and power draw) of upgrading the cell one level within its built age.
    public int UpgradeDraw(GridData grid, Vector2Int cell)
    {
        int level = grid.GetBuildingLevel(cell);
        int age = grid.GetBuiltAge(cell);
        return CapacityAt(grid, cell, level + 1, age) - CapacityAt(grid, cell, level, age);
    }

    // Extra capacity (and power draw) of rebuilding the cell at its level in another age (never negative).
    public int RedevelopDraw(GridData grid, Vector2Int cell, int newAge)
    {
        int level = grid.GetBuildingLevel(cell);
        return Math.Max(0, CapacityAt(grid, cell, level, newAge) - CapacityAt(grid, cell, level, grid.GetBuiltAge(cell)));
    }

    // Highest level a block built in the given age can reach (the config's max without ages).
    public int MaxLevelOf(int builtAge)
    {
        if (!HasAge(builtAge)) return m_Config.MaxLevel;
        return Math.Min(m_MaxLevels[builtAge], m_Config.MaxLevel);
    }
}
