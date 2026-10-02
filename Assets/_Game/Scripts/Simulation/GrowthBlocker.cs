// Why a cell won't grow (or upgrade) on the next tick; None = eligible.
public enum GrowthBlocker
{
    None,
    NotZoned,
    Occupied,
    MaxLevel,
    NoRoadAccess,
    LowDemand,
}
