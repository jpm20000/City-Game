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

    public void Compute(PopulationSystem population)
    {
        int jobs = population.Jobs;
        int unfilledJobs = Mathf.Max(0, jobs - population.Workers);
        int vacantHomes = Mathf.Max(0, population.Housing - population.Population);

        float residential = Mathf.Clamp01(
            (float)unfilledJobs / Mathf.Max(jobs, m_Config.ResidentialJobsFloor)
            - (float)vacantHomes / Mathf.Max(population.Housing, 1)
            + m_Config.ResidentialBaseDemand);
        if (population.AverageHappiness < m_Config.LowHappinessThreshold)
        {
            residential *= m_Config.LowHappinessDemandScale;
        }

        ResidentialDemand = residential;
        CommercialDemand = JobDemand(population.Population * m_Config.CommercialJobsPerResident, population.CommercialJobs);
        IndustrialDemand = JobDemand(population.Population * m_Config.IndustrialJobsPerResident, population.IndustrialJobs);
    }

    private float JobDemand(float wanted, int current)
    {
        return Mathf.Clamp01((wanted - current) / Mathf.Max(current, m_Config.JobsDemandFloor));
    }
}
