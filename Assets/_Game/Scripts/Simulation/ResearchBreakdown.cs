// Where a day's research points come from (M14), for explaining the rate to the player. The parts are
// before the techs' multiplier; Total is after it.
public readonly struct ResearchBreakdown
{
    public readonly float Commercial;   // filled commercial jobs x ResearchPerCommercialJob
    public readonly float Buildings;    // research buildings' ResearchPerDay
    public readonly float Education;    // residents x education cover x ResearchPerEducatedResident
    public readonly float Multiplier;   // researched techs' ResearchMultiplier

    public ResearchBreakdown(float commercial, float buildings, float education, float multiplier)
    {
        Commercial = commercial;
        Buildings = buildings;
        Education = education;
        Multiplier = multiplier;
    }

    public float Total => (Commercial + Buildings + Education) * Multiplier;
}
