using System;
using UnityEngine;

public sealed class PopulationSystem
{
    private readonly BalanceConfig m_Config;

    public int Population { get; private set; }
    public int Workers { get; private set; }
    public int Employed { get; private set; }
    public int Unemployed { get; private set; }
    public int Homeless { get; private set; }
    public int Housing { get; private set; }
    public int CommercialJobs { get; private set; }
    public int IndustrialJobs { get; private set; }
    public int Jobs => CommercialJobs + IndustrialJobs;
    public float AverageHappiness { get; private set; }
    public int MovedOut { get; private set; }   // residents who left in the last Step (unhappiness)
    public HappinessBreakdown Happiness { get; private set; }

    public PopulationSystem(BalanceConfig config)
    {
        m_Config = config ?? throw new ArgumentNullException(nameof(config));
        AverageHappiness = config.StartingHappiness;
        Happiness = new HappinessBreakdown(config.StartingHappiness, 0f, 0f, 0f, 0f, 0f);
    }

    public void RecountCapacity(GridData grid, CityModifiers modifiers)
    {
        int housing = modifiers.Housing;
        int commercial = modifiers.CommercialJobs;
        int industrial = modifiers.IndustrialJobs;

        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                int capacity = m_Config.CapacityForLevel(grid.GetBuildingLevel(cell));
                if (capacity == 0) continue;

                switch (grid.GetZone(cell))
                {
                    case ZoneType.Residential: housing += capacity; break;
                    case ZoneType.Commercial: commercial += capacity; break;
                    case ZoneType.Industrial: industrial += capacity; break;
                }
            }
        }

        Housing = housing;
        CommercialJobs = commercial;
        IndustrialJobs = industrial;
        Workers = Mathf.FloorToInt(Population * m_Config.WorkerRatio);
    }

    public void Step(float taxResidential, float taxCommercial, float taxIndustrial, int serviceCount)
    {
        // Residents above capacity (e.g. after a demolish) are homeless and leave this tick.
        Homeless = Mathf.Max(0, Population - Housing);

        // Vacant homes always attract newcomers; yesterday's unhappiness also drives residents out.
        int housed = Mathf.Min(Population, Housing);
        int moveIn = Mathf.CeilToInt((Housing - housed) * m_Config.MoveInRate);
        MovedOut = AverageHappiness < m_Config.LowHappinessThreshold
            ? Mathf.CeilToInt(housed * m_Config.MoveOutRate)
            : 0;
        Population = housed - MovedOut + moveIn;

        RecountEmployment();

        Happiness = ComputeHappiness(taxResidential, taxCommercial, taxIndustrial, serviceCount);
        AverageHappiness = Happiness.Total;
    }

    // Load / new game: rebuilds the breakdown for the UI without touching the saved AverageHappiness.
    public void RefreshHappinessBreakdown(float taxResidential, float taxCommercial, float taxIndustrial, int serviceCount)
    {
        Happiness = ComputeHappiness(taxResidential, taxCommercial, taxIndustrial, serviceCount);
    }

    // Happiness lost to taxes above the threshold (positive number). Also used by the tax panel preview.
    public float TaxHappinessPenalty(float taxResidential, float taxCommercial, float taxIndustrial)
    {
        float threshold = m_Config.TaxPenaltyThreshold;
        return m_Config.TaxPenalty * Mathf.Max(0f, taxResidential - threshold)
            + m_Config.JobTaxPenalty * (Mathf.Max(0f, taxCommercial - threshold) + Mathf.Max(0f, taxIndustrial - threshold));
    }

    private HappinessBreakdown ComputeHappiness(float taxResidential, float taxCommercial, float taxIndustrial, int serviceCount)
    {
        // Unemployment and pollution ramp in with size: new towns are always lopsided.
        float cityWeight = Mathf.Clamp01((float)Population / Mathf.Max(m_Config.SmallTownGracePopulation, 1));

        return new HappinessBreakdown(
            m_Config.HappinessBase,
            -m_Config.UnemploymentPenalty * cityWeight * Unemployed / Mathf.Max(Workers, 1),
            -TaxHappinessPenalty(taxResidential, taxCommercial, taxIndustrial),
            -m_Config.PollutionPenalty * cityWeight * IndustrialJobs / Mathf.Max(Housing + Jobs, 1),
            Mathf.Min(serviceCount * m_Config.ServiceBonusEach, m_Config.ServiceBonusCap),
            -m_Config.HomelessPenalty * Homeless / Mathf.Max(Population + Homeless, 1));
    }

    // Load / new game. Call RecountCapacity first so the derived worker/job stats are current.
    public void Restore(int population, float happiness)
    {
        Population = Mathf.Max(0, population);
        AverageHappiness = Mathf.Clamp01(happiness);
        Homeless = Mathf.Max(0, Population - Housing);
        RecountEmployment();
    }

    private void RecountEmployment()
    {
        Workers = Mathf.FloorToInt(Population * m_Config.WorkerRatio);
        // The doc says min(Population, Jobs), but only workers can hold jobs; otherwise
        // non-workers count as unemployed and happiness never recovers.
        Employed = Mathf.Min(Workers, Jobs);
        Unemployed = Workers - Employed;
    }
}
