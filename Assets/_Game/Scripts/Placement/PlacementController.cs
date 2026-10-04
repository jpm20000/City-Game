using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class PlacementController : MonoBehaviour
{
    public enum Mode { None, Road, Building, Demolish, Zone, Pipe }

    private const int k_BuildingsMask = 1 << 9;
    private const float k_RaycastHeight = 50f;
    private const float k_HighlightLift = 0.02f;
    private const string k_DebugPlantId = "power_plant";

    [SerializeField] private InputReader m_InputReader;
    [SerializeField] private GridSystem m_GridSystem;
    [SerializeField] private GameManager m_GameManager;
    [SerializeField] private GhostRenderer m_Ghost;
    [SerializeField] private GhostRenderer m_SelectionHighlight;
    [SerializeField] private Transform m_BuildingsContainer;

    private GridData m_GridData;
    private Camera m_Camera;
    private Mode m_Mode = Mode.None;
    private BuildingDefinition m_Selected;
    private int m_Rotation;
    private ZoneType m_ZoneBrush;
    private Vector2Int? m_SelectedCell;
    private bool m_PipeErasing;     // this drag removes pipes (it started on one)
    private bool m_PipeFundsWarned; // one "not enough money" per drag (pipes and roads)
    private byte m_RoadTier;        // the Road tool's tier: 0 = the best unlocked street tier (M16)
    private readonly Dictionary<int, BuildingInstance> m_Buildings = new();

    public Mode CurrentMode => m_Mode;
    public ZoneType ZoneBrush => m_ZoneBrush;
    public BuildingDefinition SelectedBuilding => m_Selected;

    // The Road tool's tier choice: 0 = the street tool (best unlocked street tier), else a tier (Avenue, Highway).
    public byte RoadToolTier => m_RoadTier;

    // The tier the Road tool lays or upgrades to right now.
    public byte ActiveRoadTier => m_RoadTier != 0 ? m_RoadTier : m_GameManager.Simulation.RoadTiers.BestStreetTier;

    // Short text for the cursor while a tool is active: the cost, or why the action can't happen.
    // Empty when there's nothing to say. Valid tells the hint how to colour it.
    public string CursorHint { get; private set; } = string.Empty;
    public bool CursorHintValid { get; private set; }

    // Raised on every tool change, including switching zone brush or building; read CurrentMode/ZoneBrush/SelectedBuilding.
    public event Action ModeChanged;

    // Inspection with no tool active. Raised when the selected cell changes or is cleared.
    public event Action SelectionChanged;
    public bool HasSelection => m_SelectedCell.HasValue;

    // Where the selected building would stand (Building mode, pointer on the map, footprint fits; cost
    // is ignored). Drives the info overlay's what-if preview. PreviewChanged fires when any of it changes.
    public event Action PreviewChanged;
    public bool HasBuildingPreview { get; private set; }
    public Vector2Int PreviewOrigin { get; private set; }
    public int PreviewRotation { get; private set; }
    public Vector2Int SelectedCell => m_SelectedCell.GetValueOrDefault();

    private void Awake()
    {
        m_Camera = Camera.main;
    }

    private void Start()
    {
        if (m_GameManager != null)
        {
            m_GridData = m_GameManager.Grid;
        }
    }

    private void OnEnable()
    {
        EscapeRouter.Register(this, EscapeRouter.Tool, TryEscape);
    }

    private void OnDisable()
    {
        EscapeRouter.Unregister(this);
    }

    // Esc cancels the active tool and clears the selection; with neither it falls through (side panels, pause menu).
    private bool TryEscape()
    {
        if (m_Mode == Mode.None && !HasSelection) return false;
        SetMode(Mode.None);
        ClearSelection();
        return true;
    }

    private void Update()
    {
        if (m_InputReader == null || m_GridData == null || m_GridSystem == null) return;

        HandleModeToggle();
        HandleRotation();
        HandleConfirm();
        UpdateGhost();
        UpdateSelection();
    }

    // Locked buildings (tech not researched) and obsolete ones (M13) can't be selected.
    public void SelectBuilding(BuildingDefinition definition)
    {
        if (!m_GameManager.CanBuild(definition)) return;
        m_Selected = definition;
        m_Rotation = 0;
        SetMode(Mode.Building);
    }

    // tier 0 = the street tool; Avenue / Highway need their tech (M16).
    public void SelectRoad(byte tier = 0)
    {
        if (tier != 0 && !m_GameManager.Simulation.RoadTiers.IsUnlocked(tier)) return;
        m_RoadTier = tier;
        SetMode(Mode.Road);
    }

    // M13: water pipes. Drag to lay; a drag that starts on a pipe removes pipes. Needs Waterworks.
    public void SelectPipe()
    {
        if (!m_GameManager.PipesUnlocked) return;
        SetMode(Mode.Pipe);
    }

    // ZoneType.None erases zoning.
    public void SelectZone(ZoneType zone)
    {
        m_ZoneBrush = zone;
        SetMode(Mode.Zone);
    }

    public void SelectDemolish()
    {
        SetMode(Mode.Demolish);
    }

    public void ClearMode()
    {
        SetMode(Mode.None);
    }

    // Debug shortcut: free cross-shaped road from the map edge with R/C/I zones along it.
    public void DebugSeedCity()
    {
        if (m_GridData == null) return;

        int mid = m_GridData.Width / 2;
        for (int i = 0; i < m_GridData.Width; i++)
        {
            SeedRoad(new Vector2Int(i, mid));
            SeedRoad(new Vector2Int(mid, i));
        }

        // A free power plant in the west commercial strip (same spot as the seeded-city tests), so the
        // debug city can grow past level 1. Placed before zoning, which skips occupied cells.
        BuildingDefinition plant = m_GameManager.Buildings != null ? m_GameManager.Buildings.GetById(k_DebugPlantId) : null;
        Vector2Int plantOrigin = new Vector2Int(0, mid - 3);
        if (plant != null && plant.Prefab != null && m_GridData.CanPlace(plantOrigin, plant.Size, 0))
        {
            CreateBuilding(plant, plantOrigin, 0);
        }

        for (int i = 0; i < m_GridData.Width; i++)
        {
            SeedZone(new Vector2Int(i, mid + 1), ZoneType.Residential);
            SeedZone(new Vector2Int(i, mid - 1), i < mid ? ZoneType.Commercial : ZoneType.Industrial);
            SeedZone(new Vector2Int(mid - 1, i), i > mid ? ZoneType.Residential : ZoneType.Commercial);
            SeedZone(new Vector2Int(mid + 1, i), i > mid ? ZoneType.Residential : ZoneType.Industrial);
        }
    }

    private void SeedRoad(Vector2Int cell)
    {
        if (m_GridData.CanPlace(cell, Vector2Int.one, 0)) PlaceRoad(cell, m_GameManager.Simulation.RoadTiers.BestStreetTier);
    }

    private void SeedZone(Vector2Int cell, ZoneType zone)
    {
        if (!m_GridData.InBounds(cell) || m_GridData.IsRoad(cell) || m_GridData.IsOccupied(cell)) return;
        if (m_GridData.GetZone(cell) != ZoneType.None) return;
        m_GridData.SetZone(cell, zone);
    }

    private void HandleModeToggle()
    {
        if (m_InputReader.RoadToolPressed)
        {
            bool streetToolActive = m_Mode == Mode.Road && m_RoadTier == 0;
            m_RoadTier = 0;
            SetMode(streetToolActive ? Mode.None : Mode.Road);
        }
        else if (m_InputReader.PipeToolPressed && m_GameManager.PipesUnlocked)
        {
            SetMode(m_Mode == Mode.Pipe ? Mode.None : Mode.Pipe);
        }
        else if (m_InputReader.DemolishPressed)
        {
            SetMode(m_Mode == Mode.Demolish ? Mode.None : Mode.Demolish);
        }
    }

    private void HandleRotation()
    {
        if (m_Mode != Mode.Building) return;
        if (!m_InputReader.RotatePressed) return;

        m_Rotation = (m_Rotation + 1) & 3;
    }

    private void SetMode(Mode mode)
    {
        m_Mode = mode;
        if (mode != Mode.None) ClearSelection();
        if (mode == Mode.None)
        {
            m_Selected = null;
            if (m_Ghost != null) m_Ghost.Hide();
        }
        ModeChanged?.Invoke();
    }

    private void HandleConfirm()
    {
        if (IsPointerOverUI()) return;

        // Zoning, pipes and roads paint while the button is held (a road drag lays and upgrades); other tools act once per click.
        bool painting = m_Mode == Mode.Zone || m_Mode == Mode.Pipe || m_Mode == Mode.Road;
        bool active = painting ? m_InputReader.ConfirmHeld : m_InputReader.ConfirmPressed;
        if (!active) return;

        Vector2Int cell = GetMouseCell();
        if ((m_Mode == Mode.Pipe || m_Mode == Mode.Road) && m_InputReader.ConfirmPressed)
        {
            m_PipeErasing = m_Mode == Mode.Pipe && m_GridData.InBounds(cell) && m_GridData.IsPipe(cell);
            m_PipeFundsWarned = false;
        }

        if (m_Mode == Mode.None)
        {
            SelectCell(cell);
            return;
        }

        if (!m_GridData.InBounds(cell)) return;

        switch (m_Mode)
        {
            case Mode.Road:
                TryPlaceRoad(cell);
                break;
            case Mode.Building:
                TryPlaceBuilding(cell);
                break;
            case Mode.Demolish:
                DemolishAt(cell);
                break;
            case Mode.Zone:
                TryZone(cell);
                break;
            case Mode.Pipe:
                TryPipe(cell);
                break;
        }
    }

    private void TryPipe(Vector2Int cell)
    {
        if (m_PipeErasing)
        {
            m_GridData.SetPipe(cell, false);   // no refund, like demolishing
            return;
        }
        if (m_GridData.IsRoad(cell) || m_GridData.IsPipe(cell)) return;

        int cost = m_GameManager.Balance.PipeCost;
        if (!m_GameManager.Economy.Spend(cost))
        {
            if (!m_PipeFundsWarned) GameEvents.RaiseInsufficientFunds(cost);
            m_PipeFundsWarned = true;
            return;
        }
        m_GridData.SetPipe(cell, true);
        GameEvents.RaiseMoneySpent(cost, m_GridSystem.CellToWorld(cell));
        AudioController.Play(SfxId.PipeLay, m_GridSystem.CellToWorld(cell));
    }

    private bool TrySpend(float cost)
    {
        if (m_GameManager.Economy.Spend(cost)) return true;
        GameEvents.RaiseInsufficientFunds(cost);
        return false;
    }

    private static bool IsPointerOverUI()
    {
        EventSystem eventSystem = EventSystem.current;
        return eventSystem != null && eventSystem.IsPointerOverGameObject();
    }

    private bool CanZone(Vector2Int cell)
    {
        return !m_GridData.IsRoad(cell) && !m_GridData.IsOccupied(cell);
    }

    private void TryZone(Vector2Int cell)
    {
        if (!CanZone(cell)) return;
        ClearRubble(cell);      // painting over rubble clears it (M17)
        if (m_GridData.GetZone(cell) == m_ZoneBrush) return;

        // Rezoning or unzoning bulldozes whatever had grown there.
        m_GridData.SetBuildingLevel(cell, 0);
        m_GridData.SetZone(cell, m_ZoneBrush);
        AudioController.Play(SfxId.ZonePaint, m_GridSystem.CellToWorld(cell));
    }

    private void TryPlaceRoad(Vector2Int cell)
    {
        RoadTiers tiers = m_GameManager.Simulation.RoadTiers;
        byte tier = ActiveRoadTier;
        byte existing = m_GridData.GetRoadTier(cell);
        int cost;
        if (existing != 0)
        {
            // Dragging a better tier over a road upgrades it for the price difference; the same or a better tier is skipped.
            if (existing >= tier) return;
            cost = tiers.UpgradeCost(existing, tier);
        }
        else
        {
            if (!m_GridData.CanPlace(cell, Vector2Int.one, 0)) return;
            cost = tiers.Cost(tier);
        }
        if (!m_GameManager.Economy.Spend(cost))
        {
            if (!m_PipeFundsWarned) GameEvents.RaiseInsufficientFunds(cost);   // one warning per drag
            m_PipeFundsWarned = true;
            return;
        }

        PlaceRoad(cell, tier);
        GameEvents.RaiseMoneySpent(cost, m_GridSystem.CellToWorld(cell));
        AudioController.Play(existing != 0 ? SfxId.RoadUpgrade : SfxId.RoadLay, m_GridSystem.CellToWorld(cell));
    }

    private void ClearRubble(Vector2Int cell)
    {
        m_GameManager.Simulation?.Disasters.ClearRubble(cell);
    }

    private bool IsRubble(Vector2Int cell)
    {
        return m_GameManager.Simulation != null && m_GameManager.Simulation.Disasters.IsRubble(cell);
    }

    private void PlaceRoad(Vector2Int cell, byte tier)
    {
        ClearRubble(cell);
        if (m_GridData.GetZone(cell) != ZoneType.None) m_GridData.SetZone(cell, ZoneType.None);
        m_GridData.SetRoadTier(cell, tier);
    }

    private void TryPlaceBuilding(Vector2Int cell)
    {
        if (m_Selected == null || m_Selected.Prefab == null) return;
        if (!m_GameManager.CanBuild(m_Selected)) return;
        if (!m_GridData.CanPlace(cell, m_Selected.Size, m_Rotation))
        {
            AudioController.Play(SfxId.Refused);
            return;
        }
        if (!m_GameManager.Economy.CanAfford(m_Selected.Cost))
        {
            GameEvents.RaiseInsufficientFunds(m_Selected.Cost);
            return;
        }

        if (CreateBuilding(m_Selected, cell, m_Rotation) == null) return;
        m_GameManager.Economy.Spend(m_Selected.Cost);
        GameEvents.RaiseMoneySpent(m_Selected.Cost, FootprintCenter(cell, m_Selected.Size, m_Rotation));
        AudioController.Play(SfxId.Place, FootprintCenter(cell, m_Selected.Size, m_Rotation));
    }

    // Instantiates, occupies and registers a building; clears zoning under it. No cost check.
    private BuildingInstance CreateBuilding(BuildingDefinition definition, Vector2Int origin, int rotation)
    {
        Transform parent = m_BuildingsContainer != null ? m_BuildingsContainer : transform;
        GameObject go = Instantiate(definition.Prefab, parent);
        BuildingInstance instance = go.GetComponent<BuildingInstance>();
        if (instance == null || !instance.Init(m_GridData, definition, origin, rotation))
        {
            Destroy(go);
            return null;
        }

        foreach (Vector2Int footprintCell in m_GridData.GetFootprint(origin, definition.Size, rotation))
        {
            m_GridData.SetZone(footprintCell, ZoneType.None);
            ClearRubble(footprintCell);
        }
        m_Buildings[instance.OccupantId] = instance;
        m_GameManager.RegisterBuilding(instance);
        return instance;
    }

    // --- Save / load ---

    public IEnumerable<BuildingInstance> PlacedBuildings => m_Buildings.Values;

    // Removes every placed building (no refund) and resets tool + selection. Used before a load.
    public void ClearAllBuildings()
    {
        SetMode(Mode.None);
        ClearSelection();
        foreach (BuildingInstance instance in m_Buildings.Values)
        {
            m_GameManager.UnregisterBuilding(instance);
            instance.Demolish();
            Destroy(instance.gameObject);
        }
        m_Buildings.Clear();
    }

    // Re-places a saved building for free. Returns false if the definition can't be placed.
    public bool RestoreBuilding(BuildingDefinition definition, Vector2Int origin, int rotation)
    {
        if (m_GridData == null || definition == null || definition.Prefab == null) return false;
        if (!m_GridData.CanPlace(origin, definition.Size, rotation)) return false;
        return CreateBuilding(definition, origin, rotation) != null;
    }

    // A building that burnt down (M17): the sim has already released its cells; this removes the object and its records. No refund.
    public void RemoveBurnt(int occupantId)
    {
        if (!m_Buildings.TryGetValue(occupantId, out BuildingInstance instance)) return;
        m_Buildings.Remove(occupantId);
        if (m_SelectedCell.HasValue && GetBuildingAtOccupant(instance, m_SelectedCell.Value)) ClearSelection();
        m_GameManager.UnregisterBuilding(instance);
        instance.Demolish();
        Destroy(instance.gameObject);
    }

    private static bool GetBuildingAtOccupant(BuildingInstance instance, Vector2Int cell)
    {
        foreach (Vector2Int c in CellUtils.GetFootprint(instance.Origin, instance.Definition.Size, instance.Rotation))
        {
            if (c == cell) return true;
        }
        return false;
    }

    public BuildingInstance GetBuildingAt(Vector2Int cell)
    {
        if (!m_GridData.InBounds(cell) || !m_GridData.IsOccupied(cell)) return null;
        m_Buildings.TryGetValue(m_GridData.GetOccupant(cell), out BuildingInstance instance);
        return instance;
    }

    public void Unzone(Vector2Int cell)
    {
        if (!m_GridData.InBounds(cell)) return;
        m_GridData.SetBuildingLevel(cell, 0);
        m_GridData.SetZone(cell, ZoneType.None);
    }

    // Selects whatever is at the cell; empty, unzoned land (or off-map) clears the selection.
    public void SelectCell(Vector2Int cell)
    {
        if (!IsSelectable(cell))
        {
            ClearSelection();
            return;
        }
        if (m_SelectedCell == cell) return;

        m_SelectedCell = cell;
        SelectionChanged?.Invoke();
    }

    public void ClearSelection()
    {
        if (!m_SelectedCell.HasValue) return;

        m_SelectedCell = null;
        if (m_SelectionHighlight != null) m_SelectionHighlight.Hide();
        SelectionChanged?.Invoke();
    }

    private bool IsSelectable(Vector2Int cell)
    {
        return m_GridData.InBounds(cell)
            && (m_GridData.IsRoad(cell)
                || m_GridData.IsOccupied(cell)
                || m_GridData.GetBuildingLevel(cell) > 0
                || m_GridData.GetZone(cell) != ZoneType.None
                || IsRubble(cell));
    }

    private void UpdateSelection()
    {
        if (!m_SelectedCell.HasValue) return;

        Vector2Int cell = m_SelectedCell.Value;
        if (!IsSelectable(cell))
        {
            ClearSelection();
            return;
        }
        if (m_SelectionHighlight == null) return;

        BuildingInstance building = GetBuildingAt(cell);
        Vector3 center;
        Vector2Int size;
        if (building != null)
        {
            center = FootprintCenter(building.Origin, building.Definition.Size, building.Rotation);
            size = CellUtils.EffectiveSize(building.Definition.Size, building.Rotation);
        }
        else
        {
            center = m_GridSystem.CellToWorld(cell);
            size = Vector2Int.one;
        }

        // Sit on top of whatever stands there, else the flat highlight is hidden under the building.
        center.y = SurfaceHeight(center) + k_HighlightLift;
        m_SelectionHighlight.Show(center, size, true);
    }

    private static float SurfaceHeight(Vector3 groundPoint)
    {
        Vector3 origin = new Vector3(groundPoint.x, k_RaycastHeight, groundPoint.z);
        return Physics.Raycast(origin, Vector3.down, out RaycastHit hit, k_RaycastHeight * 2f, k_BuildingsMask)
            ? hit.point.y
            : 0f;
    }

    public void DemolishAt(Vector2Int cell)
    {
        if (!m_GridData.InBounds(cell)) return;

        if (m_GridData.IsRoad(cell))
        {
            m_GridData.SetRoad(cell, false);
            AudioController.Play(SfxId.Demolish, m_GridSystem.CellToWorld(cell));
            return;
        }

        if (m_GridData.GetBuildingLevel(cell) > 0)
        {
            m_GridData.SetBuildingLevel(cell, 0);
            AudioController.Play(SfxId.Demolish, m_GridSystem.CellToWorld(cell));
            return;
        }

        if (!m_GridData.IsOccupied(cell))
        {
            ClearRubble(cell);      // demolishing bare rubble clears it for free (M17)
            return;
        }

        int occupantId = m_GridData.GetOccupant(cell);
        if (!m_Buildings.TryGetValue(occupantId, out BuildingInstance instance)) return;

        m_Buildings.Remove(occupantId);
        m_GameManager.UnregisterBuilding(instance);
        instance.Demolish();
        AudioController.Play(SfxId.Demolish, instance.transform.position);
        Destroy(instance.gameObject);
    }

    private void UpdateGhost()
    {
        SetHint(string.Empty, true);
        UpdatePreview();
        if (m_Ghost == null) return;

        if (m_Mode == Mode.None || IsPointerOverUI())
        {
            m_Ghost.Hide();
            return;
        }

        Vector2Int cell = GetMouseCell();
        if (!m_GridData.InBounds(cell))
        {
            m_Ghost.Hide();
            return;
        }
        if (m_Mode == Mode.Building && m_Selected == null)
        {
            m_Ghost.Hide();
            return;
        }

        // The hint decides validity; the ghost just mirrors it.
        UpdateHint(cell);
        if (m_Mode == Mode.Building)
        {
            m_Ghost.Show(
                FootprintCenter(cell, m_Selected.Size, m_Rotation),
                CellUtils.EffectiveSize(m_Selected.Size, m_Rotation),
                CursorHintValid);
        }
        else
        {
            m_Ghost.Show(m_GridSystem.CellToWorld(cell), Vector2Int.one, CursorHintValid);
        }
    }

    private void UpdatePreview()
    {
        bool has = false;
        Vector2Int origin = default;
        if (m_Mode == Mode.Building && m_Selected != null && !IsPointerOverUI())
        {
            origin = GetMouseCell();
            has = m_GridData.InBounds(origin) && FootprintProblem(origin, m_Selected.Size, m_Rotation) == null;
        }

        if (has == HasBuildingPreview && (!has || (origin == PreviewOrigin && m_Rotation == PreviewRotation))) return;
        HasBuildingPreview = has;
        PreviewOrigin = origin;
        PreviewRotation = m_Rotation;
        PreviewChanged?.Invoke();
    }

    private void SetHint(string text, bool valid)
    {
        CursorHint = text;
        CursorHintValid = valid;
    }

    // Sets CursorHint/CursorHintValid for the active tool. Validity mirrors what Confirm would do.
    private void UpdateHint(Vector2Int cell)
    {
        EconomySystem economy = m_GameManager.Economy;
        switch (m_Mode)
        {
            case Mode.Road:
            {
                RoadTiers tiers = m_GameManager.Simulation.RoadTiers;
                byte tier = ActiveRoadTier;
                byte existing = m_GridData.GetRoadTier(cell);
                string name = tiers.DisplayName(tier);
                string note = tiers.Frontage(tier) ? string.Empty : " — no access for blocks beside it";
                if (existing != 0)
                {
                    if (existing >= tier) { SetHint($"Already {tiers.DisplayName(existing)}", false); break; }
                    int upgrade = tiers.UpgradeCost(existing, tier);
                    if (!economy.CanAfford(upgrade)) SetHint($"Need ${upgrade:N0}", false);
                    else SetHint($"Upgrade to {name} ${upgrade:N0}", true);
                    break;
                }
                int cost = tiers.Cost(tier);
                string problem = FootprintProblem(cell, Vector2Int.one, 0);
                if (problem != null) SetHint(problem, false);
                else if (!economy.CanAfford(cost)) SetHint($"Need ${cost:N0}", false);
                else SetHint($"{name} ${cost:N0}{note}", true);
                break;
            }

            case Mode.Building:
            {
                if (m_Selected == null) break;
                string problem = FootprintProblem(cell, m_Selected.Size, m_Rotation);
                if (!m_GameManager.IsUnlocked(m_Selected)) SetHint($"Locked — research {m_GameManager.RequiredTechName(m_Selected)}", false);
                else if (m_GameManager.IsObsolete(m_Selected)) SetHint($"Obsolete in the {m_GameManager.ObsoleteAgeName(m_Selected)}", false);
                else if (m_GameManager.IsOutdated(m_Selected)) SetHint($"Outdated — build the {m_GameManager.ReplacementFor(m_Selected).DisplayName}", false);
                else if (problem != null) SetHint(problem, false);
                else if (!economy.CanAfford(m_Selected.Cost)) SetHint($"Need ${m_Selected.Cost:N0}", false);
                else SetHint($"{m_Selected.DisplayName}  ${m_Selected.Cost:N0}   [R] rotate", true);
                break;
            }

            case Mode.Demolish:
            {
                BuildingInstance building = GetBuildingAt(cell);
                if (m_GridData.IsRoad(cell)) SetHint("Demolish road", true);
                else if (building != null) SetHint($"Demolish {building.Definition.DisplayName} (no refund)", true);
                else if (m_GridData.GetBuildingLevel(cell) > 0) SetHint("Demolish grown building", true);
                else SetHint(string.Empty, false);   // nothing here: red ghost, no label
                break;
            }

            case Mode.Pipe:
            {
                int cost = m_GameManager.Balance.PipeCost;
                bool dragging = m_InputReader.ConfirmHeld;
                if (dragging ? m_PipeErasing : m_GridData.IsPipe(cell)) SetHint(m_GridData.IsPipe(cell) ? "Remove pipe" : "Drag to remove pipes", true);
                else if (m_GridData.IsRoad(cell)) SetHint("Roads carry water already", false);
                else if (m_GridData.IsPipe(cell)) SetHint("Pipe", true);
                else if (!economy.CanAfford(cost)) SetHint($"Need ${cost:N0}", false);
                else SetHint($"Pipe  ${cost:N0}  — drag to lay", true);
                break;
            }

            case Mode.Zone:
                if (m_GridData.IsRoad(cell)) SetHint("Can't zone a road", false);
                else if (m_GridData.IsOccupied(cell)) SetHint("Can't zone under a building", false);
                break;
        }
    }

    // Why a footprint can't be placed (mirrors GridData.CanPlace), or null if it can.
    private string FootprintProblem(Vector2Int origin, Vector2Int size, int rotation)
    {
        foreach (Vector2Int cell in m_GridData.GetFootprint(origin, size, rotation))
        {
            if (!m_GridData.InBounds(cell)) return "Doesn't fit on the map";
            if (m_GridData.IsRoad(cell)) return "Blocked by a road";
            if (m_GridData.IsOccupied(cell)) return "Blocked by a building";
            if (m_GridData.GetBuildingLevel(cell) > 0) return "Blocked — demolish what grew here first";
        }
        return null;
    }

    private static Vector3 FootprintCenter(Vector2Int origin, Vector2Int size, int rotation)
    {
        Vector2Int effective = CellUtils.EffectiveSize(size, rotation);
        return new Vector3(origin.x + effective.x * 0.5f, 0f, origin.y + effective.y * 0.5f);
    }

    private Vector2Int GetMouseCell()
    {
        Ray ray = m_Camera.ScreenPointToRay(m_InputReader.Pointer);
        Plane ground = new Plane(Vector3.up, Vector3.zero);
        if (ground.Raycast(ray, out float enter))
        {
            return m_GridSystem.WorldToCell(ray.GetPoint(enter));
        }
        return new Vector2Int(-1, -1);
    }
}
