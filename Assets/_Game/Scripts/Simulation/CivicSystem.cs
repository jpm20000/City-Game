using System;
using System.Collections.Generic;
using UnityEngine;

// One cell's civic numbers, for explaining them to the player (M14). Crime = CrimePotential x
// Ramp x (1 - Order); all 0..1.
public readonly struct CivicBreakdown
{
    public readonly float Ramp;             // how far the city has grown into its civic needs
    public readonly float CrimePotential;   // from the cell's density, before the ramp and policing
    public readonly float Order;            // best order cover reaching the cell
    public readonly float Crime;

    public CivicBreakdown(float ramp, float crimePotential, float order, float crime)
    {
        Ramp = ramp;
        CrimePotential = crimePotential;
        Order = order;
        Crime = crime;
    }
}

// Civic needs per cell (M14). Crime: grown residential and commercial cells breed crime in
// proportion to their capacity (CrimePerCapacity, capped at 1), scaled by the city's population
// ramp (0 up to CivicFreePopulation, 1 from CivicFullPopulation) and cut by the best order cover
// in reach. Industry and empty land have none. Derived and computed on read (a capacity lookup and
// the cover strength), so only the cover is stored; it is recomputed when the sources change.
public sealed class CivicSystem
{
    private readonly GridData m_Grid;
    private readonly BalanceConfig m_Config;
    private readonly CapacityModel m_Capacity;
    private readonly Func<int> m_Population;
    private IReadOnlyList<ServiceSource> m_Sources = Array.Empty<ServiceSource>();

    public CivicCoverage Cover { get; }

    // population: the city's current population (drives the ramp); null = always fully ramped.
    public CivicSystem(GridData grid, BalanceConfig config, CapacityModel capacity = null, Func<int> population = null)
    {
        m_Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        m_Config = config ?? throw new ArgumentNullException(nameof(config));
        m_Capacity = capacity ?? new CapacityModel(config);
        m_Population = population;
        Cover = new CivicCoverage(grid.Width, grid.Height);
        grid.OnResized += () =>
        {
            Cover.Resize(m_Grid.Width, m_Grid.Height);
            Cover.Recompute(m_Sources);
        };
    }

    public void SetSources(IReadOnlyList<ServiceSource> sources)
    {
        m_Sources = sources ?? Array.Empty<ServiceSource>();
        Cover.Recompute(m_Sources);
    }

    // 0 below CivicFreePopulation, 1 from CivicFullPopulation, linear between.
    public float Ramp => RampAt(m_Config, m_Population?.Invoke() ?? int.MaxValue);

    public static float RampAt(BalanceConfig config, int population)
    {
        int free = config.CivicFreePopulation;
        int full = Mathf.Max(config.CivicFullPopulation, free + 1);
        return Mathf.Clamp01((float)(population - free) / (full - free));
    }

    public float GetCrime(Vector2Int cell) => Explain(cell).Crime;

    public CivicBreakdown Explain(Vector2Int cell)
    {
        if (!m_Grid.InBounds(cell)) return default;
        float ramp = Ramp;
        float potential = CrimePotential(cell);
        float order = Cover.GetStrength(ServiceKind.Order, cell);
        return new CivicBreakdown(ramp, potential, order, potential * ramp * (1f - order));
    }

    private float CrimePotential(Vector2Int cell)
    {
        ZoneType zone = m_Grid.GetZone(cell);
        if (zone != ZoneType.Residential && zone != ZoneType.Commercial) return 0f;
        return Mathf.Min(1f, m_Capacity.CapacityOf(m_Grid, cell) * m_Config.CrimePerCapacity);
    }
}
