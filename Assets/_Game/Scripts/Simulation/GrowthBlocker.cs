// Why a cell won't grow (or upgrade) on the next tick; None = eligible.
public enum GrowthBlocker
{
    None,
    NotZoned,
    Occupied,
    MaxLevel,
    NoRoadAccess,
    NoPower,            // grown cell without power can't upgrade (in ages whose upgrades need power)
    PowerAtCapacity,    // powered, but its network can't supply the upgrade
    LowDemand,
    AgeMaxLevel,        // at the current age's level cap (M11)
    Outdated,           // built in an older age; waiting to be rebuilt in the current one (M11)
    KeptHistoric,       // kept historic and at its own age's level cap (M11)
}
