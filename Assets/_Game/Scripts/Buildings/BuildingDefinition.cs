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
}
