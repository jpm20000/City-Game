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

    private void Start()
    {
        if (m_Button != null) m_Button.onClick.AddListener(() => AudioController.Play(SfxId.Click));
    }

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
        if (Icon != null) SetIcon(Icon);    // the cost line may have come or gone: lay the icon out again
    }

    public void SetActive(bool active)
    {
        UiTheme.PaintButton(m_Background, active ? UiTheme.ButtonLook.Active : UiTheme.ButtonLook.Normal,
            active ? m_ActiveColor : m_NormalColor);
    }

    // For buttons with more states than active / inactive (the tech panel): the flat colour, or with the button art
    // the look and an optional tint (M32d).
    public void SetBackground(Color color, UiTheme.ButtonLook look = UiTheme.ButtonLook.Normal, Color? tint = null)
    {
        UiTheme.PaintButton(m_Background, look, color, tint);
    }

    public TMP_Text Label => m_Label;
    public TMP_Text Cost => m_Cost;
    public Sprite Icon => m_Icon != null && m_Icon.gameObject.activeSelf ? m_Icon.sprite : null;

    private Image m_Icon;                  // (M32c) made on the first SetIcon
    private bool m_LabelRectSaved;
    private Vector2 m_LabelAnchorMin, m_LabelAnchorMax, m_LabelPosition, m_LabelSize, m_LabelPivot;
    private bool m_LabelAutoSize;
    private float m_LabelSizeMin, m_LabelSizeMax;

    // (M32c) An icon over a one-line label (the cost line stays at the bottom); null puts the label back as it was, so a
    // button without an icon looks as before M32. Call after Setup: the layout depends on whether there is a cost line.
    public void SetIcon(Sprite icon)
    {
        if (icon == null && m_Icon == null) return;
        if (m_Icon == null)
        {
            var go = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(transform, false);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            m_Icon = go.GetComponent<Image>();
            m_Icon.raycastTarget = false;
            m_Icon.preserveAspect = true;
            if (m_Swatch != null) go.transform.SetSiblingIndex(m_Swatch.transform.GetSiblingIndex());
        }
        m_Icon.gameObject.SetActive(icon != null);
        m_Icon.sprite = icon;
        if (m_Label == null) return;
        var label = m_Label.rectTransform;
        if (!m_LabelRectSaved)
        {
            m_LabelAnchorMin = label.anchorMin; m_LabelAnchorMax = label.anchorMax; m_LabelPivot = label.pivot;
            m_LabelPosition = label.anchoredPosition; m_LabelSize = label.sizeDelta;
            m_LabelAutoSize = m_Label.enableAutoSizing; m_LabelSizeMin = m_Label.fontSizeMin; m_LabelSizeMax = m_Label.fontSizeMax;
            m_LabelRectSaved = true;
        }
        if (icon == null)
        {
            label.anchorMin = m_LabelAnchorMin; label.anchorMax = m_LabelAnchorMax; label.pivot = m_LabelPivot;
            label.anchoredPosition = m_LabelPosition; label.sizeDelta = m_LabelSize;
            m_Label.enableAutoSizing = m_LabelAutoSize; m_Label.fontSizeMin = m_LabelSizeMin; m_Label.fontSizeMax = m_LabelSizeMax;
            return;
        }
        // 64 tall, top to bottom: the icon (under the zone swatch), one label line, and the cost line when shown.
        bool cost = m_Cost != null && m_Cost.gameObject.activeSelf;
        // The icon fills the space above the label (28 px with a cost line, 34 without; more in a taller flyout button).
        var rect = m_Icon.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(4f, cost ? 33f : 24f);
        rect.offsetMax = new Vector2(-4f, cost ? -3f : -6f);
        label.anchorMin = new Vector2(0f, 0f);
        label.anchorMax = new Vector2(1f, 0f);
        label.pivot = new Vector2(0.5f, 0f);
        label.anchoredPosition = new Vector2(0f, cost ? 19f : 6f);
        label.sizeDelta = new Vector2(-6f, cost ? 14f : 18f);
        m_Label.enableAutoSizing = true;
        m_Label.fontSizeMax = Mathf.Min(m_LabelAutoSize ? m_LabelSizeMax : m_Label.fontSize, cost ? 13f : 15f);
        m_Label.fontSizeMin = 10f;
    }

    // The button tint alone is too subtle, so also dim the label and turn the cost red.
    public void SetAffordable(bool affordable)
    {
        if (m_Button != null) m_Button.interactable = affordable;
        if (m_Label != null) m_Label.alpha = affordable ? 1f : m_UnaffordableLabelAlpha;
        if (m_Icon != null) m_Icon.color = new Color(1f, 1f, 1f, affordable ? 1f : m_UnaffordableLabelAlpha);
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
