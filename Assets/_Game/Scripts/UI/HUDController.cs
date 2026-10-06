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
    }

    private void OnPopulationChanged(int population, int jobs)
    {
        PopulationSystem stats = m_GameManager != null ? m_GameManager.Population : null;
        if (m_PopulationText != null)
        {
            m_PopulationText.text = stats != null ? $"Pop {population:N0} / {stats.Housing:N0}" : $"Pop {population:N0}";
        }
        if (m_JobsText != null)
        {
            m_JobsText.text = stats != null ? $"Jobs {stats.Employed:N0} / {jobs:N0}" : $"Jobs {jobs:N0}";
        }
    }

    private void OnHappinessChanged(float happiness)
    {
        if (m_HappinessText != null)
        {
            // Below the threshold residents leave; flag it so the player hovers for the reasons.
            bool low = m_GameManager != null && m_GameManager.Balance != null && happiness < m_GameManager.Balance.LowHappinessThreshold;
            m_HappinessText.text = $"Happiness {happiness:P0}";
            m_HappinessText.color = low ? m_NegativeColor : Color.white;
        }
        if (m_HappinessMeter != null) m_HappinessMeter.SetValue(happiness);
    }

    private void OnPowerChanged(int supply, int demand, int unpoweredCells)
    {
        if (m_PowerText == null) return;

        if (supply == 0 && demand == 0)
        {
            m_PowerText.text = "Power  —";
            m_PowerText.color = m_IdleColor;
        }
        else if (supply == 0)
        {
            m_PowerText.text = "No power";
            m_PowerText.color = m_NegativeColor;
        }
        else
        {
            m_PowerText.text = $"Power {demand:N0} / {supply:N0}";
            m_PowerText.color = unpoweredCells > 0 ? m_NegativeColor : Color.white;
        }
    }

    // Piped ages read like power; well ages show the share of grown buildings a well reaches.
    private void OnWaterChanged(WaterStatus status)
    {
        if (m_WaterText == null) return;

        if (status.Mode == WaterRule.Coverage)
        {
            m_WaterText.text = status.GrownCells == 0 ? "Water  —" : $"Water {status.WateredShare:P0}";
            m_WaterText.color = status.GrownCells == 0 ? m_IdleColor : status.DryCells > 0 ? m_NegativeColor : Color.white;
        }
        else if (status.Supply == 0 && status.Demand == 0)
        {
            m_WaterText.text = "Water  —";
            m_WaterText.color = m_IdleColor;
        }
        else if (status.Supply == 0)
        {
            m_WaterText.text = "No water";
            m_WaterText.color = m_NegativeColor;
        }
        else
        {
            m_WaterText.text = $"Water {status.Demand:N0} / {status.Supply:N0}";
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
