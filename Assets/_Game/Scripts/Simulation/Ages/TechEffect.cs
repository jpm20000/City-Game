using System;

public enum TechEffectType
{
    UnlockBuilding,         // Target = building Id
    ResearchMultiplier,     // research income x Value
    DemandMultiplier,       // Target = zone name (Residential/Commercial/Industrial), empty = all; demand x Value
    HappinessBonus,         // + Value happiness
    UpkeepMultiplier,       // upkeep x Value
    PollutionMultiplier,    // every emission x Value (M12)
    LandValueBonus,         // + Value land value on every cell (M12)
    CivicNeedMultiplier,    // Target = Order / Fire / Health: crime / fire risk / sickness x Value (M15)
    LoanInterestMultiplier, // loan interest x Value (M15)
    TrafficMultiplier,      // commute trips x Value (M16)
    HazardMultiplier,       // Target = FireSpread / PlagueSpread / Breakdown: that hazard's spread or rate x Value (M17)
}

// One effect of a researched tech. Folded into TechModifiers.
[Serializable]
public struct TechEffect
{
    public TechEffectType Type;
    public string Target;
    public float Value;

    public TechEffect(TechEffectType type, string target, float value)
    {
        Type = type;
        Target = target;
        Value = value;
    }
}
