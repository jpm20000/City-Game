using System;
using System.Collections.Generic;
using UnityEngine;

// Local pollution (M12): grown industrial cells and polluting placed buildings (the power plant)
// emit points that spread to every cell within their radius (Chebyshev distance from the
// footprint), falling off linearly: points x (1 - d / (r + 1)). An industrial cell emits
// IndustrialPollution x its capacity x its built age's PollutionScale; its radius is the built
// age's PollutionRadius (BalanceConfig.PollutionRadius without ages). Every emission is scaled by
// the researched techs' PollutionMultiplier. Derived, never saved; recomputes lazily after any
// grid, source or tech change.
public sealed class PollutionSystem
{
    private readonly GridData m_Grid;
    private readonly BalanceConfig m_Config;
    private readonly CapacityModel m_Capacity;
    private readonly AgeDatabase m_Ages;
    private readonly Func<TechModifiers> m_Tech;
    private IReadOnlyList<ServiceSource> m_Sources = Array.Empty<ServiceSource>();
    private TechModifiers m_LastTech;
    private float[] m_Pollution;
    private bool m_Dirty = true;

    // tech null = no tech effects (multiplier 1).
    public PollutionSystem(GridData grid, BalanceConfig config, CapacityModel capacity = null,
        AgeDatabase ages = null, Func<TechModifiers> tech = null)
    {
        m_Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        m_Config = config ?? throw new ArgumentNullException(nameof(config));
        m_Capacity = capacity ?? new CapacityModel(config, ages);
        m_Ages = ages;
        m_Tech = tech;
        Allocate();
        grid.OnCellChanged += _ => m_Dirty = true;
        grid.OnResized += Allocate;
    }

    private void Allocate()
    {
        m_Pollution = new float[m_Grid.Width * m_Grid.Height];
        m_Dirty = true;
    }

    public void SetSources(IReadOnlyList<ServiceSource> sources)
    {
        m_Sources = sources ?? Array.Empty<ServiceSource>();
        m_Dirty = true;
    }

    private float Multiplier => m_Tech != null ? (m_Tech() ?? TechModifiers.None).PollutionMultiplier : 1f;

    // Pollution points reaching the cell (0 off-map).
    public float GetPollution(Vector2Int cell)
    {
        if (!m_Grid.InBounds(cell)) return 0f;
        EnsureFresh();
        return m_Pollution[cell.y * m_Grid.Width + cell.x];
    }

    // Points a grown industrial cell emits at its source (0 for anything else).
    public float EmissionOf(Vector2Int cell)
    {
        if (!m_Grid.InBounds(cell) || m_Grid.GetZone(cell) != ZoneType.Industrial) return 0f;
        int capacity = m_Capacity.CapacityOf(m_Grid, cell);
        if (capacity == 0) return 0f;
        return m_Config.IndustrialPollution * capacity * ScaleOf(m_Grid.GetBuiltAge(cell)) * Multiplier;
    }

    // How far a grown industrial cell's pollution reaches (by its built age).
    public int RadiusOf(Vector2Int cell)
    {
        return RadiusOfAge(m_Grid.GetBuiltAge(cell));
    }

    // Points a placed building emits (its definition's value x the tech multiplier).
    public float EmissionOf(ServiceSource source) => source.Pollution * Multiplier;

    private float ScaleOf(int builtAge)
    {
        if (m_Ages == null || !m_Ages.IsValidIndex(builtAge)) return 1f;
        return m_Ages[builtAge].PollutionScale;
    }

    private int RadiusOfAge(int builtAge)
    {
        if (m_Ages != null && m_Ages.IsValidIndex(builtAge) && m_Ages[builtAge].PollutionRadius > 0)
            return m_Ages[builtAge].PollutionRadius;
        return m_Config.PollutionRadius;
    }

    private void EnsureFresh()
    {
        TechModifiers tech = m_Tech?.Invoke();
        if (!m_Dirty && ReferenceEquals(tech, m_LastTech)) return;
        m_Dirty = false;
        m_LastTech = tech;

        Array.Clear(m_Pollution, 0, m_Pollution.Length);
        int width = m_Grid.Width;
        int height = m_Grid.Height;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                float points = EmissionOf(cell);
                if (points > 0f) Spread(cell, Vector2Int.one, points, RadiusOf(cell));
            }
        }
        foreach (ServiceSource source in m_Sources)
        {
            float points = EmissionOf(source);
            if (points > 0f) Spread(source.Origin, source.Size, points, source.PollutionRadius);
        }
    }

    private void Spread(Vector2Int origin, Vector2Int size, float points, int radius)
    {
        radius = Mathf.Max(0, radius);
        int width = m_Grid.Width;
        int maxX0 = origin.x + size.x - 1;
        int maxY0 = origin.y + size.y - 1;
        int minX = Mathf.Max(0, origin.x - radius);
        int minY = Mathf.Max(0, origin.y - radius);
        int maxX = Mathf.Min(width - 1, maxX0 + radius);
        int maxY = Mathf.Min(m_Grid.Height - 1, maxY0 + radius);
        float step = 1f / (radius + 1);
        for (int y = minY; y <= maxY; y++)
        {
            int dy = y < origin.y ? origin.y - y : y > maxY0 ? y - maxY0 : 0;
            for (int x = minX; x <= maxX; x++)
            {
                int dx = x < origin.x ? origin.x - x : x > maxX0 ? x - maxX0 : 0;
                int d = Mathf.Max(dx, dy);
                m_Pollution[y * width + x] += points * (1f - d * step);
            }
        }
    }
}
