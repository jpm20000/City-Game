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
    [Tooltip("(tuned, M12) Happiness a home loses per pollution point reaching it; the Pollution term is the average over homes (ramps in with SmallTownGracePopulation).")]
    [SerializeField] private float m_PollutionPenaltyPerPoint = 0.06f;
    [Tooltip("(M12) Cap on one home's pollution penalty.")]
    [SerializeField] private float m_PollutionPenaltyCap = 0.25f;
    [Tooltip("(tuned, M9) Bonus per service (park) whose coverage radius reaches a home; the Services term is the average over homes. Doc: 0.05 city-wide per service; doubled because a park now only helps the homes in its radius.")]
    [SerializeField] private float m_ServiceBonusEach = 0.10f;
    [Tooltip("(M9) Cap on one home's service bonus.")]
    [SerializeField] private float m_ServiceBonusCap = 0.20f;
    [Tooltip("(tuned, M9) Happiness lost when every home is unpowered (scaled by the unpowered share; ramps in with SmallTownGracePopulation). Kept small so a powerless town stays above LowHappinessThreshold: the pressure to build a plant is being stuck at level 1, not an exodus.")]
    [SerializeField] private float m_PowerPenalty = 0.05f;
    [Tooltip("(M13) Happiness lost when every home is without water (scaled by the dry share; ramps in with SmallTownGracePopulation), like PowerPenalty.")]
    [SerializeField] private float m_WaterPenalty = 0.05f;
    [SerializeField] private float m_HomelessPenalty = 0.30f;
    [SerializeField] private float m_LowHappinessThreshold = 0.5f;
    [SerializeField] private float m_LowHappinessDemandScale = 0.5f;

    [Header("Income")]
    [SerializeField] private float m_IncomePerWorker = 10f;
    [SerializeField] private float m_IncomePerCommercialJob = 12f;
    [SerializeField] private float m_IncomePerIndustrialJob = 12f;

    [Header("Water (M13)")]
    [Tooltip("(M13) Water units a grown cell draws per unit of capacity from the piped network (power draws 1 per unit).")]
    [SerializeField] private float m_WaterPerCapacity = 1f;
    [Tooltip("(M13) Cost of laying one cell of water pipe.")]
    [SerializeField] private int m_PipeCost = 5;
    [Tooltip("(M13) Daily upkeep per pipe cell.")]
    [SerializeField] private float m_PipeUpkeepPerDay = 0.02f;

    [Header("Pollution (M12)")]
    [Tooltip("(M12) Pollution points a grown industrial cell emits per unit of capacity (a level-1 shed = 1, level 3 = 4), x its built age's PollutionScale.")]
    [SerializeField] private float m_IndustrialPollution = 0.25f;
    [Tooltip("(M12) Cells industrial pollution reaches without ages (and in ages whose PollutionRadius is 0); falls off linearly to the edge.")]
    [SerializeField] private int m_PollutionRadius = 3;

    [Header("Land value & heritage (M12)")]
    [Tooltip("(M12) Land value of an untouched cell (0..1).")]
    [SerializeField] private float m_LandValueBase = 0.5f;
    [Tooltip("(M12) Land value per service (park) whose coverage reaches the cell.")]
    [SerializeField] private float m_LandValuePerService = 0.10f;
    [SerializeField] private float m_LandValueServiceCap = 0.20f;
    [Tooltip("(M12) Land value lost per pollution point.")]
    [SerializeField] private float m_LandValuePerPollution = 0.04f;
    [Tooltip("(M12) Residential and commercial cells need this land value to grow to level 3 (industry is exempt).")]
    [SerializeField] private float m_LandValueForLevel3 = 0.40f;
    [Tooltip("(M12) Kept historic blocks raise land value and happiness within this many cells (Chebyshev).")]
    [SerializeField] private int m_HeritageRadius = 3;
    [SerializeField] private float m_HeritageLandValueEach = 0.05f;
    [SerializeField] private float m_HeritageLandValueCap = 0.20f;
    [Tooltip("(M12) Happiness per kept historic block within HeritageRadius of a home (the Heritage term is the average over homes).")]
    [SerializeField] private float m_HeritageHappinessEach = 0.02f;
    [SerializeField] private float m_HeritageHappinessCap = 0.06f;

    [Header("Research & ages")]
    [Tooltip("(tuned, M11g) Research points per day for each filled commercial job (filled = CommercialJobs x Employed / Jobs). 0.25 puts each age at ~60-90 days of engaged play (AgeBalanceTests).")]
    [SerializeField] private float m_ResearchPerCommercialJob = 0.25f;
    [Tooltip("(M11) Outdated grown cells rebuilt in the current age's style per day (separate from MaxGrowthPerDay).")]
    [SerializeField] private int m_RedevelopPerDay = 3;
    [Tooltip("(M11) Research projects that can wait behind the active one.")]
    [SerializeField] private int m_ResearchQueueMax = 5;
    [Tooltip("(M11) With nothing being researched, RP bank up to this many days of the current income.")]
    [SerializeField] private float m_ResearchBankDays = 30f;

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
    public float PollutionPenaltyPerPoint => m_PollutionPenaltyPerPoint;
    public float PollutionPenaltyCap => m_PollutionPenaltyCap;
    public float ServiceBonusEach => m_ServiceBonusEach;
    public float ServiceBonusCap => m_ServiceBonusCap;
    public float PowerPenalty => m_PowerPenalty;
    public float WaterPenalty => m_WaterPenalty;
    public float WaterPerCapacity => m_WaterPerCapacity;
    public int PipeCost => m_PipeCost;
    public float PipeUpkeepPerDay => m_PipeUpkeepPerDay;
    public float HomelessPenalty => m_HomelessPenalty;
    public float LowHappinessThreshold => m_LowHappinessThreshold;
    public float LowHappinessDemandScale => m_LowHappinessDemandScale;

    public float IncomePerWorker => m_IncomePerWorker;
    public float IncomePerCommercialJob => m_IncomePerCommercialJob;
    public float IncomePerIndustrialJob => m_IncomePerIndustrialJob;

    public float IndustrialPollution => m_IndustrialPollution;
    public int PollutionRadius => m_PollutionRadius;

    public float LandValueBase => m_LandValueBase;
    public float LandValuePerService => m_LandValuePerService;
    public float LandValueServiceCap => m_LandValueServiceCap;
    public float LandValuePerPollution => m_LandValuePerPollution;
    public float LandValueForLevel3 => m_LandValueForLevel3;
    public int HeritageRadius => m_HeritageRadius;
    public float HeritageLandValueEach => m_HeritageLandValueEach;
    public float HeritageLandValueCap => m_HeritageLandValueCap;
    public float HeritageHappinessEach => m_HeritageHappinessEach;
    public float HeritageHappinessCap => m_HeritageHappinessCap;

    public float ResearchPerCommercialJob => m_ResearchPerCommercialJob;
    public int RedevelopPerDay => m_RedevelopPerDay;
    public int ResearchQueueMax => m_ResearchQueueMax;
    public float ResearchBankDays => m_ResearchBankDays;

    // Residents (or jobs) for a grown cell at the given level; 0 = undeveloped.
    public int CapacityForLevel(int level)
    {
        if (level <= 0) return 0;
        return m_LevelCapacity[Mathf.Min(level, m_LevelCapacity.Length) - 1];
    }
}
