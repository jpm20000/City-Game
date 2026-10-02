// Stats contributed by player-placed buildings (not grown zone cells). Supplied by the
// runtime layer because BuildingDefinition lives outside the Simulation assembly.
public struct CityModifiers
{
    public int Housing;
    public int CommercialJobs;
    public int IndustrialJobs;
    public float UpkeepPerDay;
    public int ServiceCount;
}
