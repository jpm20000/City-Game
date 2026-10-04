using UnityEngine;

// One road tier (M16): dirt track, cobbled street, paved road, avenue, highway. Listed in
// TechDatabase.RoadTiers; the tier's tech unlocks it. Highways have no frontage: zoned land beside
// only a highway has no road access.
[CreateAssetMenu(fileName = "RoadTier", menuName = "CityBuilder/Road Tier")]
public sealed class RoadTierDefinition : ScriptableObject
{
    [Tooltip("1..5; the byte stored in GridData.")]
    [SerializeField] private byte m_Tier = 1;
    [SerializeField] private string m_Id = "";
    [SerializeField] private string m_DisplayName = "";
    [Tooltip("The tech that unlocks this tier; none = available from the start.")]
    [SerializeField] private TechDefinition m_RequiredTech;
    [SerializeField] private int m_Cost = 50;
    [SerializeField] private float m_UpkeepPerDay = 1f;
    [Tooltip("Trips per day the road carries before it jams.")]
    [SerializeField] private float m_Capacity = 160f;
    [Tooltip("Cost of entering a cell of this road in the commute flow (lower = faster).")]
    [SerializeField] private int m_TravelCost = 4;
    [Tooltip("False for highways: neighbouring land gets no road access from them.")]
    [SerializeField] private bool m_Frontage = true;
    [Tooltip("The tier that replaces this one on the toolbar once researched (0 = none).")]
    [SerializeField] private byte m_ObsoleteBy;

    public byte Tier => m_Tier;
    public string Id => m_Id;
    public string DisplayName => string.IsNullOrEmpty(m_DisplayName) ? m_Id : m_DisplayName;
    public TechDefinition RequiredTech => m_RequiredTech;
    public int Cost => m_Cost;
    public float UpkeepPerDay => m_UpkeepPerDay;
    public float Capacity => m_Capacity;
    public int TravelCost => m_TravelCost;
    public bool Frontage => m_Frontage;
    public byte ObsoleteBy => m_ObsoleteBy;

    internal void Init(byte tier, string id, TechDefinition requiredTech, int cost, float upkeepPerDay, float capacity,
        int travelCost, bool frontage, byte obsoleteBy = 0)
    {
        m_Tier = tier;
        m_Id = id;
        m_DisplayName = id;
        m_RequiredTech = requiredTech;
        m_Cost = cost;
        m_UpkeepPerDay = upkeepPerDay;
        m_Capacity = capacity;
        m_TravelCost = travelCost;
        m_Frontage = frontage;
        m_ObsoleteBy = obsoleteBy;
    }
}
