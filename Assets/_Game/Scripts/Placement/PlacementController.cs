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
    private ZoneType m_LastZone = ZoneType.Residential;    // the zone a density button falls back to (M23)
    private Density m_DensityBrush;                        // Medium by default, remembered for the session (M23)
    private readonly Dictionary<Vector2Int, Density> m_PendingRepaint = new();   // built cells a drag would shrink, asked about on release
    private Vector2Int? m_SelectedCell;
    private bool m_PipeErasing;     // this drag removes pipes (it started on one)
    private bool m_PipeFundsWarned; // one "not enough money" per drag (pipes and roads)
    private byte m_RoadTier;        // the Road tool's tier: 0 = the best unlocked street tier (M16)
    private byte m_AvenueHeading = RoadLayout.East;   // the heading the Avenue pair follows (the last drag's; default +x) (M22)
    private byte m_AvenueSide = RoadLayout.South;     // R: the side the second lane goes on (turns a quarter per press)
    private Vector2Int? m_DragLast; // the last cell this road drag laid (the heading comes from the step to the next one)
    private Vector2Int? m_PendingLane;  // the drag's first Avenue cell, laid once the second cell shows the heading
    private bool m_HighwayTwoWay;   // R: the Highway tool lays two-way cells instead of one-way ones (M22)
    private byte m_HighwayHeading;  // the heading of the current Highway drag (0 until the second cell)
    private readonly HashSet<Vector2Int> m_FreshHighway = new();    // highway cells this drag laid (they take its direction)
    private Vector2Int? m_LineStart;    // M27: the cell a road / pipe line was pressed on (the line is built on release)
    private Vector2Int m_LineEnd;       // the cursor cell the line last reached (kept while the pointer is over the UI)
    private bool m_BatchSpend;          // a line is being built: its costs add up into one MoneySpent popup
    private float m_BatchSpent;
    private bool m_DemolishDrag;        // M27: the Demolish button is held: every cell the cursor enters is cleared once
    private readonly HashSet<Vector2Int> m_DemolishVisited = new();
    private readonly List<int> m_PendingDemolish = new();      // placed buildings the drag touched, asked about on release
    private readonly Dictionary<int, BuildingInstance> m_Buildings = new();

    public Mode CurrentMode => m_Mode;
    public ZoneType ZoneBrush => m_ZoneBrush;

    // The density the brush paints now: the picked one, or the nearest allowed while the age has not unlocked it (M23).
    public Density DensityBrush
    {
        get
        {
            ZoneType zone = m_ZoneBrush != ZoneType.None ? m_ZoneBrush : m_LastZone;
            if (m_DensityBrush == Density.High && !m_GameManager.HighDensityUnlocked(zone)) return m_GameManager.MediumDensityUnlocked ? Density.Medium : Density.Low;
            if (m_DensityBrush == Density.Medium && !m_GameManager.MediumDensityUnlocked) return Density.Low;
            return m_DensityBrush;
        }
    }
    public BuildingDefinition SelectedBuilding => m_Selected;

    // The Road tool's tier choice: 0 = the street tool (best unlocked street tier), else a tier (Avenue, Highway).
    public byte RoadToolTier => m_RoadTier;
    public byte AvenueSideChoice => m_AvenueSide;
    public bool HighwayTwoWay => m_HighwayTwoWay;

    // M27: a road / pipe line is being dragged (pressed, not yet released or cancelled).
    public bool LineActive => m_LineStart.HasValue;

    // M27: a demolish drag is held, and how many placed buildings it will remove (or ask about) on release.
    public bool DemolishDragActive => m_DemolishDrag;
    public int PendingDemolishCount => m_PendingDemolish.Count;

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
        if (m_LineStart.HasValue)
        {
            m_LineStart = null;     // the first Esc / right click drops the line being dragged, the next one the tool
            return true;
        }
        if (m_DemolishDrag)
        {
            EndDemolishDrag();      // what was cleared stays cleared; the placed buildings it collected are left standing
            return true;
        }
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
        FlushPendingLane();
        FlushPendingRepaint();
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
        if (zone != ZoneType.None)
        {
            m_LastZone = zone;
            if (m_DensityBrush == Density.High && !m_GameManager.HighDensityUnlocked(zone)) m_DensityBrush = Density.Medium;
        }
        SetMode(Mode.Zone);
    }

    // M23: the density the zone brush paints. Picking one while no zone is selected starts painting the last zone.
    public void SelectDensity(Density density)
    {
        if (density == Density.High && !m_GameManager.HighDensityUnlocked(m_ZoneBrush != ZoneType.None ? m_ZoneBrush : m_LastZone)) return;
        if (density == Density.Medium && !m_GameManager.MediumDensityUnlocked) return;
        m_DensityBrush = density;
        if (m_Mode == Mode.Zone && m_ZoneBrush != ZoneType.None) ModeChanged?.Invoke();
        else SelectZone(m_LastZone);
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
        if (m_Mode == Mode.Road && m_RoadTier == RoadTiers.Avenue && m_InputReader.RotatePressed)
        {
            // R turns the pair a quarter: the second lane moves to the next side and the idle heading follows (M22).
            m_AvenueSide = RoadLayout.RightOf(m_AvenueSide);
            m_AvenueHeading = RoadLayout.LeftOf(m_AvenueSide);
            return;
        }
        if (m_Mode == Mode.Road && m_RoadTier == RoadTiers.Highway && m_InputReader.RotatePressed)
        {
            m_HighwayTwoWay = !m_HighwayTwoWay;
            return;
        }
        if (m_Mode != Mode.Building) return;
        if (!m_InputReader.RotatePressed) return;

        m_Rotation = (m_Rotation + 1) & 3;
    }

    private void SetMode(Mode mode)
    {
        m_Mode = mode;
        m_PendingLane = null;
        m_DragLast = null;
        m_LineStart = null;
        EndDemolishDrag();
        if (mode != Mode.None) ClearSelection();
        if (mode == Mode.None)
        {
            m_Selected = null;
            if (m_Ghost != null) m_Ghost.Hide();
        }
        ModeChanged?.Invoke();
    }

    private bool IsLineTool => m_Mode == Mode.Road || m_Mode == Mode.Pipe;

    private void HandleConfirm()
    {
        if (IsLineTool)
        {
            HandleLine();
            return;
        }
        if (m_Mode == Mode.Demolish)
        {
            HandleDemolishDrag();
            return;
        }
        if (IsPointerOverUI()) return;

        // Zoning paints while the button is held; other tools act once per click.
        bool painting = m_Mode == Mode.Zone;
        bool active = painting ? m_InputReader.ConfirmHeld : m_InputReader.ConfirmPressed;
        if (!active) return;

        Vector2Int cell = GetMouseCell();
        if (m_Mode == Mode.None)
        {
            SelectCell(cell);
            return;
        }

        if (!m_GridData.InBounds(cell)) return;

        switch (m_Mode)
        {
            case Mode.Building:
                TryPlaceBuilding(cell);
                break;
            case Mode.Zone:
                TryZone(cell);
                break;
        }
    }

    // M27: every road tier and the pipe tool lay a straight line. Press on a cell, drag (the line follows the cursor along
    // x or z, whichever is longer), release to build it; Esc / right click while dragging drops it. Nothing is laid
    // before the release, and a release over the UI still builds the line as it last stood on the map.
    private void HandleLine()
    {
        if (m_LineStart.HasValue)
        {
            if (m_InputReader.Blocked)
            {
                m_LineStart = null;     // a dialog opened mid-drag: nothing is built
                return;
            }
            if (!IsPointerOverUI()) m_LineEnd = GetMouseCell();
            if (!m_InputReader.ConfirmHeld) CommitLine();
            return;
        }
        if (!m_InputReader.ConfirmPressed || IsPointerOverUI()) return;
        Vector2Int cell = GetMouseCell();
        if (!m_GridData.InBounds(cell)) return;
        m_LineStart = cell;
        m_LineEnd = cell;
        if (!m_InputReader.ConfirmHeld) CommitLine();   // pressed and released in one frame: a click lays one cell
    }

    // Lays the line cell by cell from the start through the same code a single cell uses, so Avenue pairing, the
    // Highway's one-way heading and upgrade prices come from consecutive cells exactly as before; money runs out
    // part-way with one warning. A single cell of Avenue waits for FlushPendingLane (the last heading).
    private void CommitLine()
    {
        Vector2Int start = m_LineStart.Value;
        m_LineStart = null;
        int length = RoadLayout.StraightRun(m_GridData, start, m_LineEnd, out byte heading);
        m_PipeErasing = m_Mode == Mode.Pipe && m_GridData.IsPipe(start);
        m_PipeFundsWarned = false;
        m_DragLast = null;
        m_PendingLane = null;
        m_HighwayHeading = RoadLayout.None;
        m_FreshHighway.Clear();
        m_BatchSpend = true;
        m_BatchSpent = 0f;
        foreach (Vector2Int cell in RoadLayout.RunCells(start, heading, length))
        {
            if (m_Mode == Mode.Pipe) TryPipe(cell);
            else TryPlaceRoad(cell);
        }
        m_BatchSpend = false;
        Vector2Int end = start + RoadLayout.Offset(heading) * length;
        if (m_BatchSpent > 0f) GameEvents.RaiseMoneySpent(m_BatchSpent, (m_GridSystem.CellToWorld(start) + m_GridSystem.CellToWorld(end)) * 0.5f);
    }

    // M27: Demolish clears whatever is under the cursor while the button is held — roads (an avenue lane takes its
    // partner), grown blocks and rubble at once, each cell once per drag; placed buildings are collected and removed on
    // release (one confirmation for all of them when Confirm demolish is on). A click is a drag of one cell.
    private void HandleDemolishDrag()
    {
        if (m_InputReader.ConfirmPressed && !IsPointerOverUI())
        {
            m_DemolishDrag = true;
            m_DemolishVisited.Clear();
            m_PendingDemolish.Clear();
        }
        if (!m_DemolishDrag) return;
        if (m_InputReader.Blocked)
        {
            EndDemolishDrag();
            return;
        }
        if (!IsPointerOverUI())
        {
            Vector2Int cell = GetMouseCell();
            if (m_GridData.InBounds(cell) && m_DemolishVisited.Add(cell)) DemolishAt(cell, deferBuildings: true);
        }
        if (m_InputReader.ConfirmHeld) return;
        m_DemolishDrag = false;
        FlushPendingDemolish();
    }

    private void EndDemolishDrag()
    {
        m_DemolishDrag = false;
        m_DemolishVisited.Clear();
        m_PendingDemolish.Clear();
    }

    // The placed buildings a demolish drag collected: removed, or asked about once.
    private void FlushPendingDemolish()
    {
        if (m_PendingDemolish.Count == 0) return;
        var ids = new List<int>(m_PendingDemolish);
        m_PendingDemolish.Clear();
        void Apply()
        {
            foreach (int id in ids) RemoveBuilding(id);
        }
        if (!GameSettings.ConfirmDemolish || GameFlow.Instance == null)
        {
            Apply();
            return;
        }
        string title = ids.Count == 1 && m_Buildings.TryGetValue(ids[0], out BuildingInstance only)
            ? $"Demolish {only.Definition.DisplayName}?"
            : $"Demolish {ids.Count} buildings?";
        GameFlow.Instance.Confirm.Ask(title, ids.Count == 1 ? "It is removed with no refund." : "They are removed with no refund.",
            new ConfirmDialog.Choice("Demolish", Apply, primary: true),
            new ConfirmDialog.Choice("Cancel", null));
    }

    // One MoneySpent per action, or one per line while a line is being built.
    private void ReportSpent(float cost, Vector2Int cell)
    {
        if (m_BatchSpend) m_BatchSpent += cost;
        else GameEvents.RaiseMoneySpent(cost, m_GridSystem.CellToWorld(cell));
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
        ReportSpent(cost, cell);
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
        bool zoning = m_ZoneBrush != ZoneType.None;
        Density density = zoning ? DensityBrush : Density.Medium;
        if (density == Density.High && !m_GameManager.HighDensityUnlocked(m_ZoneBrush)) return;
        ClearRubble(cell);      // painting over rubble clears it (M17)
        if (m_GridData.GetZone(cell) == m_ZoneBrush)
        {
            if (zoning && m_GridData.GetDensity(cell) != density) RepaintDensity(cell, density);
            return;
        }

        // Rezoning or unzoning bulldozes whatever had grown there.
        m_GridData.SetBuildingLevel(cell, 0);
        m_GridData.SetZone(cell, m_ZoneBrush);
        if (zoning && density != Density.Medium) m_GridData.SetDensity(cell, density);
        AudioController.Play(SfxId.ZonePaint, m_GridSystem.CellToWorld(cell));
    }

    // Painting the same zone with another density (M23). A built block keeps its level and its capacity is re-fitted; one
    // that would hold fewer people or jobs waits for a confirmation when the drag ends.
    private void RepaintDensity(Vector2Int cell, Density density)
    {
        if (ShrinkOf(cell, density) > 0)
        {
            m_PendingRepaint[cell] = density;
            return;
        }
        m_GridData.SetDensity(cell, density);
        AudioController.Play(SfxId.ZonePaint, m_GridSystem.CellToWorld(cell));
    }

    // How many fewer residents / jobs the built block at the cell would hold at a density (0 when it grows or is empty).
    private int ShrinkOf(Vector2Int cell, Density density)
    {
        int level = m_GridData.GetBuildingLevel(cell);
        if (level <= 0) return 0;
        CapacityModel capacity = m_GameManager.Simulation.Capacity;
        int before = capacity.CapacityOf(m_GridData, cell);
        int after = capacity.Capacity(level, m_GridData.GetBuiltAge(cell), m_GridData.GetZone(cell), density);
        return Mathf.Max(0, before - after);
    }

    private void FlushPendingRepaint()
    {
        if (m_PendingRepaint.Count == 0 || m_InputReader.ConfirmHeld) return;
        var cells = new List<KeyValuePair<Vector2Int, Density>>(m_PendingRepaint);
        m_PendingRepaint.Clear();
        int homes = 0;
        int jobs = 0;
        foreach (KeyValuePair<Vector2Int, Density> pair in cells)
        {
            int shrink = ShrinkOf(pair.Key, pair.Value);
            if (m_GridData.GetZone(pair.Key) == ZoneType.Residential) homes += shrink;
            else jobs += shrink;
        }
        void Apply()
        {
            foreach (KeyValuePair<Vector2Int, Density> pair in cells)
            {
                if (m_GridData.InBounds(pair.Key) && m_GridData.GetZone(pair.Key) != ZoneType.None) m_GridData.SetDensity(pair.Key, pair.Value);
            }
        }
        if (GameFlow.Instance == null)
        {
            Apply();
            return;
        }
        string what = homes > 0 && jobs > 0 ? $"{homes} residents and {jobs} jobs" : homes > 0 ? $"{homes} residents" : $"{jobs} jobs";
        string blocks = cells.Count == 1 ? "1 built block" : $"{cells.Count} built blocks";
        GameFlow.Instance.Confirm.Ask($"Change the density of {blocks}?", $"They keep their level but hold {what} fewer; the surplus move out or lose work.",
            new ConfirmDialog.Choice("Change density", Apply, primary: true),
            new ConfirmDialog.Choice("Cancel", null));
    }

    // The avenue pair for a cell: the other lane's side from the heading and the R flip (M22).
    // The chosen side while it is square to the heading; a drag along the chosen side's axis puts the lane on its right.
    private byte AvenueSide(byte heading) =>
        RoadLayout.IsVertical(m_AvenueSide) != RoadLayout.IsVertical(heading) ? m_AvenueSide : RoadLayout.RightOf(heading);

    // Avenue drag: the heading comes from the step between two painted cells, so the first cell waits for the second
    // (or for the button to be released, FlushPendingLane); every later cell lays itself and its other lane.
    private void TryPlaceAvenue(Vector2Int cell)
    {
        if (!m_DragLast.HasValue)
        {
            m_DragLast = cell;
            m_PendingLane = cell;
            return;
        }
        if (cell == m_DragLast.Value) return;
        byte heading = RoadLayout.HeadingOf(m_DragLast.Value, cell);
        m_AvenueHeading = heading;
        m_DragLast = cell;
        if (m_PendingLane.HasValue)
        {
            LayAvenuePair(m_PendingLane.Value);
            m_PendingLane = null;
        }
        LayAvenuePair(cell);
    }

    // A click or a drag released before moving: lay the waiting first cell along the last heading.
    private void FlushPendingLane()
    {
        if (!m_PendingLane.HasValue || m_InputReader.ConfirmHeld) return;
        Vector2Int cell = m_PendingLane.Value;
        m_PendingLane = null;
        if (m_Mode == Mode.Road && m_RoadTier == RoadTiers.Avenue) LayAvenuePair(cell);
    }

    private int LaneCost(RoadTiers tiers, Vector2Int cell, byte tier)
    {
        byte existing = m_GridData.GetRoadTier(cell);
        return existing == 0 ? tiers.Cost(tier) : existing < tier ? tiers.UpgradeCost(existing, tier) : 0;
    }

    private int PairCost(Vector2Int cell, byte side)
    {
        RoadTiers tiers = m_GameManager.Simulation.RoadTiers;
        if (m_GridData.GetRoadPair(cell) == side) return 0;
        return LaneCost(tiers, cell, RoadTiers.Avenue) + LaneCost(tiers, cell + RoadLayout.Offset(side), RoadTiers.Avenue);
    }

    // Lays the cell and its other lane as one paired Avenue, paying for both (new land at the full price, a street
    // upgraded for the difference). Refused as a whole when either lane cannot be one.
    private void LayAvenuePair(Vector2Int cell)
    {
        byte side = AvenueSide(m_AvenueHeading);
        if (RoadLayout.CheckPair(m_GridData, cell, side, RoadTiers.Avenue) != RoadLayout.LaneProblem.None) return;
        Vector2Int partner = cell + RoadLayout.Offset(side);
        if (m_GridData.GetRoadPair(cell) == side) return;       // already this pair
        if (RoadLayout.IsCrossing(m_GridData, cell, side))
        {
            RoadLayout.MakeJunction(m_GridData, cell, side);    // across an avenue: an open junction, nothing to pay
            AudioController.Play(SfxId.RoadLay, m_GridSystem.CellToWorld(cell));
            return;
        }
        bool pairIt = m_GridData.GetRoadTier(cell) != RoadTiers.Avenue && m_GridData.GetRoadTier(partner) != RoadTiers.Avenue;
        int cost = PairCost(cell, side);
        if (!m_GameManager.Economy.Spend(cost))
        {
            if (!m_PipeFundsWarned) GameEvents.RaiseInsufficientFunds(cost);
            m_PipeFundsWarned = true;
            return;
        }
        bool upgraded = m_GridData.IsRoad(cell) || m_GridData.IsRoad(partner);
        PlaceRoad(cell, RoadTiers.Avenue);
        PlaceRoad(partner, RoadTiers.Avenue);
        if (pairIt) m_GridData.SetRoadPair(cell, side);         // avenue cells already there (a junction, an old single lane) keep theirs
        ReportSpent(cost, cell);
        AudioController.Play(upgraded ? SfxId.RoadUpgrade : SfxId.RoadLay, m_GridSystem.CellToWorld(cell));
    }

    private void TryPlaceRoad(Vector2Int cell)
    {
        if (ActiveRoadTier == RoadTiers.Avenue)
        {
            TryPlaceAvenue(cell);
            return;
        }
        RoadTiers tiers = m_GameManager.Simulation.RoadTiers;
        byte tier = ActiveRoadTier;
        byte existing = m_GridData.GetRoadTier(cell);
        if (tier == RoadTiers.Highway) FollowHighwayDrag(cell);
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
        if (tier == RoadTiers.Highway)
        {
            m_FreshHighway.Add(cell);
            if (!m_HighwayTwoWay && m_HighwayHeading != RoadLayout.None) m_GridData.SetRoadDirection(cell, m_HighwayHeading);
        }
        ReportSpent(cost, cell);
        AudioController.Play(existing != 0 ? SfxId.RoadUpgrade : SfxId.RoadLay, m_GridSystem.CellToWorld(cell));
    }

    // A Highway drag is one-way along its heading (M22): the heading is the step between two painted cells, so the
    // first cell takes it once the second shows it. Only the cells this drag laid take the direction; a highway that
    // was already there keeps its own (Reverse / Make two-way live in the selection panel).
    private void FollowHighwayDrag(Vector2Int cell)
    {
        if (m_DragLast.HasValue && m_DragLast.Value != cell)
        {
            m_HighwayHeading = RoadLayout.HeadingOf(m_DragLast.Value, cell);
            Vector2Int last = m_DragLast.Value;
            if (!m_HighwayTwoWay && m_FreshHighway.Contains(last) && m_GridData.GetRoadTier(last) == RoadTiers.Highway
                && m_GridData.GetRoadDirection(last) == RoadLayout.None)
            {
                m_GridData.SetRoadDirection(last, m_HighwayHeading);
            }
        }
        m_DragLast = cell;
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

    // deferBuildings (a demolish drag): a placed building is only collected, for FlushPendingDemolish on release.
    public void DemolishAt(Vector2Int cell, bool deferBuildings = false)
    {
        if (!m_GridData.InBounds(cell)) return;

        if (m_GridData.IsRoad(cell))
        {
            // One lane of a paired avenue takes the other with it (M22).
            if (RoadLayout.TryGetPartner(m_GridData, cell, out Vector2Int partner)) m_GridData.SetRoad(partner, false);
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
        if (deferBuildings)
        {
            if (!m_PendingDemolish.Contains(occupantId)) m_PendingDemolish.Add(occupantId);
            return;
        }
        if (GameSettings.ConfirmDemolish && GameFlow.Instance != null)
        {
            int occupant = occupantId;
            GameFlow.Instance.Confirm.Ask($"Demolish {instance.Definition.DisplayName}?", "It is removed with no refund.",
                new ConfirmDialog.Choice("Demolish", () => RemoveBuilding(occupant), primary: true),
                new ConfirmDialog.Choice("Cancel", null));
            return;
        }

        RemoveBuilding(occupantId);
    }

    private void RemoveBuilding(int occupantId)
    {
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
        if (m_LineStart.HasValue)
        {
            UpdateLineGhost();
            return;
        }
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
        else if (m_Mode == Mode.Road && ActiveRoadTier == RoadTiers.Avenue)
        {
            // The pair: the cell and its other lane.
            Vector2Int offset = RoadLayout.Offset(AvenueSide(m_AvenueHeading));
            Vector3 centre = m_GridSystem.CellToWorld(cell) + new Vector3(offset.x, 0f, offset.y) * 0.5f;
            m_Ghost.Show(centre, new Vector2Int(Mathf.Abs(offset.x) + 1, Mathf.Abs(offset.y) + 1), CursorHintValid);
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
        CursorHint = KeyBindings.Fill(text);
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
                if (tier == RoadTiers.Highway) note += m_HighwayTwoWay ? "  two-way  [R] one-way" : "  one-way along the drag  [R] two-way";
                if (tier == RoadTiers.Avenue)
                {
                    AvenueHint(cell, tiers, economy);
                    break;
                }
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
                int pending = m_PendingDemolish.Count;
                string onRelease = pending == 0 ? string.Empty : $"{pending} building{(pending == 1 ? "" : "s")} on release";
                string release = pending == 0 ? string.Empty : "  — " + onRelease;
                if (building != null && m_PendingDemolish.Contains(building.OccupantId)) SetHint($"{building.Definition.DisplayName} goes on release{(pending > 1 ? $" ({pending} buildings)" : "")}", true);
                else if (m_GridData.IsRoad(cell)) SetHint("Demolish road — hold to clear a path" + release, true);
                else if (building != null) SetHint($"Demolish {building.Definition.DisplayName} (no refund)" + release, true);
                else if (m_GridData.GetBuildingLevel(cell) > 0) SetHint("Demolish grown building" + release, true);
                else SetHint(onRelease, pending > 0);   // nothing here: red ghost, no label
                break;
            }

            case Mode.Pipe:
            {
                int cost = m_GameManager.Balance.PipeCost;
                if (m_GridData.IsPipe(cell)) SetHint("Remove pipe — drag a line to remove pipes", true);
                else if (m_GridData.IsRoad(cell)) SetHint("Roads carry water already", false);
                else if (!economy.CanAfford(cost)) SetHint($"Need ${cost:N0}", false);
                else SetHint($"Pipe  ${cost:N0}  — drag a line", true);
                break;
            }

            case Mode.Zone:
                if (m_GridData.IsRoad(cell)) SetHint("Can't zone a road", false);
                else if (m_GridData.IsOccupied(cell)) SetHint("Can't zone under a building", false);
                else if (m_ZoneBrush != ZoneType.None && DensityBrush != Density.Medium)
                {
                    Density brush = DensityBrush;
                    int shrink = m_GridData.GetZone(cell) == m_ZoneBrush ? ShrinkOf(cell, brush) : 0;
                    string note = shrink > 0 ? $"  — {shrink} fewer {(m_ZoneBrush == ZoneType.Residential ? "residents" : "jobs")}, asks first" : string.Empty;
                    SetHint($"{m_ZoneBrush} — {DensityUtils.Name(brush)} density{note}", true);
                }
                break;
        }
    }

    // What laying one cell of a line would do, without doing it (the same rules as TryPlaceRoad / LayAvenuePair /
    // TryPipe): false when the cell is blocked; cost 0 when there is nothing to pay (that road or better already there,
    // a free avenue junction, a pipe already laid or a road, which carries water anyway).
    private bool LineCellPlan(Vector2Int cell, byte heading, bool erasing, out int cost)
    {
        cost = 0;
        if (m_Mode == Mode.Pipe)
        {
            if (!erasing && !m_GridData.IsRoad(cell) && !m_GridData.IsPipe(cell)) cost = m_GameManager.Balance.PipeCost;
            return true;
        }
        RoadTiers tiers = m_GameManager.Simulation.RoadTiers;
        byte tier = ActiveRoadTier;
        if (tier == RoadTiers.Avenue)
        {
            byte side = AvenueSide(heading);
            if (RoadLayout.CheckPair(m_GridData, cell, side, RoadTiers.Avenue) != RoadLayout.LaneProblem.None) return false;
            if (m_GridData.GetRoadPair(cell) == side || RoadLayout.IsCrossing(m_GridData, cell, side)) return true;
            cost = PairCost(cell, side);
            return true;
        }
        byte existing = m_GridData.GetRoadTier(cell);
        if (existing != 0)
        {
            cost = existing < tier ? tiers.UpgradeCost(existing, tier) : 0;
            return true;
        }
        if (!m_GridData.CanPlace(cell, Vector2Int.one, 0)) return false;
        cost = tiers.Cost(tier);
        return true;
    }

    // The line being dragged: one rectangle over the run (two cells wide for an Avenue), the total and what is in the way.
    private void UpdateLineGhost()
    {
        Vector2Int start = m_LineStart.Value;
        int length = RoadLayout.StraightRun(m_GridData, start, m_LineEnd, out byte heading);
        bool avenue = m_Mode == Mode.Road && ActiveRoadTier == RoadTiers.Avenue;
        byte laneHeading = heading != RoadLayout.None ? heading : m_AvenueHeading;
        bool erasing = m_Mode == Mode.Pipe && m_GridData.IsPipe(start);

        int total = 0, blocked = 0, work = 0;
        foreach (Vector2Int cell in RoadLayout.RunCells(start, heading, length))
        {
            if (erasing)
            {
                if (m_GridData.IsPipe(cell)) work++;
                continue;
            }
            if (!LineCellPlan(cell, avenue ? laneHeading : heading, false, out int cost)) blocked++;
            else if (cost > 0) { total += cost; work++; }
        }
        LineHint(length + 1, total, blocked, work, erasing);

        if (m_Ghost == null) return;
        Vector2Int end = start + RoadLayout.Offset(heading) * length;
        Vector2Int min = Vector2Int.Min(start, end);
        Vector2Int max = Vector2Int.Max(start, end);
        if (avenue)
        {
            Vector2Int offset = RoadLayout.Offset(AvenueSide(laneHeading));
            min = Vector2Int.Min(min, min + offset);
            max = Vector2Int.Max(max, max + offset);
        }
        Vector3 centre = (m_GridSystem.CellToWorld(min) + m_GridSystem.CellToWorld(max)) * 0.5f;
        m_Ghost.Show(centre, max - min + Vector2Int.one, CursorHintValid);
    }

    // "Paved road ×12  $180", "… — $45 short", "(3 blocked)"; red when nothing would be built or it is unaffordable.
    private void LineHint(int cells, int total, int blocked, int work, bool erasing)
    {
        if (erasing)
        {
            SetHint(work > 0 ? $"Remove {work} pipe{(work == 1 ? "" : "s")}" : "No pipes to remove", work > 0);
            return;
        }
        RoadTiers tiers = m_GameManager.Simulation.RoadTiers;
        byte tier = ActiveRoadTier;
        string name = m_Mode == Mode.Pipe ? "Pipe" : tiers.DisplayName(tier);
        string note = string.Empty;
        if (m_Mode == Mode.Road && tier == RoadTiers.Highway) note = m_HighwayTwoWay ? "  two-way" : "  one-way along the drag";
        if (blocked > 0) note += $"  ({blocked} blocked)";
        if (work == 0)
        {
            SetHint(blocked == cells ? "Blocked" : $"Already {(m_Mode == Mode.Pipe ? "laid" : name)}", false);
            return;
        }
        float money = m_GameManager.Economy.Money;
        if (total > money) SetHint($"{name} ×{cells}  ${total:N0} — ${total - money:N0} short{note}", false);
        else SetHint($"{name} ×{cells}  ${total:N0}{note}", true);
    }

    // The Avenue tool's hint: the pair's price, or why the two lanes do not fit.
    private void AvenueHint(Vector2Int cell, RoadTiers tiers, EconomySystem economy)
    {
        byte side = AvenueSide(m_AvenueHeading);
        switch (RoadLayout.CheckPair(m_GridData, cell, side, RoadTiers.Avenue))
        {
            case RoadLayout.LaneProblem.OffMap: SetHint("Needs two tiles: off the map  [R] rotate", false); return;
            case RoadLayout.LaneProblem.Blocked: SetHint("Needs the lane beside it free: blocked  [R] rotate", false); return;
            case RoadLayout.LaneProblem.Taken: SetHint("The lane beside it belongs to another avenue  [R] rotate", false); return;
            case RoadLayout.LaneProblem.Higher: SetHint("A highway is in the way", false); return;
        }
        if (m_GridData.GetRoadPair(cell) == side) { SetHint("Already an Avenue", false); return; }
        if (RoadLayout.IsCrossing(m_GridData, cell, side)) { SetHint("Junction with the avenue (free)  [R] rotate", true); return; }
        int cost = PairCost(cell, side);
        if (!economy.CanAfford(cost)) SetHint($"Need ${cost:N0}", false);
        else SetHint($"{tiers.DisplayName(RoadTiers.Avenue)} (2 tiles wide) ${cost:N0}  [R] rotate", true);
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
