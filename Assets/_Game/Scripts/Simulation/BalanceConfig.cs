using UnityEngine;

// All §7 tunables from Docs/GamePlan.md. Deviations from the doc are marked "(tuned)".
[CreateAssetMenu(fileName = "BalanceConfig", menuName = "CityBuilder/Balance Config")]
public sealed class BalanceConfig : ScriptableObject
{
    [Header("Time")]
    [SerializeField] private float m_SecondsPerDay = 1f;
    [SerializeField] private int m_DaysPerMonth = 30;
    [SerializeField] private int m_MonthsPerYear = 12;

    [Header("Starting values")]
    [SerializeField] private float m_StartingMoney = 50000f;
    [SerializeField] private float m_StartingHappiness = 0.75f;
    [SerializeField, Range(0f, 1f)] private float m_TaxResidential = 0.10f;
    [SerializeField, Range(0f, 1f)] private float m_TaxCommercial = 0.10f;
    [SerializeField, Range(0f, 1f)] private float m_TaxIndustrial = 0.10f;

    [Header("Roads")]
    [SerializeField] private int m_RoadCost = 50;
    [SerializeField] private float m_RoadUpkeepPerDay = 1f;

    [Header("Growth")]
    [SerializeField] private int m_MaxGrowthPerDay = 3;
    [SerializeField] private float m_GrowthDemandThreshold = 0.15f;
    [SerializeField] private int[] m_LevelCapacity = { 4, 8, 16 };

    [Header("Population")]
    [SerializeField] private float m_WorkerRatio = 0.6f;
    [Tooltip("(tuned) Fraction of vacant homes filled per day. The doc scales this by residential demand, which deadlocks growth.")]
    [SerializeField] private float m_MoveInRate = 0.20f;
    [Tooltip("(tuned, M8) Fraction of residents leaving per day while happiness is below LowHappinessThreshold. Move-in continues, so an unhappy city settles at roughly MoveInRate / (MoveInRate + MoveOutRate) occupancy.")]
    [SerializeField] private float m_MoveOutRate = 0.05f;

    [Header("Demand")]
    [Tooltip("(tuned) Constant residential pull. The doc applies 0.30 only at population 0, which deadlocks growth.")]
    [SerializeField] private float m_ResidentialBaseDemand = 0.40f;
    [SerializeField] private int m_ResidentialJobsFloor = 10;
    [SerializeField] private float m_CommercialJobsPerResident = 0.30f;
    [SerializeField] private float m_IndustrialJobsPerResident = 0.40f;
    [Tooltip("(tuned) Denominator floor for C/I demand. The doc's 20 never crosses the growth threshold at low population.")]
    [SerializeField] private int m_JobsDemandFloor = 5;
    [Tooltip("(tuned, M8) Each zone's demand is scaled by 1 - this * (its tax - TaxPenaltyThreshold): taxes above the threshold slow growth, below it speed it up.")]
    [SerializeField] private float m_TaxDemandScale = 4f;

    [Header("Happiness")]
    [Tooltip("(tuned, M8) Doc: 0.80. Lowered so pollution and taxes bite and services are needed to keep a city content.")]
    [SerializeField] private float m_HappinessBase = 0.70f;
    [SerializeField] private float m_UnemploymentPenalty = 0.60f;
    [Tooltip("(tuned, M8) Unemployment and pollution penalties ramp in linearly up to this population. New towns are lopsided (homes or factories grow first); without the grace they never get happy enough to grow.")]
    [SerializeField] private int m_SmallTownGracePopulation = 40;
    [SerializeField] private float m_TaxPenalty = 0.50f;
    [SerializeField] private float m_TaxPenaltyThreshold = 0.10f;
    [Tooltip("(tuned, M8) Happiness lost per point of commercial and of industrial tax above the threshold (cost of living).")]
    [SerializeField] private float m_JobTaxPenalty = 0.50f;
    [Tooltip("(tuned, M8) Pollution: happiness lost scales with industry's share of all development, IndustrialJobs / (Housing + Jobs).")]
    [SerializeField] private float m_PollutionPenalty = 0.50f;
    [Tooltip("(M9) Bonus per service (park) whose coverage radius reaches a home; the Services term is the average over homes.")]
    [SerializeField] private float m_ServiceBonusEach = 0.05f;
    [Tooltip("(M9) Cap on one home's service bonus.")]
    [SerializeField] private float m_ServiceBonusCap = 0.20f;
    [Tooltip("(M9) Happiness lost when every home is unpowered (scaled by the unpowered share; ramps in with SmallTownGracePopulation).")]
    [SerializeField] private float m_PowerPenalty = 0.10f;
    [SerializeField] private float m_HomelessPenalty = 0.30f;
    [SerializeField] private float m_LowHappinessThreshold = 0.5f;
    [SerializeField] private float m_LowHappinessDemandScale = 0.5f;

    [Header("Income")]
    [SerializeField] private float m_IncomePerWorker = 10f;
    [SerializeField] private float m_IncomePerCommercialJob = 12f;
    [SerializeField] private float m_IncomePerIndustrialJob = 12f;

    public float SecondsPerDay => m_SecondsPerDay;
    public int DaysPerMonth => m_DaysPerMonth;
    public int MonthsPerYear => m_MonthsPerYear;

    public float StartingMoney => m_StartingMoney;
    public float StartingHappiness => m_StartingHappiness;
    public float TaxResidential => m_TaxResidential;
    public float TaxCommercial => m_TaxCommercial;
    public float TaxIndustrial => m_TaxIndustrial;

    public int RoadCost => m_RoadCost;
    public float RoadUpkeepPerDay => m_RoadUpkeepPerDay;

    public int MaxGrowthPerDay => m_MaxGrowthPerDay;
    public float GrowthDemandThreshold => m_GrowthDemandThreshold;
    public int MaxLevel => m_LevelCapacity.Length;

    public float WorkerRatio => m_WorkerRatio;
    public float MoveInRate => m_MoveInRate;
    public float MoveOutRate => m_MoveOutRate;

    public float ResidentialBaseDemand => m_ResidentialBaseDemand;
    public int ResidentialJobsFloor => m_ResidentialJobsFloor;
    public float CommercialJobsPerResident => m_CommercialJobsPerResident;
    public float IndustrialJobsPerResident => m_IndustrialJobsPerResident;
    public int JobsDemandFloor => m_JobsDemandFloor;
    public float TaxDemandScale => m_TaxDemandScale;

    public float HappinessBase => m_HappinessBase;
    public float UnemploymentPenalty => m_UnemploymentPenalty;
    public int SmallTownGracePopulation => m_SmallTownGracePopulation;
    public float TaxPenalty => m_TaxPenalty;
    public float TaxPenaltyThreshold => m_TaxPenaltyThreshold;
    public float JobTaxPenalty => m_JobTaxPenalty;
    public float PollutionPenalty => m_PollutionPenalty;
    public float ServiceBonusEach => m_ServiceBonusEach;
    public float ServiceBonusCap => m_ServiceBonusCap;
    public float PowerPenalty => m_PowerPenalty;
    public float HomelessPenalty => m_HomelessPenalty;
    public float LowHappinessThreshold => m_LowHappinessThreshold;
    public float LowHappinessDemandScale => m_LowHappinessDemandScale;

    public float IncomePerWorker => m_IncomePerWorker;
    public float IncomePerCommercialJob => m_IncomePerCommercialJob;
    public float IncomePerIndustrialJob => m_IncomePerIndustrialJob;

    // Residents (or jobs) for a grown cell at the given level; 0 = undeveloped.
    public int CapacityForLevel(int level)
    {
        if (level <= 0) return 0;
        return m_LevelCapacity[Mathf.Min(level, m_LevelCapacity.Length) - 1];
    }
}
