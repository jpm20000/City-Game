using System;
using UnityEngine;

public sealed class DemandSystem
{
    private readonly BalanceConfig m_Config;

    public float ResidentialDemand { get; private set; }
    public float CommercialDemand { get; private set; }
    public float IndustrialDemand { get; private set; }
    public DemandSnapshot Snapshot => new DemandSnapshot(ResidentialDemand, CommercialDemand, IndustrialDemand);

    public DemandSystem(BalanceConfig config)
    {
        m_Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    // tech = researched techs' per-zone demand multipliers (null = none).
    public void Compute(PopulationSystem population, float taxResidential, float taxCommercial, float taxIndustrial,
        TechModifiers tech = null)
    {
        tech ??= TechModifiers.None;
        int jobs = population.Jobs;
        int unfilledJobs = Mathf.Max(0, jobs - population.Workers);
        int vacantHomes = Mathf.Max(0, population.Housing - population.Population);

        float residential = Mathf.Clamp01(
            ((float)unfilledJobs / Mathf.Max(jobs, m_Config.ResidentialJobsFloor)
             - (float)vacantHomes / Mathf.Max(population.Housing, 1)
             + m_Config.ResidentialBaseDemand)
            * TaxMultiplier(taxResidential));
        if (population.AverageHappiness < m_Config.LowHappinessThreshold)
        {
            residential *= m_Config.LowHappinessDemandScale;
        }

        ResidentialDemand = Mathf.Clamp01(residential * tech.DemandMultiplier(ZoneType.Residential));
        CommercialDemand = Mathf.Clamp01(tech.DemandMultiplier(ZoneType.Commercial)
            * JobDemand(population.Population * m_Config.CommercialJobsPerResident, population.CommercialJobs, taxCommercial));
        IndustrialDemand = Mathf.Clamp01(tech.DemandMultiplier(ZoneType.Industrial)
            * JobDemand(population.Population * m_Config.IndustrialJobsPerResident, population.IndustrialJobs, taxIndustrial));
    }

    // Residents and businesses weigh their zone's tax: above the threshold demand shrinks, below it grows.
    public float TaxMultiplier(float tax)
    {
        return Mathf.Max(0f, 1f - m_Config.TaxDemandScale * (tax - m_Config.TaxPenaltyThreshold));
    }

    private float JobDemand(float wanted, int current, float tax)
    {
        return Mathf.Clamp01((wanted - current) / Mathf.Max(current, m_Config.JobsDemandFloor) * TaxMultiplier(tax));
    }
}
