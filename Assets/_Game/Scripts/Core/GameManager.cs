using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class GameManager : MonoBehaviour
{
    [SerializeField] private GridSystem m_GridSystem;
    [SerializeField] private BuildingDatabase m_BuildingDatabase;
    [SerializeField] private BalanceConfig m_Balance;
    [Tooltip("Ages and techs (M11). Leave both empty to play by today's rules (Industrial, no research).")]
    [SerializeField] private AgeDatabase m_AgeDatabase;
    [SerializeField] private TechDatabase m_TechDatabase;
    [SerializeField] private TimeManager m_Time;
    [SerializeField] private GrowthVisuals m_GrowthVisuals;
    [Tooltip("Road and ground art (M18c). Empty = the runtime-drawn roads and the scene's ground tile.")]
    [SerializeField] private TileArtSet m_TileArt;

    private CityModifiers m_Modifiers;
    private readonly List<BuildingInstance> m_SourceBuildings = new();
    private readonly List<ServiceSource> m_Sources = new();
    private bool m_PowerDirty = true;

    public GridData Grid { get; private set; }
    public RoadNetwork Roads { get; private set; }
    public BuildingDatabase Buildings => m_BuildingDatabase;
    public BalanceConfig Balance => m_Balance;
    // Null unless both databases are assigned (the sim then has ages and research).
    public AgeDatabase Ages => m_AgeDatabase != null && m_TechDatabase != null ? m_AgeDatabase : null;
    public TechDatabase Techs => m_AgeDatabase != null && m_TechDatabase != null ? m_TechDatabase : null;
    public TimeManager Clock => m_Time;
    public TileArtSet TileArt => m_TileArt;
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

        if ((m_AgeDatabase == null) != (m_TechDatabase == null))
        {
            Debug.LogWarning("GameManager: assign both the AgeDatabase and the TechDatabase (or neither); playing without ages.", this);
        }
        Simulation = new SimulationSystem(Grid, Roads, m_Balance, Ages, Techs);
        Simulation.Economy.OnMoneyChanged += GameEvents.RaiseMoneyChanged;
        // The startup city counts as a new one: disasters and events on (M17), the RNG seeded from the clock.
        Simulation.Disasters.Enabled = true;
        Simulation.Disasters.Random.Seed((ulong)System.Environment.TickCount);
        if (Simulation.Tech != null)
        {
            Simulation.Tech.TechCompleted += HandleTechCompleted;
            Simulation.Tech.AgeAdvanced += HandleAgeAdvanced;
        }

        if (m_GrowthVisuals != null) m_GrowthVisuals.Init(Grid, Ages);

        GameEvents.AgeChanged += ApplyGroundArt;
        GameEvents.CityLoaded += ApplyGroundArt;
        ApplyGroundArt();

        if (m_Time != null)
        {
            m_Time.Init(m_Balance);
            m_Time.OnTick += HandleTick;
            // The startup city is in the sim's starting age (Industrial); its calendar matches.
            if (Simulation.Tech != null) m_Time.SetDate(1, 1, Simulation.Tech.CurrentAgeDefinition.StartYear);
        }
        else
        {
            Debug.LogError("GameManager: no TimeManager assigned; simulation will not tick.", this);
        }
    }

    private void OnDestroy()
    {
        if (m_Time != null) m_Time.OnTick -= HandleTick;
        GameEvents.AgeChanged -= ApplyGroundArt;
        GameEvents.CityLoaded -= ApplyGroundArt;
        foreach (TileBase[] tiles in m_GroundTiles.Values)
        {
            if (tiles == null) continue;
            foreach (TileBase tile in tiles) Destroy(tile);
        }
        m_GroundTiles.Clear();
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

    // Whether the player may place this building now: no required tech, no age data, or its tech is
    // researched. Loads restore locked buildings anyway.
    public bool IsUnlocked(BuildingDefinition def)
    {
        if (def == null) return false;
        if (string.IsNullOrEmpty(def.RequiredTech) || Simulation == null || Simulation.Tech == null) return true;
        TechDefinition tech = Simulation.Tech.Techs.GetById(def.RequiredTech);
        return tech != null && Simulation.Tech.IsResearched(tech);
    }

    // Whether the city's age is at or past the building's ObsoleteAge (M13): it can't be built any
    // more, but placed ones stay. Never without age data.
    public bool IsObsolete(BuildingDefinition def)
    {
        if (def == null || string.IsNullOrEmpty(def.ObsoleteAge) || Simulation?.Tech == null) return false;
        int age = Simulation.Tech.Ages.IndexOf(def.ObsoleteAge);
        return age >= 0 && Simulation.Tech.CurrentAge >= age;
    }

    // Unlocked, not obsolete and not outdated: the toolbar shows it and the placement tool accepts it.
    public bool CanBuild(BuildingDefinition def) => IsUnlocked(def) && !IsObsolete(def) && ReplacementFor(def) == null;

    // The civic building that supersedes this one (M14): the latest-age building of the same line
    // that is unlocked and not obsolete, if it comes from a later age than this one. A building's age
    // is its RequiredTech's age. Null for non-civic buildings, without age data, or when nothing newer
    // can be built yet. Placed copies keep working; the panel suggests the replacement.
    public BuildingDefinition ReplacementFor(BuildingDefinition def)
    {
        if (def == null || def.CivicKind == ServiceKind.None || Simulation?.Tech == null || m_BuildingDatabase == null) return null;
        BuildingDefinition best = null;
        int bestAge = TierAge(def);
        foreach (BuildingDefinition other in m_BuildingDatabase.Entries)
        {
            if (other == null || other == def || other.CivicKind != def.CivicKind) continue;
            int age = TierAge(other);
            if (age <= bestAge || !IsUnlocked(other) || IsObsolete(other)) continue;
            best = other;
            bestAge = age;
        }
        return best;
    }

    public bool IsOutdated(BuildingDefinition def) => ReplacementFor(def) != null;

    // Placed buildings the sim sees as sources (services, utilities, civic buildings).
    public IReadOnlyList<BuildingInstance> SourceBuildings => m_SourceBuildings;

    // Whether a civic line is part of the game yet (M14): one of its buildings is unlocked. Its view
    // stays hidden until then. Always true without age data.
    public bool CivicUnlocked(ServiceKind kind)
    {
        if (Simulation == null || Simulation.Tech == null || m_BuildingDatabase == null) return true;
        foreach (BuildingDefinition def in m_BuildingDatabase.Entries)
        {
            if (def != null && def.CivicKind == kind && def.CivicRadius > 0 && IsUnlocked(def)) return true;
        }
        return false;
    }

    // The newest building of a line the player can build now, or null.
    public BuildingDefinition BestCivic(ServiceKind kind)
    {
        if (m_BuildingDatabase == null) return null;
        foreach (BuildingDefinition def in m_BuildingDatabase.Entries)
        {
            if (def != null && def.CivicKind == kind && def.CivicRadius > 0 && CanBuild(def)) return def;
        }
        return null;
    }

    // The age of the tech that unlocks a building (-1 when it needs none).
    private int TierAge(BuildingDefinition def)
    {
        if (string.IsNullOrEmpty(def.RequiredTech)) return -1;
        TechDefinition tech = Simulation.Tech.Techs.GetById(def.RequiredTech);
        return tech != null ? tech.Age : -1;
    }

    // Display name of the age a building became obsolete in, or null.
    public string ObsoleteAgeName(BuildingDefinition def)
    {
        if (!IsObsolete(def)) return null;
        return Simulation.Tech.Ages[Simulation.Tech.Ages.IndexOf(def.ObsoleteAge)].DisplayName;
    }

    // Whether a budget line has anything to fund yet (M15): a building of it is unlocked. Always true
    // without age data. Parks: any building with a park reach.
    public bool BudgetLineUnlocked(BudgetLine line)
    {
        if (Simulation == null || Simulation.Tech == null || m_BuildingDatabase == null) return true;
        switch (line)
        {
            case BudgetLine.Power: return PowerUnlocked;
            case BudgetLine.Water: return WaterUnlocked;
            case BudgetLine.Order: return CivicUnlocked(ServiceKind.Order);
            case BudgetLine.Fire: return CivicUnlocked(ServiceKind.Fire);
            case BudgetLine.Health: return CivicUnlocked(ServiceKind.Health);
            case BudgetLine.Education: return CivicUnlocked(ServiceKind.Education);
        }
        foreach (BuildingDefinition def in m_BuildingDatabase.Entries)
        {
            if (def != null && def.CoverageRadius > 0 && IsUnlocked(def)) return true;
        }
        return false;
    }

    // Funding, a loan or an ordinance changed (the Budget panel): the networks and views re-read the funded
    // state at the next LateUpdate, and listeners hear about it.
    public void NotifyBudgetChanged()
    {
        m_PowerDirty = true;
        GameEvents.RaiseBudgetChanged();
    }

    // Whether any power source can be built (or is already researched for): the Power HUD group,
    // Power view and power toasts stay hidden until then. Always true without age data.
    public bool PowerUnlocked
    {
        get
        {
            if (Simulation == null || Simulation.Tech == null || m_BuildingDatabase == null) return true;
            foreach (BuildingDefinition def in m_BuildingDatabase.Entries)
            {
                if (def != null && def.PowerSupply > 0 && IsUnlocked(def)) return true;
            }
            return false;
        }
    }

    // Whether water is part of the game yet (M13): the Water HUD group, view and toasts stay hidden
    // until the current age needs water or a water building is unlocked. Always true without age data.
    public bool WaterUnlocked
    {
        get
        {
            if (Simulation == null || Simulation.Tech == null || m_BuildingDatabase == null) return true;
            if (Simulation.Rules.Water != WaterRule.None) return true;
            foreach (BuildingDefinition def in m_BuildingDatabase.Entries)
            {
                if (def != null && (def.WaterSupply > 0 || def.WaterRadius > 0) && CanBuild(def)) return true;
            }
            return false;
        }
    }

    // Whether the pipe tool is available (M13): once a piped water source can be built (Waterworks).
    // Always true without age data.
    public bool PipesUnlocked
    {
        get
        {
            if (Simulation == null || Simulation.Tech == null || m_BuildingDatabase == null) return true;
            foreach (BuildingDefinition def in m_BuildingDatabase.Entries)
            {
                if (def != null && def.WaterSupply > 0 && IsUnlocked(def)) return true;
            }
            return false;
        }
    }

    // Display name of the current age, or null without age data.
    public string CurrentAgeName => Simulation?.Tech?.CurrentAgeDefinition.DisplayName;

    // Display name of the tech a building needs, or null when it needs none (or the Id is unknown).
    public string RequiredTechName(BuildingDefinition def)
    {
        if (def == null || string.IsNullOrEmpty(def.RequiredTech) || Simulation?.Tech == null) return null;
        TechDefinition tech = Simulation.Tech.Techs.GetById(def.RequiredTech);
        return tech != null ? tech.DisplayName : def.RequiredTech;
    }

    private void HandleTechCompleted(TechDefinition tech)
    {
        GameEvents.RaiseTechCompleted(tech.Id);
    }

    // Advancing moves the calendar to max(current year, the age's start year).
    private void HandleAgeAdvanced(int age)
    {
        if (m_Time != null) m_Time.SetYear(Simulation.Tech.Ages[age].YearOnEntering(m_Time.Year));
        m_PowerDirty = true;   // the water rule may have switched (wells -> piped)
        GameEvents.RaiseAgeChanged(age);
    }

    private static bool IsSource(BuildingDefinition def)
    {
        return def.CoverageRadius > 0 || def.PowerSupply > 0 || def.Pollution > 0f || def.WaterSupply > 0 || def.WaterRadius > 0
            || (def.CivicKind != ServiceKind.None && def.CivicRadius > 0);
    }

    // The age of the tech that unlocks the building (M17: older plants break down more often); -1 when unknown.
    private int TechAgeOf(BuildingDefinition def)
    {
        if (string.IsNullOrEmpty(def.RequiredTech) || Simulation?.Tech == null) return -1;
        TechDefinition tech = Simulation.Tech.Techs.GetById(def.RequiredTech);
        return tech != null ? tech.Age : -1;
    }

    private void RebuildSources()
    {
        m_Sources.Clear();
        foreach (BuildingInstance b in m_SourceBuildings)
        {
            BuildingDefinition def = b.Definition;
            m_Sources.Add(new ServiceSource(b.Origin, CellUtils.EffectiveSize(def.Size, b.Rotation),
                def.CoverageRadius, def.PowerSupply, def.Pollution, def.PollutionRadius, def.WaterSupply, def.WaterRadius,
                def.CivicKind, def.CivicRadius, def.CivicStrength, def.UpkeepPerDay, def.ResearchPerDay, def.Cost, TechAgeOf(def)));
        }
        Simulation.Sources = m_Sources;
        m_PowerDirty = true;
    }

    // The ground follows the city's current age (M18c): 4 art variants per age, picked per cell by hash.
    private readonly Dictionary<int, TileBase[]> m_GroundTiles = new();

    private void ApplyGroundArt(int age) => ApplyGroundArt();

    private void ApplyGroundArt()
    {
        if (m_TileArt == null || m_GridSystem == null) return;
        int age = Simulation != null && Simulation.Tech != null ? Simulation.Tech.CurrentAge : 2;
        if (!m_GroundTiles.TryGetValue(age, out TileBase[] tiles))
        {
            var made = new List<TileBase>();
            for (int v = 0; v < TileArtSet.GroundVariants; v++)
            {
                Sprite sprite = m_TileArt.Ground(age, v);
                if (sprite == null) continue;
                Tile tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = sprite;
                made.Add(tile);
            }
            tiles = made.Count > 0 ? made.ToArray() : null;
            m_GroundTiles[age] = tiles;
        }
        m_GridSystem.SetGroundVariants(tiles);
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

    // Power and water can change on any road, zone, level or source change (also while paused), so
    // they're pushed at most once a frame rather than per tick.
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
        GameEvents.RaiseWaterChanged(Simulation.Water.Status);
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
        m_Modifiers.ResearchPerDay += sign * def.ResearchPerDay;

        Simulation.Modifiers = m_Modifiers;
    }

    private void HandleTick()
    {
        Simulation.Tick();
        if (Simulation.Growth.Redeveloped.Count > 0) GameEvents.RaiseRedeveloped(Simulation.Growth.Redeveloped.Count);
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
        if (Simulation.Tech != null) GameEvents.RaiseResearchChanged();
        // Deferred to LateUpdate: after a load this lands after GameEvents.CityLoaded, so listeners
        // that re-sync on load don't treat the loaded power / water state as news.
        m_PowerDirty = true;
    }
}
