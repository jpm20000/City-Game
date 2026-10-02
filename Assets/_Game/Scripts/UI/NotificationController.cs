using TMPro;
using UnityEngine;

// Short fading toasts (e.g. "Not enough money") plus a persistent banner while money is negative.
// Uses unscaled time so toasts still fade while the game is paused.
public sealed class NotificationController : MonoBehaviour
{
    [SerializeField] private GameManager m_GameManager;

    [Header("Toast")]
    [SerializeField] private CanvasGroup m_Toast;
    [SerializeField] private TMP_Text m_ToastText;
    [SerializeField] private float m_ToastDuration = 2.5f;
    [SerializeField] private float m_FadeDuration = 0.4f;

    [Header("Debt banner")]
    [SerializeField] private GameObject m_Banner;
    [SerializeField] private TMP_Text m_BannerText;

    private float m_ToastTimer;

    private void OnEnable()
    {
        GameEvents.InsufficientFunds += OnInsufficientFunds;
        GameEvents.MoneyChanged += OnMoneyChanged;
    }

    private void OnDisable()
    {
        GameEvents.InsufficientFunds -= OnInsufficientFunds;
        GameEvents.MoneyChanged -= OnMoneyChanged;
    }

    private void Start()
    {
        SetToastAlpha(0f);
        float money = m_GameManager != null && m_GameManager.Economy != null ? m_GameManager.Economy.Money : 0f;
        OnMoneyChanged(money);
    }

    private void Update()
    {
        if (m_ToastTimer <= 0f) return;

        m_ToastTimer -= Time.unscaledDeltaTime;
        SetToastAlpha(Mathf.Clamp01(m_ToastTimer / m_FadeDuration));
    }

    public void ShowToast(string message)
    {
        if (m_ToastText != null) m_ToastText.text = message;
        m_ToastTimer = m_ToastDuration;
        SetToastAlpha(1f);
    }

    private void OnInsufficientFunds(float cost)
    {
        float money = m_GameManager != null ? m_GameManager.Economy.Money : 0f;
        ShowToast($"Not enough money — costs ${cost:N0}, you have ${Mathf.Max(0f, money):N0}");
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
