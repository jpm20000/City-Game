using UnityEngine;

// A player-placed building as the spatial systems see it: where it stands, how far its service
// reaches and how much power it feeds into the roads it touches and how much it pollutes. Built by the runtime layer
// because BuildingDefinition lives outside the Simulation assembly.
public readonly struct ServiceSource
{
    public readonly Vector2Int Origin;
    public readonly Vector2Int Size;        // effective (rotated) footprint
    public readonly int CoverageRadius;     // cells, Chebyshev distance from the footprint; 0 = none
    public readonly int PowerSupply;        // units; 0 = not a power source
    public readonly float Pollution;        // points emitted at the footprint (M12); 0 = clean
    public readonly int PollutionRadius;    // cells the pollution spreads (M12)

    public ServiceSource(Vector2Int origin, Vector2Int size, int coverageRadius, int powerSupply,
        float pollution = 0f, int pollutionRadius = 0)
    {
        Origin = origin;
        Size = size;
        CoverageRadius = coverageRadius;
        PowerSupply = powerSupply;
        Pollution = pollution;
        PollutionRadius = pollutionRadius;
    }
}
