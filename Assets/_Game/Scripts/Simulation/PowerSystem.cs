// Power flows from each plant into the roads it touches and along every road 4-connected to them
// (the road needn't reach the map edge). Plants on the same road network pool their supply. Grown
// cells beside an energised road draw their capacity; supply is handed out in BFS order from the
// plants, so the cells furthest out go dark first. The network model lives in UtilityNetwork (M13),
// shared with piped water.
public sealed class PowerSystem : UtilityNetwork
{
    public PowerSystem(GridData grid, BalanceConfig config, CapacityModel capacity = null)
        : base(grid, config, capacity)
    {
    }

    protected override int SupplyOf(ServiceSource source) => source.PowerSupply;

    // Grown cells left without power (no plant on their road, or their network ran out).
    public int UnpoweredCells => UnservedCells;

    public bool IsPowered(UnityEngine.Vector2Int cell) => IsServed(cell);

    public bool IsEnergisedRoad(UnityEngine.Vector2Int cell) => IsCarrying(cell);
}
