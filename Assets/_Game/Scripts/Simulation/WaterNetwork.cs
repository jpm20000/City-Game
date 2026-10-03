using UnityEngine;

// The piped water network of the Industrial and Modern ages (M13): towers and pumps feed the roads
// they touch, like plants feed power (the same UtilityNetwork model). A grown cell draws
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

    public override int DrawFor(int capacity)
    {
        if (capacity <= 0) return 0;
        return Mathf.Max(1, Mathf.RoundToInt(capacity * m_PerCapacity));
    }
}
