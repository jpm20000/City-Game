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

    public PopulationSystem(BalanceConfig config)
    {
        m_Config = config ?? throw new ArgumentNullException(nameof(config));
        AverageHappiness = config.StartingHappiness;
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

    public void Step(float taxResidential, int serviceCount)
    {
        // Residents above capacity (e.g. after a demolish) are homeless and leave this tick.
        Homeless = Mathf.Max(0, Population - Housing);

        int vacant = Mathf.Max(0, Housing - Population);
        int moveIn = Mathf.CeilToInt(vacant * m_Config.MoveInRate);
        Population = Mathf.Min(Population + moveIn, Housing);

        Workers = Mathf.FloorToInt(Population * m_Config.WorkerRatio);
        // The doc says min(Population, Jobs), but only workers can hold jobs; otherwise
        // non-workers count as unemployed and happiness never recovers.
        Employed = Mathf.Min(Workers, Jobs);
        Unemployed = Workers - Employed;

        float serviceBonus = Mathf.Min(serviceCount * m_Config.ServiceBonusEach, m_Config.ServiceBonusCap);
        AverageHappiness = Mathf.Clamp01(
            m_Config.HappinessBase
            - m_Config.UnemploymentPenalty * Unemployed / Mathf.Max(Workers, 1)
            - m_Config.TaxPenalty * Mathf.Max(0f, taxResidential - m_Config.TaxPenaltyThreshold)
            + serviceBonus
            - m_Config.HomelessPenalty * Homeless / Mathf.Max(Population + Homeless, 1));
    }
}
