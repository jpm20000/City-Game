using System;
using UnityEngine;

// The parts of one cell's land value, for explaining it to the player. Pollution is negative.
public readonly struct LandValueBreakdown
{
    public readonly float Base;
    public readonly float Services;     // parks (and later services) reaching the cell
    public readonly float Heritage;     // kept historic blocks nearby
    public readonly float Technology;   // researched techs' LandValueBonus
    public readonly float Pollution;

    public LandValueBreakdown(float baseValue, float services, float heritage, float technology, float pollution)
    {
        Base = baseValue;
        Services = services;
        Heritage = heritage;
        Technology = technology;
        Pollution = pollution;
    }

    public float Total => Mathf.Clamp01(Base + Services + Heritage + Technology + Pollution);
}

// Land value per cell (M12), 0..1: LandValueBase + services in range (capped) + kept historic
// blocks within HeritageRadius (capped) + the techs' LandValueBonus - pollution x
// LandValuePerPollution. Homes and shops need LandValueForLevel3 to reach level 3. Derived, never
// saved. Only the heritage counts are cached (recomputed after any grid change); the rest reads the
// coverage, pollution and tech state live.
public sealed class LandValueSystem
{
    private readonly GridData m_Grid;
    private readonly BalanceConfig m_Config;
    private readonly CoverageSystem m_Coverage;
    private readonly PollutionSystem m_Pollution;
    private readonly Func<TechModifiers> m_Tech;
    private byte[] m_Heritage;
    private bool m_Dirty = true;

    // tech null = no tech bonus.
    public LandValueSystem(GridData grid, BalanceConfig config, CoverageSystem coverage, PollutionSystem pollution,
        Func<TechModifiers> tech = null)
    {
        m_Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        m_Config = config ?? throw new ArgumentNullException(nameof(config));
        m_Coverage = coverage ?? throw new ArgumentNullException(nameof(coverage));
        m_Pollution = pollution ?? throw new ArgumentNullException(nameof(pollution));
        m_Tech = tech;
        Allocate();
        grid.OnCellChanged += _ => m_Dirty = true;
        grid.OnResized += Allocate;
    }

    private void Allocate()
    {
        m_Heritage = new byte[m_Grid.Width * m_Grid.Height];
        m_Dirty = true;
    }

    public float GetLandValue(Vector2Int cell) => Explain(cell).Total;

    // Whether a residential or commercial cell may grow to the given level (industry is never gated).
    public bool AllowsLevel(Vector2Int cell, int level)
    {
        if (level < 3) return true;
        ZoneType zone = m_Grid.GetZone(cell);
        if (zone != ZoneType.Residential && zone != ZoneType.Commercial) return true;
        return GetLandValue(cell) >= m_Config.LandValueForLevel3 - 1e-4f;
    }

    // Kept historic grown cells within HeritageRadius (the cell itself included).
    public int HeritageCount(Vector2Int cell)
    {
        if (!m_Grid.InBounds(cell)) return 0;
        EnsureFresh();
        return m_Heritage[cell.y * m_Grid.Width + cell.x];
    }

    public LandValueBreakdown Explain(Vector2Int cell)
    {
        if (!m_Grid.InBounds(cell)) return default;
        float services = Mathf.Min(m_Coverage.GetCoverage(cell) * m_Config.LandValuePerService, m_Config.LandValueServiceCap);
        float heritage = Mathf.Min(HeritageCount(cell) * m_Config.HeritageLandValueEach, m_Config.HeritageLandValueCap);
        float tech = (m_Tech?.Invoke() ?? TechModifiers.None).LandValueBonus;
        float pollution = -m_Pollution.GetPollution(cell) * m_Config.LandValuePerPollution;
        return new LandValueBreakdown(m_Config.LandValueBase, services, heritage, tech, pollution);
    }

    private void EnsureFresh()
    {
        if (!m_Dirty) return;
        m_Dirty = false;

        Array.Clear(m_Heritage, 0, m_Heritage.Length);
        int width = m_Grid.Width;
        int height = m_Grid.Height;
        int r = Mathf.Max(0, m_Config.HeritageRadius);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (!m_Grid.IsHistoric(cell) || m_Grid.GetBuildingLevel(cell) == 0) continue;
                int maxY = Mathf.Min(height - 1, y + r);
                int maxX = Mathf.Min(width - 1, x + r);
                for (int yy = Mathf.Max(0, y - r); yy <= maxY; yy++)
                {
                    for (int xx = Mathf.Max(0, x - r); xx <= maxX; xx++)
                    {
                        int i = yy * width + xx;
                        if (m_Heritage[i] < byte.MaxValue) m_Heritage[i]++;
                    }
                }
            }
        }
    }
}
