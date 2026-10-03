// What the HUD and toasts show about water (M13): the rule in force, piped supply / demand, and how
// many grown buildings are dry out of how many there are.
public readonly struct WaterStatus
{
    public readonly WaterRule Mode;
    public readonly int Supply;         // piped units fed in (0 in coverage ages)
    public readonly int Demand;         // piped units every grown cell would draw (0 in coverage ages)
    public readonly int DryCells;       // grown cells without water under the current rule
    public readonly int GrownCells;

    public WaterStatus(WaterRule mode, int supply, int demand, int dryCells, int grownCells)
    {
        Mode = mode;
        Supply = supply;
        Demand = demand;
        DryCells = dryCells;
        GrownCells = grownCells;
    }

    // Share of grown buildings with water (1 when nothing has grown yet).
    public float WateredShare => GrownCells == 0 ? 1f : 1f - (float)DryCells / GrownCells;
}
