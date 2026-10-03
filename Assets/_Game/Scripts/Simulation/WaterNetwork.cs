using UnityEngine;

// The piped water network of the Industrial and Modern ages (M13): towers and pumps feed the roads
// and pipes they touch, like plants feed power (the same UtilityNetwork model). Roads and player-drawn
// pipes both carry water; a pipe under a grown cell feeds that cell. A grown cell draws
// WaterPerCapacity units per unit of capacity.
public sealed class WaterNetwork : UtilityNetwork
{
    private readonly float m_PerCapacity;

    public WaterNetwork(GridData grid, BalanceConfig config, CapacityModel capacity = null)
        : base(grid, config, capacity)
    {
        m_PerCapacity = config.WaterPerCapacity;
    }

    protected override int SupplyOf(ServiceSource source) => source.WaterSupply;

    protected override bool Carries(Vector2Int cell) => m_Grid.IsRoad(cell) || m_Grid.IsPipe(cell);

    public override int DrawFor(int capacity)
    {
        if (capacity <= 0) return 0;
        return Mathf.Max(1, Mathf.RoundToInt(capacity * m_PerCapacity));
    }
}
