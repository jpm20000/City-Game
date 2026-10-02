using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shows what's on the selected cell (placed building, road, grown cell, or zoned land) and why it
// isn't growing. Refreshes immediately on selection; cell and day changes (a growth tick can change
// dozens of cells) are batched into at most one refresh per frame.
public sealed class SelectionPanel : MonoBehaviour
{
    private enum Action { None, Demolish, Unzone }

    [SerializeField] private PlacementController m_Placement;
    [SerializeField] private GameManager m_GameManager;
    [SerializeField] private GameObject m_Root;
    [SerializeField] private TMP_Text m_Title;
    [SerializeField] private TMP_Text m_Body;
    [SerializeField] private Button m_ActionButton;
    [SerializeField] private TMP_Text m_ActionLabel;
    [SerializeField] private Button m_CloseButton;

    private readonly StringBuilder m_Text = new();
    private Action m_Action;
    private bool m_Dirty;

    private void Start()
    {
        if (m_Placement == null || m_GameManager == null) return;

        m_Placement.SelectionChanged += Refresh;
        GameEvents.CellChanged += OnCellChanged;
        GameEvents.DateChanged += OnDateChanged;
        if (m_ActionButton != null) m_ActionButton.onClick.AddListener(OnAction);
        if (m_CloseButton != null) m_CloseButton.onClick.AddListener(m_Placement.ClearSelection);
        Refresh();
    }

    private void OnDestroy()
    {
        if (m_Placement != null) m_Placement.SelectionChanged -= Refresh;
        GameEvents.CellChanged -= OnCellChanged;
        GameEvents.DateChanged -= OnDateChanged;
    }

    private void OnCellChanged(Vector2Int cell)
    {
        m_Dirty = true;
    }

    private void OnDateChanged(int day, int month, int year)
    {
        m_Dirty = true;
    }

    private void LateUpdate()
    {
        if (!m_Dirty) return;
        m_Dirty = false;
        if (m_Placement.HasSelection) Refresh();
    }

    private void OnAction()
    {
        if (!m_Placement.HasSelection) return;

        Vector2Int cell = m_Placement.SelectedCell;
        if (m_Action == Action.Demolish) m_Placement.DemolishAt(cell);
        else if (m_Action == Action.Unzone) m_Placement.Unzone(cell);
    }

    private void Refresh()
    {
        if (!m_Placement.HasSelection)
        {
            m_Root.SetActive(false);
            return;
        }

        m_Root.SetActive(true);
        m_Text.Clear();
        Vector2Int cell = m_Placement.SelectedCell;
        GridData grid = m_GameManager.Grid;

        BuildingInstance building = m_Placement.GetBuildingAt(cell);
        if (building != null) DescribeBuilding(building);
        else if (grid.IsRoad(cell)) DescribeRoad(cell);
        else if (grid.GetBuildingLevel(cell) > 0) DescribeGrown(cell);
        else DescribeZone(cell);

        m_Body.text = m_Text.ToString().TrimEnd();
        if (m_ActionButton != null) m_ActionButton.gameObject.SetActive(m_Action != Action.None);
        if (m_ActionLabel != null) m_ActionLabel.text = m_Action == Action.Unzone ? "Unzone" : "Demolish";
    }

    private void DescribeBuilding(BuildingInstance building)
    {
        BuildingDefinition def = building.Definition;
        BalanceConfig balance = m_GameManager.Balance;
        m_Title.text = def.DisplayName;
        Line($"{def.Category}  ·  {def.Size.x}x{def.Size.y}");
        Line($"Upkeep  ${def.UpkeepPerDay:N0} / day");
        if (def.HousingCapacity > 0) Line($"Housing  {def.HousingCapacity}");
        if (def.JobsProvided > 0) Line($"Jobs  {def.JobsProvided}");
        if (def.Category == BuildingCategory.Service)
        {
            Line($"Happiness  +{balance.ServiceBonusEach:P0} (services cap at +{balance.ServiceBonusCap:P0})");
        }
        m_Action = Action.Demolish;
    }

    private void DescribeRoad(Vector2Int cell)
    {
        bool connected = m_GameManager.Roads.IsConnectedToEntry(cell);
        m_Title.text = "Road";
        Line(connected
            ? "Connected to the map edge."
            : "<color=#F2665A>Not connected to the map edge</color> — nothing along it can grow.");
        Line($"Upkeep  ${m_GameManager.Balance.RoadUpkeepPerDay:N0} / day");
        m_Action = Action.Demolish;
    }

    private void DescribeGrown(Vector2Int cell)
    {
        GridData grid = m_GameManager.Grid;
        BalanceConfig balance = m_GameManager.Balance;
        ZoneType zone = grid.GetZone(cell);
        int level = grid.GetBuildingLevel(cell);
        string unit = zone == ZoneType.Residential ? "Homes" : "Jobs";

        m_Title.text = $"{ZoneName(zone)} building";
        Line($"Level {level} / {balance.MaxLevel}");
        Line($"{unit}  {balance.CapacityForLevel(level)}");
        if (level < balance.MaxLevel)
        {
            Line($"Next level: {unit.ToLowerInvariant()} {balance.CapacityForLevel(level + 1)}");
            Line(BlockerText(cell, zone, "Upgrade"));
        }
        else
        {
            Line("Fully grown.");
        }
        Line("<size=85%><color=#9AA3B2>Demolishing leaves the zone, so it will regrow.</color></size>");
        m_Action = Action.Demolish;
    }

    private void DescribeZone(Vector2Int cell)
    {
        ZoneType zone = m_GameManager.Grid.GetZone(cell);
        m_Title.text = $"{ZoneName(zone)} zone";
        Line("Undeveloped.");
        Line(BlockerText(cell, zone, "Growth"));
        m_Action = Action.Unzone;
    }

    private string BlockerText(Vector2Int cell, ZoneType zone, string verb)
    {
        DemandSnapshot demand = m_GameManager.Demand.Snapshot;
        switch (m_GameManager.Simulation.Growth.GetBlocker(cell, demand))
        {
            case GrowthBlocker.None:
                return $"<color=#73D973>{verb} ready</color> — happens when today's growth budget reaches it.";
            case GrowthBlocker.NoRoadAccess:
                return $"<color=#F2665A>{verb} blocked:</color> needs an adjacent road connected to the map edge.";
            case GrowthBlocker.LowDemand:
                return $"<color=#F2C14E>{verb} waiting:</color> {ZoneName(zone).ToLowerInvariant()} demand is {demand.Get(zone):P0} " +
                       $"(needs over {m_GameManager.Balance.GrowthDemandThreshold:P0}).";
            case GrowthBlocker.NoPower:
                return $"<color=#F2665A>{verb} blocked:</color> needs power — connect a power plant to its road.";
            case GrowthBlocker.PowerAtCapacity:
                return $"<color=#F2C14E>{verb} waiting:</color> its power network is at capacity — build another plant.";
            case GrowthBlocker.Occupied:
                return $"<color=#F2665A>{verb} blocked:</color> a placed building occupies this cell.";
            default:
                return string.Empty;
        }
    }

    private void Line(string text)
    {
        if (!string.IsNullOrEmpty(text)) m_Text.AppendLine(text);
    }

    private static string ZoneName(ZoneType zone)
    {
        return zone == ZoneType.None ? "Unzoned" : zone.ToString();
    }
}
