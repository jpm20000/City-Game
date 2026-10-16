using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Top bar. Driven by GameEvents; reads current values once in Start because events only fire on change.
public sealed class HUDController : MonoBehaviour
{
    [SerializeField] private GameManager m_GameManager;

    [Header("Time")]
    [SerializeField] private TMP_Text m_DateText;
    [SerializeField] private Button m_PauseButton;
    [SerializeField] private Button m_Speed1Button;
    [SerializeField] private Button m_Speed2Button;
    [SerializeField] private Button m_Speed4Button;

    [Header("Economy")]
    [SerializeField] private TMP_Text m_MoneyText;
    [SerializeField] private TMP_Text m_CashFlowText;
    [SerializeField] private Color m_PositiveColor = new Color(0.45f, 0.85f, 0.45f);
    [SerializeField] private Color m_NegativeColor = new Color(0.95f, 0.40f, 0.35f);

    [Header("City")]
    [SerializeField] private TMP_Text m_PopulationText;
    [SerializeField] private TMP_Text m_JobsText;
    [SerializeField] private TMP_Text m_HappinessText;
    [SerializeField] private UIMeter m_HappinessMeter;
    [SerializeField] private UIMeter m_ResidentialDemand;
    [SerializeField] private UIMeter m_CommercialDemand;
    [SerializeField] private UIMeter m_IndustrialDemand;
    [Tooltip("Power demand / supply; red while any grown building is unpowered.")]
    [SerializeField] private TMP_Text m_PowerText;
    [Tooltip("The utilities group (power line, and the water line since M13); hidden until either is unlocked.")]
    [SerializeField] private GameObject m_PowerGroup;
    [Tooltip("(M13) Piped: water demand / supply; well ages: share of buildings with water. Red while any grown building is dry.")]
    [SerializeField] private TMP_Text m_WaterText;
    [Tooltip("(M13) The water line under the power line; hidden until water is part of the game (GameManager.WaterUnlocked).")]
    [SerializeField] private GameObject m_WaterGroup;
    [SerializeField] private Color m_IdleColor = new Color(0.60f, 0.64f, 0.70f);

    // (M24) Made at runtime from the industrial demand meter (no scene edit): shows how well the city's goods cover its demand.
    private GameObject m_GoodsGroup;
    private UIMeter m_GoodsMeter;
    private UnityEngine.UI.Image m_GoodsFill;

    [Header("Age & research (M11)")]
    [Tooltip("Hidden when the game has no age data.")]
    [SerializeField] private GameObject m_AgeGroup;
    [SerializeField] private TMP_Text m_AgeText;
    [SerializeField] private TMP_Text m_ResearchText;
    [SerializeField] private UIMeter m_ResearchMeter;
    [SerializeField] private Color m_WarningColor = new Color(0.95f, 0.76f, 0.31f);

    // (M32c) Theme icons before the readouts, made at runtime (no scene edit). A readout with an icon drops its word
    // ("Pop", "Power", "Happiness"...); without the sprite it keeps the word, as before M32.
    private Image m_CashFlowIcon, m_PopulationIcon, m_JobsIcon, m_HappinessIcon, m_PowerIcon, m_WaterIcon;

    private void OnEnable()
    {
        GameEvents.DateChanged += OnDateChanged;
        GameEvents.SpeedChanged += OnSpeedChanged;
        GameEvents.MoneyChanged += OnMoneyChanged;
        GameEvents.CashFlowChanged += OnCashFlowChanged;
        GameEvents.PopulationChanged += OnPopulationChanged;
        GameEvents.HappinessChanged += OnHappinessChanged;
        GameEvents.DemandChanged += OnDemandChanged;
        GameEvents.PowerChanged += OnPowerChanged;
        GameEvents.WaterChanged += OnWaterChanged;
        GameEvents.GoodsChanged += OnGoodsChanged;
        GameEvents.ResearchChanged += RefreshResearch;
        GameEvents.AgeChanged += OnAgeChanged;
        GameEvents.TechCompleted += OnTechCompleted;
        GameEvents.CityLoaded += OnCityLoaded;
    }

    private void OnDisable()
    {
        GameEvents.DateChanged -= OnDateChanged;
        GameEvents.SpeedChanged -= OnSpeedChanged;
        GameEvents.MoneyChanged -= OnMoneyChanged;
        GameEvents.CashFlowChanged -= OnCashFlowChanged;
        GameEvents.PopulationChanged -= OnPopulationChanged;
        GameEvents.HappinessChanged -= OnHappinessChanged;
        GameEvents.DemandChanged -= OnDemandChanged;
        GameEvents.PowerChanged -= OnPowerChanged;
        GameEvents.WaterChanged -= OnWaterChanged;
        GameEvents.GoodsChanged -= OnGoodsChanged;
        GameEvents.ResearchChanged -= RefreshResearch;
        GameEvents.AgeChanged -= OnAgeChanged;
        GameEvents.TechCompleted -= OnTechCompleted;
        GameEvents.CityLoaded -= OnCityLoaded;
    }

    private void Start()
    {
        BindSpeedButton(m_PauseButton, GameSpeed.Paused);
        BindSpeedButton(m_Speed1Button, GameSpeed.x1);
        BindSpeedButton(m_Speed2Button, GameSpeed.x2);
        BindSpeedButton(m_Speed4Button, GameSpeed.x4);
        FitHudBar();
        CreateIcons();

        if (m_GameManager == null || m_GameManager.Simulation == null) return;

        TimeManager clock = m_GameManager.Clock;
        if (clock != null)
        {
            OnDateChanged(clock.Day, clock.Month, clock.Year);
            OnSpeedChanged(clock.Speed);
        }

        EconomySystem economy = m_GameManager.Economy;
        OnMoneyChanged(economy.Money);
        OnCashFlowChanged(economy.IncomePerDay, economy.ExpensePerDay);

        PopulationSystem population = m_GameManager.Population;
        OnPopulationChanged(population.Population, population.Jobs);
        OnHappinessChanged(population.AverageHappiness);
        OnDemandChanged(m_GameManager.Demand.Snapshot);

        PowerSystem power = m_GameManager.Simulation.Power;
        OnPowerChanged(power.Supply, power.Demand, power.UnpoweredCells);
        OnWaterChanged(m_GameManager.Simulation.Water.Status);

        if (m_AgeGroup != null) m_AgeGroup.SetActive(m_GameManager.Simulation.Tech != null);
        CreateGoodsMeter();
        OnGoodsChanged(m_GameManager.Simulation.Goods.Last);
        OnCityLoaded();
    }

    private void CreateIcons()
    {
        AddIcon(m_DateText, UiIcons.Date);
        AddIcon(m_MoneyText, UiIcons.Money);
        m_CashFlowIcon = AddIcon(m_CashFlowText, UiIcons.IncomeUp);
        m_PopulationIcon = AddIcon(m_PopulationText, UiIcons.Population);
        m_JobsIcon = AddIcon(m_JobsText, UiIcons.Jobs);
        m_HappinessIcon = AddIcon(m_HappinessText, UiIcons.Happy);
        m_PowerIcon = AddIcon(m_PowerText, UiIcons.Power);
        m_WaterIcon = AddIcon(m_WaterText, UiIcons.Water);
        AddIcon(m_AgeText, UiIcons.Age);
        AddIcon(m_ResearchText, UiIcons.Research);
        IconButton(m_PauseButton, UiIcons.Pause);
        IconButton(m_Speed1Button, UiIcons.Play1);
        IconButton(m_Speed2Button, UiIcons.Play2);
        IconButton(m_Speed4Button, UiIcons.Play3);
    }

    // (M32d) Fits the HUD to its bar art, whose ornamented ends take 85 px a side and whose blue band is only 40 px
    // tall (y 19-59 of 76): the happiness meter gives up 80 px of width (UiTheme.PaintHudBar tightens the spacing for
    // the rest); the speed buttons, the daily income and the research bar (at the happiness bar's size) hang below the
    // bar on small framed tabs, and the R / C / I / G demand bars on a tab at the top right under Settings (their objects
    // stay where they are, so names and paths are unchanged; the selection panel moves down below that tab); the other
    // readouts shrink to the band. Runs before CreateIcons so the icons are sized to the fitted fonts. Without the art
    // the HUD is left as it was.
    private const float HudBand = 40f;
    private const float TabGap = 33f;       // from the HUD's middle to the bar art's bottom edge
    private const float TabPadX = 8f, TabPadY = 6f;
    private RectTransform m_DemandGroup, m_DemandTab;

    private void FitHudBar()
    {
        if (UiTheme.Current.HudBarSprite == null) return;
        if (m_HappinessMeter != null && m_HappinessMeter.transform.parent is Transform group &&
            group.GetComponent<LayoutElement>() is LayoutElement element && element.preferredWidth > 0f)
        {
            element.preferredWidth -= 80f;
            var meter = (RectTransform)m_HappinessMeter.transform;
            meter.sizeDelta = new Vector2(Mathf.Min(meter.sizeDelta.x, element.preferredWidth), meter.sizeDelta.y);
            if (meter.GetComponent<LayoutElement>() is LayoutElement own)
            {
                if (own.minWidth > element.preferredWidth) own.minWidth = element.preferredWidth;
                if (own.preferredWidth > element.preferredWidth) own.preferredWidth = element.preferredWidth;
            }
        }
        if (m_PauseButton != null && m_PauseButton.transform.parent is RectTransform speed && speed.parent != transform)
            HangBelow(speed, new Vector2(4 * 52f + 3 * 6f, 28f));
        if (m_CashFlowText != null) HangBelow(m_CashFlowText.rectTransform, new Vector2(170f, 22f));
        if (m_ResearchMeter != null && m_ResearchMeter.transform.parent != transform)
        {
            var research = (RectTransform)m_ResearchMeter.transform;
            float width = m_HappinessMeter != null ? ((RectTransform)m_HappinessMeter.transform).sizeDelta.x : 140f;
            HangBelow(research, new Vector2(width, 12f));
            if (m_ResearchMeter.GetComponent<LayoutElement>() is LayoutElement bar) bar.minWidth = bar.preferredWidth = width;
            UiTheme.PaintMeter(research.GetComponent<Image>(), research.Find("Fill")?.GetComponent<Image>());  // re-inset the fill
        }
        if (m_ResidentialDemand != null && m_ResidentialDemand.transform.parent?.parent is RectTransform demand && demand.parent == transform)
        {
            // Top right, its right edge under the Settings button's (the HUD's right padding), from the bar art's bottom edge.
            m_DemandGroup = demand;
            float right = GetComponent<HorizontalLayoutGroup>() is HorizontalLayoutGroup row ? row.padding.right : 0f;
            float height = 0f;
            foreach (Transform column in demand) height = Mathf.Max(height, ColumnHeight(column));
            m_DemandTab = HangBelow(demand, new Vector2(DemandWidth(), height), new Vector2(1f, 0f), new Vector2(-right, 38f - TabGap), 1f);
            var layout = demand.GetComponent<HorizontalLayoutGroup>();
            if (layout != null) layout.childAlignment = TextAnchor.UpperRight;
            if (FindAnyObjectByType<SelectionPanel>() is SelectionPanel selection && selection.transform is RectTransform panel)
            {
                float below = 76f - (38f - TabGap) + height + 2f * TabPadY + 8f;   // the tab's bottom, plus a gap
                if (-panel.anchoredPosition.y < below) panel.anchoredPosition = new Vector2(panel.anchoredPosition.x, -below);
            }
        }
        foreach (Transform child in transform) FitToBand((RectTransform)child, HudBand);
        // With the demand bars on their tab the bar has room to spare: spread it between the groups.
        if (GetComponent<HorizontalLayoutGroup>() is HorizontalLayoutGroup groups) groups.childForceExpandWidth = true;
    }

    // A demand column's height from its rows' fixed heights (the layout hasn't run yet in Start).
    private static float ColumnHeight(Transform column)
    {
        float height = 0f;
        int rows = 0;
        foreach (Transform child in column)
        {
            if (!child.gameObject.activeSelf || child.GetComponent<LayoutElement>() is not LayoutElement row || row.ignoreLayout) continue;
            height += Mathf.Max(row.preferredHeight, row.minHeight);
            rows++;
        }
        float spacing = column.GetComponent<VerticalLayoutGroup>() is VerticalLayoutGroup v ? v.spacing : 0f;
        return height + spacing * Mathf.Max(0, rows - 1);
    }

    // The demand row's width: its shown columns (the goods column comes and goes).
    private float DemandWidth()
    {
        var layout = m_DemandGroup.GetComponent<HorizontalLayoutGroup>();
        float width = 0f;
        int shown = 0;
        foreach (Transform column in m_DemandGroup)
        {
            if (!column.gameObject.activeSelf) continue;
            width += LayoutUtility.GetPreferredWidth((RectTransform)column);
            shown++;
        }
        return width + (layout != null ? layout.spacing * Mathf.Max(0, shown - 1) : 0f);
    }

    // Keeps the hanging demand tab as wide as its shown columns.
    private void SizeDemandTab()
    {
        if (m_DemandTab == null) return;
        float width = DemandWidth();
        if (Mathf.Approximately(width, m_DemandGroup.sizeDelta.x)) return;
        m_DemandGroup.sizeDelta = new Vector2(width, m_DemandGroup.sizeDelta.y);
        m_DemandTab.sizeDelta = new Vector2(width + 2f * TabPadX, m_DemandTab.sizeDelta.y);
    }

    // Takes item out of its group's layout and puts it on a framed tab hanging below the bar, left-aligned with the
    // group. The group sits centred on the HUD's middle, so the tab is placed from the group's middle.
    private static RectTransform HangBelow(RectTransform item, Vector2 size) =>
        HangBelow(item, size, new Vector2(0f, 0.5f), new Vector2(0f, -TabGap), 0f);

    // The general case: the tab's top corner at `corner` from `anchor` in the item's parent; side 0 = the tab runs right
    // of the corner, 1 = left of it. Returns the tab (a framed plate drawn under the item).
    private static RectTransform HangBelow(RectTransform item, Vector2 size, Vector2 anchor, Vector2 corner, float side)
    {
        var parent = (RectTransform)item.parent;
        var plate = new GameObject("Tab", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        plate.transform.SetParent(parent, false);
        plate.transform.SetSiblingIndex(item.GetSiblingIndex());
        plate.GetComponent<LayoutElement>().ignoreLayout = true;
        UiTheme.PaintPanel(plate.GetComponent<Image>(), UiKit.PanelColor);
        float outward = side > 0.5f ? TabPadX : -TabPadX;
        var tab = (RectTransform)plate.transform;
        Place(tab, anchor, side, corner + new Vector2(outward, 0f), size + new Vector2(2f * TabPadX, 2f * TabPadY));
        (item.GetComponent<LayoutElement>() ?? item.gameObject.AddComponent<LayoutElement>()).ignoreLayout = true;
        Place(item, anchor, side, corner + new Vector2(0f, -TabPadY), size);
        return tab;
    }

    private static void Place(RectTransform rect, Vector2 anchor, float side, Vector2 corner, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(side, 1f);
        rect.anchoredPosition = corner;
        rect.sizeDelta = size;
    }

    // Shrinks a readout group taller than the band: its spacing first, then its children's heights and fonts in
    // proportion (fonts no smaller than 10 pt). A row group fits each of its columns. Button columns are left alone
    // (shrunk buttons would be too small to click).
    private static void FitToBand(RectTransform group, float band)
    {
        if (!group.gameObject.activeSelf || group.GetComponentInChildren<Selectable>(true) != null) return;
        if (group.GetComponent<LayoutElement>() is LayoutElement hung && hung.ignoreLayout) return;      // on a tab
        if (group.GetComponent<HorizontalLayoutGroup>() != null)
        {
            foreach (Transform child in group) FitToBand((RectTransform)child, band);
            return;
        }
        if (group.GetComponent<VerticalLayoutGroup>() is not VerticalLayoutGroup column) return;
        var rows = new System.Collections.Generic.List<LayoutElement>();
        float height = 0f;
        foreach (Transform child in group)
        {
            if (!child.gameObject.activeSelf || child.GetComponent<LayoutElement>() is not LayoutElement row || row.ignoreLayout) continue;
            if (row.preferredHeight <= 0f) return;      // a row without a fixed height: leave the group alone
            rows.Add(row);
            height += row.preferredHeight;
        }
        if (rows.Count == 0) return;
        column.spacing = Mathf.Min(column.spacing, 1f);
        float gaps = column.spacing * (rows.Count - 1);
        if (height + gaps <= band) return;
        float k = (band - gaps) / height;
        foreach (LayoutElement row in rows)
        {
            row.preferredHeight *= k;
            if (row.minHeight > 0f) row.minHeight *= k;
            if (row.GetComponent<TMP_Text>() is TMP_Text text) text.fontSize = Mathf.Max(10f, text.fontSize * k);
        }
    }

    // An icon at the left of a readout, sized to its font; the text moves over by a left margin (which TMP counts in
    // its preferred width, so the layout group makes room). Null when the theme has no such icon.
    private static Image AddIcon(TMP_Text text, string name)
    {
        Sprite sprite = UiTheme.Icon(name);
        if (text == null || sprite == null) return null;
        float size = Mathf.Round(Mathf.Clamp(text.fontSize * 1.15f, 16f, 30f));
        var go = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(text.transform, false);
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(size, size);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        Vector4 margin = text.margin;
        text.margin = new Vector4(size + 5f, margin.y, margin.z, margin.w);
        return image;
    }

    // The speed buttons show an icon instead of II / 1x / 2x / 4x.
    private static void IconButton(Button button, string name)
    {
        Sprite sprite = UiTheme.Icon(name);
        if (button == null || sprite == null) return;
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.gameObject.SetActive(false);
        var go = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(button.transform, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(20f, 20f);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    private static void SetIcon(Image image, string name)
    {
        if (image == null) return;
        Sprite sprite = UiTheme.Icon(name);
        if (sprite != null) image.sprite = sprite;
    }

    // The word before a readout, or nothing when its icon stands in for it.
    private static string Word(Image icon, string word) => icon != null ? string.Empty : word + " ";

    // A copy of the industrial demand column labelled G, beside the three demand meters; hidden while goods are off.
    private void CreateGoodsMeter()
    {
        if (m_GoodsGroup != null || m_IndustrialDemand == null) return;
        Transform column = m_IndustrialDemand.transform.parent;
        if (column == null || column.parent == null) return;
        m_GoodsGroup = Instantiate(column.gameObject, column.parent);
        m_GoodsGroup.name = "DemandG";
        m_GoodsGroup.transform.SetSiblingIndex(column.GetSiblingIndex() + 1);
        m_GoodsMeter = m_GoodsGroup.GetComponentInChildren<UIMeter>(true);
        TMP_Text label = m_GoodsGroup.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = "G";
        if (m_GoodsMeter != null) m_GoodsFill = m_GoodsMeter.transform.Find("Fill")?.GetComponent<Image>();
        m_GoodsGroup.AddComponent<GoodsTooltip>().Init(m_GameManager);
        m_GoodsGroup.SetActive(false);
    }

    // Supply is the share of the city's goods demand that was met: teal when comfortable, amber when it is about to hold
    // growth, red in a deep shortage. Hidden while goods are not in play.
    private void OnGoodsChanged(GoodsReport report)
    {
        if (m_GoodsGroup == null) return;
        bool active = m_GameManager != null && m_GameManager.Simulation != null && m_GameManager.Simulation.GoodsActive;
        m_GoodsGroup.SetActive(active);
        SizeDemandTab();
        if (!active) return;
        float supply = m_GameManager.Simulation.GoodsSupply();
        m_GoodsMeter.SetValue(supply);
        float hold = m_GameManager.Balance != null ? m_GameManager.Balance.GoodsLevel3Supply : 0.7f;
        if (m_GoodsFill != null) m_GoodsFill.color = supply >= 1f ? new Color(0.35f, 0.80f, 0.70f) : supply >= hold ? m_WarningColor : m_NegativeColor;
    }

    private void OnAgeChanged(int age) => OnCityLoaded();
    private void OnTechCompleted(string techId) => OnCityLoaded();

    // Age name, research line and the Power / Water groups' visibility (power appears with Electricity).
    private void OnCityLoaded()
    {
        if (m_GameManager == null || m_GameManager.Simulation == null) return;
        bool power = m_GameManager.PowerUnlocked;
        bool water = m_GameManager.WaterUnlocked;
        if (m_PowerGroup != null) m_PowerGroup.SetActive(power || water);
        if (m_PowerText != null) m_PowerText.gameObject.SetActive(power);
        if (m_WaterGroup != null) m_WaterGroup.SetActive(water);
        if (m_AgeText != null) m_AgeText.text = m_GameManager.CurrentAgeName ?? string.Empty;
        RefreshResearch();
        if (m_GameManager.Simulation != null) OnGoodsChanged(m_GameManager.Simulation.Goods.Last);
    }

    private void RefreshResearch()
    {
        TechSystem tech = m_GameManager != null && m_GameManager.Simulation != null ? m_GameManager.Simulation.Tech : null;
        if (tech == null) return;

        float rate = m_GameManager.Simulation.ResearchIncome();
        ResearchProject active = tech.Active;
        float progress = active != null && active.Cost > 0f ? Mathf.Clamp01(tech.Progress / active.Cost) : 0f;
        if (m_ResearchText != null)
        {
            m_ResearchText.text = active != null
                ? $"{active.DisplayName} {progress:P0}  ·  {rate:0.#} RP/day"
                : $"No research  ·  {rate:0.#} RP/day";
            m_ResearchText.color = active != null ? Color.white : m_WarningColor;
        }
        if (m_ResearchMeter != null) m_ResearchMeter.SetValue(progress);
    }

    private void BindSpeedButton(Button button, GameSpeed speed)
    {
        if (button == null) return;
        button.onClick.AddListener(() =>
        {
            if (m_GameManager != null && m_GameManager.Clock != null) m_GameManager.Clock.SetSpeed(speed);
        });
    }

    private void OnDateChanged(int day, int month, int year)
    {
        if (m_DateText != null) m_DateText.text = $"Day {day}  ·  Month {month}  ·  Year {year}";
    }

    // The active speed's button is non-interactable, which shows its disabled (highlight) colour.
    private void OnSpeedChanged(GameSpeed speed)
    {
        if (m_PauseButton != null) m_PauseButton.interactable = speed != GameSpeed.Paused;
        if (m_Speed1Button != null) m_Speed1Button.interactable = speed != GameSpeed.x1;
        if (m_Speed2Button != null) m_Speed2Button.interactable = speed != GameSpeed.x2;
        if (m_Speed4Button != null) m_Speed4Button.interactable = speed != GameSpeed.x4;
    }

    private void OnMoneyChanged(float money)
    {
        if (m_MoneyText == null) return;
        m_MoneyText.text = money < 0f ? $"-${-money:N0}" : $"${money:N0}";
        m_MoneyText.color = money < 0f ? m_NegativeColor : Color.white;
    }

    private void OnCashFlowChanged(float income, float expense)
    {
        if (m_CashFlowText == null) return;
        float net = income - expense;
        m_CashFlowText.text = $"{(net >= 0f ? "+" : "-")}${Mathf.Abs(net):N0} / day";
        m_CashFlowText.color = net >= 0f ? m_PositiveColor : m_NegativeColor;
        SetIcon(m_CashFlowIcon, net >= 0f ? UiIcons.IncomeUp : UiIcons.IncomeDown);
    }

    private void OnPopulationChanged(int population, int jobs)
    {
        PopulationSystem stats = m_GameManager != null ? m_GameManager.Population : null;
        if (m_PopulationText != null)
        {
            string pop = Word(m_PopulationIcon, "Pop");
            m_PopulationText.text = stats != null ? $"{pop}{population:N0} / {stats.Housing:N0}" : $"{pop}{population:N0}";
        }
        if (m_JobsText != null)
        {
            string word = Word(m_JobsIcon, "Jobs");
            m_JobsText.text = stats != null ? $"{word}{stats.Employed:N0} / {jobs:N0}" : $"{word}{jobs:N0}";
        }
    }

    private const float HappyFrom = 0.6f;    // (M32c) the happy face from here; below the leaving threshold, the unhappy one

    private void OnHappinessChanged(float happiness)
    {
        if (m_HappinessText != null)
        {
            // Below the threshold residents leave; flag it so the player hovers for the reasons.
            bool low = m_GameManager != null && m_GameManager.Balance != null && happiness < m_GameManager.Balance.LowHappinessThreshold;
            m_HappinessText.text = $"{Word(m_HappinessIcon, "Happiness")}{happiness:P0}";
            m_HappinessText.color = low ? m_NegativeColor : Color.white;
            SetIcon(m_HappinessIcon, low ? UiIcons.Unhappy : happiness < HappyFrom ? UiIcons.Neutral : UiIcons.Happy);
        }
        if (m_HappinessMeter != null) m_HappinessMeter.SetValue(happiness);
    }

    private void OnPowerChanged(int supply, int demand, int unpoweredCells)
    {
        if (m_PowerText == null) return;

        if (supply == 0 && demand == 0)
        {
            m_PowerText.text = m_PowerIcon != null ? "—" : "Power  —";
            m_PowerText.color = m_IdleColor;
        }
        else if (supply == 0)
        {
            m_PowerText.text = "No power";
            m_PowerText.color = m_NegativeColor;
        }
        else
        {
            m_PowerText.text = $"{Word(m_PowerIcon, "Power")}{demand:N0} / {supply:N0}";
            m_PowerText.color = unpoweredCells > 0 ? m_NegativeColor : Color.white;
        }
    }

    // Piped ages read like power; well ages show the share of grown buildings a well reaches.
    private void OnWaterChanged(WaterStatus status)
    {
        if (m_WaterText == null) return;

        if (status.Mode == WaterRule.Coverage)
        {
            m_WaterText.text = status.GrownCells == 0 ? (m_WaterIcon != null ? "—" : "Water  —") : $"{Word(m_WaterIcon, "Water")}{status.WateredShare:P0}";
            m_WaterText.color = status.GrownCells == 0 ? m_IdleColor : status.DryCells > 0 ? m_NegativeColor : Color.white;
        }
        else if (status.Supply == 0 && status.Demand == 0)
        {
            m_WaterText.text = m_WaterIcon != null ? "—" : "Water  —";
            m_WaterText.color = m_IdleColor;
        }
        else if (status.Supply == 0)
        {
            m_WaterText.text = "No water";
            m_WaterText.color = m_NegativeColor;
        }
        else
        {
            m_WaterText.text = $"{Word(m_WaterIcon, "Water")}{status.Demand:N0} / {status.Supply:N0}";
            m_WaterText.color = status.DryCells > 0 ? m_NegativeColor : Color.white;
        }
    }

    private void OnDemandChanged(DemandSnapshot demand)
    {
        if (m_ResidentialDemand != null) m_ResidentialDemand.SetValue(demand.Residential);
        if (m_CommercialDemand != null) m_CommercialDemand.SetValue(demand.Commercial);
        if (m_IndustrialDemand != null) m_IndustrialDemand.SetValue(demand.Industrial);
    }
}
