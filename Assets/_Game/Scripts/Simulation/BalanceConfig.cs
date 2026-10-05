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
    [Tooltip("(M16) Trips per day a Paved road carries before it jams (every road in the age-less sim).")]
    [SerializeField] private float m_RoadCapacity = 160f;
    [Tooltip("(M16) Commute-flow cost of entering a Paved road cell.")]
    [SerializeField] private int m_RoadTravelCost = 4;

    [Header("Traffic (M16)")]
    [Tooltip("(M16) Commute trips per resident per day (before the techs' traffic multiplier).")]
    [SerializeField] private float m_TripsPerWorker = 1f;
    [Tooltip("(M16) Flow cost of leaving town through a map-edge road: a last resort behind any job.")]
    [SerializeField] private int m_OutsideTripCost = 40;
    [Tooltip("(M16) Road load / capacity up to which nothing is lost.")]
    [SerializeField] private float m_CongestionFree = 0.8f;
    [Tooltip("(M16) Happiness a home loses per unit of commute congestion above CongestionFree.")]
    [SerializeField] private float m_TrafficPenalty = 0.10f;
    [SerializeField] private float m_TrafficPenaltyCap = 0.08f;
    [Tooltip("(M16) Land value lost per unit of congestion above CongestionFree on the roads beside a cell.")]
    [SerializeField] private float m_LandValuePerCongestion = 0.10f;
    [SerializeField] private float m_LandValueCongestionCap = 0.10f;

    [Header("Disasters (M17)")]
    [Tooltip("(M17) Chance per day of a new fire = 1 - exp(-this x the sum of every grown block's fire risk).")]
    [SerializeField] private float m_FireIgnitionPerRisk = 0.0015f;
    [Tooltip("(M17) Chance per day that a burning block sets a neighbour alight, before flammability and fire cover.")]
    [SerializeField] private float m_FireSpreadChance = 0.3f;
    [Tooltip("(M17) Spread chance across a one-cell road, as a share of the normal one.")]
    [SerializeField] private float m_FireJumpFactor = 0.15f;
    [Tooltip("(M17) How flammable a placed building is next to a grown block of the same age.")]
    [SerializeField] private float m_PlacedFlammability = 0.5f;
    [Tooltip("(M17) Daily chance a fire goes out by itself; fire cover adds FireExtinguishPerCover x its strength.")]
    [SerializeField] private float m_FireExtinguishBase = 0.1f;
    [SerializeField] private float m_FireExtinguishPerCover = 0.7f;
    [Tooltip("(M17) Extinguish chance x this where the water rule is on but the block has no water.")]
    [SerializeField] private float m_DryExtinguishFactor = 0.5f;
    [Tooltip("(M17) Days a fire may burn before the block is destroyed.")]
    [SerializeField] private int m_FireBurnDays = 3;
    [Tooltip("(M17) Days rubble blocks regrowth.")]
    [SerializeField] private int m_RubbleDays = 15;
    [Tooltip("(M17) Plague needs at least this many residents.")]
    [SerializeField] private int m_PlagueMinPopulation = 120;
    [Tooltip("(M17) Daily chance of an outbreak = this x the age's PlagueRisk x the homes' mean sickness.")]
    [SerializeField] private float m_PlagueOutbreakPerDay = 0.06f;
    [Tooltip("(M17) Chance per day that an infected home infects a healthy one in reach, x the target's sickness.")]
    [SerializeField] private float m_PlagueSpread = 0.12f;
    [SerializeField] private int m_PlagueRadius = 2;
    [Tooltip("(M17) Days a home stays infected (then it is immune until the outbreak ends).")]
    [SerializeField] private int m_PlagueDays = 10;
    [Tooltip("(M17) Share of an infected home's residents who die each day.")]
    [SerializeField] private float m_PlagueDeathRate = 0.01f;
    [Tooltip("(M17) Happiness lost per share of housing infected, capped.")]
    [SerializeField] private float m_PlaguePenalty = 0.5f;
    [SerializeField] private float m_PlaguePenaltyCap = 0.15f;
    [Tooltip("(M17) Days after an outbreak before another can start.")]
    [SerializeField] private int m_PlagueCooldownDays = 180;
    [Tooltip("(M17) Daily breakdown chance per plant / tower / pump, before age, funding and techs.")]
    [SerializeField] private float m_BreakdownPerDay = 0.006f;
    [Tooltip("(M17) Extra breakdown rate per age the source's tech is behind the current age (x1 + this per age).")]
    [SerializeField] private float m_BreakdownPerAgeBehind = 0.5f;
    [Tooltip("(M17) Days a broken source gives nothing unless repaired.")]
    [SerializeField] private int m_BreakdownDays = 10;
    [Tooltip("(M17) Repairing a broken source costs this share of its build cost.")]
    [SerializeField] private float m_RepairCostFraction = 0.2f;
    [Tooltip("(M17) Days between random events: counted from the day the last one was answered.")]
    [SerializeField] private int m_EventIntervalMin = 45;
    [SerializeField] private int m_EventIntervalMax = 90;
    [Tooltip("(M17) Days before the same event can be offered again.")]
    [SerializeField] private int m_EventRepeatDays = 360;
    [Tooltip("(M17) Days an unanswered event waits before it takes its last (free) choice.")]
    [SerializeField] private int m_EventAutoDays = 10;

    [Header("Growth")]
    [SerializeField] private int m_MaxGrowthPerDay = 3;
    [SerializeField] private float m_GrowthDemandThreshold = 0.15f;
    [SerializeField] private int[] m_LevelCapacity = { 4, 8, 16 };

    [Header("Density (M23)")]
    [Tooltip("(tuned, M23) Capacity of a Low-density cell relative to Medium (Medium = 1, the identity).")]
    [SerializeField] private float m_LowDensityScale = 0.5f;
    [Tooltip("(tuned, M23) Capacity of a High-density home or shop relative to Medium.")]
    [SerializeField] private float m_HighDensityScale = 2f;
    [Tooltip("(tuned, M23) Capacity of a High-density factory relative to Medium.")]
    [SerializeField] private float m_HighDensityScaleIndustrial = 1.6f;
    [Tooltip("(tuned, M23) Age index from which High density can be zoned for shops (Renaissance).")]
    [SerializeField] private int m_HighDensityMinAgeCommercial = 1;
    [Tooltip("(tuned, M23) Age index from which High density can be zoned for homes and factories (Industrial).")]
    [SerializeField] private int m_HighDensityMinAgeOther = 2;
    [Tooltip("(tuned, M23) Age index from which Medium density can be zoned (Renaissance); before it only Low is offered. 0 = always.")]
    [SerializeField] private int m_MediumDensityMinAge = 1;
    [Tooltip("(tuned, M23) Land value a Low-density cell gains (quiet streets, gardens).")]
    [SerializeField] private float m_LowDensityLandValue = 0.05f;
    [Tooltip("(tuned, M23) Land value a High-density cell loses (crowding).")]
    [SerializeField] private float m_HighDensityLandValuePenalty = 0.03f;
    [Tooltip("(tuned, M23) Extra pollution multiplier of a High-density factory, on top of its larger capacity.")]
    [SerializeField] private float m_HighDensityPollution = 1.25f;

    [Header("Goods (M24)")]
    [Tooltip("(tuned, M24d) Age index from which goods are in play (Industrial = 2). 99 = off (24a-24c shipped dark).")]
    [SerializeField] private int m_GoodsMinAge = 2;
    [Tooltip("(tuned, M24d) Goods made per day by each filled industrial job. 0.5 left an engaged city with a permanent surplus (supply 1.0, only exports); 0.2 makes an engaged city import a third to a half of what it uses until it builds more industry.")]
    [SerializeField] private float m_GoodsPerIndustrialJob = 0.2f;
    [Tooltip("(tuned, M24) Goods a shop sells per day for each commercial job.")]
    [SerializeField] private float m_GoodsPerCommercialJob = 0.35f;
    [Tooltip("(tuned, M24) Goods each resident buys per day.")]
    [SerializeField] private float m_GoodsPerResident = 0.05f;
    [Tooltip("(tuned, M24) The stock holds this many days of demand; production above it is exported.")]
    [SerializeField] private float m_GoodsStockDays = 10f;
    [Tooltip("(tuned, M24) Price of one imported unit.")]
    [SerializeField] private float m_GoodsImportPrice = 3f;
    [Tooltip("(tuned, M24) Imports can cover at most this share of a day's demand.")]
    [SerializeField] private float m_GoodsMaxImportShare = 0.5f;
    [Tooltip("(tuned, M24) Price of one exported unit.")]
    [SerializeField] private float m_GoodsExportPrice = 1.5f;
    [Tooltip("(tuned, M24) Share of commercial income and research that survives a total goods shortage.")]
    [SerializeField] private float m_GoodsShopFloor = 0.4f;
    [Tooltip("(tuned, M24) Happiness lost at homes in a total shortage (ramped in with city size).")]
    [SerializeField] private float m_GoodsPenalty = 0.08f;
    [Tooltip("(tuned, M24) Level 3 homes and shops are blocked while supply is below this share of demand.")]
    [SerializeField] private float m_GoodsLevel3Supply = 0.7f;

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

    [Header("Civic services (M14)")]
    [Tooltip("(M14) Civic needs (crime, fire risk, sickness) are 0 up to this population...")]
    [SerializeField] private int m_CivicFreePopulation = 100;
    [Tooltip("(M14) ...and reach full strength at this population (linear between).")]
    [SerializeField] private int m_CivicFullPopulation = 700;
    [Tooltip("(M14) Crime per unit of capacity of a grown home or shop before policing (capped at 1; an Industrial level-1 home = 0.16, level 3 = 0.64).")]
    [SerializeField] private float m_CrimePerCapacity = 0.04f;
    [Tooltip("(M14) Happiness a home loses per unit of crime; the Crime term is the average over homes.")]
    [SerializeField] private float m_CrimePenalty = 0.15f;
    [Tooltip("(M14) Cap on one home's crime penalty.")]
    [SerializeField] private float m_CrimePenaltyCap = 0.10f;
    [Tooltip("(M14) Land value lost per unit of crime.")]
    [SerializeField] private float m_LandValuePerCrime = 0.15f;
    [Tooltip("(M14) Fire risk of a grown block without ages (and in ages whose FireRisk is 0), before the ramp and fire cover.")]
    [SerializeField] private float m_FireRisk = 0.35f;
    [Tooltip("(M14) Industrial blocks' fire risk x this.")]
    [SerializeField] private float m_FireRiskIndustrialFactor = 1.5f;
    [Tooltip("(M14) Happiness a home loses per unit of fire risk; the Fire term is the average over homes.")]
    [SerializeField] private float m_FirePenalty = 0.10f;
    [Tooltip("(M14) Cap on one home's fire-risk penalty.")]
    [SerializeField] private float m_FirePenaltyCap = 0.06f;
    [Tooltip("(M14) Happiness a home without any health care loses at full ramp (x (1 - health cover)).")]
    [SerializeField] private float m_HealthPenalty = 0.08f;
    [Tooltip("(M14) Research points per day per resident x the education cover at their home.")]
    [SerializeField] private float m_ResearchPerEducatedResident = 0.01f;

    [Header("Budget (M15)")]
    [Tooltip("(M15) Lowest funding a budget line can be set to (1 = 100%).")]
    [SerializeField] private float m_FundingMin = 0.5f;
    [Tooltip("(M15) Highest funding a budget line can be set to.")]
    [SerializeField] private float m_FundingMax = 1.5f;
    [Tooltip("(M15) Funding moves in steps of this.")]
    [SerializeField] private float m_FundingStep = 0.1f;
    [Tooltip("(M15) Above 100% funding, the effect grows by this share of the extra funding (0.5: 150% = x1.25).")]
    [SerializeField] private float m_FundingOverSlope = 0.5f;
    [Tooltip("(M15) Reach changes by this share of the effect change (0.5: 50% funding = x0.75 reach).")]
    [SerializeField] private float m_FundingReachSlope = 0.5f;
    [Tooltip("(M15) Loan principal when the sim has no ages (AgeDefinition.LoanAmount otherwise).")]
    [SerializeField] private float m_LoanAmount = 25000f;
    [Tooltip("(M15) Most loans open at once.")]
    [SerializeField] private int m_MaxLoans = 2;
    [Tooltip("(M15) Flat interest over the whole term (0.10 = repay 110%).")]
    [SerializeField] private float m_LoanInterest = 0.10f;
    [Tooltip("(M15) Days a loan is repaid over (one year).")]
    [SerializeField] private int m_LoanTermDays = 360;

    [Header("Research & ages")]
    [Tooltip("(tuned, M11g) Research points per day for each filled commercial job (filled = CommercialJobs x Employed / Jobs). 0.25 puts each age at ~60-90 days of engaged play (AgeBalanceTests).")]
    [SerializeField] private float m_ResearchPerCommercialJob = 0.25f;
    [Tooltip("(M11) Outdated grown cells rebuilt in the current age's style per day (separate from MaxGrowthPerDay).")]
    [SerializeField] private int m_RedevelopPerDay = 3;
    [Tooltip("(M11) Research projects that can wait behind the active one.")]
    [SerializeField] private int m_ResearchQueueMax = 5;
    [Tooltip("(M11) With nothing being researched, RP bank up to this many days of the current income.")]
    [SerializeField] private float m_ResearchBankDays = 30f;
    [Tooltip("(M21e) Research income multiplier in the guided tutorial city, so its research steps stay quick while every tech costs 12x what it did in 1.0.")]
    [SerializeField] private float m_TutorialResearchBoost = 12f;

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
    public float RoadCapacity => m_RoadCapacity;
    public int RoadTravelCost => m_RoadTravelCost;
    public float TripsPerWorker => m_TripsPerWorker;
    public int OutsideTripCost => m_OutsideTripCost;
    public float CongestionFree => m_CongestionFree;
    public float TrafficPenalty => m_TrafficPenalty;
    public float TrafficPenaltyCap => m_TrafficPenaltyCap;
    public float LandValuePerCongestion => m_LandValuePerCongestion;
    public float LandValueCongestionCap => m_LandValueCongestionCap;
    public float FireIgnitionPerRisk => m_FireIgnitionPerRisk;
    public float FireSpreadChance => m_FireSpreadChance;
    public float FireJumpFactor => m_FireJumpFactor;
    public float PlacedFlammability => m_PlacedFlammability;
    public float FireExtinguishBase => m_FireExtinguishBase;
    public float FireExtinguishPerCover => m_FireExtinguishPerCover;
    public float DryExtinguishFactor => m_DryExtinguishFactor;
    public int FireBurnDays => m_FireBurnDays;
    public int RubbleDays => m_RubbleDays;
    public int PlagueMinPopulation => m_PlagueMinPopulation;
    public float PlagueOutbreakPerDay => m_PlagueOutbreakPerDay;
    public float PlagueSpread => m_PlagueSpread;
    public int PlagueRadius => m_PlagueRadius;
    public int PlagueDays => m_PlagueDays;
    public float PlagueDeathRate => m_PlagueDeathRate;
    public float PlaguePenalty => m_PlaguePenalty;
    public float PlaguePenaltyCap => m_PlaguePenaltyCap;
    public int PlagueCooldownDays => m_PlagueCooldownDays;
    public float BreakdownPerDay => m_BreakdownPerDay;
    public float BreakdownPerAgeBehind => m_BreakdownPerAgeBehind;
    public int BreakdownDays => m_BreakdownDays;
    public float RepairCostFraction => m_RepairCostFraction;
    public int EventIntervalMin => m_EventIntervalMin;
    public int EventIntervalMax => m_EventIntervalMax;
    public int EventRepeatDays => m_EventRepeatDays;
    public int EventAutoDays => m_EventAutoDays;

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

    public int CivicFreePopulation => m_CivicFreePopulation;
    public int CivicFullPopulation => m_CivicFullPopulation;
    public float CrimePerCapacity => m_CrimePerCapacity;
    public float CrimePenalty => m_CrimePenalty;
    public float CrimePenaltyCap => m_CrimePenaltyCap;
    public float LandValuePerCrime => m_LandValuePerCrime;
    public float FireRisk => m_FireRisk;
    public float FireRiskIndustrialFactor => m_FireRiskIndustrialFactor;
    public float FirePenalty => m_FirePenalty;
    public float FirePenaltyCap => m_FirePenaltyCap;
    public float HealthPenalty => m_HealthPenalty;
    public float ResearchPerEducatedResident => m_ResearchPerEducatedResident;

    public float FundingMin => m_FundingMin;
    public float FundingMax => m_FundingMax;
    public float FundingStep => m_FundingStep;
    public float FundingOverSlope => m_FundingOverSlope;
    public float FundingReachSlope => m_FundingReachSlope;
    public float LoanAmount => m_LoanAmount;
    public int MaxLoans => m_MaxLoans;
    public float LoanInterest => m_LoanInterest;
    public int LoanTermDays => m_LoanTermDays;

    public float ResearchPerCommercialJob => m_ResearchPerCommercialJob;
    public int RedevelopPerDay => m_RedevelopPerDay;
    public int ResearchQueueMax => m_ResearchQueueMax;
    public float ResearchBankDays => m_ResearchBankDays;
    public float TutorialResearchBoost => m_TutorialResearchBoost;

    public int GoodsMinAge => m_GoodsMinAge;
    public float GoodsPerIndustrialJob => m_GoodsPerIndustrialJob;
    public float GoodsPerCommercialJob => m_GoodsPerCommercialJob;
    public float GoodsPerResident => m_GoodsPerResident;
    public float GoodsStockDays => m_GoodsStockDays;
    public float GoodsImportPrice => m_GoodsImportPrice;
    public float GoodsMaxImportShare => m_GoodsMaxImportShare;
    public float GoodsExportPrice => m_GoodsExportPrice;
    public float GoodsShopFloor => m_GoodsShopFloor;
    public float GoodsPenalty => m_GoodsPenalty;
    public float GoodsLevel3Supply => m_GoodsLevel3Supply;

    public float LowDensityLandValue => m_LowDensityLandValue;
    public float HighDensityLandValuePenalty => m_HighDensityLandValuePenalty;
    public float HighDensityPollution => m_HighDensityPollution;

    // Capacity multiplier of a density for a zone (M23); Medium is exactly 1 for every zone.
    public float DensityScale(ZoneType zone, Density density)
    {
        switch (density)
        {
            case Density.Low: return m_LowDensityScale;
            case Density.High: return zone == ZoneType.Industrial ? m_HighDensityScaleIndustrial : m_HighDensityScale;
            default: return 1f;
        }
    }

    // First age index in which Medium density can be painted (M23); Low is always available.
    public int MediumDensityMinAge => m_MediumDensityMinAge;

    // First age index in which the zone can be painted High (M23).
    public int HighDensityMinAge(ZoneType zone)
    {
        return zone == ZoneType.Commercial ? m_HighDensityMinAgeCommercial : m_HighDensityMinAgeOther;
    }

    // Residents (or jobs) for a grown cell at the given level; 0 = undeveloped.
    public int CapacityForLevel(int level)
    {
        if (level <= 0) return 0;
        return m_LevelCapacity[Mathf.Min(level, m_LevelCapacity.Length) - 1];
    }
}
