using UnityEngine;

// A player-placed building as the spatial systems see it: where it stands, how far its service
// reaches, how much power it feeds into the roads it touches, how much it pollutes, how it
// supplies water and which civic service it provides. Built by the runtime layer because BuildingDefinition lives outside the Simulation assembly.
public readonly struct ServiceSource
{
    public readonly Vector2Int Origin;
    public readonly Vector2Int Size;        // effective (rotated) footprint
    public readonly int CoverageRadius;     // cells, Chebyshev distance from the footprint; 0 = none
    public readonly int PowerSupply;        // units; 0 = not a power source
    public readonly float Pollution;        // points emitted at the footprint (M12); 0 = clean
    public readonly int PollutionRadius;    // cells the pollution spreads (M12)
    public readonly int WaterSupply;        // units fed into the piped network (M13); 0 = none
    public readonly int WaterRadius;        // cells a well or fountain waters in coverage ages (M13); 0 = none
    public readonly ServiceKind CivicKind;  // civic service line (M14); None = not a civic building
    public readonly int CivicRadius;        // cells its civic service reaches (Chebyshev from the footprint) (M14)
    public readonly float CivicStrength;    // 0..1, how well it covers a cell in reach (M14)
    public readonly float UpkeepPerDay;     // the building's own upkeep at 100% funding (M15); 0 = not listed
    public readonly float ResearchPerDay;   // its research points per day at 100% funding (M15)
    public readonly float Cost;             // what it cost to build (M17: the repair price is a share of it); 0 = unknown
    public readonly int TechAge;            // the age of the tech that unlocked it (M17: older plants break down more); -1 = unknown

    public ServiceSource(Vector2Int origin, Vector2Int size, int coverageRadius, int powerSupply,
        float pollution = 0f, int pollutionRadius = 0, int waterSupply = 0, int waterRadius = 0,
        ServiceKind civicKind = ServiceKind.None, int civicRadius = 0, float civicStrength = 0f,
        float upkeepPerDay = 0f, float researchPerDay = 0f, float cost = 0f, int techAge = -1)
    {
        Origin = origin;
        Size = size;
        CoverageRadius = coverageRadius;
        PowerSupply = powerSupply;
        Pollution = pollution;
        PollutionRadius = pollutionRadius;
        WaterSupply = waterSupply;
        WaterRadius = waterRadius;
        CivicKind = civicKind;
        CivicRadius = civicRadius;
        CivicStrength = civicStrength;
        UpkeepPerDay = upkeepPerDay;
        ResearchPerDay = researchPerDay;
        Cost = cost;
        TechAge = techAge;
    }
}
