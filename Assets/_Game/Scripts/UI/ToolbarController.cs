using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Bottom build toolbar. Fixed tools are wired in the prefab; Service/Utility buildings are generated
// from the BuildingDatabase so new definitions appear without UI work.
public sealed class ToolbarController : MonoBehaviour
{
    [SerializeField] private PlacementController m_Placement;
    [SerializeField] private GameManager m_GameManager;

    [Header("Fixed tools")]
    [SerializeField] private ToolButton m_RoadButton;
    [SerializeField] private ToolButton m_ResidentialButton;
    [SerializeField] private ToolButton m_CommercialButton;
    [SerializeField] private ToolButton m_IndustrialButton;
    [SerializeField] private ToolButton m_UnzoneButton;
    [SerializeField] private ToolButton m_DemolishButton;

    [Header("Buildings")]
    [SerializeField] private ToolButton m_ButtonTemplate;
    [SerializeField] private Transform m_BuildingsContainer;

    [Header("Tooltip")]
    [SerializeField] private GameObject m_TooltipRoot;
    [SerializeField] private TMP_Text m_TooltipText;

    [Header("Zone colours")]
    [SerializeField] private Color m_ResidentialColor = new Color(0.40f, 0.85f, 0.35f);
    [SerializeField] private Color m_CommercialColor = new Color(0.30f, 0.55f, 0.95f);
    [SerializeField] private Color m_IndustrialColor = new Color(0.95f, 0.80f, 0.25f);

    private readonly Dictionary<BuildingDefinition, ToolButton> m_BuildingButtons = new();

    private void Start()
    {
        if (m_Placement == null || m_GameManager == null) return;

        int roadCost = m_GameManager.Balance.RoadCost;
        Bind(m_RoadButton, "Road", $"${roadCost}", Color.clear,
            $"Road  [B]\nLay road from the map edge. ${roadCost} each, $1/day upkeep.",
            () => Toggle(PlacementController.Mode.Road, m_Placement.SelectRoad));
        BindZone(m_ResidentialButton, "Residential", ZoneType.Residential, m_ResidentialColor, "Homes grow here when residential demand is high.");
        BindZone(m_CommercialButton, "Commercial", ZoneType.Commercial, m_CommercialColor, "Shops grow here, providing jobs.");
        BindZone(m_IndustrialButton, "Industrial", ZoneType.Industrial, m_IndustrialColor, "Factories grow here, providing jobs.");
        BindZone(m_UnzoneButton, "Unzone", ZoneType.None, Color.clear, "Remove zoning (and anything grown on it).");
        Bind(m_DemolishButton, "Demolish", string.Empty, Color.clear,
            "Demolish  [Del]\nRemove a road, building or grown cell. No refund.",
            () => Toggle(PlacementController.Mode.Demolish, m_Placement.SelectDemolish));

        CreateBuildingButtons();

        m_Placement.ModeChanged += RefreshActive;
        GameEvents.MoneyChanged += RefreshAffordable;
        HideTooltip(null);
        RefreshActive();
        RefreshAffordable(m_GameManager.Economy.Money);
    }

    private void OnDestroy()
    {
        if (m_Placement != null) m_Placement.ModeChanged -= RefreshActive;
        GameEvents.MoneyChanged -= RefreshAffordable;
    }

    private void CreateBuildingButtons()
    {
        if (m_ButtonTemplate == null || m_BuildingsContainer == null || m_GameManager.Buildings == null) return;

        foreach (BuildingDefinition def in m_GameManager.Buildings.Entries)
        {
            if (def == null) continue;
            if (def.Category != BuildingCategory.Service && def.Category != BuildingCategory.Utility) continue;

            ToolButton button = Instantiate(m_ButtonTemplate, m_BuildingsContainer);
            button.name = $"Build_{def.Id}";
            BuildingDefinition captured = def;
            Bind(button, def.DisplayName, $"${def.Cost:N0}", Color.clear, BuildingTooltip(def),
                () => ToggleBuilding(captured));
            m_BuildingButtons[def] = button;
        }
    }

    private static string BuildingTooltip(BuildingDefinition def)
    {
        string text = $"{def.DisplayName}  ({def.Size.x}x{def.Size.y})\n${def.Cost:N0} to build, ${def.UpkeepPerDay:N0}/day upkeep.";
        if (def.Category == BuildingCategory.Service) text += "\nRaises happiness. [R] rotates.";
        return text;
    }

    private void Bind(ToolButton button, string label, string cost, Color swatch, string tooltip, UnityEngine.Events.UnityAction onClick)
    {
        if (button == null) return;
        button.Setup(label, cost, swatch, tooltip);
        button.Button.onClick.AddListener(onClick);
        button.Hovered += ShowTooltip;
        button.Unhovered += HideTooltip;
    }

    private void BindZone(ToolButton button, string label, ZoneType zone, Color swatch, string tooltip)
    {
        Bind(button, label, string.Empty, swatch, $"{label}\n{tooltip} Click and drag to paint.", () =>
        {
            bool active = m_Placement.CurrentMode == PlacementController.Mode.Zone && m_Placement.ZoneBrush == zone;
            if (active) m_Placement.ClearMode();
            else m_Placement.SelectZone(zone);
        });
    }

    // Clicking the active tool again turns it off.
    private void Toggle(PlacementController.Mode mode, System.Action select)
    {
        if (m_Placement.CurrentMode == mode) m_Placement.ClearMode();
        else select();
    }

    private void ToggleBuilding(BuildingDefinition def)
    {
        if (m_Placement.CurrentMode == PlacementController.Mode.Building && m_Placement.SelectedBuilding == def)
        {
            m_Placement.ClearMode();
        }
        else
        {
            m_Placement.SelectBuilding(def);
        }
    }

    private void RefreshActive()
    {
        PlacementController.Mode mode = m_Placement.CurrentMode;
        bool zoning = mode == PlacementController.Mode.Zone;
        ZoneType brush = m_Placement.ZoneBrush;

        SetActive(m_RoadButton, mode == PlacementController.Mode.Road);
        SetActive(m_ResidentialButton, zoning && brush == ZoneType.Residential);
        SetActive(m_CommercialButton, zoning && brush == ZoneType.Commercial);
        SetActive(m_IndustrialButton, zoning && brush == ZoneType.Industrial);
        SetActive(m_UnzoneButton, zoning && brush == ZoneType.None);
        SetActive(m_DemolishButton, mode == PlacementController.Mode.Demolish);

        foreach (KeyValuePair<BuildingDefinition, ToolButton> pair in m_BuildingButtons)
        {
            pair.Value.SetActive(mode == PlacementController.Mode.Building && m_Placement.SelectedBuilding == pair.Key);
        }
    }

    private void RefreshAffordable(float money)
    {
        if (m_RoadButton != null) m_RoadButton.SetAffordable(money >= m_GameManager.Balance.RoadCost);
        foreach (KeyValuePair<BuildingDefinition, ToolButton> pair in m_BuildingButtons)
        {
            pair.Value.SetAffordable(money >= pair.Key.Cost);
        }
    }

    private static void SetActive(ToolButton button, bool active)
    {
        if (button != null) button.SetActive(active);
    }

    private void ShowTooltip(ToolButton button)
    {
        if (m_TooltipRoot == null || string.IsNullOrEmpty(button.Tooltip)) return;
        m_TooltipText.text = button.Tooltip;
        m_TooltipRoot.SetActive(true);
    }

    private void HideTooltip(ToolButton button)
    {
        if (m_TooltipRoot != null) m_TooltipRoot.SetActive(false);
    }
}
