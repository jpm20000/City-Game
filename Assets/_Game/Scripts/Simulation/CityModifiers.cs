// Stats contributed by player-placed buildings (not grown zone cells). Supplied by the
// runtime layer because BuildingDefinition lives outside the Simulation assembly. Spatial
// effects (coverage, power) come from SimulationSystem.Sources instead.
public struct CityModifiers
{
    public int Housing;
    public int CommercialJobs;
    public int IndustrialJobs;
    public float UpkeepPerDay;
}
