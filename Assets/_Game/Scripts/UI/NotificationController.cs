using TMPro;
using UnityEngine;

// Short fading toasts (e.g. "Not enough money", GameEvents.Notification) plus a persistent banner while money is negative.
// Uses unscaled time so toasts still fade while the game is paused.
public sealed class NotificationController : MonoBehaviour
{
    [SerializeField] private GameManager m_GameManager;

    [Header("Toast")]
    [SerializeField] private CanvasGroup m_Toast;
    [SerializeField] private TMP_Text m_ToastText;
    [SerializeField] private float m_ToastDuration = 2.5f;
    [SerializeField] private float m_FadeDuration = 0.4f;

    [Header("City events")]
    [SerializeField] private int[] m_PopulationMilestones = { 50, 100, 250, 500, 1000, 2000 };

    [Header("Debt banner")]
    [SerializeField] private GameObject m_Banner;
    [SerializeField] private TMP_Text m_BannerText;

    private float m_ToastTimer;
    private int m_MilestoneIndex;     // next milestone to announce
    private bool m_WasUnhappy;
    private bool m_HadPower;          // a connected plant was supplying power
    private bool m_WasShort;          // some grown buildings were unpowered despite a plant
    private bool m_NudgedNoPower;     // "build a power plant" hint already shown

    private void OnEnable()
    {
        GameEvents.InsufficientFunds += OnInsufficientFunds;
        GameEvents.MoneyChanged += OnMoneyChanged;
        GameEvents.Notification += ShowToast;
        GameEvents.PopulationChanged += OnPopulationChanged;
        GameEvents.HappinessChanged += OnHappinessChanged;
        GameEvents.CityLoaded += SyncCityState;
        GameEvents.PowerChanged += OnPowerChanged;
    }

    private void OnDisable()
    {
        GameEvents.InsufficientFunds -= OnInsufficientFunds;
        GameEvents.MoneyChanged -= OnMoneyChanged;
        GameEvents.Notification -= ShowToast;
        GameEvents.PopulationChanged -= OnPopulationChanged;
        GameEvents.HappinessChanged -= OnHappinessChanged;
        GameEvents.CityLoaded -= SyncCityState;
        GameEvents.PowerChanged -= OnPowerChanged;
    }

    private void Start()
    {
        SetToastAlpha(0f);
        float money = m_GameManager != null && m_GameManager.Economy != null ? m_GameManager.Economy.Money : 0f;
        OnMoneyChanged(money);
        SyncCityState();
    }

    private void Update()
    {
        if (m_ToastTimer <= 0f) return;

        m_ToastTimer -= Time.unscaledDeltaTime;
        SetToastAlpha(Mathf.Clamp01(m_ToastTimer / m_FadeDuration));
    }

    private void ShowToast(string message)
    {
        if (m_ToastText != null) m_ToastText.text = message;
        m_ToastTimer = m_ToastDuration;
        SetToastAlpha(1f);
    }

    private void OnInsufficientFunds(float cost)
    {
        float money = m_GameManager != null ? m_GameManager.Economy.Money : 0f;
        ShowToast($"<color=#F26659>Not enough money</color> — costs ${cost:N0}, you have ${Mathf.Max(0f, money):N0}");
    }

    // After a load or new game, treat the current state as already announced.
    private void SyncCityState()
    {
        if (m_GameManager == null || m_GameManager.Population == null) return;

        PopulationSystem population = m_GameManager.Population;
        m_MilestoneIndex = 0;
        while (m_MilestoneIndex < m_PopulationMilestones.Length && population.Population >= m_PopulationMilestones[m_MilestoneIndex])
        {
            m_MilestoneIndex++;
        }
        m_WasUnhappy = population.AverageHappiness < m_GameManager.Balance.LowHappinessThreshold;

        PowerSystem power = m_GameManager.Simulation.Power;
        m_HadPower = power.Supply > 0;
        m_WasShort = power.Supply > 0 && power.UnpoweredCells > 0;
        m_NudgedNoPower = false;   // a loaded city without power still gets the hint
    }

    // Each fires once per change of state, not on every grid change.
    private void OnPowerChanged(int supply, int demand, int unpoweredCells)
    {
        if (m_GameManager == null || m_GameManager.Population == null) return;

        if (supply > 0 && !m_HadPower)
        {
            ShowToast("<color=#FFD133>Power plant online</color> — buildings along its roads can now grow past level 1. [V] shows the power grid.");
        }
        else if (supply == 0 && m_HadPower && demand > 0)
        {
            ShowToast("<color=#F26659>Power lost</color> — no plant is connected to a road. Buildings can't upgrade and residents are unhappy.");
        }
        m_HadPower = supply > 0;

        bool shortage = supply > 0 && unpoweredCells > 0;
        if (shortage && !m_WasShort)
        {
            ShowToast($"<color=#F26659>Power shortage</color> — {unpoweredCells} building{(unpoweredCells == 1 ? "" : "s")} without power. Build another plant or connect their roads. [V] shows the power grid.");
        }
        m_WasShort = shortage;

        int grace = m_GameManager.Balance.SmallTownGracePopulation;
        if (supply == 0 && !m_NudgedNoPower && m_GameManager.Population.Population >= grace)
        {
            ShowToast("Buildings can't grow past level 1 without power — build a <color=#FFD133>Power Plant</color> beside a road.");
            m_NudgedNoPower = true;
        }
    }

    private void OnPopulationChanged(int population, int jobs)
    {
        int reached = -1;
        while (m_MilestoneIndex < m_PopulationMilestones.Length && population >= m_PopulationMilestones[m_MilestoneIndex])
        {
            reached = m_PopulationMilestones[m_MilestoneIndex++];
        }
        if (reached > 0) ShowToast($"Population milestone: {reached:N0} residents!");
    }

    // Fires once per drop below the threshold, not every unhappy day.
    private void OnHappinessChanged(float happiness)
    {
        if (m_GameManager == null || m_GameManager.Balance == null) return;

        bool unhappy = happiness < m_GameManager.Balance.LowHappinessThreshold;
        if (unhappy && !m_WasUnhappy)
        {
            ShowToast($"Happiness below {m_GameManager.Balance.LowHappinessThreshold:P0} — residents are leaving. Hover Happiness to see why.");
        }
        m_WasUnhappy = unhappy;
    }

    private void OnMoneyChanged(float money)
    {
        if (m_Banner == null) return;

        bool inDebt = money < 0f;
        if (m_Banner.activeSelf != inDebt) m_Banner.SetActive(inDebt);
        if (inDebt && m_BannerText != null)
        {
            m_BannerText.text = $"In debt: -${-money:N0}. Raise taxes or cut upkeep — roads and buildings are locked until you're out of debt.";
        }
    }

    private void SetToastAlpha(float alpha)
    {
        if (m_Toast == null) return;
        m_Toast.alpha = alpha;
        m_Toast.gameObject.SetActive(alpha > 0f);
    }
}
