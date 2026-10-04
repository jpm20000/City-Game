using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Bottom build toolbar. Fixed tools are wired in the prefab; Service/Utility buildings are generated
// from the BuildingDatabase so new definitions appear without UI work.
public sealed class ToolbarController : MonoBehaviour
{
    [SerializeField] private PlacementController m_Placement;
    [SerializeField] private GameManager m_GameManager;

    [Header("Fixed tools")]
    [SerializeField] private ToolButton m_RoadButton;
    [Tooltip("(M13) Water pipes; hidden until Waterworks.")]
    [SerializeField] private ToolButton m_PipeButton;
    [SerializeField] private ToolButton m_ResidentialButton;
    [SerializeField] private ToolButton m_CommercialButton;
    [SerializeField] private ToolButton m_IndustrialButton;
    [SerializeField] private ToolButton m_UnzoneButton;
    [SerializeField] private ToolButton m_DemolishButton;

    [Header("Views")]
    [SerializeField] private InfoOverlay m_InfoOverlay;
    [SerializeField] private ToolButton m_PowerViewButton;
    [SerializeField] private ToolButton m_WaterViewButton;
    [SerializeField] private ToolButton m_CoverageViewButton;
    [SerializeField] private ToolButton m_AgeViewButton;
    [SerializeField] private ToolButton m_PollutionViewButton;
    [SerializeField] private ToolButton m_LandValueViewButton;

    [Header("Buildings")]
    [SerializeField] private ToolButton m_ButtonTemplate;
    [SerializeField] private Transform m_BuildingsContainer;
    [Tooltip("Width of a building button while at most FullWidthBuildings are shown (the template's width).")]
    [SerializeField] private float m_BuildingButtonWidth = 104f;
    [SerializeField] private float m_MinBuildingButtonWidth = 80f;
    [SerializeField] private int m_FullWidthBuildings = 5;
    [Tooltip("(M13) Space kept free at each screen edge before the toolbar scales down to fit.")]
    [SerializeField] private float m_ScreenMargin = 8f;

    [Header("Tooltip")]
    [SerializeField] private GameObject m_TooltipRoot;
    [SerializeField] private TMP_Text m_TooltipText;

    private readonly Dictionary<BuildingDefinition, ToolButton> m_BuildingButtons = new();
    private ToolButton m_ServicesViewButton;   // M14: made at runtime from the Age view button
    private ToolButton m_AvenueButton;         // M16: made at runtime from the Road button
    private ToolButton m_HighwayButton;
    private ToolButton m_TrafficViewButton;    // M16: made at runtime from the Value view button
    private float m_FittedWidth = -1f;

    // M13: the toolbar keeps gaining buttons; when it is wider than the screen (minus a margin) it
    // scales down to fit instead of running off both edges.
    private void LateUpdate()
    {
        var rect = (RectTransform)transform;
        var parent = rect.parent as RectTransform;
        if (parent == null) return;
        float width = rect.rect.width;
        if (Mathf.Approximately(width, m_FittedWidth)) return;
        m_FittedWidth = width;
        float available = parent.rect.width - 2f * m_ScreenMargin;
        float scale = width > available && width > 0f ? available / width : 1f;
        rect.localScale = new Vector3(scale, scale, 1f);
    }

    private void Start()
    {
        if (m_Placement == null || m_GameManager == null) return;

        CreateRoadButtons();
        Bind(m_RoadButton, string.Empty, string.Empty, Color.clear, string.Empty, () => ToggleRoad(0));
        Bind(m_AvenueButton, string.Empty, string.Empty, Color.clear, string.Empty, () => ToggleRoad(RoadTiers.Avenue));
        Bind(m_HighwayButton, string.Empty, string.Empty, Color.clear, string.Empty, () => ToggleRoad(RoadTiers.Highway));
        RefreshRoadButtons();
        int pipeCost = m_GameManager.Balance.PipeCost;
        Bind(m_PipeButton, "Pipes", $"${pipeCost}", Color.clear,
            $"Water pipes  [P]\nDrag to lay pipes under any land but roads (${pipeCost} each). Roads carry water already; pipes reach a tower off the road network, join two road networks or feed the building above them. Drag from a pipe to remove pipes.",
            () => Toggle(PlacementController.Mode.Pipe, m_Placement.SelectPipe));
        BindZone(m_ResidentialButton, "Residential", ZoneType.Residential, ZonePalette.Residential, "Homes grow here when residential demand is high.");
        BindZone(m_CommercialButton, "Commercial", ZoneType.Commercial, ZonePalette.Commercial, "Shops grow here, providing jobs.");
        BindZone(m_IndustrialButton, "Industrial", ZoneType.Industrial, ZonePalette.Industrial, "Factories grow here, providing jobs.");
        BindZone(m_UnzoneButton, "Unzone", ZoneType.None, Color.clear, "Remove zoning (and anything grown on it).");
        Bind(m_DemolishButton, "Demolish", string.Empty, Color.clear,
            "Demolish  [Del]\nRemove a road, building or grown cell. No refund.",
            () => Toggle(PlacementController.Mode.Demolish, m_Placement.SelectDemolish));

        BindView(m_PowerViewButton, "Power", InfoOverlay.View.Power,
            "Power view  [V]\n<color=#FFD133>Yellow</color> roads carry power from a plant. Buildings: <color=#59D966>powered</color> / <color=#F2554A>no power</color> (can't upgrade). Faint tints show zoned land that would / wouldn't get power.");
        BindView(m_WaterViewButton, "Water", InfoOverlay.View.Water,
            "Water view  [V]\nWell ages: <color=#59A6F2>blue</color> land is in a well's reach. Piped ages: <color=#59A6F2>blue</color> roads carry water from a tower. Buildings: <color=#59A6F2>water</color> / <color=#F2554A>dry</color> (can't upgrade).");
        BindView(m_CoverageViewButton, "Parks", InfoOverlay.View.Coverage,
            "Park coverage view  [V]\nGreener homes get more happiness from nearby parks (up to 4 parks count). Light grey homes have none; dark grey buildings are jobs, which parks don't affect.");
        BindView(m_PollutionViewButton, "Pollution", InfoOverlay.View.Pollution,
            "Pollution view  [V]\n<color=#B07AA8>Purple</color> haze = pollution from industry and power plants; dark buildings are the polluters. Polluted homes are unhappier and lower land value.");
        BindView(m_LandValueViewButton, "Value", InfoOverlay.View.LandValue,
            "Land value view  [V]\n<color=#F2554A>Red</color> = low, <color=#59D966>green</color> = high. Parks and kept historic blocks raise it, pollution lowers it. Homes and shops need enough of it for level 3; darker, striped = held at level 2 by it.");
        CreateTrafficViewButton();
        CreateServicesViewButton();
        BindView(m_AgeViewButton, "Ages", InfoOverlay.View.Age,
            "Age view  [V]\nThe age each building was built in: <color=#E6853A>orange</color> = oldest, <color=#5299F5>blue</color> = newest. Darker, striped = outdated (will be rebuilt). <color=#F2CC4D>Gold</color> = kept historic.");
        CreateBuildingButtons();

        m_Placement.ModeChanged += RefreshActive;
        if (m_InfoOverlay != null) m_InfoOverlay.ViewChanged += RefreshActive;
        GameEvents.MoneyChanged += RefreshAffordable;
        GameEvents.TechCompleted += OnTechCompleted;
        GameEvents.AgeChanged += OnAgeChanged;
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
        GameEvents.AgeChanged -= OnAgeChanged;
        GameEvents.CityLoaded -= RefreshUnlocked;
    }

    private void OnTechCompleted(string techId) => RefreshUnlocked();

    private void OnAgeChanged(int age) => RefreshUnlocked();

    // Locked buildings get no button; a tech unlocking one (or a load) shows it. Obsolete ones (M13:
    // wells once water is piped) lose theirs when the city enters that age. Views that aren't
    // available yet (Power before Electricity, Age without age data) are hidden the same way.
    private void RefreshUnlocked()
    {
        RefreshRoadButtons();
        RefreshActive();
        RefreshAffordable(m_GameManager.Economy.Money);
        if (m_InfoOverlay != null)
        {
            if (m_PowerViewButton != null) m_PowerViewButton.gameObject.SetActive(m_InfoOverlay.IsAvailable(InfoOverlay.View.Power));
            if (m_WaterViewButton != null) m_WaterViewButton.gameObject.SetActive(m_InfoOverlay.IsAvailable(InfoOverlay.View.Water));
            if (m_AgeViewButton != null) m_AgeViewButton.gameObject.SetActive(m_InfoOverlay.IsAvailable(InfoOverlay.View.Age));
            if (m_ServicesViewButton != null) m_ServicesViewButton.gameObject.SetActive(AnyCivicViewAvailable());
        }
        if (m_PipeButton != null) m_PipeButton.gameObject.SetActive(m_GameManager.PipesUnlocked);
        bool any = false;
        int visible = 0;
        foreach (KeyValuePair<BuildingDefinition, ToolButton> pair in m_BuildingButtons)
        {
            bool unlocked = m_GameManager.CanBuild(pair.Key);
            pair.Value.gameObject.SetActive(unlocked);
            any |= unlocked;
            if (unlocked) visible++;
        }
        FitBuildingButtons(visible);
        // The whole BUILDINGS section (header included) hides while nothing can be built.
        if (m_BuildingsContainer != null && m_BuildingsContainer.parent != null) m_BuildingsContainer.parent.gameObject.SetActive(any);
    }

    // Building buttons narrow as more unlock (M13: seven in the Industrial age) so the toolbar still
    // fits 1920 px; their labels shrink to fit.
    private void FitBuildingButtons(int visible)
    {
        float width = visible <= m_FullWidthBuildings ? m_BuildingButtonWidth
            : Mathf.Max(m_MinBuildingButtonWidth, m_BuildingButtonWidth - (visible - m_FullWidthBuildings) * 12f);
        foreach (ToolButton button in m_BuildingButtons.Values)
        {
            var layout = button.GetComponent<LayoutElement>();
            if (layout != null) layout.preferredWidth = width;
        }
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
            if (button.Label != null)
            {
                button.Label.enableAutoSizing = true;
                button.Label.fontSizeMax = button.Label.fontSize;
                button.Label.fontSizeMin = 11f;
            }
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
        if (def.WaterRadius > 0) text += $"\nWaters blocks within {def.WaterRadius} cells (Medieval and Renaissance). Upgrades past level 1 need water.";
        if (def.WaterSupply > 0) text += $"\nPumps {def.WaterSupply} units of water along the roads it touches (L1/L2/L3 buildings use 4/8/16). Upgrades past level 1 need water.";
        if (def.ResearchPerDay > 0f) text += $"\nProduces {def.ResearchPerDay:0.#} research points a day.";
        if (def.CivicKind != ServiceKind.None && def.CivicRadius > 0) text += $"\n{CivicTooltip(def.CivicKind)} within {def.CivicRadius} cells (strength {def.CivicStrength:P0}).";
        text += "\n[R] rotates.";
        return text;
    }

    private static string CivicTooltip(ServiceKind kind)
    {
        switch (kind)
        {
            case ServiceKind.Order: return "Keeps crime down (happier homes, higher land value)";
            case ServiceKind.Fire: return "Lowers fire risk";
            case ServiceKind.Health: return "Cares for the sick";
            default: return "Schools residents, who then produce research";
        }
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

    // M14: one VIEW button for the four civic views, so the view row only grows by one. It is a copy
    // of the Age view button (same look and layout), placed right after it.
    private void CreateServicesViewButton()
    {
        if (m_AgeViewButton == null || m_InfoOverlay == null) return;
        m_ServicesViewButton = Instantiate(m_AgeViewButton, m_AgeViewButton.transform.parent);
        m_ServicesViewButton.name = "ServicesView";
        m_ServicesViewButton.transform.SetSiblingIndex(m_AgeViewButton.transform.GetSiblingIndex() + 1);
        Bind(m_ServicesViewButton, "Services", string.Empty, Color.clear,
            "Services views  [V]\nClick again for the next one: <color=#5B8DEF>order</color> (crime), <color=#F2733F>fire</color> (fire risk), " +
            "<color=#59D966>health</color> (sickness), <color=#B07AD8>education</color> (schooling). The ground shows each service's reach; " +
            "buildings go from pale to <color=#E6382E>red</color> as the need grows. Striped = needs it but nothing reaches it.",
            CycleServicesView);
    }

    // Off / another view -> the first available civic view -> the next ... -> Off.
    private void CycleServicesView()
    {
        InfoOverlay.View[] views = InfoOverlay.CivicViews;
        int start = System.Array.IndexOf(views, m_InfoOverlay.Chosen);
        for (int i = start + 1; i < views.Length; i++)
        {
            if (!m_InfoOverlay.IsAvailable(views[i])) continue;
            m_InfoOverlay.SetView(views[i]);
            return;
        }
        m_InfoOverlay.SetView(InfoOverlay.View.Off);
    }

    private bool AnyCivicViewAvailable()
    {
        foreach (InfoOverlay.View view in InfoOverlay.CivicViews)
        {
            if (m_InfoOverlay.IsAvailable(view)) return true;
        }
        return false;
    }

    private static string CivicViewLabel(InfoOverlay.View view)
    {
        switch (view)
        {
            case InfoOverlay.View.Order: return "Crime";
            case InfoOverlay.View.Fire: return "Fire";
            case InfoOverlay.View.Health: return "Health";
            default: return "Schools";
        }
    }

    // M16: the Avenue and Highway buttons are copies of the Road button, hidden until their tech is
    // researched; the Road button itself follows the best unlocked street tier.
    private void CreateRoadButtons()
    {
        if (m_RoadButton == null) return;
        Transform parent = m_RoadButton.transform.parent;
        int index = m_RoadButton.transform.GetSiblingIndex();
        m_AvenueButton = Instantiate(m_RoadButton, parent);
        m_AvenueButton.name = "Road_avenue";
        m_AvenueButton.transform.SetSiblingIndex(index + 1);
        m_HighwayButton = Instantiate(m_RoadButton, parent);
        m_HighwayButton.name = "Road_highway";
        m_HighwayButton.transform.SetSiblingIndex(index + 2);
        foreach (ToolButton button in new[] { m_RoadButton, m_AvenueButton, m_HighwayButton })
        {
            if (button.Label == null) continue;
            button.Label.enableAutoSizing = true;
            button.Label.fontSizeMax = button.Label.fontSize;
            button.Label.fontSizeMin = 11f;
        }
    }

    private void RefreshRoadButtons()
    {
        RoadTiers tiers = m_GameManager.Simulation.RoadTiers;
        SetupRoadButton(m_RoadButton, tiers.BestStreetTier, "  [B]", "Lay it from the map edge. Drag a better tier over a road to upgrade it for the price difference.");
        SetupRoadButton(m_AvenueButton, RoadTiers.Avenue, string.Empty, "Wide, fast and busy-street proof. Drag over a jammed road to upgrade it; the Traffic view shows where it helps.");
        SetupRoadButton(m_HighwayButton, RoadTiers.Highway, string.Empty, "The biggest capacity, but no frontage: land beside a highway gets no road access, so connect it with streets.");
        if (m_AvenueButton != null) m_AvenueButton.gameObject.SetActive(tiers.IsUnlocked(RoadTiers.Avenue) && tiers.HasContent);
        if (m_HighwayButton != null) m_HighwayButton.gameObject.SetActive(tiers.IsUnlocked(RoadTiers.Highway) && tiers.HasContent);
    }

    private void SetupRoadButton(ToolButton button, byte tier, string key, string blurb)
    {
        if (button == null) return;
        RoadTiers tiers = m_GameManager.Simulation.RoadTiers;
        string name = tiers.DisplayName(tier);
        int cost = tiers.Cost(tier);
        string tooltip = $"{name}{key}\n{blurb}\n${cost:N0} each, ${tiers.UpkeepPerDay(tier):0.##}/day upkeep, " +
            $"carries {tiers.Capacity(tier):N0} trips a day.";
        button.Setup(name, $"${cost}", Color.clear, tooltip);
    }

    // Clicking the active road tool again turns it off; tier 0 is the street tool.
    private void ToggleRoad(byte tier)
    {
        if (m_Placement.CurrentMode == PlacementController.Mode.Road && m_Placement.RoadToolTier == tier) m_Placement.ClearMode();
        else m_Placement.SelectRoad(tier);
    }

    // M16: the Traffic VIEW button is a copy of the Value view button, right after it.
    private void CreateTrafficViewButton()
    {
        if (m_LandValueViewButton == null || m_InfoOverlay == null) return;
        m_TrafficViewButton = Instantiate(m_LandValueViewButton, m_LandValueViewButton.transform.parent);
        m_TrafficViewButton.name = "TrafficView";
        m_TrafficViewButton.transform.SetSiblingIndex(m_LandValueViewButton.transform.GetSiblingIndex() + 1);
        BindView(m_TrafficViewButton, "Traffic", InfoOverlay.View.Traffic,
            "Traffic view  [V]\nRoads go from <color=#59D966>green</color> (free-flowing) to <color=#F2554A>red</color> (carrying more trips than they hold). " +
            "Homes show how jammed their commute is. Upgrade the red roads: drag an Avenue over them.");
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

        bool roading = mode == PlacementController.Mode.Road;
        SetActive(m_RoadButton, roading && m_Placement.RoadToolTier == 0);
        SetActive(m_AvenueButton, roading && m_Placement.RoadToolTier == RoadTiers.Avenue);
        SetActive(m_HighwayButton, roading && m_Placement.RoadToolTier == RoadTiers.Highway);
        SetActive(m_PipeButton, mode == PlacementController.Mode.Pipe);
        SetActive(m_ResidentialButton, zoning && brush == ZoneType.Residential);
        SetActive(m_CommercialButton, zoning && brush == ZoneType.Commercial);
        SetActive(m_IndustrialButton, zoning && brush == ZoneType.Industrial);
        SetActive(m_UnzoneButton, zoning && brush == ZoneType.None);
        SetActive(m_DemolishButton, mode == PlacementController.Mode.Demolish);
        InfoOverlay.View view = m_InfoOverlay != null ? m_InfoOverlay.Shown : InfoOverlay.View.Off;
        SetActive(m_PowerViewButton, view == InfoOverlay.View.Power);
        SetActive(m_WaterViewButton, view == InfoOverlay.View.Water);
        SetActive(m_CoverageViewButton, view == InfoOverlay.View.Coverage);
        SetActive(m_AgeViewButton, view == InfoOverlay.View.Age);
        SetActive(m_PollutionViewButton, view == InfoOverlay.View.Pollution);
        SetActive(m_LandValueViewButton, view == InfoOverlay.View.LandValue);
        SetActive(m_TrafficViewButton, view == InfoOverlay.View.Traffic);
        if (m_ServicesViewButton != null)
        {
            bool civic = InfoOverlay.IsCivic(view);
            SetActive(m_ServicesViewButton, civic);
            if (m_ServicesViewButton.Label != null) m_ServicesViewButton.Label.text = civic ? CivicViewLabel(view) : "Services";
        }

        foreach (KeyValuePair<BuildingDefinition, ToolButton> pair in m_BuildingButtons)
        {
            pair.Value.SetActive(mode == PlacementController.Mode.Building && m_Placement.SelectedBuilding == pair.Key);
        }
    }

    private void RefreshAffordable(float money)
    {
        RoadTiers tiers = m_GameManager.Simulation.RoadTiers;
        if (m_RoadButton != null) m_RoadButton.SetAffordable(money >= tiers.Cost(tiers.BestStreetTier));
        if (m_AvenueButton != null) m_AvenueButton.SetAffordable(money >= tiers.Cost(RoadTiers.Avenue));
        if (m_HighwayButton != null) m_HighwayButton.SetAffordable(money >= tiers.Cost(RoadTiers.Highway));
        if (m_PipeButton != null) m_PipeButton.SetAffordable(money >= m_GameManager.Balance.PipeCost);
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
