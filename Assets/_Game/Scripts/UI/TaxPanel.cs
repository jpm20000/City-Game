using TMPro;
using UnityEngine;
using UnityEngine.UI;

// R/C/I tax sliders (whole percent). Rates apply immediately; happiness reacts on the next tick.
public sealed class TaxPanel : MonoBehaviour
{
    [SerializeField] private GameManager m_GameManager;
    [SerializeField] private GameObject m_Root;
    [SerializeField] private Button m_ToggleButton;
    [SerializeField] private Button m_CloseButton;
    [SerializeField] private Slider m_ResidentialSlider;
    [SerializeField] private Slider m_CommercialSlider;
    [SerializeField] private Slider m_IndustrialSlider;
    [SerializeField] private TMP_Text m_ResidentialValue;
    [SerializeField] private TMP_Text m_CommercialValue;
    [SerializeField] private TMP_Text m_IndustrialValue;
    [SerializeField] private TMP_Text m_Hint;
    [SerializeField] private int m_MaxPercent = 20;

    private EconomySystem m_Economy;

    private void Start()
    {
        if (m_GameManager == null || m_GameManager.Economy == null) return;
        m_Economy = m_GameManager.Economy;

        Setup(m_ResidentialSlider, m_ResidentialValue, m_Economy.TaxResidential, v => m_Economy.TaxResidential = v);
        Setup(m_CommercialSlider, m_CommercialValue, m_Economy.TaxCommercial, v => m_Economy.TaxCommercial = v);
        Setup(m_IndustrialSlider, m_IndustrialValue, m_Economy.TaxIndustrial, v => m_Economy.TaxIndustrial = v);

        if (m_ToggleButton != null) m_ToggleButton.onClick.AddListener(() => SetOpen(!m_Root.activeSelf));
        if (m_CloseButton != null) m_CloseButton.onClick.AddListener(() => SetOpen(false));
        GameEvents.CashFlowChanged += OnCashFlowChanged;
        GameEvents.CityLoaded += OnCityLoaded;
        SidePanels.Opened += OnSidePanelOpened;

        SetOpen(false);
    }

    private void OnDestroy()
    {
        GameEvents.CashFlowChanged -= OnCashFlowChanged;
        GameEvents.CityLoaded -= OnCityLoaded;
        SidePanels.Opened -= OnSidePanelOpened;
    }

    private void SetOpen(bool open)
    {
        m_Root.SetActive(open);
        if (!open) return;
        RefreshHint();
        SidePanels.RaiseOpened(this);
    }

    private void OnSidePanelOpened(object panel)
    {
        if (!ReferenceEquals(panel, this) && m_Root.activeSelf) m_Root.SetActive(false);
    }

    private void Setup(Slider slider, TMP_Text label, float rate, System.Action<float> apply)
    {
        if (slider == null) return;

        slider.wholeNumbers = true;
        slider.minValue = 0;
        slider.maxValue = m_MaxPercent;
        slider.SetValueWithoutNotify(Mathf.Round(rate * 100f));
        SetLabel(label, slider.value);
        slider.onValueChanged.AddListener(percent =>
        {
            apply(percent / 100f);
            SetLabel(label, percent);
            RefreshHint();
        });
    }

    // A load or new game replaced the rates; show them without re-applying.
    private void OnCityLoaded()
    {
        Sync(m_ResidentialSlider, m_ResidentialValue, m_Economy.TaxResidential);
        Sync(m_CommercialSlider, m_CommercialValue, m_Economy.TaxCommercial);
        Sync(m_IndustrialSlider, m_IndustrialValue, m_Economy.TaxIndustrial);
        if (m_Root.activeSelf) RefreshHint();
    }

    private static void Sync(Slider slider, TMP_Text label, float rate)
    {
        if (slider == null) return;
        slider.SetValueWithoutNotify(Mathf.Round(rate * 100f));
        SetLabel(label, slider.value);
    }

    private static void SetLabel(TMP_Text label, float percent)
    {
        if (label != null) label.text = $"{percent:0}%";
    }

    private void OnCashFlowChanged(float income, float expense)
    {
        if (m_Root.activeSelf) RefreshHint();
    }

    private void RefreshHint()
    {
        if (m_Hint == null || m_Economy == null) return;

        float threshold = m_GameManager.Balance.TaxPenaltyThreshold;
        float penalty = m_GameManager.Population.TaxHappinessPenalty(m_Economy.TaxResidential, m_Economy.TaxCommercial, m_Economy.TaxIndustrial);
        string happiness = penalty > 0f
            ? $"<color=#F2C14E>Taxes above {threshold:P0} cost {penalty:P0} happiness and slow growth in those zones.</color>"
            : $"Taxes above {threshold:P0} lower happiness and slow growth; below it, zones grow faster.";
        m_Hint.text = $"{happiness}\nYesterday: +${m_Economy.IncomePerDay:N0} income, -${m_Economy.ExpensePerDay:N0} costs.\n" +
                      "<size=85%><color=#9AA3B2>New rates apply from the next day.</color></size>";
    }
}
