// The checklist for advancing into the next age. Ready = the "Advance" project may be started;
// its RP cost is then paid like any tech.
public readonly struct AdvanceStatus
{
    public readonly int NextAge;            // -1 when the city is in the last age
    public readonly float RpCost;
    public readonly int TechsDone;          // researched techs of the current age
    public readonly int TechsNeeded;
    public readonly int RequiredTechsMissing;
    public readonly int Population;
    public readonly int PopulationNeeded;

    public bool HasNextAge => NextAge >= 0;
    public bool TechsMet => TechsDone >= TechsNeeded && RequiredTechsMissing == 0;
    public bool PopulationMet => Population >= PopulationNeeded;
    public bool Ready => HasNextAge && TechsMet && PopulationMet;

    public AdvanceStatus(int nextAge, float rpCost, int techsDone, int techsNeeded, int requiredTechsMissing,
        int population, int populationNeeded)
    {
        NextAge = nextAge;
        RpCost = rpCost;
        TechsDone = techsDone;
        TechsNeeded = techsNeeded;
        RequiredTechsMissing = requiredTechsMissing;
        Population = population;
        PopulationNeeded = populationNeeded;
    }
}
