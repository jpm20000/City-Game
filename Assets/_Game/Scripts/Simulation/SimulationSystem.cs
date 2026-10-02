using System;
using System.Collections.Generic;

// Pure daily tick in the fixed order Demand -> Growth -> Population -> Economy -> Research. Power and
// coverage are derived views (power recomputes lazily after any grid change), so they need no tick
// step. Built without age/tech databases, the sim plays by AgeRules.Legacy and has no research.
public sealed class SimulationSystem
{
    private readonly GridData m_Grid;
    private readonly BalanceConfig m_Config;
    private IReadOnlyList<ServiceSource> m_Sources = Array.Empty<ServiceSource>();

    public EconomySystem Economy { get; }
    public PopulationSystem Population { get; }
    public DemandSystem Demand { get; }
    public GrowthSystem Growth { get; }
    public PowerSystem Power { get; }
    public CoverageSystem Coverage { get; }

    // Research and the current age; null when the sim was built without age/tech databases.
    public TechSystem Tech { get; }

    // The current age's growth rules (Legacy without ages).
    public AgeRules Rules => Tech != null ? Tech.Rules : AgeRules.Legacy;

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
            Power.SetSources(m_Sources);
            Coverage.Recompute(m_Sources);
        }
    }

    public SimulationSystem(GridData grid, RoadNetwork roads, BalanceConfig config,
        AgeDatabase ages = null, TechDatabase techs = null)
    {
        if ((ages == null) != (techs == null))
            throw new ArgumentException("Pass both the age and tech databases, or neither.");

        m_Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        m_Config = config ?? throw new ArgumentNullException(nameof(config));

        Economy = new EconomySystem(config);
        Population = new PopulationSystem(config);
        Demand = new DemandSystem(config);
        Power = new PowerSystem(grid, config);
        Coverage = new CoverageSystem(grid.Width, grid.Height);
        Growth = new GrowthSystem(grid, roads, Power, config);
        if (ages != null) Tech = new TechSystem(ages, techs, config);
        grid.OnResized += () =>
        {
            Coverage.Resize(grid.Width, grid.Height);
            Coverage.Recompute(m_Sources);
        };
    }

    public ServiceStats MeasureServices()
    {
        return ServiceStats.Measure(m_Grid, m_Config, Coverage, Power);
    }

    // Research points earned per day at the current population: filled commercial jobs plus
    // research buildings, times the researched techs' multiplier. 0 without ages.
    public float ResearchIncome()
    {
        if (Tech == null) return 0f;
        int jobs = Population.Jobs;
        float filledCommercial = jobs > 0 ? (float)Population.CommercialJobs * Population.Employed / jobs : 0f;
        return (filledCommercial * m_Config.ResearchPerCommercialJob + Modifiers.ResearchPerDay)
            * Tech.Modifiers.ResearchMultiplier;
    }

    // Load / new game: sets persisted state and recomputes derived stats (capacity, employment,
    // demand) so the UI is correct before the next tick. Grid, Modifiers and Sources must already be restored.
    public void Restore(float money, float incomePerDay, float expensePerDay,
        float taxResidential, float taxCommercial, float taxIndustrial,
        int population, float happiness)
    {
        Economy.Restore(money, incomePerDay, expensePerDay, taxResidential, taxCommercial, taxIndustrial);
        Population.RecountCapacity(m_Grid, Modifiers);
        Population.Restore(population, happiness);
        Population.RefreshHappinessBreakdown(taxResidential, taxCommercial, taxIndustrial, MeasureServices());
        Demand.Compute(Population, taxResidential, taxCommercial, taxIndustrial);
    }

    public void Tick()
    {
        CityModifiers modifiers = Modifiers;

        Population.RecountCapacity(m_Grid, modifiers);
        Demand.Compute(Population, Economy.TaxResidential, Economy.TaxCommercial, Economy.TaxIndustrial);

        Growth.Apply(Demand.Snapshot);

        Population.RecountCapacity(m_Grid, modifiers);
        Population.Step(Economy.TaxResidential, Economy.TaxCommercial, Economy.TaxIndustrial, MeasureServices());

        float income = Population.Employed * m_Config.IncomePerWorker * Economy.TaxResidential
            + Population.CommercialJobs * m_Config.IncomePerCommercialJob * Economy.TaxCommercial
            + Population.IndustrialJobs * m_Config.IncomePerIndustrialJob * Economy.TaxIndustrial;
        float expense = modifiers.UpkeepPerDay + m_Grid.CountRoads() * m_Config.RoadUpkeepPerDay;
        Economy.ApplyDay(income, expense);

        Tech?.Step(ResearchIncome());
    }
}
