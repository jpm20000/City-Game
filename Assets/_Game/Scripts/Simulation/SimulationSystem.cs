using System;

// Pure daily tick in the fixed order Demand -> Growth -> Population -> Economy.
public sealed class SimulationSystem
{
    private readonly GridData m_Grid;
    private readonly BalanceConfig m_Config;

    public EconomySystem Economy { get; }
    public PopulationSystem Population { get; }
    public DemandSystem Demand { get; }
    public GrowthSystem Growth { get; }

    // Set by the runtime layer whenever player-placed buildings change.
    public CityModifiers Modifiers { get; set; }

    public SimulationSystem(GridData grid, RoadNetwork roads, BalanceConfig config)
    {
        m_Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        m_Config = config ?? throw new ArgumentNullException(nameof(config));

        Economy = new EconomySystem(config);
        Population = new PopulationSystem(config);
        Demand = new DemandSystem(config);
        Growth = new GrowthSystem(grid, roads, config);
    }

    public void Tick()
    {
        CityModifiers modifiers = Modifiers;

        Population.RecountCapacity(m_Grid, modifiers);
        Demand.Compute(Population);

        Growth.Apply(Demand.Snapshot);

        Population.RecountCapacity(m_Grid, modifiers);
        Population.Step(Economy.TaxResidential, modifiers.ServiceCount);

        float income = Population.Employed * m_Config.IncomePerWorker * Economy.TaxResidential
            + Population.CommercialJobs * m_Config.IncomePerCommercialJob * Economy.TaxCommercial
            + Population.IndustrialJobs * m_Config.IncomePerIndustrialJob * Economy.TaxIndustrial;
        float expense = modifiers.UpkeepPerDay + m_Grid.CountRoads() * m_Config.RoadUpkeepPerDay;
        Economy.ApplyDay(income, expense);
    }
}
