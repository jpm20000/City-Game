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

    [Header("Views")]
    [SerializeField] private InfoOverlay m_InfoOverlay;
    [SerializeField] private ToolButton m_PowerViewButton;
    [SerializeField] private ToolButton m_CoverageViewButton;
    [SerializeField] private ToolButton m_AgeViewButton;

    [Header("Buildings")]
    [SerializeField] private ToolButton m_ButtonTemplate;
    [SerializeField] private Transform m_BuildingsContainer;

    [Header("Tooltip")]
    [SerializeField] private GameObject m_TooltipRoot;
    [SerializeField] private TMP_Text m_TooltipText;

    private readonly Dictionary<BuildingDefinition, ToolButton> m_BuildingButtons = new();

    private void Start()
    {
        if (m_Placement == null || m_GameManager == null) return;

        int roadCost = m_GameManager.Balance.RoadCost;
        Bind(m_RoadButton, "Road", $"${roadCost}", Color.clear,
            $"Road  [B]\nLay road from the map edge. ${roadCost} each, $1/day upkeep.",
            () => Toggle(PlacementController.Mode.Road, m_Placement.SelectRoad));
        BindZone(m_ResidentialButton, "Residential", ZoneType.Residential, ZonePalette.Residential, "Homes grow here when residential demand is high.");
        BindZone(m_CommercialButton, "Commercial", ZoneType.Commercial, ZonePalette.Commercial, "Shops grow here, providing jobs.");
        BindZone(m_IndustrialButton, "Industrial", ZoneType.Industrial, ZonePalette.Industrial, "Factories grow here, providing jobs.");
        BindZone(m_UnzoneButton, "Unzone", ZoneType.None, Color.clear, "Remove zoning (and anything grown on it).");
        Bind(m_DemolishButton, "Demolish", string.Empty, Color.clear,
            "Demolish  [Del]\nRemove a road, building or grown cell. No refund.",
            () => Toggle(PlacementController.Mode.Demolish, m_Placement.SelectDemolish));

        BindView(m_PowerViewButton, "Power", InfoOverlay.View.Power,
            "Power view  [V]\n<color=#FFD133>Yellow</color> roads carry power from a plant. Buildings: <color=#59D966>powered</color> / <color=#F2554A>no power</color> (can't upgrade). Faint tints show zoned land that would / wouldn't get power.");
        BindView(m_CoverageViewButton, "Parks", InfoOverlay.View.Coverage,
            "Park coverage view  [V]\nGreener homes get more happiness from nearby parks (up to 4 parks count). Light grey homes have none; dark grey buildings are jobs, which parks don't affect.");
        BindView(m_AgeViewButton, "Ages", InfoOverlay.View.Age,
            "Age view  [V]\nThe age each building was built in: <color=#E6853A>orange</color> = oldest, <color=#5299F5>blue</color> = newest. Darker, striped = outdated (will be rebuilt). <color=#F2CC4D>Gold</color> = kept historic.");
        CreateBuildingButtons();

        m_Placement.ModeChanged += RefreshActive;
        if (m_InfoOverlay != null) m_InfoOverlay.ViewChanged += RefreshActive;
        GameEvents.MoneyChanged += RefreshAffordable;
        GameEvents.TechCompleted += OnTechCompleted;
        GameEvents.CityLoaded += RefreshUnlocked;
        HideTooltip(null);
        RefreshUnlocked();
        RefreshActive();
        RefreshAffordable(m_GameManager.Economy.Money);
    }

    private void OnDestroy()
    {
        if (m_Placement != null) m_Placement.ModeChanged -= RefreshActive;
        if (m_InfoOverlay != null) m_InfoOverlay.ViewChanged -= RefreshActive;
        GameEvents.MoneyChanged -= RefreshAffordable;
        GameEvents.TechCompleted -= OnTechCompleted;
        GameEvents.CityLoaded -= RefreshUnlocked;
    }

    private void OnTechCompleted(string techId) => RefreshUnlocked();

    // Locked buildings get no button; a tech unlocking one (or a load) shows it. Views that aren't
    // available yet (Power before Electricity, Age without age data) are hidden the same way.
    private void RefreshUnlocked()
    {
        if (m_InfoOverlay != null)
        {
            if (m_PowerViewButton != null) m_PowerViewButton.gameObject.SetActive(m_InfoOverlay.IsAvailable(InfoOverlay.View.Power));
            if (m_AgeViewButton != null) m_AgeViewButton.gameObject.SetActive(m_InfoOverlay.IsAvailable(InfoOverlay.View.Age));
        }
        bool any = false;
        foreach (KeyValuePair<BuildingDefinition, ToolButton> pair in m_BuildingButtons)
        {
            bool unlocked = m_GameManager.IsUnlocked(pair.Key);
            pair.Value.gameObject.SetActive(unlocked);
            any |= unlocked;
        }
        // The whole BUILDINGS section (header included) hides while nothing can be built.
        if (m_BuildingsContainer != null && m_BuildingsContainer.parent != null) m_BuildingsContainer.parent.gameObject.SetActive(any);
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
        if (def.PowerSupply > 0) text += $"\nPowers {def.PowerSupply} units along the roads it touches (L1/L2/L3 buildings use 4/8/16). Upgrades past level 1 need power.";
        if (def.CoverageRadius > 0) text += $"\nRaises happiness of homes within {def.CoverageRadius} cells.";
        if (def.ResearchPerDay > 0f) text += $"\nProduces {def.ResearchPerDay:0.#} research points a day.";
        text += "\n[R] rotates.";
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

    // Clicking the active view again turns the overlay off. Views don't clear the current tool.
    private void BindView(ToolButton button, string label, InfoOverlay.View view, string tooltip)
    {
        if (m_InfoOverlay == null) return;
        Bind(button, label, string.Empty, Color.clear, tooltip,
            () => m_InfoOverlay.SetView(m_InfoOverlay.Chosen == view ? InfoOverlay.View.Off : view));
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
        InfoOverlay.View view = m_InfoOverlay != null ? m_InfoOverlay.Shown : InfoOverlay.View.Off;
        SetActive(m_PowerViewButton, view == InfoOverlay.View.Power);
        SetActive(m_CoverageViewButton, view == InfoOverlay.View.Coverage);
        SetActive(m_AgeViewButton, view == InfoOverlay.View.Age);

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
