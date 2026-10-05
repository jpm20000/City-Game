using UnityEngine;

[CreateAssetMenu(fileName = "BuildingDefinition", menuName = "CityBuilder/Building Definition")]
public sealed class BuildingDefinition : ScriptableObject
{
    [SerializeField] private string m_Id;
    [SerializeField] private string m_DisplayName;
    [SerializeField] private BuildingCategory m_Category;
    [SerializeField] private Sprite m_Icon;
    [SerializeField] private GameObject m_Prefab;
    [SerializeField] private Vector2Int m_Size = Vector2Int.one;
    [SerializeField] private int m_Cost;
    [SerializeField] private float m_UpkeepPerDay;
    [SerializeField] private int m_HousingCapacity;
    [SerializeField] private int m_JobsProvided;
    [SerializeField] private float m_HappinessEffect;
    [SerializeField] private ZoneType m_ZoneRestriction;
    [SerializeField] private float m_Height = 2f;
    [Tooltip("Service reach in cells (Chebyshev distance from the footprint); 0 = no coverage.")]
    [SerializeField] private int m_CoverageRadius;
    [Tooltip("Power units fed into the roads this building touches; 0 = not a power source.")]
    [SerializeField] private int m_PowerSupply;
    [Tooltip("Tech Id that unlocks this building (M11); empty = always available. Ignored without age data.")]
    [SerializeField] private string m_RequiredTech = "";
    [Tooltip("Research points per day while placed (M11).")]
    [SerializeField] private float m_ResearchPerDay;
    [Tooltip("Pollution points emitted at the footprint (M12); 0 = clean.")]
    [SerializeField] private float m_Pollution;
    [Tooltip("Cells the pollution spreads, falling off linearly (M12).")]
    [SerializeField] private int m_PollutionRadius;
    [Tooltip("Water units fed into the piped network through the roads this building touches (M13; Industrial age on); 0 = none.")]
    [SerializeField] private int m_WaterSupply;
    [Tooltip("Cells a well or fountain waters (Chebyshev distance from the footprint; M13, Medieval and Renaissance); 0 = none.")]
    [SerializeField] private int m_WaterRadius;
    [Tooltip("Age Id from which this building can no longer be built (M13, e.g. wells once water is piped); empty = never. Placed ones stay.")]
    [SerializeField] private string m_ObsoleteAge = "";
    [Tooltip("Civic service line (M14): order, fire, health or education; None = not a civic building.")]
    [SerializeField] private ServiceKind m_CivicKind;
    [Tooltip("Cells its civic service reaches (Chebyshev distance from the footprint; M14).")]
    [SerializeField] private int m_CivicRadius;
    [Tooltip("How well it covers a cell in reach, 0..1 (M14); newer tiers are stronger.")]
    [SerializeField] private float m_CivicStrength;
    [Tooltip("(M20) Toolbar group; Auto derives it from the category, civic line and effects (ToolbarGroups.Resolve).")]
    [SerializeField] private ToolbarGroup m_ToolbarGroup;
    [Tooltip("(M20) Toolbar group from LaterGroupAge on (e.g. the Fountain is a water building until water is piped, then a park); Auto = no change.")]
    [SerializeField] private ToolbarGroup m_LaterToolbarGroup;
    [Tooltip("Age Id from which LaterToolbarGroup applies.")]
    [SerializeField] private string m_LaterGroupAge = "";

    public string Id => m_Id;
    public string DisplayName => m_DisplayName;
    public BuildingCategory Category => m_Category;
    public Sprite Icon => m_Icon;
    public GameObject Prefab => m_Prefab;
    public Vector2Int Size => m_Size;
    public int Cost => m_Cost;
    public float UpkeepPerDay => m_UpkeepPerDay;
    public int HousingCapacity => m_HousingCapacity;
    public int JobsProvided => m_JobsProvided;
    public float HappinessEffect => m_HappinessEffect;
    public ZoneType ZoneRestriction => m_ZoneRestriction;
    public float Height => m_Height;
    public int CoverageRadius => m_CoverageRadius;
    public int PowerSupply => m_PowerSupply;
    public string RequiredTech => m_RequiredTech;
    public float ResearchPerDay => m_ResearchPerDay;
    public float Pollution => m_Pollution;
    public int PollutionRadius => m_PollutionRadius;
    public int WaterSupply => m_WaterSupply;
    public int WaterRadius => m_WaterRadius;
    public string ObsoleteAge => m_ObsoleteAge;
    public ServiceKind CivicKind => m_CivicKind;
    public int CivicRadius => m_CivicRadius;
    public float CivicStrength => m_CivicStrength;
    public ToolbarGroup ToolbarGroupOf => ToolbarGroups.Resolve(m_ToolbarGroup, m_Category == BuildingCategory.Utility, m_CivicKind, m_ResearchPerDay, m_HappinessEffect);
    public ToolbarGroup LaterToolbarGroup => m_LaterToolbarGroup;
    public string LaterGroupAge => m_LaterGroupAge;
}
