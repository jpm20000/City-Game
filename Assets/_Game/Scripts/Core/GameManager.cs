using System.Collections.Generic;
using UnityEngine;

public sealed class GameManager : MonoBehaviour
{
    [SerializeField] private GridSystem m_GridSystem;
    [SerializeField] private BuildingDatabase m_BuildingDatabase;
    [SerializeField] private BalanceConfig m_Balance;
    [SerializeField] private TimeManager m_Time;
    [SerializeField] private GrowthVisuals m_GrowthVisuals;

    private CityModifiers m_Modifiers;
    private readonly List<BuildingInstance> m_SourceBuildings = new();
    private readonly List<ServiceSource> m_Sources = new();
    private bool m_PowerDirty = true;

    public GridData Grid { get; private set; }
    public RoadNetwork Roads { get; private set; }
    public BuildingDatabase Buildings => m_BuildingDatabase;
    public BalanceConfig Balance => m_Balance;
    public TimeManager Clock => m_Time;
    public SimulationSystem Simulation { get; private set; }
    public EconomySystem Economy => Simulation?.Economy;
    public PopulationSystem Population => Simulation?.Population;
    public DemandSystem Demand => Simulation?.Demand;
    public Vector2Int MapSize => Grid != null ? new Vector2Int(Grid.Width, Grid.Height) : Vector2Int.zero;

    private void Awake()
    {
        if (m_GridSystem == null) return;

        Grid = new GridData(m_GridSystem.GridSize.x, m_GridSystem.GridSize.y);
        Roads = new RoadNetwork(Grid);
        Grid.OnCellChanged += GameEvents.RaiseCellChanged;
        Grid.OnCellChanged += MarkPowerDirty;
        Grid.OnResized += HandleResized;
        m_GridSystem.PaintGround(MapSize);

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
        if (IsSource(building.Definition))
        {
            m_SourceBuildings.Add(building);
            RebuildSources();
        }
    }

    public void UnregisterBuilding(BuildingInstance building)
    {
        ApplyModifiers(building.Definition, -1);
        if (m_SourceBuildings.Remove(building)) RebuildSources();
    }

    private static bool IsSource(BuildingDefinition def)
    {
        return def.CoverageRadius > 0 || def.PowerSupply > 0;
    }

    private void RebuildSources()
    {
        m_Sources.Clear();
        foreach (BuildingInstance b in m_SourceBuildings)
        {
            BuildingDefinition def = b.Definition;
            m_Sources.Add(new ServiceSource(b.Origin, CellUtils.EffectiveSize(def.Size, b.Rotation),
                def.CoverageRadius, def.PowerSupply));
        }
        Simulation.Sources = m_Sources;
        m_PowerDirty = true;
    }

    // New city / load replaced the map (GridData.Resize). The sim systems resize themselves.
    private void HandleResized()
    {
        m_GridSystem.PaintGround(MapSize);
        m_PowerDirty = true;
        GameEvents.RaiseWorldResized(MapSize);
    }

    private void MarkPowerDirty(Vector2Int cell)
    {
        m_PowerDirty = true;
    }

    // Power can change on any road, zone, level or plant change (also while paused), so it's pushed
    // at most once a frame rather than per tick.
    private void LateUpdate()
    {
        if (!m_PowerDirty || Simulation == null) return;
        m_PowerDirty = false;
        RaisePowerChanged();
    }

    private void RaisePowerChanged()
    {
        PowerSystem power = Simulation.Power;
        GameEvents.RaisePowerChanged(power.Supply, power.Demand, power.UnpoweredCells);
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
        // Deferred to LateUpdate: after a load this lands after GameEvents.CityLoaded, so listeners
        // that re-sync on load don't treat the loaded power state as news.
        m_PowerDirty = true;
    }
}
