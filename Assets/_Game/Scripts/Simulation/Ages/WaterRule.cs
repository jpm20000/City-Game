// What growing blocks need for water in an age (M13). Upgrades past level 1 need it, like power.
public enum WaterRule
{
    None,       // no water needed (tests only)
    Coverage,   // a well or fountain must reach the cell (Medieval, Renaissance)
    Piped,      // the cell must be fed by the piped network with headroom (Industrial, Modern)
}
