using System;
using UnityEngine;

// One historical age (GamePlan §12, M11). Its index in the AgeDatabase is the age number used by
// the sim and saves. Fields describing entry (TechsToAdvance, RequiredTechs, PopulationToEnter,
// AdvanceCost) are the conditions for advancing *into* this age from the previous one.
[CreateAssetMenu(fileName = "Age", menuName = "CityBuilder/Age")]
public sealed class AgeDefinition : ScriptableObject
{
    [SerializeField] private string m_Id = "";
    [SerializeField] private string m_DisplayName = "";
    [SerializeField] private int m_StartYear = 1760;

    [Header("Growth rules")]
    [Tooltip("Highest level a block built in this age can reach (1..3).")]
    [SerializeField, Range(1, 3)] private int m_MaxLevel = 3;
    [Tooltip("Capacity per cell = CapacityForLevel(level) x this, for blocks built in this age.")]
    [SerializeField] private float m_CapacityScale = 1f;
    [SerializeField] private bool m_UpgradesNeedPower = true;
    [Tooltip("(M13) What upgrades past level 1 need for water: a well or fountain in reach (Coverage) or the piped network (Piped).")]
    [SerializeField] private WaterRule m_Water;

    [Header("Pollution (M12)")]
    [Tooltip("Emission of industrial blocks built in this age, x BalanceConfig.IndustrialPollution x capacity.")]
    [SerializeField] private float m_PollutionScale = 1f;
    [Tooltip("Cells the pollution of industrial blocks built in this age reaches; 0 = BalanceConfig.PollutionRadius.")]
    [SerializeField] private int m_PollutionRadius;

    [Header("Civic services (M14)")]
    [Tooltip("Fire risk of blocks built in this age (timber burns, brick and steel less); 0 = BalanceConfig.FireRisk.")]
    [SerializeField] private float m_FireRisk;

    [Header("Entering this age")]
    [Tooltip("Techs of the previous age that must be researched before advancing into this age.")]
    [SerializeField] private int m_TechsToAdvance;
    [Tooltip("Specific techs needed (on top of the count) before advancing into this age.")]
    [SerializeField] private TechDefinition[] m_RequiredTechs = Array.Empty<TechDefinition>();
    [SerializeField] private int m_PopulationToEnter;
    [Tooltip("Research points the 'Advance to this age' project costs.")]
    [SerializeField] private float m_AdvanceCost;

    [Header("Starting in this age")]
    [Tooltip("Granted only when a city starts in this age (on top of every earlier age's techs).")]
    [SerializeField] private TechDefinition[] m_StartingTechs = Array.Empty<TechDefinition>();
    [SerializeField] private float m_StartingMoney = 50000f;

    [Tooltip("Display names for Residential, Commercial, Industrial in this age; empty = default.")]
    [SerializeField] private string[] m_ZoneNames = new string[3];

    public string Id => m_Id;
    public string DisplayName => string.IsNullOrEmpty(m_DisplayName) ? m_Id : m_DisplayName;
    public int StartYear => m_StartYear;
    public int MaxLevel => m_MaxLevel;
    public float CapacityScale => m_CapacityScale;
    public bool UpgradesNeedPower => m_UpgradesNeedPower;
    public WaterRule Water => m_Water;
    public float PollutionScale => m_PollutionScale;
    public int PollutionRadius => m_PollutionRadius;
    public float FireRisk => m_FireRisk;
    public int TechsToAdvance => m_TechsToAdvance;
    public TechDefinition[] RequiredTechs => m_RequiredTechs ?? Array.Empty<TechDefinition>();
    public int PopulationToEnter => m_PopulationToEnter;
    public float AdvanceCost => m_AdvanceCost;
    public TechDefinition[] StartingTechs => m_StartingTechs ?? Array.Empty<TechDefinition>();
    public float StartingMoney => m_StartingMoney;

    public AgeRules Rules => new AgeRules(m_MaxLevel, m_CapacityScale, m_UpgradesNeedPower, m_Water);

    // The calendar year after advancing into this age: history is compressed, never reversed.
    public int YearOnEntering(int currentYear) => Math.Max(currentYear, m_StartYear);

    // Age-specific name for a zone ("Crafts" for Industrial in the Medieval age), or null for the default.
    public string ZoneName(ZoneType zone)
    {
        int i = (int)zone - 1;
        if (m_ZoneNames == null || i < 0 || i >= m_ZoneNames.Length) return null;
        return string.IsNullOrEmpty(m_ZoneNames[i]) ? null : m_ZoneNames[i];
    }

    // Tests and balance harnesses build ages in code.
    internal void Init(string id, int startYear, int maxLevel = 3, float capacityScale = 1f,
        bool upgradesNeedPower = true, int techsToAdvance = 0, int populationToEnter = 0,
        float advanceCost = 0f, TechDefinition[] requiredTechs = null, TechDefinition[] startingTechs = null,
        float startingMoney = 50000f, float pollutionScale = 1f, int pollutionRadius = 0, WaterRule water = WaterRule.None,
        float fireRisk = 0f)
    {
        m_FireRisk = fireRisk;
        m_Water = water;
        m_PollutionScale = pollutionScale;
        m_PollutionRadius = pollutionRadius;
        m_Id = id;
        m_DisplayName = id;
        m_StartYear = startYear;
        m_MaxLevel = maxLevel;
        m_CapacityScale = capacityScale;
        m_UpgradesNeedPower = upgradesNeedPower;
        m_TechsToAdvance = techsToAdvance;
        m_PopulationToEnter = populationToEnter;
        m_AdvanceCost = advanceCost;
        m_RequiredTechs = requiredTechs ?? Array.Empty<TechDefinition>();
        m_StartingTechs = startingTechs ?? Array.Empty<TechDefinition>();
        m_StartingMoney = startingMoney;
    }
}
