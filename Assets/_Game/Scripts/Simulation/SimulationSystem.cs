using System;
using System.Collections.Generic;
using UnityEngine;

// Pure daily tick in the fixed order Demand -> Growth -> Population -> Economy -> Research -> Disasters (M17). Power,
// water, coverage and pollution are derived views (the networks and pollution recompute lazily after any grid
// change), so they need no tick step. Built without age/tech databases, the sim plays by AgeRules.Legacy and has no research.
public sealed class SimulationSystem
{
    private readonly GridData m_Grid;
    private readonly BalanceConfig m_Config;
    private IReadOnlyList<ServiceSource> m_Sources = Array.Empty<ServiceSource>();   // as placed (100% funding)
    private IReadOnlyList<ServiceSource> m_Funded = Array.Empty<ServiceSource>();    // what the spatial systems see (M15)
    private ServiceStats m_LastServices;    // measured in the last Tick / Restore (education share for research)

    public EconomySystem Economy { get; }
    // The road tiers' costs, capacities and unlocks (M16); every tier is today's Paved road without ages.
    public RoadTiers RoadTiers { get; }
    // The homes -> jobs commute flow and the congestion it causes (M16); derived, updated at the end of each tick.
    public TrafficSystem Traffic { get; }
    // Fires, plague, breakdowns and random events (M17); off unless the city asked for them (and has ages).
    public DisasterSystem Disasters { get; }
    // Funding per budget line (M15); a change re-feeds the funded sources to the spatial systems.
    public BudgetSystem Budget { get; }
    // The city-wide goods pool (M24); off (supply 1, no flows) until the age in BalanceConfig.GoodsMinAge, and without ages.
    public GoodsSystem Goods { get; }
    public PopulationSystem Population { get; }
    public DemandSystem Demand { get; }
    public GrowthSystem Growth { get; }
    public PowerSystem Power { get; }
    // Wells / fountains (coverage ages) and the piped network (M13); its mode follows Rules.Water.
    public WaterSystem Water { get; }
    public CoverageSystem Coverage { get; }
    public PollutionSystem Pollution { get; }
    public LandValueSystem LandValue { get; }
    // Civic service cover and the needs it meets: crime, fire risk, sickness, education (M14).
    public CivicSystem Civic { get; }

    // Research and the current age; null when the sim was built without age/tech databases.
    public TechSystem Tech { get; }

    // The current age's growth rules (Legacy without ages).
    public AgeRules Rules => Tech != null ? Tech.Rules : AgeRules.Legacy;

    // Researched techs' effects (identity without ages).
    public TechModifiers TechModifiers => Tech != null ? Tech.Modifiers : TechModifiers.None;

    // Scaled capacity per grown cell (by the age it was built in).
    public CapacityModel Capacity { get; }

    // Set by the runtime layer whenever player-placed buildings change.
    public CityModifiers Modifiers { get; set; }

    // Placed buildings with a coverage radius or power supply. Set by the runtime layer whenever
    // player-placed buildings change.
    public IReadOnlyList<ServiceSource> Sources
    {
        get => m_Sources;
        set
        {
            m_Sources = value ?? Array.Empty<ServiceSource>();
            ApplySources();
        }
    }

    // Feeds the spatial systems the sources with each line's funding applied (the placed list itself at 100%).
    private void ApplySources()
    {
        m_Funded = Budget.IsDefault ? m_Sources : Budget.FundAll(m_Sources);
        if (Disasters.Breakdowns.AnyBroken) m_Funded = Disasters.Breakdowns.Apply(m_Funded);   // M17: broken sources give nothing
        Power.SetSources(m_Funded);
        Water.SetSources(m_Funded);
        Coverage.Recompute(m_Funded);
        Pollution.SetSources(m_Funded);
        Civic.SetSources(m_Funded);
    }

    public SimulationSystem(GridData grid, RoadNetwork roads, BalanceConfig config,
        AgeDatabase ages = null, TechDatabase techs = null)
    {
        if ((ages == null) != (techs == null))
            throw new ArgumentException("Pass both the age and tech databases, or neither.");

        m_Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        m_Config = config ?? throw new ArgumentNullException(nameof(config));

        if (ages != null) Tech = new TechSystem(ages, techs, config);
        Capacity = new CapacityModel(config, ages);
        RoadTiers = new RoadTiers(config, techs, tech => Tech != null && Tech.IsResearched(tech));
        Traffic = new TrafficSystem(grid, config, Capacity, RoadTiers);
        roads.SetFrontage(RoadTiers.HasContent ? tier => RoadTiers.Frontage(tier) : null);
        Economy = new EconomySystem(config);
        Budget = new BudgetSystem(config, () => TechModifiers.LoanInterestMultiplier);
        Goods = new GoodsSystem(config);
        Population = new PopulationSystem(config, Capacity);
        Demand = new DemandSystem(config);
        Power = new PowerSystem(grid, config, Capacity);
        Water = new WaterSystem(grid, config, Capacity, () => Rules.Water);
        Coverage = new CoverageSystem(grid.Width, grid.Height);
        Pollution = new PollutionSystem(grid, config, Capacity, ages, () => TechModifiers);
        Civic = new CivicSystem(grid, config, Capacity, () => Population.Population, ages, () => TechModifiers);
        LandValue = new LandValueSystem(grid, config, Coverage, Pollution, () => TechModifiers, Civic,
            () => Budget.EffectFactor(BudgetLine.Parks), Traffic);
        Disasters = new DisasterSystem(grid, config, ages != null, Civic, Water, () => TechModifiers, () => Tech != null ? Tech.CurrentAge : 0,
            Population, Capacity, Budget, () => m_Sources, () => Tech != null ? Tech.CurrentAgeDefinition.PlagueRisk : 0f, techs, Tech, Economy);
        Disasters.Breakdowns.Changed += ApplySources;
        Growth = new GrowthSystem(grid, roads, Power, config, Capacity, Tech, LandValue, Water, Disasters.IsRubble);
        grid.OnResized += () =>
        {
            Coverage.Resize(grid.Width, grid.Height);
            Coverage.Recompute(m_Funded);
        };
        Budget.Changed += ApplySources;
    }

    // Goods are in play from the configured age (never without ages: the age-less sim is the regression baseline).
    public bool GoodsActive => Tech != null && Tech.CurrentAge >= m_Config.GoodsMinAge;

    // Goods made today: each filled industrial job, times the researched techs' multiplier.
    public float GoodsProduction()
    {
        int jobs = Population.Jobs;
        float filledIndustrial = jobs > 0 ? (float)Population.IndustrialJobs * Population.Employed / jobs : 0f;
        return filledIndustrial * m_Config.GoodsPerIndustrialJob * TechModifiers.GoodsMultiplier;
    }

    // Goods wanted today: shops by their jobs, homes by their residents.
    public float GoodsDemand()
    {
        return Population.CommercialJobs * m_Config.GoodsPerCommercialJob + Population.Population * m_Config.GoodsPerResident;
    }

    // How supplied the city is right now (1 when goods are off): what the next day would deliver from the current stock.
    public float GoodsSupply() => Goods.Evaluate(GoodsProduction(), GoodsDemand(), GoodsActive).Supply;

    // The Power term only counts in ages whose upgrades need power; the Water term in ages needing water.
    public ServiceStats MeasureServices()
    {
        return ServiceStats.Measure(m_Grid, m_Config, Coverage, Power, Capacity, Rules.UpgradesNeedPower, Pollution, LandValue, Water,
            Civic, Budget.EffectFactor(BudgetLine.Parks), Traffic);
    }

    // Raised at the end of a tick in which fires destroyed placed buildings (M17), with their occupant ids. The
    // sim has already released their cells and dropped their sources; the runtime removes the objects and its
    // building records (no refund) and sets Sources / Modifiers again.
    public event Action<IReadOnlyList<int>> BuildingsDestroyed;

    // Sources whose building burnt down stop counting at once (power, water, cover and upkeep sources alike).
    private void DropDestroyedSources()
    {
        IReadOnlyList<int> destroyed = Disasters.Fire.DestroyedBuildings;
        if (destroyed.Count == 0) return;
        var kept = new List<ServiceSource>(m_Sources.Count);
        foreach (ServiceSource source in m_Sources)
        {
            if (!Disasters.Fire.WasDestroyed(source.Origin)) kept.Add(source);
        }
        if (kept.Count != m_Sources.Count) Sources = kept;
        BuildingsDestroyed?.Invoke(destroyed);
    }

    // The Plague happiness term from the outbreak as it stood after the last Step (0 when none or the switch is off).
    private float PlagueTerm()
    {
        return Disasters.Enabled ? Disasters.Epidemic.HappinessTerm(Population.Housing) : 0f;
    }

    // What repairing the broken source at `origin` costs now (0 when it is working or unknown).
    public float RepairCost(Vector2Int origin)
    {
        if (!Disasters.Breakdowns.IsBroken(origin)) return 0f;
        foreach (ServiceSource source in m_Sources)
        {
            if (source.Origin == origin && BreakdownSystem.CanBreak(source)) return Disasters.Breakdowns.RepairCost(source);
        }
        return 0f;
    }

    // Pays for the repair and puts the source back to work; false when it isn't broken or the city can't pay.
    public bool Repair(Vector2Int origin)
    {
        if (!Disasters.Breakdowns.IsBroken(origin)) return false;
        float cost = RepairCost(origin);
        if (cost > 0f && !Economy.Spend(cost)) return false;
        return Disasters.Breakdowns.Repair(origin);
    }

    // Recomputes the commute flow from today's population, employment and techs.
    private void UpdateTraffic()
    {
        float occupancy = Population.Housing > 0 ? Math.Min(1f, (float)Population.Population / Population.Housing) : 0f;
        float employment = Population.Workers > 0 ? (float)Population.Employed / Population.Workers : 0f;
        Traffic.Update(occupancy, employment, TechModifiers.TrafficMultiplier);
    }

    // Research points earned per day at the current population: filled commercial jobs, research
    // buildings and educated residents (M14: population x the education cover measured at homes in the
    // last tick), times the researched techs' multiplier. 0 without ages.
    public float ResearchIncome() => ResearchBreakdown().Total;

    // (M21e) Scales the whole research income; 1 normally, the config's TutorialResearchBoost in the tutorial city.
    public float ResearchBoost { get; set; } = 1f;

    public ResearchBreakdown ResearchBreakdown()
    {
        if (Tech == null) return default;
        int jobs = Population.Jobs;
        float filledCommercial = jobs > 0 ? (float)Population.CommercialJobs * Population.Employed / jobs : 0f;
        return new ResearchBreakdown(filledCommercial * m_Config.ResearchPerCommercialJob,
            Modifiers.ResearchPerDay + Budget.ResearchDelta(m_Sources),
            Population.Population * m_LastServices.EducatedShare * m_Config.ResearchPerEducatedResident,
            Tech.Modifiers.ResearchMultiplier * ResearchBoost);
    }

    // Load / new game: sets persisted state and recomputes derived stats (capacity, employment,
    // demand) so the UI is correct before the next tick. Grid, Modifiers and Sources must already be restored.
    public void Restore(float money, float incomePerDay, float expensePerDay,
        float taxResidential, float taxCommercial, float taxIndustrial,
        int population, float happiness, float goodsStock = 0f)
    {
        Economy.Restore(money, incomePerDay, expensePerDay, taxResidential, taxCommercial, taxIndustrial);
        if (Disasters.Breakdowns.Prune(m_Sources) > 0 || Disasters.Breakdowns.AnyBroken) ApplySources();   // saved breakdowns
        Population.RecountCapacity(m_Grid, Modifiers);
        Population.Restore(population, happiness);
        Goods.Restore(goodsStock, GoodsProduction(), GoodsDemand(), GoodsActive);
        UpdateTraffic();
        m_LastServices = MeasureServices();
        Population.RefreshHappinessBreakdown(taxResidential, taxCommercial, taxIndustrial, m_LastServices,
            TechModifiers.HappinessBonus, TechModifiers.OrdinanceHappiness, PlagueTerm(), TechModifiers.EventHappiness,
            Goods.Last.Shortage);
        Demand.Compute(Population, taxResidential, taxCommercial, taxIndustrial, TechModifiers);
    }

    // Today's money in and out (M15): the numbers Tick charges. Placed buildings' upkeep follows each
    // line's funding; roads and pipes are fixed costs.
    public BudgetBreakdown Ledger()
    {
        CityModifiers modifiers = Modifiers;
        float residential = Population.Employed * m_Config.IncomePerWorker * Economy.TaxResidential;
        float commercial = Population.CommercialJobs * m_Config.IncomePerCommercialJob * Economy.TaxCommercial;
        float industrial = Population.IndustrialJobs * m_Config.IncomePerIndustrialJob * Economy.TaxIndustrial;

        var byLine = new float[BudgetSystem.Lines];
        float sourceUpkeep = Budget.Accumulate(m_Sources, byLine, out float fundingDelta);
        float roads = RoadTiers.UpkeepPerDay(m_Grid);
        float pipes = m_Grid.CountPipes() * m_Config.PipeUpkeepPerDay;
        float multiplier = TechModifiers.UpkeepMultiplier;
        float loans = Budget.DailyLoanPayments;
        float ordinances = Tech != null ? Tech.OrdinanceCostPerDay(Population.Population) : 0f;
        float imports = Goods.ImportCost(Goods.Last);
        float exports = Goods.ExportIncome(Goods.Last);
        float expense = (modifiers.UpkeepPerDay + fundingDelta + roads + pipes) * multiplier + loans + ordinances + imports;
        return new BudgetBreakdown(residential, commercial, industrial, byLine, modifiers.UpkeepPerDay - sourceUpkeep,
            roads, pipes, multiplier, loans, ordinances, expense, imports, exports);
    }

    // The principal a loan taken now would carry: the age's LoanAmount, else the config default.
    public float LoanOffer()
    {
        float amount = Tech != null ? Tech.CurrentAgeDefinition.LoanAmount : 0f;
        return amount > 0f ? amount : m_Config.LoanAmount;
    }

    // Takes a loan: the principal is credited at once, the payments come with the daily ledger.
    // False at MaxLoans. Allowed in debt (that is what it is for).
    public bool TakeLoan()
    {
        float offer = LoanOffer();
        if (Budget.Borrow(offer) == null) return false;
        Economy.Refund(offer);
        return true;
    }

    // Pays loan `index` off early for its remaining principal; false when the city cannot afford it.
    public bool RepayLoan(int index)
    {
        if (index < 0 || index >= Budget.Loans.Count) return false;
        if (!Economy.Spend(Budget.RemainingPrincipal(index))) return false;
        Budget.RemoveLoan(index);
        return true;
    }

    public void Tick()
    {
        CityModifiers modifiers = Modifiers;
        TechModifiers tech = TechModifiers;

        Population.RecountCapacity(m_Grid, modifiers);
        Demand.Compute(Population, Economy.TaxResidential, Economy.TaxCommercial, Economy.TaxIndustrial, tech);

        Growth.Apply(Demand.Snapshot);

        Population.RecountCapacity(m_Grid, modifiers);
        m_LastServices = MeasureServices();
        // Yesterday's supply (from the stock the city holds now) drives today's happiness; Restore sees the same state.
        float goodsShortage = 1f - GoodsSupply();
        Population.Step(Economy.TaxResidential, Economy.TaxCommercial, Economy.TaxIndustrial, m_LastServices,
            tech.HappinessBonus, tech.OrdinanceHappiness, PlagueTerm(), tech.EventHappiness, goodsShortage);
        Goods.Step(GoodsProduction(), GoodsDemand(), GoodsActive);

        BudgetBreakdown ledger = Ledger();
        Economy.ApplyDay(ledger.Income, ledger.Expense);
        Budget.StepLoans();

        Tech?.Step(ResearchIncome());

        Disasters.Step();
        DropDestroyedSources();

        // After everything that moves population, employment or techs, so a restored city (Restore
        // recomputes it the same way) continues exactly like an uninterrupted one.
        UpdateTraffic();
    }
}
