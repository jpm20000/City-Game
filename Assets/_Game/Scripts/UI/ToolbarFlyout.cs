using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// M20b: a row of buttons that opens above a toolbar button (the building groups; the Views button in 20c). Built in code
// under its anchor, so there is no prefab edit; a nested canvas draws it over the rest of the toolbar. Only one is open
// at a time. It closes on Esc (ToolbarController registers CloseOpen with the EscapeRouter), on a right click and on a
// left click outside it and its anchor.
public sealed class ToolbarFlyout : MonoBehaviour
{
    private static ToolbarFlyout s_Open;

    private RectTransform m_Panel;
    private RectTransform m_Anchor;

    public RectTransform Content => m_Panel;
    public bool IsOpen => m_Panel != null && m_Panel.gameObject.activeSelf;
    public event System.Action Opened;
    public event System.Action Closed;

    // columns > 0 lays the buttons out in a grid of fixed-size cells instead of one row (the Views flyout).
    public static ToolbarFlyout Create(RectTransform anchor, string name, int columns = 0, Vector2 cell = default)
    {
        var flyout = anchor.gameObject.AddComponent<ToolbarFlyout>();
        flyout.m_Anchor = anchor;
        var go = new GameObject(name, typeof(RectTransform), typeof(Image),
            columns > 0 ? typeof(GridLayoutGroup) : typeof(HorizontalLayoutGroup),
            typeof(ContentSizeFitter), typeof(LayoutElement), typeof(Canvas), typeof(GraphicRaycaster));
        go.transform.SetParent(anchor, false);
        flyout.m_Panel = (RectTransform)go.transform;
        flyout.m_Panel.anchorMin = flyout.m_Panel.anchorMax = new Vector2(0.5f, 1f);
        flyout.m_Panel.pivot = new Vector2(0.5f, 0f);
        flyout.m_Panel.anchoredPosition = new Vector2(0f, 30f);   // clears the section header above the button row
        go.GetComponent<Image>().color = UiKit.PanelColor;
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        var canvas = go.GetComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 30;
        if (columns > 0)
        {
            var grid = go.GetComponent<GridLayoutGroup>();
            grid.padding = new RectOffset(6, 6, 6, 6);
            grid.spacing = new Vector2(4f, 4f);
            grid.cellSize = cell;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
        }
        else
        {
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
        }
        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        go.SetActive(false);
        return flyout;
    }

    // Height of the open flyout (plus its gap) in world units, 0 when none is open: the tooltip rises by it.
    public static float OpenHeight => s_Open != null && s_Open.IsOpen
        ? (s_Open.m_Panel.rect.height + s_Open.m_Panel.anchoredPosition.y) * s_Open.m_Panel.lossyScale.y : 0f;

    public static bool CloseOpen()
    {
        if (s_Open == null || !s_Open.IsOpen) return false;
        s_Open.Hide();
        return true;
    }

    public void Toggle()
    {
        if (IsOpen) Hide();
        else Show();
    }

    public void Show()
    {
        if (s_Open != null && s_Open != this) s_Open.Hide();
        s_Open = this;
        m_Panel.gameObject.SetActive(true);
        Opened?.Invoke();
    }

    public void Hide()
    {
        if (m_Panel == null || !m_Panel.gameObject.activeSelf) return;
        m_Panel.gameObject.SetActive(false);
        if (s_Open == this) s_Open = null;
        Closed?.Invoke();
    }

    private void OnDisable() => Hide();

    private void Update()
    {
        if (!IsOpen) return;
        Mouse mouse = Mouse.current;
        if (mouse == null) return;
        if (mouse.rightButton.wasPressedThisFrame) { Hide(); return; }
        if (!mouse.leftButton.wasPressedThisFrame) return;
        Vector2 point = mouse.position.ReadValue();
        Canvas root = m_Panel.GetComponentInParent<Canvas>().rootCanvas;
        Camera cam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        if (!RectTransformUtility.RectangleContainsScreenPoint(m_Panel, point, cam)
            && !RectTransformUtility.RectangleContainsScreenPoint(m_Anchor, point, cam))
        {
            Hide();
        }
    }
}
