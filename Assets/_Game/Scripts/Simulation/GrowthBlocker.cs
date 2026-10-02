// Why a cell won't grow (or upgrade) on the next tick; None = eligible.
public enum GrowthBlocker
{
    None,
    NotZoned,
    Occupied,
    MaxLevel,
    NoRoadAccess,
    NoPower,            // grown cell without power can't upgrade
    PowerAtCapacity,    // powered, but its network can't supply the upgrade
    LowDemand,
}
