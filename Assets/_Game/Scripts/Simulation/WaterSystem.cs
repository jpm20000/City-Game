using System;
using System.Collections.Generic;
using UnityEngine;

// Water for growth (M13). In Coverage ages a cell has water when a well or fountain (WaterRadius)
// reaches it; in Piped ages when the WaterNetwork feeds it, and upgrades need headroom for their
// extra draw. The mode follows the current age's AgeRules.Water and is read live, so advancing an
// age switches it with no recompute. Coverage is recomputed when the sources change; the network is lazy.
public sealed class WaterSystem
{
    private readonly GridData m_Grid;
    private readonly CapacityModel m_Capacity;
    private readonly Func<WaterRule> m_Mode;
    private IReadOnlyList<ServiceSource> m_Sources = Array.Empty<ServiceSource>();

    public WaterNetwork Network { get; }

    // How many wells and fountains reach each cell.
    public CoverageSystem Coverage { get; }

    // mode null = WaterRule.Piped (the age-less rules).
    public WaterSystem(GridData grid, BalanceConfig config, CapacityModel capacity = null, Func<WaterRule> mode = null)
    {
        m_Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        if (config == null) throw new ArgumentNullException(nameof(config));
        m_Capacity = capacity ?? new CapacityModel(config);
        m_Mode = mode ?? (() => WaterRule.Piped);
        Network = new WaterNetwork(grid, config, m_Capacity);
        Coverage = new CoverageSystem(grid.Width, grid.Height, source => source.WaterRadius);
        grid.OnResized += () =>
        {
            Coverage.Resize(grid.Width, grid.Height);
            Coverage.Recompute(m_Sources);
        };
    }

    public WaterRule Mode => m_Mode();

    public void SetSources(IReadOnlyList<ServiceSource> sources)
    {
        m_Sources = sources ?? Array.Empty<ServiceSource>();
        Network.SetSources(m_Sources);
        Coverage.Recompute(m_Sources);
    }

    // Whether the cell has water under the current rule (always true when the age needs none).
    // In Piped mode only grown cells can be fed.
    public bool HasWater(Vector2Int cell)
    {
        switch (Mode)
        {
            case WaterRule.Coverage: return Coverage.GetCoverage(cell) > 0;
            case WaterRule.Piped: return Network.IsServed(cell);
            default: return true;
        }
    }

    // Whether the cell can grow from one capacity to another: watered and, when piped, with headroom
    // on its network for the extra draw.
    public bool HasHeadroom(Vector2Int cell, int fromCapacity, int toCapacity)
    {
        if (Mode != WaterRule.Piped) return HasWater(cell);
        return Network.HasHeadroom(cell, Network.ExtraDraw(fromCapacity, toCapacity));
    }

    // Reserves the extra draw on the cell's network (piped), so several upgrades in one tick can't overdraw it.
    public bool TryReserve(Vector2Int cell, int fromCapacity, int toCapacity)
    {
        if (Mode != WaterRule.Piped) return HasWater(cell);
        return Network.TryReserve(cell, Network.ExtraDraw(fromCapacity, toCapacity));
    }

    // Everything the HUD and toasts need, in one pass.
    public WaterStatus Status
    {
        get
        {
            WaterRule mode = Mode;
            int grown = 0;
            for (int y = 0; y < m_Grid.Height; y++)
            {
                for (int x = 0; x < m_Grid.Width; x++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    if (m_Grid.GetZone(cell) != ZoneType.None && !m_Grid.IsRoad(cell) && m_Capacity.CapacityOf(m_Grid, cell) > 0) grown++;
                }
            }
            bool piped = mode == WaterRule.Piped;
            return new WaterStatus(mode, piped ? Network.Supply : 0, piped ? Network.Demand : 0, DryCells, grown);
        }
    }

    // Grown cells without water under the current rule.
    public int DryCells
    {
        get
        {
            switch (Mode)
            {
                case WaterRule.Piped: return Network.UnservedCells;
                case WaterRule.Coverage:
                    int dry = 0;
                    for (int y = 0; y < m_Grid.Height; y++)
                    {
                        for (int x = 0; x < m_Grid.Width; x++)
                        {
                            Vector2Int cell = new Vector2Int(x, y);
                            if (m_Grid.GetZone(cell) == ZoneType.None || m_Grid.IsRoad(cell)) continue;
                            if (m_Capacity.CapacityOf(m_Grid, cell) > 0 && Coverage.GetCoverage(cell) == 0) dry++;
                        }
                    }
                    return dry;
                default: return 0;
            }
        }
    }
}
