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
    [SerializeField] private Color m_IdleColor = new Color(0.60f, 0.64f, 0.70f);

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

    private void OnDemandChanged(DemandSnapshot demand)
    {
        if (m_ResidentialDemand != null) m_ResidentialDemand.SetValue(demand.Residential);
        if (m_CommercialDemand != null) m_CommercialDemand.SetValue(demand.Commercial);
        if (m_IndustrialDemand != null) m_IndustrialDemand.SetValue(demand.Industrial);
    }
}
