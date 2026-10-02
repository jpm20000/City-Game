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
    [SerializeField] private int m_UnlockPopulation;
    [SerializeField] private float m_Height = 2f;
    [Tooltip("Service reach in cells (Chebyshev distance from the footprint); 0 = no coverage.")]
    [SerializeField] private int m_CoverageRadius;
    [Tooltip("Power units fed into the roads this building touches; 0 = not a power source.")]
    [SerializeField] private int m_PowerSupply;

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
    public int UnlockPopulation => m_UnlockPopulation;
    public float Height => m_Height;
    public int CoverageRadius => m_CoverageRadius;
    public int PowerSupply => m_PowerSupply;
}
