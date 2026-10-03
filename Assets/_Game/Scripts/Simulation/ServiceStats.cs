using UnityEngine;

// What the spatial systems (services, power, water, pollution, heritage, crime) mean for residents, averaged over homes (weighted by each grown
// residential cell's capacity). Fed into PopulationSystem.Step for the happiness terms.
public readonly struct ServiceStats
{
    public readonly float ServiceBonus;             // average per-home service bonus (already capped per home)
    public readonly float UnpoweredHousingShare;    // 0..1
    public readonly float PollutionPenalty;         // average per-home pollution penalty (already capped per home; positive) (M12)
    public readonly float HeritageBonus;            // average per-home heritage bonus (already capped per home) (M12)
    public readonly float UnwateredHousingShare;    // 0..1 (M13)
    public readonly float CrimePenalty;             // average per-home crime penalty (already capped per home; positive) (M14)

    public ServiceStats(float serviceBonus, float unpoweredHousingShare, float pollutionPenalty = 0f, float heritageBonus = 0f,
        float unwateredHousingShare = 0f, float crimePenalty = 0f)
    {
        CrimePenalty = crimePenalty;
        UnwateredHousingShare = unwateredHousingShare;
        HeritageBonus = heritageBonus;
        ServiceBonus = serviceBonus;
        UnpoweredHousingShare = unpoweredHousingShare;
        PollutionPenalty = pollutionPenalty;
    }

    // countPower false (ages whose upgrades don't need power) leaves UnpoweredHousingShare at 0;
    // pollution / landValue null leave PollutionPenalty / HeritageBonus at 0; water null (or an age
    // needing no water) leaves UnwateredHousingShare at 0; civic null leaves CrimePenalty at 0.
    public static ServiceStats Measure(GridData grid, BalanceConfig config, CoverageSystem coverage, PowerSystem power,
        CapacityModel capacityModel = null, bool countPower = true, PollutionSystem pollution = null,
        LandValueSystem landValue = null, WaterSystem water = null, CivicSystem civic = null)
    {
        bool countWater = water != null && water.Mode != WaterRule.None;
        capacityModel ??= new CapacityModel(config);
        float bonus = 0f;
        float polluted = 0f;
        float heritage = 0f;
        float crime = 0f;
        int housing = 0;
        int unpowered = 0;
        int unwatered = 0;

        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (grid.GetZone(cell) != ZoneType.Residential) continue;
                int capacity = capacityModel.CapacityOf(grid, cell);
                if (capacity == 0) continue;

                housing += capacity;
                bonus += capacity * Mathf.Min(coverage.GetCoverage(cell) * config.ServiceBonusEach, config.ServiceBonusCap);
                if (countPower && !power.IsPowered(cell)) unpowered += capacity;
                if (countWater && !water.HasWater(cell)) unwatered += capacity;
                if (pollution != null) polluted += capacity * PollutionPenaltyAt(config, pollution.GetPollution(cell));
                if (landValue != null) heritage += capacity * HeritageBonusAt(config, landValue.HeritageCount(cell));
                if (civic != null) crime += capacity * CrimePenaltyAt(config, civic.GetCrime(cell));
            }
        }

        return housing == 0
            ? default
            : new ServiceStats(bonus / housing, (float)unpowered / housing, polluted / housing, heritage / housing,
                (float)unwatered / housing, crime / housing);
    }

    // One home's happiness gain from kept historic blocks nearby (capped).
    public static float HeritageBonusAt(BalanceConfig config, int historicNearby)
    {
        return Mathf.Min(historicNearby * config.HeritageHappinessEach, config.HeritageHappinessCap);
    }

    // One home's happiness loss from crime (positive, capped).
    public static float CrimePenaltyAt(BalanceConfig config, float crime)
    {
        return Mathf.Min(crime * config.CrimePenalty, config.CrimePenaltyCap);
    }

    // One home's happiness loss from the pollution reaching it (positive, capped).
    public static float PollutionPenaltyAt(BalanceConfig config, float pollution)
    {
        return Mathf.Min(pollution * config.PollutionPenaltyPerPoint, config.PollutionPenaltyCap);
    }
}
