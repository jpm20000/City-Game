using System;
using System.Collections.Generic;
using UnityEngine;

// One cell's civic numbers, for explaining them to the player (M14). Crime = CrimePotential x
// Ramp x (1 - Order); FireRisk = FireRiskBase x Ramp x (1 - Fire); Sickness (homes) = Ramp x
// (1 - Health). Order, Fire, Health and Education are the best cover of each line reaching the cell.
// All 0..1.
public readonly struct CivicBreakdown
{
    public readonly float Ramp;             // how far the city has grown into its civic needs
    public readonly float CrimePotential;   // from the cell's density, before the ramp and policing
    public readonly float Order;
    public readonly float Crime;
    public readonly float FireRiskBase;     // by built age (and x the industrial factor), before the ramp and cover
    public readonly float Fire;
    public readonly float FireRisk;
    public readonly float Health;
    public readonly float Sickness;
    public readonly float Education;

    public CivicBreakdown(float ramp, float crimePotential, float order, float crime, float fireRiskBase, float fire,
        float fireRisk, float health, float sickness, float education)
    {
        Ramp = ramp;
        CrimePotential = crimePotential;
        Order = order;
        Crime = crime;
        FireRiskBase = fireRiskBase;
        Fire = fire;
        FireRisk = fireRisk;
        Health = health;
        Sickness = sickness;
        Education = education;
    }
}

// Civic needs per cell (M14). Every need scales with the city's population ramp (0 up to
// CivicFreePopulation, 1 from CivicFullPopulation) and is cut by the best cover of its line:
// - crime: grown homes and shops, capacity x CrimePerCapacity (capped at 1); industry has none;
// - fire risk: every grown block, its built age's FireRisk (BalanceConfig.FireRisk without ages or
//   when the age's is 0), x FireRiskIndustrialFactor for industry;
// - sickness: grown homes without health care.
// Education cover isn't a need: it turns residents into research (SimulationSystem.ResearchBreakdown).
// Derived and computed on read (a capacity lookup and the cover strengths), so only the cover is
// stored; it is recomputed when the sources change.
public sealed class CivicSystem
{
    private readonly GridData m_Grid;
    private readonly BalanceConfig m_Config;
    private readonly CapacityModel m_Capacity;
    private readonly Func<int> m_Population;
    private readonly float[] m_FireRiskByAge;   // per age index; empty without ages
    private readonly Func<TechModifiers> m_Tech;   // ordinances' need multipliers (M15); null = x1
    private TechModifiers m_NeedsFor;               // the modifiers the three cached multipliers were read from
    private float m_CrimeMultiplier = 1f;
    private float m_FireMultiplier = 1f;
    private float m_SickMultiplier = 1f;
    private IReadOnlyList<ServiceSource> m_Sources = Array.Empty<ServiceSource>();

    public CivicCoverage Cover { get; }

    // population: the city's current population (drives the ramp); null = always fully ramped.
    // ages: fire risk per built age; null = BalanceConfig.FireRisk everywhere.
    public CivicSystem(GridData grid, BalanceConfig config, CapacityModel capacity = null, Func<int> population = null,
        AgeDatabase ages = null, Func<TechModifiers> tech = null)
    {
        m_Tech = tech;
        m_Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        m_Config = config ?? throw new ArgumentNullException(nameof(config));
        m_Capacity = capacity ?? new CapacityModel(config, ages);
        m_Population = population;
        m_FireRiskByAge = new float[ages != null ? ages.Count : 0];
        for (int i = 0; i < m_FireRiskByAge.Length; i++)
        {
            float risk = ages[i] != null ? ages[i].FireRisk : 0f;
            m_FireRiskByAge[i] = risk > 0f ? risk : config.FireRisk;
        }
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

    // Crime, fire risk and sickness x the enacted ordinances' multipliers, refreshed when the modifiers object changes.
    private void RefreshNeeds()
    {
        TechModifiers tech = m_Tech?.Invoke() ?? TechModifiers.None;
        if (ReferenceEquals(tech, m_NeedsFor)) return;
        m_NeedsFor = tech;
        m_CrimeMultiplier = tech.CivicNeed(ServiceKind.Order);
        m_FireMultiplier = tech.CivicNeed(ServiceKind.Fire);
        m_SickMultiplier = tech.CivicNeed(ServiceKind.Health);
    }

    // 0 below CivicFreePopulation, 1 from CivicFullPopulation, linear between.
    public float Ramp => RampAt(m_Config, m_Population?.Invoke() ?? int.MaxValue);

    public static float RampAt(BalanceConfig config, int population)
    {
        int free = config.CivicFreePopulation;
        int full = Mathf.Max(config.CivicFullPopulation, free + 1);
        return Mathf.Clamp01((float)(population - free) / (full - free));
    }

    // Crime alone (land value reads it per cell): the zone and capacity checks first, so empty land and
    // industry skip the capacity lookup.
    public float GetCrime(Vector2Int cell)
    {
        if (!m_Grid.InBounds(cell)) return 0f;
        ZoneType zone = m_Grid.GetZone(cell);
        if (zone != ZoneType.Residential && zone != ZoneType.Commercial || m_Grid.GetBuildingLevel(cell) == 0) return 0f;
        int capacity = m_Capacity.CapacityOf(m_Grid, cell);
        if (capacity == 0) return 0f;
        RefreshNeeds();
        return Mathf.Min(1f, capacity * m_Config.CrimePerCapacity) * Ramp * (1f - Cover.GetStrength(ServiceKind.Order, cell))
            * m_CrimeMultiplier;
    }

    public float GetFireRisk(Vector2Int cell) => Explain(cell).FireRisk;
    public float GetSickness(Vector2Int cell) => Explain(cell).Sickness;

    public CivicBreakdown Explain(Vector2Int cell)
    {
        if (!m_Grid.InBounds(cell)) return default;
        RefreshNeeds();
        float ramp = Ramp;
        ZoneType zone = m_Grid.GetZone(cell);
        int capacity = m_Capacity.CapacityOf(m_Grid, cell);
        float order = Cover.GetStrength(ServiceKind.Order, cell);
        float fire = Cover.GetStrength(ServiceKind.Fire, cell);
        float health = Cover.GetStrength(ServiceKind.Health, cell);
        float education = Cover.GetStrength(ServiceKind.Education, cell);

        float crimePotential = capacity > 0 && (zone == ZoneType.Residential || zone == ZoneType.Commercial)
            ? Mathf.Min(1f, capacity * m_Config.CrimePerCapacity)
            : 0f;
        float fireBase = capacity > 0 ? FireRiskOfAge(m_Grid.GetBuiltAge(cell)) : 0f;
        if (zone == ZoneType.Industrial) fireBase *= m_Config.FireRiskIndustrialFactor;
        float sickness = capacity > 0 && zone == ZoneType.Residential ? ramp * (1f - health) : 0f;

        return new CivicBreakdown(ramp, crimePotential, order, crimePotential * ramp * (1f - order) * m_CrimeMultiplier,
            fireBase, fire, fireBase * ramp * (1f - fire) * m_FireMultiplier, health, sickness * m_SickMultiplier, education);
    }

    // Hot path for ServiceStats.Measure (14d): a grown home's needs, given the capacity it already
    // looked up and the ramp read once per measure. Same numbers as Explain.
    public void HomeNeeds(Vector2Int cell, int capacity, float ramp, out float crime, out float fireRisk, out float sickness,
        out float education)
    {
        RefreshNeeds();
        int width = m_Grid.Width;
        int i = cell.y * width + cell.x;
        float order = Cover.StrengthAt(ServiceKind.Order, i);
        float fire = Cover.StrengthAt(ServiceKind.Fire, i);
        float health = Cover.StrengthAt(ServiceKind.Health, i);
        education = Cover.StrengthAt(ServiceKind.Education, i);
        crime = Mathf.Min(1f, capacity * m_Config.CrimePerCapacity) * ramp * (1f - order) * m_CrimeMultiplier;
        fireRisk = FireRiskOfAge(m_Grid.GetBuiltAge(cell)) * ramp * (1f - fire) * m_FireMultiplier;
        sickness = ramp * (1f - health) * m_SickMultiplier;
    }

    // Fire risk of a built age before the ramp and cover (M17: the fire system's flammability reads it).
    public float FireBaseOfAge(int builtAge) => FireRiskOfAge(builtAge);

    // A grown block's fire risk without the capacity lookup and the other civic numbers: the daily ignition
    // sum reads it for every grown block (M17). Same number as Explain(cell).FireRisk; 0 on undeveloped land.
    public float GrownFireRisk(Vector2Int cell, float ramp)
    {
        if (m_Grid.GetBuildingLevel(cell) == 0) return 0f;
        RefreshNeeds();
        float risk = FireRiskOfAge(m_Grid.GetBuiltAge(cell));
        if (m_Grid.GetZone(cell) == ZoneType.Industrial) risk *= m_Config.FireRiskIndustrialFactor;
        float fire = Cover.StrengthAt(ServiceKind.Fire, cell.y * m_Grid.Width + cell.x);
        return risk * ramp * (1f - fire) * m_FireMultiplier;
    }

    private float FireRiskOfAge(int builtAge)
    {
        return builtAge >= 0 && builtAge < m_FireRiskByAge.Length ? m_FireRiskByAge[builtAge] : m_Config.FireRisk;
    }
}
