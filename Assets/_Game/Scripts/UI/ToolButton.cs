using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One toolbar button. Background colour shows the active tool; Button.interactable shows affordability.
public sealed class ToolButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Button m_Button;
    [SerializeField] private Image m_Background;
    [SerializeField] private Image m_Swatch;
    [SerializeField] private TMP_Text m_Label;
    [SerializeField] private TMP_Text m_Cost;
    [SerializeField] private Color m_NormalColor = new Color(0.24f, 0.27f, 0.33f);
    [SerializeField] private Color m_ActiveColor = new Color(0.30f, 0.55f, 0.92f);
    [SerializeField] private Color m_UnaffordableCostColor = new Color(0.95f, 0.40f, 0.35f);
    [SerializeField, Range(0f, 1f)] private float m_UnaffordableLabelAlpha = 0.45f;

    private Color m_CostColor;
    private bool m_CostColorCached;

    public Button Button => m_Button;
    public string Tooltip { get; set; }

    public event Action<ToolButton> Hovered;
    public event Action<ToolButton> Unhovered;

    // swatch.a == 0 hides the swatch; empty cost hides the cost line.
    public void Setup(string label, string cost, Color swatch, string tooltip)
    {
        if (m_Label != null) m_Label.text = label;
        if (m_Cost != null)
        {
            m_Cost.text = cost;
            m_Cost.gameObject.SetActive(!string.IsNullOrEmpty(cost));
        }
        if (m_Swatch != null)
        {
            m_Swatch.color = swatch;
            m_Swatch.gameObject.SetActive(swatch.a > 0f);
        }
        Tooltip = tooltip;
        SetActive(false);
    }

    public void SetActive(bool active)
    {
        if (m_Background != null) m_Background.color = active ? m_ActiveColor : m_NormalColor;
    }

    // The button tint alone is too subtle, so also dim the label and turn the cost red.
    public void SetAffordable(bool affordable)
    {
        if (m_Button != null) m_Button.interactable = affordable;
        if (m_Label != null) m_Label.alpha = affordable ? 1f : m_UnaffordableLabelAlpha;
        if (m_Cost != null)
        {
            if (!m_CostColorCached)
            {
                m_CostColor = m_Cost.color;
                m_CostColorCached = true;
            }
            m_Cost.color = affordable ? m_CostColor : m_UnaffordableCostColor;
        }
    }

    public void OnPointerEnter(PointerEventData eventData) => Hovered?.Invoke(this);
    public void OnPointerExit(PointerEventData eventData) => Unhovered?.Invoke(this);
}
