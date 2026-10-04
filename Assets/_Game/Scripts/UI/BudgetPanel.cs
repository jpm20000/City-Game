using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The Budget panel (M15d): the day's ledger on top, then three tabs: Services (a funding slider per
// budget line whose buildings are unlocked), Loans and Ordinances. Built at runtime (like the Services
// view button) in the slot the Taxes and Research panels share, with its own HUD button next to theirs.
public sealed class BudgetPanel : MonoBehaviour
{
    private enum Tab { Services, Loans, Ordinances }

    private static readonly Color PanelColor = new Color(0.10f, 0.12f, 0.15f, 0.94f);
    private static readonly Color ButtonColor = new Color(0.24f, 0.27f, 0.33f);
    private static readonly Color ActiveColor = new Color(0.30f, 0.55f, 0.92f);
    private static readonly Color MutedColor = new Color(0.60f, 0.64f, 0.70f);
    private static readonly Color WarnColor = new Color(0.95f, 0.76f, 0.31f);
    private const float PanelWidth = 460f;

    private sealed class LineRow
    {
        public BudgetLine Line;
        public Slider Slider;
        public TMP_Text Value;
        public TMP_Text Detail;
    }

    private GameManager m_Game;
    private SimulationSystem m_Sim;
    private GameObject m_Root;
    private TMP_Text m_Ledger;
    private RectTransform m_Content;
    private LayoutElement m_ScrollElement;
    private const float MaxContentHeight = 560f;
    private readonly List<Button> m_TabButtons = new();
    private readonly List<LineRow> m_Rows = new();
    private Tab m_Tab = Tab.Services;
    private bool m_Open;

    public bool IsOpen => m_Open;

    private static readonly string[] LineNames = { "Parks", "Power", "Water", "Order", "Fire", "Health", "Schools" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        GameManager game = FindAnyObjectByType<GameManager>();
        TaxPanel taxPanel = FindAnyObjectByType<TaxPanel>(FindObjectsInactive.Include);
        if (game == null || taxPanel == null) return;

        // The HUD button: a copy of the Taxes button, last in the Budget group.
        Button taxes = null;
        foreach (Button b in taxPanel.transform.root.GetComponentsInChildren<Button>(true))
        {
            if (b.name == "TaxesButton") taxes = b;
        }
        if (taxes == null) return;

        var go = new GameObject("BudgetPanel", typeof(RectTransform));
        go.transform.SetParent(taxPanel.transform.parent, false);
        var panel = go.AddComponent<BudgetPanel>();
        panel.Init(game, taxPanel.GetComponent<RectTransform>());

        Button toggle = Instantiate(taxes, taxes.transform.parent);
        toggle.name = "BudgetButton";
        toggle.onClick.RemoveAllListeners();
        toggle.GetComponentInChildren<TMP_Text>().text = "Budget";
        toggle.onClick.AddListener(panel.Toggle);
    }

    private void Init(GameManager game, RectTransform slot)
    {
        m_Game = game;

        var rt = (RectTransform)transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = slot.anchoredPosition;
        rt.sizeDelta = new Vector2(PanelWidth, 0f);

        m_Root = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        m_Root.transform.SetParent(transform, false);
        var rootRect = (RectTransform)m_Root.transform;
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(1f, 1f);
        rootRect.pivot = new Vector2(0.5f, 1f);
        rootRect.anchoredPosition = Vector2.zero;
        rootRect.sizeDelta = Vector2.zero;
        m_Root.GetComponent<Image>().color = PanelColor;
        var layout = m_Root.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 12);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        m_Root.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform header = Row(m_Root.transform, 28f);
        Text(header, "Budget", 22, Color.white, flexible: true);
        Button close = MakeButton(header, "x", Close, 28f);
        close.GetComponent<LayoutElement>().minWidth = 28f;

        m_Ledger = Text(m_Root.transform, "", 14, new Color(0.75f, 0.80f, 0.88f, 1f));

        RectTransform tabs = Row(m_Root.transform, 30f);
        foreach (Tab tab in new[] { Tab.Services, Tab.Loans, Tab.Ordinances })
        {
            Tab captured = tab;
            Button b = MakeButton(tabs, tab.ToString(), () => SelectTab(captured), 0f);
            b.GetComponent<LayoutElement>().flexibleWidth = 1f;
            m_TabButtons.Add(b);
        }

        // The tab content scrolls once it is taller than MaxContentHeight (ten ordinances do not fit under the HUD).
        var scroll = new GameObject("Scroll", typeof(RectTransform), typeof(RectMask2D), typeof(ScrollRect), typeof(LayoutElement));
        scroll.transform.SetParent(m_Root.transform, false);
        m_ScrollElement = scroll.GetComponent<LayoutElement>();
        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(scroll.transform, false);
        m_Content = (RectTransform)content.transform;
        m_Content.anchorMin = new Vector2(0f, 1f);
        m_Content.anchorMax = new Vector2(1f, 1f);
        m_Content.pivot = new Vector2(0.5f, 1f);
        m_Content.anchoredPosition = Vector2.zero;
        m_Content.sizeDelta = Vector2.zero;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scrollRect = scroll.GetComponent<ScrollRect>();
        scrollRect.content = m_Content;
        scrollRect.viewport = (RectTransform)scroll.transform;
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;
        var contentLayout = content.GetComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 6f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        m_Root.SetActive(false);
    }

    private void Start()
    {
        if (m_Game == null) return;
        m_Sim = m_Game.Simulation;
        GameEvents.CashFlowChanged += OnCashFlowChanged;
        GameEvents.CityLoaded += OnCityLoaded;
        GameEvents.TechCompleted += OnTechCompleted;
        GameEvents.AgeChanged += OnAgeChanged;
        SidePanels.Opened += OnSidePanelOpened;
        EscapeRouter.Register(this, EscapeRouter.SidePanel, TryEscape);
    }

    private void OnDestroy()
    {
        GameEvents.CashFlowChanged -= OnCashFlowChanged;
        GameEvents.CityLoaded -= OnCityLoaded;
        GameEvents.TechCompleted -= OnTechCompleted;
        GameEvents.AgeChanged -= OnAgeChanged;
        SidePanels.Opened -= OnSidePanelOpened;
        EscapeRouter.Unregister(this);
    }

    // --- Open / close ---

    private bool TryEscape()
    {
        if (!m_Open) return false;
        Close();
        return true;
    }

    private void Toggle()
    {
        if (m_Open) Close();
        else Open();
    }

    private void Open()
    {
        m_Open = true;
        m_Root.SetActive(true);
        SidePanels.RaiseOpened(this);
        SelectTab(m_Tab);
    }

    private void Close()
    {
        m_Open = false;
        m_Root.SetActive(false);
    }

    private void OnSidePanelOpened(object panel)
    {
        if (!ReferenceEquals(panel, this) && m_Open) Close();
    }

    private void LateUpdate()
    {
        if (m_Open) m_ScrollElement.preferredHeight = Mathf.Min(m_Content.rect.height, MaxContentHeight);
    }

    private void OnCashFlowChanged(float income, float expense)
    {
        if (m_Open) Refresh();
    }

    private void OnCityLoaded()
    {
        if (m_Open) SelectTab(m_Tab);
    }

    private void OnTechCompleted(string id)
    {
        if (m_Open) SelectTab(m_Tab);
    }

    private void OnAgeChanged(int age)
    {
        if (m_Open) SelectTab(m_Tab);
    }

    // --- Tabs ---

    private void SelectTab(Tab tab)
    {
        m_Tab = tab;
        for (int i = 0; i < m_TabButtons.Count; i++)
        {
            m_TabButtons[i].GetComponent<Image>().color = i == (int)tab ? ActiveColor : ButtonColor;
        }

        for (int i = m_Content.childCount - 1; i >= 0; i--)
        {
            GameObject old = m_Content.GetChild(i).gameObject;
            old.SetActive(false);   // out of the layout now; Destroy only lands at the end of the frame
            Destroy(old);
        }
        m_Rows.Clear();

        switch (tab)
        {
            case Tab.Services: BuildServices(); break;
            case Tab.Loans: BuildLoans(); break;
            default: BuildOrdinances(); break;
        }
        Refresh();
    }

    private void BuildServices()
    {
        BudgetSystem budget = m_Sim.Budget;
        bool any = false;
        foreach (BudgetLine line in System.Enum.GetValues(typeof(BudgetLine)))
        {
            if (!m_Game.BudgetLineUnlocked(line)) continue;
            any = true;
            var row = new LineRow { Line = line };

            RectTransform top = Row(m_Content, 22f);
            Text(top, LineNames[(int)line], 16, Color.white).GetComponent<LayoutElement>().minWidth = 80f;
            row.Slider = MakeSlider(top, budget.GetFunding(line), m_Game.Balance);
            row.Value = Text(top, "", 16, Color.white);
            row.Value.GetComponent<LayoutElement>().minWidth = 52f;
            row.Value.alignment = TextAlignmentOptions.Right;
            row.Detail = Text(m_Content, "", 13, MutedColor);

            BudgetLine captured = line;
            row.Slider.onValueChanged.AddListener(step =>
            {
                m_Sim.Budget.SetFunding(captured, step * m_Game.Balance.FundingStep);
                m_Game.NotifyBudgetChanged();
                Refresh();
            });
            m_Rows.Add(row);
        }
        if (!any) Text(m_Content, "Nothing to fund yet: build parks, power, water or civic buildings first.", 14, MutedColor);
        else Text(m_Content, "Funding scales a line's upkeep, and with diminishing returns the reach and effect of its buildings.", 13, MutedColor);
    }

    private void BuildLoans()
    {
        if (m_Sim.Budget.Loans.Count == 0) Text(m_Content, "No loans.", 14, MutedColor);
        for (int i = 0; i < m_Sim.Budget.Loans.Count; i++)
        {
            int index = i;
            Loan loan = m_Sim.Budget.Loans[i];
            RectTransform row = Row(m_Content, 28f);
            Text(row, $"Loan ${loan.Amount:N0}: ${loan.DailyPayment:N0}/day, {loan.DaysLeft} days left", 15, Color.white, flexible: true);
            float cost = m_Sim.Budget.RemainingPrincipal(i);
            Button repay = MakeButton(row, $"Repay ${cost:N0}", () =>
            {
                if (m_Sim.RepayLoan(index))
                {
                    m_Game.NotifyBudgetChanged();
                    GameEvents.RaiseNotification("Loan repaid.");
                }
                SelectTab(Tab.Loans);
            }, 130f);
            repay.interactable = m_Sim.Economy.CanAfford(cost);
        }

        float offer = m_Sim.LoanOffer();
        BalanceConfig config = m_Game.Balance;
        float interest = config.LoanInterest * m_Sim.TechModifiers.LoanInterestMultiplier;
        float daily = offer * (1f + interest) / config.LoanTermDays;
        RectTransform offerRow = Row(m_Content, 28f);
        Text(offerRow, $"Offer ${offer:N0} now, ${daily:N0}/day for {config.LoanTermDays} days ({interest:P0} interest)", 15, Color.white, flexible: true);
        Button take = MakeButton(offerRow, "Take loan", () =>
        {
            if (m_Sim.TakeLoan())
            {
                m_Game.NotifyBudgetChanged();
                GameEvents.RaiseNotification($"Loan taken: ${offer:N0} now, ${daily:N0}/day for {config.LoanTermDays} days.");
            }
            SelectTab(Tab.Loans);
        }, 110f);
        take.interactable = m_Sim.Budget.CanBorrow;
        if (!m_Sim.Budget.CanBorrow) Text(m_Content, $"At most {config.MaxLoans} loans at once.", 13, MutedColor);
        else Text(m_Content, "Paying a loan off early costs only the principal still owed.", 13, MutedColor);
    }

    private void BuildOrdinances()
    {
        TechSystem tech = m_Sim.Tech;
        if (tech == null)
        {
            Text(m_Content, "Ordinances need ages and research.", 14, MutedColor);
            return;
        }

        int shown = 0;
        foreach (OrdinanceDefinition ordinance in tech.Techs.Ordinances)
        {
            bool unlocked = tech.IsUnlocked(ordinance);
            if (!unlocked && ordinance.Age > tech.CurrentAge) continue;
            shown++;

            OrdinanceDefinition captured = ordinance;
            RectTransform row = Row(m_Content, 26f);
            bool enacted = tech.IsEnacted(ordinance);
            Text(row, ordinance.DisplayName, 16, unlocked ? Color.white : MutedColor, flexible: true);
            if (unlocked)
            {
                Text(row, CostText(ordinance), 14, MutedColor);
                MakeButton(row, enacted ? "Repeal" : "Enact", () =>
                {
                    if (tech.IsEnacted(captured)) tech.Repeal(captured);
                    else tech.Enact(captured);
                    m_Game.NotifyBudgetChanged();
                    SelectTab(Tab.Ordinances);
                }, 76f).GetComponent<Image>().color = enacted ? ActiveColor : ButtonColor;
                Text(m_Content, ordinance.Description, 13, MutedColor);
            }
            else
            {
                Text(row, $"needs {ordinance.RequiredTech.DisplayName}", 14, MutedColor);
            }
        }
        if (shown == 0) Text(m_Content, "No ordinances yet: research the techs that enable them.", 14, MutedColor);
    }

    private string CostText(OrdinanceDefinition ordinance)
    {
        int population = m_Sim.Population.Population;
        float cost = ordinance.DailyCost(population);
        return cost <= 0f ? "free" : $"${cost:N0}/day";
    }

    // --- Refresh ---

    private void Refresh()
    {
        if (m_Sim == null) return;
        RefreshLedger();
        BudgetBreakdown ledger = m_Sim.Ledger();
        foreach (LineRow row in m_Rows)
        {
            float funding = m_Sim.Budget.GetFunding(row.Line);
            row.Slider.SetValueWithoutNotify(Mathf.Round(funding / m_Game.Balance.FundingStep));
            row.Value.text = $"{funding:P0}";
            string detail = $"upkeep ${ledger.UpkeepByLine[(int)row.Line]:N0}/day  -  effect x{m_Sim.Budget.EffectFactor(row.Line):0.00}, reach x{m_Sim.Budget.ReachFactor(row.Line):0.00}";
            if (row.Line == BudgetLine.Power && m_Sim.Power.Supply > 0 && funding < 1f)
            {
                bool short_ = m_Sim.Power.Supply < m_Sim.Power.Demand;
                detail += short_
                    ? $"\n<color=#F2C14E>Supply {m_Sim.Power.Supply} is below demand {m_Sim.Power.Demand}: the furthest blocks go dark.</color>"
                    : $"\nSupply {m_Sim.Power.Supply}, demand {m_Sim.Power.Demand}.";
            }
            row.Detail.text = detail;
        }
    }

    private void RefreshLedger()
    {
        BudgetBreakdown l = m_Sim.Ledger();
        float services = 0f;
        foreach (float line in l.UpkeepByLine) services += line;
        services += l.OtherUpkeep;
        string mult = Mathf.Approximately(l.TechUpkeepMultiplier, 1f) ? "" : $" (x{l.TechUpkeepMultiplier:0.00} techs)";
        string net = l.Net >= 0f ? $"<color=#6BD67A>+${l.Net:N0}</color>" : $"<color=#F26B5B>-${-l.Net:N0}</color>";
        m_Ledger.text = $"Income +${l.Income:N0}  (homes ${l.IncomeResidential:N0}, shops ${l.IncomeCommercial:N0}, industry ${l.IncomeIndustrial:N0})\n" +
                        $"Costs -${l.Expense:N0}  (buildings ${services:N0}, roads ${l.Roads:N0}, pipes ${l.Pipes:N0}{mult}, loans ${l.Loans:N0}, ordinances ${l.Ordinances:N0})\n" +
                        $"Net {net} / day, as of today's city";
    }

    // --- Widgets ---

    private static RectTransform Row(Transform parent, float height) => UiKit.Row(parent, height);

    private static TMP_Text Text(Transform parent, string text, float size, Color color, bool flexible = false) => UiKit.Text(parent, text, size, color, flexible);

    private static Button MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, float width) => UiKit.MakeButton(parent, label, onClick, width, ButtonColor);

    // Whole steps of 10%, from FundingMin to FundingMax.
    private static Slider MakeSlider(Transform parent, float funding, BalanceConfig config)
    {
        GameObject go = DefaultControls.CreateSlider(new DefaultControls.Resources());
        go.transform.SetParent(parent, false);
        var element = go.AddComponent<LayoutElement>();
        element.flexibleWidth = 1f;
        element.minHeight = 20f;
        foreach (Image image in go.GetComponentsInChildren<Image>())
        {
            image.color = image.name == "Background" ? new Color(0.20f, 0.23f, 0.28f)
                : image.name == "Fill" ? new Color(0.30f, 0.55f, 0.95f)
                : Color.white;
        }
        var slider = go.GetComponent<Slider>();
        slider.wholeNumbers = true;
        slider.minValue = Mathf.Round(config.FundingMin / config.FundingStep);
        slider.maxValue = Mathf.Round(config.FundingMax / config.FundingStep);
        slider.SetValueWithoutNotify(Mathf.Round(funding / config.FundingStep));
        return slider;
    }
}
