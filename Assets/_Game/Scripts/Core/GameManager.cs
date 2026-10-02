using UnityEngine;

public sealed class GameManager : MonoBehaviour
{
    [SerializeField] private GridSystem m_GridSystem;
    [SerializeField] private BuildingDatabase m_BuildingDatabase;
    [SerializeField] private BalanceConfig m_Balance;
    [SerializeField] private TimeManager m_Time;
    [SerializeField] private GrowthVisuals m_GrowthVisuals;

    private CityModifiers m_Modifiers;

    public GridData Grid { get; private set; }
    public RoadNetwork Roads { get; private set; }
    public BuildingDatabase Buildings => m_BuildingDatabase;
    public BalanceConfig Balance => m_Balance;
    public TimeManager Clock => m_Time;
    public SimulationSystem Simulation { get; private set; }
    public EconomySystem Economy => Simulation?.Economy;
    public PopulationSystem Population => Simulation?.Population;
    public DemandSystem Demand => Simulation?.Demand;

    private void Awake()
    {
        if (m_GridSystem == null) return;

        Grid = new GridData(m_GridSystem.GridSize.x, m_GridSystem.GridSize.y);
        Roads = new RoadNetwork(Grid);
        Grid.OnCellChanged += GameEvents.RaiseCellChanged;

        if (m_Balance == null)
        {
            Debug.LogWarning("GameManager: no BalanceConfig assigned; using defaults.", this);
            m_Balance = ScriptableObject.CreateInstance<BalanceConfig>();
        }

        Simulation = new SimulationSystem(Grid, Roads, m_Balance);
        Simulation.Economy.OnMoneyChanged += GameEvents.RaiseMoneyChanged;

        if (m_GrowthVisuals != null) m_GrowthVisuals.Init(Grid);

        if (m_Time != null)
        {
            m_Time.Init(m_Balance);
            m_Time.OnTick += HandleTick;
        }
        else
        {
            Debug.LogError("GameManager: no TimeManager assigned; simulation will not tick.", this);
        }
    }

    private void OnDestroy()
    {
        if (m_Time != null) m_Time.OnTick -= HandleTick;
    }

    public void RegisterBuilding(BuildingInstance building)
    {
        ApplyModifiers(building.Definition, 1);
    }

    public void UnregisterBuilding(BuildingInstance building)
    {
        ApplyModifiers(building.Definition, -1);
    }

    private void ApplyModifiers(BuildingDefinition def, int sign)
    {
        m_Modifiers.Housing += sign * def.HousingCapacity;
        if (def.ZoneRestriction == ZoneType.Industrial)
        {
            m_Modifiers.IndustrialJobs += sign * def.JobsProvided;
        }
        else
        {
            m_Modifiers.CommercialJobs += sign * def.JobsProvided;
        }
        m_Modifiers.UpkeepPerDay += sign * def.UpkeepPerDay;
        if (def.Category == BuildingCategory.Service) m_Modifiers.ServiceCount += sign;

        Simulation.Modifiers = m_Modifiers;
    }

    private void HandleTick()
    {
        Simulation.Tick();
        RaiseStateEvents();
    }

    // Pushes current population / demand / happiness / cash flow to the UI. Called per tick and after a load.
    public void RaiseStateEvents()
    {
        PopulationSystem population = Simulation.Population;
        GameEvents.RaisePopulationChanged(population.Population, population.Jobs);
        GameEvents.RaiseDemandChanged(Simulation.Demand.Snapshot);
        GameEvents.RaiseHappinessChanged(population.AverageHappiness);
        GameEvents.RaiseCashFlowChanged(Simulation.Economy.IncomePerDay, Simulation.Economy.ExpensePerDay);
    }
}
