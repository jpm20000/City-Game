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
    [Tooltip("Width of a building button inside a group flyout, and of a group button.")]
    [SerializeField] private float m_BuildingButtonWidth = 104f;
    [Tooltip("(M13) Space kept free at each screen edge before the toolbar scales down to fit.")]
    [SerializeField] private float m_ScreenMargin = 8f;

    [Header("Tooltip")]
    [SerializeField] private GameObject m_TooltipRoot;
    [SerializeField] private TMP_Text m_TooltipText;

    private readonly Dictionary<BuildingDefinition, ToolButton> m_BuildingButtons = new();
    private readonly Dictionary<BuildingDefinition, Group> m_GroupOf = new();
    private readonly List<Group> m_Groups = new();
    private readonly HashSet<BuildingDefinition> m_Seen = new();   // unlocked at the last refresh (for the new dot)
    private bool m_SeenInit;

    // M20b: one toolbar button per building group; its flyout holds the unlocked buildings of the group.
    private sealed class Group
    {
        public ToolbarGroup Id;
        public ToolButton Button;
        public ToolbarFlyout Flyout;
        public GameObject Dot;
        public BuildingDefinition Last;      // the last building picked from the group (shown on its button)
        public readonly List<BuildingDefinition> Members = new();
    }
    private readonly Dictionary<InfoOverlay.View, ToolButton> m_ViewButtons = new();   // every view entry in the Views flyout
    private ToolButton m_ViewsButton;          // M20c: the one VIEW button; its flyout holds the views
    private ToolButton m_ViewOffButton;
    private ToolbarFlyout m_ViewsFlyout;
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
        CreateCivicViewButtons();
        BindView(m_AgeViewButton, "Ages", InfoOverlay.View.Age,
            "Age view  [V]\nThe age each building was built in: <color=#E6853A>orange</color> = oldest, <color=#5299F5>blue</color> = newest. Darker, striped = outdated (will be rebuilt). <color=#F2CC4D>Gold</color> = kept historic.");
        CreateViewsFlyout();
        CreateRotateButtons();
        CreateBuildingButtons();

        EscapeRouter.Register(this, EscapeRouter.Tool + 5, ToolbarFlyout.CloseOpen);
        m_Placement.ModeChanged += RefreshActive;
        if (m_InfoOverlay != null) m_InfoOverlay.ViewChanged += RefreshActive;
        GameEvents.MoneyChanged += RefreshAffordable;
        GameEvents.TechCompleted += OnTechCompleted;
        GameEvents.AgeChanged += OnAgeChanged;
        GameEvents.CityLoaded += OnCityLoaded;
        HideTooltip(null);
        RefreshUnlocked();
        RefreshActive();
        RefreshAffordable(m_GameManager.Economy.Money);
    }

    private void OnDestroy()
    {
        EscapeRouter.Unregister(this);
        if (m_Placement != null) m_Placement.ModeChanged -= RefreshActive;
        if (m_InfoOverlay != null) m_InfoOverlay.ViewChanged -= RefreshActive;
        GameEvents.MoneyChanged -= RefreshAffordable;
        GameEvents.TechCompleted -= OnTechCompleted;
        GameEvents.AgeChanged -= OnAgeChanged;
        GameEvents.CityLoaded -= OnCityLoaded;
    }

    // A new or loaded city starts with its unlocks already seen: no new dots.
    private void OnCityLoaded()
    {
        m_SeenInit = false;
        foreach (Group group in m_Groups) group.Dot.SetActive(false);
        RefreshUnlocked();
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
            foreach (KeyValuePair<InfoOverlay.View, ToolButton> pair in m_ViewButtons)
            {
                pair.Value.gameObject.SetActive(m_InfoOverlay.IsAvailable(pair.Key));
            }
        }
        if (m_PipeButton != null) m_PipeButton.gameObject.SetActive(m_GameManager.PipesUnlocked);
        RegroupBuildings();
        bool any = false;
        foreach (KeyValuePair<BuildingDefinition, ToolButton> pair in m_BuildingButtons)
        {
            bool unlocked = m_GameManager.CanBuild(pair.Key);
            pair.Value.gameObject.SetActive(unlocked);
            any |= unlocked;
            // A building unlocked since the last refresh flags its group with a dot until the flyout is opened.
            if (unlocked && m_Seen.Add(pair.Key) && m_SeenInit && m_GroupOf.TryGetValue(pair.Key, out Group fresh)) fresh.Dot.SetActive(!fresh.Flyout.IsOpen);
            if (!unlocked) m_Seen.Remove(pair.Key);
        }
        m_SeenInit = true;
        foreach (Group group in m_Groups)
        {
            bool shown = false;
            foreach (BuildingDefinition member in group.Members) shown |= m_BuildingButtons[member].gameObject.activeSelf;
            group.Button.gameObject.SetActive(shown);
            if (!shown) group.Flyout.Hide();
        }
        // The whole BUILDINGS section (header included) hides while nothing can be built.
        if (m_BuildingsContainer != null && m_BuildingsContainer.parent != null) m_BuildingsContainer.parent.gameObject.SetActive(any);
    }

    private void CreateBuildingButtons()
    {
        if (m_ButtonTemplate == null || m_BuildingsContainer == null || m_GameManager.Buildings == null) return;

        float height = ((RectTransform)m_ButtonTemplate.transform).sizeDelta.y;
        if (height < 20f) height = 56f;
        foreach (BuildingDefinition def in m_GameManager.Buildings.Entries)
        {
            if (def == null) continue;
            if (def.Category != BuildingCategory.Service && def.Category != BuildingCategory.Utility) continue;

            Group group = GroupFor(m_GameManager.ToolbarGroupFor(def));
            ToolButton button = Instantiate(m_ButtonTemplate, group.Flyout.Content);
            button.name = $"Build_{def.Id}";
            if (button.Label != null)
            {
                button.Label.enableAutoSizing = true;
                button.Label.fontSizeMax = button.Label.fontSize;
                button.Label.fontSizeMin = 11f;
            }
            var layout = button.GetComponent<LayoutElement>();
            if (layout == null) layout = button.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = m_BuildingButtonWidth;
            layout.preferredHeight = height;
            BuildingDefinition captured = def;
            Group owner = group;
            Bind(button, def.DisplayName, $"${def.Cost:N0}", Color.clear, BuildingTooltip(def), () =>
            {
                ToggleBuilding(captured);
                owner.Flyout.Hide();
            });
            m_BuildingButtons[def] = button;
            m_GroupOf[def] = group;
            group.Members.Add(def);
        }
    }

    // A building can change group with the city's age (the Fountain: water building, then park): its button moves to the
    // other group's flyout.
    private void RegroupBuildings()
    {
        foreach (KeyValuePair<BuildingDefinition, ToolButton> pair in m_BuildingButtons)
        {
            BuildingDefinition def = pair.Key;
            Group current = m_GroupOf[def];
            ToolbarGroup wanted = m_GameManager.ToolbarGroupFor(def);
            if (current.Id == wanted) continue;
            Group target = GroupFor(wanted);
            current.Members.Remove(def);
            target.Members.Add(def);
            m_GroupOf[def] = target;
            pair.Value.transform.SetParent(target.Flyout.Content, false);
            if (current.Last == def) current.Last = null;
            Group owner = target;
            pair.Value.Button.onClick.RemoveAllListeners();
            pair.Value.Button.onClick.AddListener(() =>
            {
                ToggleBuilding(def);
                owner.Flyout.Hide();
            });
            if (current.Flyout.IsOpen) current.Flyout.Hide();
        }
    }

    private static string GroupName(ToolbarGroup id)
    {
        switch (id)
        {
            case ToolbarGroup.Utilities: return "Utilities";
            case ToolbarGroup.Services: return "Services";
            case ToolbarGroup.Health: return "Health";
            case ToolbarGroup.Education: return "Education";
            default: return "Parks";
        }
    }

    private static string GroupTooltip(ToolbarGroup id)
    {
        switch (id)
        {
            case ToolbarGroup.Utilities: return "Water and power buildings.";
            case ToolbarGroup.Services: return "Police and fire buildings.";
            case ToolbarGroup.Health: return "Clinics and hospitals.";
            case ToolbarGroup.Education: return "Schools and research buildings.";
            default: return "Parks and fountains.";
        }
    }

    // The group button for a building group, made on first use. Clicking it always opens the flyout (the building last
    // picked is only shown on the button).
    private Group GroupFor(ToolbarGroup id)
    {
        foreach (Group existing in m_Groups) if (existing.Id == id) return existing;

        var group = new Group { Id = id };
        group.Button = Instantiate(m_ButtonTemplate, m_BuildingsContainer);
        group.Button.name = $"Group_{id}";
        var layout = group.Button.GetComponent<LayoutElement>();
        if (layout != null) layout.preferredWidth = m_BuildingButtonWidth;
        group.Flyout = ToolbarFlyout.Create((RectTransform)group.Button.transform, $"{id}Flyout");
        Group captured = group;
        Bind(group.Button, GroupName(id), string.Empty, Color.clear,
            $"{GroupName(id)}\n{GroupTooltip(id)} Click to choose a building.", () => OnGroupClicked(captured));

        var dot = new GameObject("NewDot", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        dot.transform.SetParent(group.Button.transform, false);
        dot.GetComponent<LayoutElement>().ignoreLayout = true;
        dot.GetComponent<Image>().color = UiKit.TitleColor;
        dot.GetComponent<Image>().raycastTarget = false;
        var dotRect = (RectTransform)dot.transform;
        dotRect.anchorMin = dotRect.anchorMax = dotRect.pivot = new Vector2(0f, 1f);
        dotRect.anchoredPosition = new Vector2(3f, -3f);
        dotRect.sizeDelta = new Vector2(10f, 10f);
        dot.SetActive(false);
        group.Dot = dot;
        group.Flyout.Opened += () => { dot.SetActive(false); HideTooltip(null); };
        group.Flyout.Closed += () => HideTooltip(null);

        m_Groups.Add(group);
        int order = 0;
        foreach (Group other in m_Groups) if (other.Id < id) order++;
        group.Button.transform.SetSiblingIndex(order);
        return group;
    }

    private void OnGroupClicked(Group group)
    {
        group.Flyout.Toggle();
    }

    private void SetGroupLast(Group group, BuildingDefinition def)
    {
        group.Last = def;
        group.Button.Setup(GroupName(group.Id), def.DisplayName, Color.clear, group.Button.Tooltip);
        RefreshActive();
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

    // The four civic views (M14) each get an entry of their own (M20c; they used to share one cycling button): copies of
    // the Age view button.
    private void CreateCivicViewButtons()
    {
        if (m_AgeViewButton == null || m_InfoOverlay == null) return;
        string[] tips =
        {
            "Crime view  [V]\n<color=#5B8DEF>Blue</color> ground = police reach. Buildings go from pale to <color=#E6382E>red</color> as crime grows. Striped = needs it but nothing reaches it.",
            "Fire view  [V]\n<color=#F2733F>Orange</color> ground = fire cover. Buildings go from pale to <color=#E6382E>red</color> as fire risk grows. Striped = no fire cover.",
            "Health view  [V]\n<color=#59D966>Green</color> ground = health care reach. Buildings go from pale to <color=#E6382E>red</color> as sickness grows. Striped = no care.",
            "Schools view  [V]\n<color=#B07AD8>Purple</color> ground = school reach; schooled homes turn purple. Striped = needs schooling but nothing reaches it.",
        };
        for (int i = 0; i < InfoOverlay.CivicViews.Length; i++)
        {
            InfoOverlay.View view = InfoOverlay.CivicViews[i];
            ToolButton button = Instantiate(m_AgeViewButton, m_AgeViewButton.transform.parent);
            button.name = view + "View";
            BindView(button, CivicViewLabel(view), view, tips[i]);
            m_ViewButtons[view] = button;
        }
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

    private static string ViewLabel(InfoOverlay.View view)
    {
        switch (view)
        {
            case InfoOverlay.View.Power: return "Power";
            case InfoOverlay.View.Water: return "Water";
            case InfoOverlay.View.Coverage: return "Parks";
            case InfoOverlay.View.Pollution: return "Pollution";
            case InfoOverlay.View.LandValue: return "Value";
            case InfoOverlay.View.Traffic: return "Traffic";
            case InfoOverlay.View.Age: return "Ages";
            case InfoOverlay.View.Off: return "Views";
            default: return CivicViewLabel(view);
        }
    }

    // M20d: two small buttons after Demolish turn the view; copies of the Demolish button (the font has no arrow glyphs).
    private void CreateRotateButtons()
    {
        var camera = FindAnyObjectByType<IsoCameraController>();
        if (m_DemolishButton == null || camera == null) return;
        Transform parent = m_DemolishButton.transform.parent;
        int index = m_DemolishButton.transform.GetSiblingIndex();
        MakeRotateButton(parent, index + 1, "RotateLeft", "Turn L", "Rotate the view left  [Q]\nTurns the map a quarter turn around the screen centre.", () => camera.Rotate(-1));
        MakeRotateButton(parent, index + 2, "RotateRight", "Turn R", "Rotate the view right  [E]\nTurns the map a quarter turn around the screen centre.", () => camera.Rotate(1));
    }

    private void MakeRotateButton(Transform parent, int index, string name, string label, string tooltip, UnityEngine.Events.UnityAction onClick)
    {
        ToolButton button = Instantiate(m_DemolishButton, parent);
        button.name = name;
        button.transform.SetSiblingIndex(index);
        Bind(button, label, string.Empty, Color.clear, tooltip, onClick);
    }

    // M20c: the VIEW section is one Views button (a copy of the Age button) showing the active view's name; its flyout
    // is a grid of every view plus Off. The view buttons were made above and are moved in, not duplicated. V still
    // cycles the views (InfoOverlay).
    private void CreateViewsFlyout()
    {
        if (m_AgeViewButton == null || m_InfoOverlay == null) return;
        Transform parent = m_AgeViewButton.transform.parent;
        m_ViewsButton = Instantiate(m_AgeViewButton, parent);
        m_ViewsButton.name = "ViewsButton";
        m_ViewsButton.transform.SetSiblingIndex(m_AgeViewButton.transform.GetSiblingIndex());
        if (m_ViewsButton.Label != null)
        {
            m_ViewsButton.Label.enableAutoSizing = true;
            m_ViewsButton.Label.fontSizeMax = m_ViewsButton.Label.fontSize;
            m_ViewsButton.Label.fontSizeMin = 11f;
        }
        m_ViewsFlyout = ToolbarFlyout.Create((RectTransform)m_ViewsButton.transform, "ViewsFlyout", 6, new Vector2(96f, 48f));
        Bind(m_ViewsButton, "Views", string.Empty, Color.clear,
            "Views  [V]\nInfo views colour the map to show power, water, parks, pollution, land value, traffic, building ages and the civic services. V cycles through them.",
            () => m_ViewsFlyout.Toggle());
        m_ViewsFlyout.Opened += () => HideTooltip(null);
        m_ViewsFlyout.Closed += () => HideTooltip(null);

        m_ViewOffButton = Instantiate(m_AgeViewButton, parent);
        m_ViewOffButton.name = "OffView";
        Bind(m_ViewOffButton, "Off", string.Empty, Color.clear, "Turn the info view off.", () => m_InfoOverlay.SetView(InfoOverlay.View.Off));

        AddView(InfoOverlay.View.Power, m_PowerViewButton);
        AddView(InfoOverlay.View.Water, m_WaterViewButton);
        AddView(InfoOverlay.View.Coverage, m_CoverageViewButton);
        AddView(InfoOverlay.View.Pollution, m_PollutionViewButton);
        AddView(InfoOverlay.View.LandValue, m_LandValueViewButton);
        AddView(InfoOverlay.View.Traffic, m_TrafficViewButton);
        AddView(InfoOverlay.View.Age, m_AgeViewButton);
        InfoOverlay.View[] order =
        {
            InfoOverlay.View.Power, InfoOverlay.View.Water, InfoOverlay.View.Coverage, InfoOverlay.View.Pollution,
            InfoOverlay.View.LandValue, InfoOverlay.View.Traffic, InfoOverlay.View.Age,
            InfoOverlay.View.Order, InfoOverlay.View.Fire, InfoOverlay.View.Health, InfoOverlay.View.Education,
        };
        Move(m_ViewOffButton);
        foreach (InfoOverlay.View view in order) if (m_ViewButtons.TryGetValue(view, out ToolButton entry)) Move(entry);

        void AddView(InfoOverlay.View view, ToolButton button)
        {
            if (button != null) m_ViewButtons[view] = button;
        }

        void Move(ToolButton button)
        {
            if (button == null) return;
            button.transform.SetParent(m_ViewsFlyout.Content, false);
            button.Button.onClick.AddListener(() => m_ViewsFlyout.Hide());
            if (button.Label != null)
            {
                button.Label.enableAutoSizing = true;
                button.Label.fontSizeMax = button.Label.fontSize;
                button.Label.fontSizeMin = 11f;
            }
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
            if (m_GroupOf.TryGetValue(def, out Group group)) SetGroupLast(group, def);
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
        foreach (KeyValuePair<InfoOverlay.View, ToolButton> pair in m_ViewButtons) SetActive(pair.Value, pair.Key == view);
        SetActive(m_ViewOffButton, view == InfoOverlay.View.Off);
        if (m_ViewsButton != null)
        {
            m_ViewsButton.SetActive(view != InfoOverlay.View.Off);
            if (m_ViewsButton.Label != null) m_ViewsButton.Label.text = ViewLabel(view);
        }

        foreach (KeyValuePair<BuildingDefinition, ToolButton> pair in m_BuildingButtons)
        {
            pair.Value.SetActive(mode == PlacementController.Mode.Building && m_Placement.SelectedBuilding == pair.Key);
        }
        foreach (Group group in m_Groups)
        {
            bool placing = mode == PlacementController.Mode.Building && m_Placement.SelectedBuilding != null
                && m_GroupOf.TryGetValue(m_Placement.SelectedBuilding, out Group owner) && owner == group;
            group.Button.SetActive(placing);
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

    private float m_TooltipDue = -1f;     // unscaled time at which the pending tooltip shows (-1 = none)

    // The tooltip shows after the Settings tooltip delay (0 = at once).
    private void ShowTooltip(ToolButton button)
    {
        if (m_TooltipRoot == null || string.IsNullOrEmpty(button.Tooltip)) return;
        foreach (Group group in m_Groups) if (group.Button == button && group.Flyout.IsOpen) return;
        if (button == m_ViewsButton && m_ViewsFlyout != null && m_ViewsFlyout.IsOpen) return;
        m_TooltipText.text = KeyBindings.Fill(button.Tooltip);
        RaiseTooltip();
        float delay = GameSettings.TooltipDelay;
        if (delay <= 0f)
        {
            m_TooltipRoot.SetActive(true);
            m_TooltipDue = -1f;
            return;
        }
        m_TooltipRoot.SetActive(false);
        m_TooltipDue = Time.unscaledTime + delay;
    }

    // The tooltip floats above the bar; while a flyout is open it sits above the flyout instead of behind it.
    private void RaiseTooltip()
    {
        var rect = (RectTransform)m_TooltipRoot.transform;
        if (!m_TooltipBaseSet)
        {
            m_TooltipBase = rect.anchoredPosition;
            m_TooltipBaseSet = true;
        }
        float parentScale = rect.parent != null ? rect.parent.lossyScale.y : 1f;
        rect.anchoredPosition = m_TooltipBase + new Vector2(0f, parentScale > 0f ? ToolbarFlyout.OpenHeight / parentScale : 0f);
    }

    private Vector2 m_TooltipBase;
    private bool m_TooltipBaseSet;

    private void HideTooltip(ToolButton button)
    {
        m_TooltipDue = -1f;
        if (m_TooltipRoot != null) m_TooltipRoot.SetActive(false);
    }

    private void Update()
    {
        if (m_TooltipDue < 0f || Time.unscaledTime < m_TooltipDue) return;
        m_TooltipDue = -1f;
        if (m_TooltipRoot != null) m_TooltipRoot.SetActive(true);
    }
}
