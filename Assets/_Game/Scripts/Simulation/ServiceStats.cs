using UnityEngine;

// What the spatial systems mean for residents, averaged over homes (weighted by each grown
// residential cell's capacity). Fed into PopulationSystem.Step for the happiness terms.
public readonly struct ServiceStats
{
    public readonly float ServiceBonus;             // average per-home service bonus (already capped per home)
    public readonly float UnpoweredHousingShare;    // 0..1

    public ServiceStats(float serviceBonus, float unpoweredHousingShare)
    {
        ServiceBonus = serviceBonus;
        UnpoweredHousingShare = unpoweredHousingShare;
    }

    public static ServiceStats Measure(GridData grid, BalanceConfig config, CoverageSystem coverage, PowerSystem power)
    {
        float bonus = 0f;
        int housing = 0;
        int unpowered = 0;

        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (grid.GetZone(cell) != ZoneType.Residential) continue;
                int capacity = config.CapacityForLevel(grid.GetBuildingLevel(cell));
                if (capacity == 0) continue;

                housing += capacity;
                bonus += capacity * Mathf.Min(coverage.GetCoverage(cell) * config.ServiceBonusEach, config.ServiceBonusCap);
                if (!power.IsPowered(cell)) unpowered += capacity;
            }
        }

        return housing == 0
            ? default
            : new ServiceStats(bonus / housing, (float)unpowered / housing);
    }
}
