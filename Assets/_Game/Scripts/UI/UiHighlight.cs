using UnityEngine;
using UnityEngine.UI;

// A pulsing frame around one control (M19f): the tutorial points at the button an objective needs. One at a time; the
// frame is a child of the target, ignores layout and never takes clicks.
public sealed class UiHighlight : MonoBehaviour
{
    private static UiHighlight s_Current;
    private Image[] m_Edges;

    public static Transform Target => s_Current != null ? s_Current.transform.parent : null;

    public static void Show(RectTransform target)
    {
        if (target == null || !target.gameObject.activeInHierarchy)
        {
            Clear();
            return;
        }
        if (s_Current != null && s_Current.transform.parent == target) return;
        Clear();

        var go = new GameObject("TutorialHighlight", typeof(RectTransform), typeof(LayoutElement), typeof(UiHighlight));
        go.transform.SetParent(target, false);
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-4f, -4f);
        rect.offsetMax = new Vector2(4f, 4f);

        var highlight = go.GetComponent<UiHighlight>();
        highlight.m_Edges = new Image[4];
        for (int i = 0; i < 4; i++)
        {
            var edge = new GameObject("Edge" + i, typeof(RectTransform), typeof(Image));
            edge.transform.SetParent(go.transform, false);
            var image = edge.GetComponent<Image>();
            image.raycastTarget = false;
            var r = (RectTransform)edge.transform;
            const float t = 3f;
            switch (i)
            {
                case 0: r.anchorMin = new Vector2(0f, 1f); r.anchorMax = Vector2.one; r.pivot = new Vector2(0.5f, 1f); r.sizeDelta = new Vector2(0f, t); break;
                case 1: r.anchorMin = Vector2.zero; r.anchorMax = new Vector2(1f, 0f); r.pivot = new Vector2(0.5f, 0f); r.sizeDelta = new Vector2(0f, t); break;
                case 2: r.anchorMin = Vector2.zero; r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 0.5f); r.sizeDelta = new Vector2(t, 0f); break;
                default: r.anchorMin = new Vector2(1f, 0f); r.anchorMax = Vector2.one; r.pivot = new Vector2(1f, 0.5f); r.sizeDelta = new Vector2(t, 0f); break;
            }
            highlight.m_Edges[i] = image;
        }
        s_Current = highlight;
    }

    public static void Clear()
    {
        if (s_Current != null) Destroy(s_Current.gameObject);
        s_Current = null;
    }

    private void Update()
    {
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
        var color = new Color(1f, 0.82f, 0.25f, Mathf.Lerp(0.35f, 1f, pulse));
        foreach (Image edge in m_Edges) edge.color = color;
    }
}
