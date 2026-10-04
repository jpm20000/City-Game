using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// Hover the HUD happiness readout to see what is raising or lowering it (PopulationSystem.Happiness).
// Lives on the hover target, which needs a raycastable Graphic.
public sealed class HappinessTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private GameManager m_GameManager;
    [SerializeField] private GameObject m_Root;
    [SerializeField] private TMP_Text m_Text;
    [SerializeField] private Color m_GoodColor = new Color(0.45f, 0.85f, 0.45f);
    [SerializeField] private Color m_BadColor = new Color(0.95f, 0.40f, 0.35f);

    private readonly StringBuilder m_Builder = new();

    private void OnEnable()
    {
        GameEvents.HappinessChanged += OnHappinessChanged;
    }

    private void OnDisable()
    {
        GameEvents.HappinessChanged -= OnHappinessChanged;
    }

    private void Start()
    {
        if (m_Root != null) m_Root.SetActive(false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (m_Root == null) return;
        m_Root.SetActive(true);
        Refresh();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (m_Root != null) m_Root.SetActive(false);
    }

    private void OnHappinessChanged(float happiness)
    {
        if (m_Root != null && m_Root.activeSelf) Refresh();
    }

    private void Refresh()
    {
        if (m_Text == null || m_GameManager == null || m_GameManager.Population == null) return;

        PopulationSystem population = m_GameManager.Population;
        HappinessBreakdown h = population.Happiness;
        BalanceConfig balance = m_GameManager.Balance;
        string good = ColorUtility.ToHtmlStringRGB(m_GoodColor);
        string bad = ColorUtility.ToHtmlStringRGB(m_BadColor);

        m_Builder.Clear();
        m_Builder.Append("<b>Happiness</b>\n");
        Line("Base", h.Base, good, bad, plain: true);
        Line("Unemployment", h.Unemployment, good, bad);
        Line("Taxes", h.Taxes, good, bad);
        Line("Pollution (near homes)", h.Pollution, good, bad);
        Line("Parks & services", h.Services, good, bad);
        Line("Power outages", h.Power, good, bad);
        Line("No water", h.Water, good, bad);
        Line("Crime", h.Crime, good, bad);
        Line("Traffic (commutes)", h.Traffic, good, bad);
        Line("Fire risk", h.Fire, good, bad);
        Line("Sickness (no health care)", h.Health, good, bad);
        Line("Homelessness", h.Homeless, good, bad);
        Line("Technology", h.Technology, good, bad);
        Line("Ordinances", h.Ordinances, good, bad);
        Line("Heritage (kept blocks)", h.Heritage, good, bad);
        m_Builder.Append($"<b>Total  {h.Total:P0}</b>");

        if (population.Population > 0 && population.Population < balance.SmallTownGracePopulation)
        {
            m_Builder.Append($"\n<size=85%><color=#9AA3B2>Small town: unemployment, pollution, outages and dry homes count fully from {balance.SmallTownGracePopulation} residents.</color></size>");
        }
        if (h.Total < balance.LowHappinessThreshold)
        {
            m_Builder.Append($"\n<color=#{bad}>Below {balance.LowHappinessThreshold:P0}: residents are leaving and homes grow slower.</color>");
        }
        m_Text.text = m_Builder.ToString();
    }

    // Zero terms are skipped so the list only shows what currently matters.
    private void Line(string label, float value, string good, string bad, bool plain = false)
    {
        if (!plain && Mathf.Abs(value) < 0.005f) return;

        string formatted = $"{(value >= 0f ? "+" : "-")}{Mathf.Abs(value):P0}";
        if (!plain) formatted = $"<color=#{(value >= 0f ? good : bad)}>{formatted}</color>";
        m_Builder.Append($"{label}  {formatted}\n");
    }
}
