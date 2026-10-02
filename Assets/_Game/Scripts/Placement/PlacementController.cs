using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Tilemaps;

public sealed class PlacementController : MonoBehaviour
{
    public enum Mode { None, Road, Building, Demolish, Zone }

    private const int k_BuildingsMask = 1 << 9;
    private const float k_RaycastHeight = 50f;
    private const float k_HighlightLift = 0.02f;

    [SerializeField] private InputReader m_InputReader;
    [SerializeField] private GridSystem m_GridSystem;
    [SerializeField] private GameManager m_GameManager;
    [SerializeField] private Tilemap m_RoadTilemap;
    [SerializeField] private TileBase m_RoadTile;
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
    private readonly Dictionary<int, BuildingInstance> m_Buildings = new();

    public Mode CurrentMode => m_Mode;
    public ZoneType ZoneBrush => m_ZoneBrush;
    public BuildingDefinition SelectedBuilding => m_Selected;

    // Raised on every tool change, including switching zone brush or building; read CurrentMode/ZoneBrush/SelectedBuilding.
    public event Action ModeChanged;

    // Inspection with no tool active. Raised when the selected cell changes or is cleared.
    public event Action SelectionChanged;
    public bool HasSelection => m_SelectedCell.HasValue;
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

    private void Update()
    {
        if (m_InputReader == null || m_GridData == null || m_GridSystem == null) return;

        HandleModeToggle();
        HandleRotation();
        HandleConfirm();
        UpdateGhost();
        UpdateSelection();
    }

    public void SelectBuilding(BuildingDefinition definition)
    {
        m_Selected = definition;
        m_Rotation = 0;
        SetMode(Mode.Building);
    }

    public void SelectRoad()
    {
        SetMode(Mode.Road);
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
        if (m_GridData.CanPlace(cell, Vector2Int.one, 0)) PlaceRoad(cell);
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
            SetMode(m_Mode == Mode.Road ? Mode.None : Mode.Road);
        }
        else if (m_InputReader.DemolishPressed)
        {
            SetMode(m_Mode == Mode.Demolish ? Mode.None : Mode.Demolish);
        }
        else if (m_InputReader.CancelPressed)
        {
            SetMode(Mode.None);
            ClearSelection();
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

        // Zoning paints while the button is held; other tools act once per click.
        bool active = m_Mode == Mode.Zone ? m_InputReader.ConfirmHeld : m_InputReader.ConfirmPressed;
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
        }
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
        if (!CanZone(cell) || m_GridData.GetZone(cell) == m_ZoneBrush) return;

        // Rezoning or unzoning bulldozes whatever had grown there.
        m_GridData.SetBuildingLevel(cell, 0);
        m_GridData.SetZone(cell, m_ZoneBrush);
    }

    private void TryPlaceRoad(Vector2Int cell)
    {
        if (!m_GridData.CanPlace(cell, Vector2Int.one, 0)) return;
        if (!TrySpend(m_GameManager.Balance.RoadCost)) return;

        PlaceRoad(cell);
    }

    private void PlaceRoad(Vector2Int cell)
    {
        m_GridData.SetZone(cell, ZoneType.None);
        m_GridData.SetRoad(cell, true);
        if (m_RoadTilemap != null && m_RoadTile != null)
        {
            m_RoadTilemap.SetTile(m_GridSystem.LogicalToTileCell(cell), m_RoadTile);
        }
    }

    private void TryPlaceBuilding(Vector2Int cell)
    {
        if (m_Selected == null || m_Selected.Prefab == null) return;
        if (!m_GridData.CanPlace(cell, m_Selected.Size, m_Rotation)) return;
        if (!m_GameManager.Economy.CanAfford(m_Selected.Cost))
        {
            GameEvents.RaiseInsufficientFunds(m_Selected.Cost);
            return;
        }

        Transform parent = m_BuildingsContainer != null ? m_BuildingsContainer : transform;
        GameObject go = Instantiate(m_Selected.Prefab, parent);
        BuildingInstance instance = go.GetComponent<BuildingInstance>();
        if (instance == null)
        {
            Destroy(go);
            return;
        }

        if (!instance.Init(m_GridData, m_Selected, cell, m_Rotation))
        {
            Destroy(go);
            return;
        }

        m_GameManager.Economy.Spend(m_Selected.Cost);
        foreach (Vector2Int footprintCell in m_GridData.GetFootprint(cell, m_Selected.Size, m_Rotation))
        {
            m_GridData.SetZone(footprintCell, ZoneType.None);
        }
        m_Buildings[instance.OccupantId] = instance;
        m_GameManager.RegisterBuilding(instance);
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
                || m_GridData.GetZone(cell) != ZoneType.None);
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
            size = EffectiveSize(building.Definition.Size, building.Rotation);
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
            if (m_RoadTilemap != null)
            {
                m_RoadTilemap.SetTile(m_GridSystem.LogicalToTileCell(cell), null);
            }
            return;
        }

        if (m_GridData.GetBuildingLevel(cell) > 0)
        {
            m_GridData.SetBuildingLevel(cell, 0);
            return;
        }

        if (!m_GridData.IsOccupied(cell)) return;

        int occupantId = m_GridData.GetOccupant(cell);
        if (!m_Buildings.TryGetValue(occupantId, out BuildingInstance instance)) return;

        m_Buildings.Remove(occupantId);
        m_GameManager.UnregisterBuilding(instance);
        instance.Demolish();
        Destroy(instance.gameObject);
    }

    private void UpdateGhost()
    {
        if (m_Ghost == null) return;

        if (m_Mode == Mode.None)
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

        switch (m_Mode)
        {
            case Mode.Road:
                m_Ghost.Show(
                    m_GridSystem.CellToWorld(cell),
                    Vector2Int.one,
                    m_GridData.CanPlace(cell, Vector2Int.one, 0)
                        && m_GameManager.Economy.CanAfford(m_GameManager.Balance.RoadCost));
                break;

            case Mode.Building:
                if (m_Selected == null)
                {
                    m_Ghost.Hide();
                    break;
                }
                m_Ghost.Show(
                    FootprintCenter(cell, m_Selected.Size, m_Rotation),
                    EffectiveSize(m_Selected.Size, m_Rotation),
                    m_GridData.CanPlace(cell, m_Selected.Size, m_Rotation)
                        && m_GameManager.Economy.CanAfford(m_Selected.Cost));
                break;

            case Mode.Demolish:
                m_Ghost.Show(
                    m_GridSystem.CellToWorld(cell),
                    Vector2Int.one,
                    m_GridData.IsRoad(cell) || m_GridData.IsOccupied(cell)
                        || m_GridData.GetBuildingLevel(cell) > 0);
                break;

            case Mode.Zone:
                m_Ghost.Show(m_GridSystem.CellToWorld(cell), Vector2Int.one, CanZone(cell));
                break;
        }
    }

    private static Vector2Int EffectiveSize(Vector2Int size, int rotation)
    {
        return (rotation & 1) == 0 ? size : new Vector2Int(size.y, size.x);
    }

    private static Vector3 FootprintCenter(Vector2Int origin, Vector2Int size, int rotation)
    {
        Vector2Int effective = EffectiveSize(size, rotation);
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
