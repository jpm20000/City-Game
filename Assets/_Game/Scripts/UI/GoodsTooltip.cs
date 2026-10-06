using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Hover the HUD's G meter to see what goods are and how the city stands (M24 follow-up). Added to the meter's group by
// HUDController; the panel is built here, below the meter, and follows the Settings tooltip delay.
public sealed class GoodsTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private static readonly Color TealColor = new Color(0.35f, 0.80f, 0.70f);

    private GameManager m_GameManager;
    private GameObject m_Root;
    private TMP_Text m_Text;
    private float m_Due = -1f;     // unscaled time at which the pending tooltip shows (-1 = none)
    private readonly StringBuilder m_Builder = new();

    public bool IsShown => m_Root != null && m_Root.activeSelf;
    public string Text => m_Text != null ? m_Text.text : string.Empty;

    public void Init(GameManager game)
    {
        m_GameManager = game;
        Build();
    }

    private void OnEnable() => GameEvents.GoodsChanged += OnGoodsChanged;

    private void OnDisable()
    {
        GameEvents.GoodsChanged -= OnGoodsChanged;
        m_Due = -1f;
        if (m_Root != null) m_Root.SetActive(false);
    }

    private void Build()
    {
        m_Root = new GameObject("GoodsTooltip", typeof(RectTransform), typeof(Canvas), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement));
        m_Root.transform.SetParent(transform, false);
        m_Root.GetComponent<LayoutElement>().ignoreLayout = true;     // the HUD row must not make room for it
        var canvas = m_Root.GetComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 50;
        var rect = (RectTransform)m_Root.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(24f, -8f);
        rect.sizeDelta = new Vector2(330f, 0f);
        var image = m_Root.GetComponent<Image>();
        image.color = UiKit.PanelColor;
        image.raycastTarget = false;
        var layout = m_Root.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 10);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        m_Root.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        m_Text = UiKit.Text(m_Root.transform, string.Empty, 15f, UiKit.BodyColor);
        m_Text.raycastTarget = false;
        m_Root.SetActive(false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        Refresh();
        float delay = GameSettings.TooltipDelay;
        if (delay <= 0f) m_Root.SetActive(true);
        else m_Due = Time.unscaledTime + delay;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        m_Due = -1f;
        m_Root.SetActive(false);
    }

    private void Update()
    {
        if (m_Due < 0f || Time.unscaledTime < m_Due) return;
        m_Due = -1f;
        m_Root.SetActive(true);
    }

    private void OnGoodsChanged(GoodsReport report)
    {
        if (IsShown) Refresh();
    }

    private void Refresh()
    {
        if (m_Text == null || m_GameManager == null || m_GameManager.Simulation == null) return;
        SimulationSystem sim = m_GameManager.Simulation;
        GoodsReport last = sim.Goods.Last;
        float hold = m_GameManager.Balance != null ? m_GameManager.Balance.GoodsLevel3Supply : 0.7f;
        m_Text.text = Describe(last, sim.GoodsSupply(), hold, sim.Goods.Stock);
    }

    // What the meter means and the last day's numbers. Pure, so it can be read without a scene.
    public static string Describe(GoodsReport last, float supply, float hold, float stock)
    {
        var b = new StringBuilder();
        string teal = ColorUtility.ToHtmlStringRGB(TealColor);
        b.Append($"<b><color=#{teal}>Goods</color></b>\n");
        b.Append("Factories make goods; shops and homes use them. What the city cannot make it imports, and a surplus is exported (both are lines in the Budget).\n");
        b.Append($"<b>Supply  {supply:P0}</b> of what the city wants\n");
        b.Append($"Made {last.Produced:F0}  ·  wanted {last.Demanded:F0}  ·  imported {last.Imported:F0}  ·  exported {last.Exported:F0}  ·  stock {stock:F0}\n");
        if (supply < hold) b.Append($"<color=#F26659>Below {hold:P0}: homes and shops stop at level 2 and shops earn less. Zone more Industrial land.</color>\n");
        else if (supply < 1f) b.Append($"<color=#F2C14E>Under 100%: still growing, but a bigger city will hold at {hold:P0}. Zone more Industrial land.</color>\n");
        else b.Append("Covered: nothing is held back.\n");
        b.Append("<size=85%><color=#9AA3B2>The Goods view in Views shows who makes and who is short.</color></size>");
        return b.ToString();
    }
}
