// How tightly a zoned cell is built (M23). Chosen by the player when zoning and never changed by the simulation:
// growth moves a building through levels 1-3 inside its density. Zero is Medium, so a zero-filled layer is the
// city as it was before densities existed.
public enum Density : byte
{
    Medium = 0,
    Low = 1,
    High = 2,
}

public static class DensityUtils
{
    public const int Count = 3;

    public static bool IsValid(byte code) => code < Count;
}
