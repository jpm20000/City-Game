using UnityEngine;

// What the spatial systems (services, power, water, pollution, heritage, civic services) mean for residents, averaged over homes (weighted by each grown
// residential cell's capacity). Fed into PopulationSystem.Step for the happiness terms.
public readonly struct ServiceStats
{
    public readonly float ServiceBonus;             // average per-home service bonus (already capped per home)
    public readonly float UnpoweredHousingShare;    // 0..1
    public readonly float PollutionPenalty;         // average per-home pollution penalty (already capped per home; positive) (M12)
    public readonly float HeritageBonus;            // average per-home heritage bonus (already capped per home) (M12)
    public readonly float UnwateredHousingShare;    // 0..1 (M13)
    public readonly float CrimePenalty;             // average per-home crime penalty (already capped per home; positive) (M14)
    public readonly float FirePenalty;              // average per-home fire-risk penalty (capped per home; positive) (M14)
    public readonly float HealthPenalty;            // average per-home sickness penalty (positive) (M14)
    public readonly float EducatedShare;            // housing-weighted education cover at homes, 0..1 (M14)
    public readonly float TrafficPenalty;           // average per-home commute-congestion penalty (capped per home; positive) (M16)

    public ServiceStats(float serviceBonus, float unpoweredHousingShare, float pollutionPenalty = 0f, float heritageBonus = 0f,
        float unwateredHousingShare = 0f, float crimePenalty = 0f, float firePenalty = 0f, float healthPenalty = 0f,
        float educatedShare = 0f, float trafficPenalty = 0f)
    {
        TrafficPenalty = trafficPenalty;
        CrimePenalty = crimePenalty;
        FirePenalty = firePenalty;
        HealthPenalty = healthPenalty;
        EducatedShare = educatedShare;
        UnwateredHousingShare = unwateredHousingShare;
        HeritageBonus = heritageBonus;
        ServiceBonus = serviceBonus;
        UnpoweredHousingShare = unpoweredHousingShare;
        PollutionPenalty = pollutionPenalty;
    }

    // countPower false (ages whose upgrades don't need power) leaves UnpoweredHousingShare at 0;
    // pollution / landValue null leave PollutionPenalty / HeritageBonus at 0; water null (or an age
    // needing no water) leaves UnwateredHousingShare at 0; civic null leaves the civic terms at 0.
    public static ServiceStats Measure(GridData grid, BalanceConfig config, CoverageSystem coverage, PowerSystem power,
        CapacityModel capacityModel = null, bool countPower = true, PollutionSystem pollution = null,
        LandValueSystem landValue = null, WaterSystem water = null, CivicSystem civic = null, float parkFactor = 1f,
        TrafficSystem traffic = null)
    {
        bool countWater = water != null && water.Mode != WaterRule.None;
        capacityModel ??= new CapacityModel(config);
        float bonus = 0f;
        float polluted = 0f;
        float heritage = 0f;
        float crime = 0f;
        float fire = 0f;
        float sick = 0f;
        float educated = 0f;
        float jam = 0f;
        int housing = 0;
        int unpowered = 0;
        int unwatered = 0;
        float ramp = civic != null ? civic.Ramp : 0f;

        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (grid.GetZone(cell) != ZoneType.Residential) continue;
                int capacity = capacityModel.CapacityOf(grid, cell);
                if (capacity == 0) continue;

                housing += capacity;
                bonus += capacity * Mathf.Min(coverage.GetCoverage(cell) * config.ServiceBonusEach * parkFactor, config.ServiceBonusCap);
                if (countPower && !power.IsPowered(cell)) unpowered += capacity;
                if (countWater && !water.HasWater(cell)) unwatered += capacity;
                if (pollution != null) polluted += capacity * PollutionPenaltyAt(config, pollution.GetPollution(cell));
                if (landValue != null) heritage += capacity * HeritageBonusAt(config, landValue.HeritageCount(cell));
                if (traffic != null) jam += capacity * TrafficSystem.TrafficPenaltyAt(config, traffic.CommuteCongestion(cell));
                if (civic != null)
                {
                    civic.HomeNeeds(cell, capacity, ramp, out float cellCrime, out float cellFire, out float cellSick, out float cellEducation);
                    crime += capacity * CrimePenaltyAt(config, cellCrime);
                    fire += capacity * FirePenaltyAt(config, cellFire);
                    sick += capacity * HealthPenaltyAt(config, cellSick);
                    educated += capacity * cellEducation;
                }
            }
        }

        return housing == 0
            ? default
            : new ServiceStats(bonus / housing, (float)unpowered / housing, polluted / housing, heritage / housing,
                (float)unwatered / housing, crime / housing, fire / housing, sick / housing, educated / housing, jam / housing);
    }

    // One home's happiness gain from kept historic blocks nearby (capped).
    public static float HeritageBonusAt(BalanceConfig config, int historicNearby)
    {
        return Mathf.Min(historicNearby * config.HeritageHappinessEach, config.HeritageHappinessCap);
    }

    // One home's happiness loss from fire risk (positive, capped).
    public static float FirePenaltyAt(BalanceConfig config, float fireRisk)
    {
        return Mathf.Min(fireRisk * config.FirePenalty, config.FirePenaltyCap);
    }

    // One home's happiness loss from sickness (positive; sickness is already 0..1).
    public static float HealthPenaltyAt(BalanceConfig config, float sickness)
    {
        return sickness * config.HealthPenalty;
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
