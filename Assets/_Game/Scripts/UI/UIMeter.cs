using UnityEngine;
using UnityEngine.UI;

// Sprite-free fill bar: stretches the fill rect's anchor to the 0..1 value. (M32d) A horizontal meter takes the theme's
// meter art on Awake (the fill keeps its colour as a tint); without it, or vertical, it stays flat.
public sealed class UIMeter : MonoBehaviour
{
    [SerializeField] private RectTransform m_Fill;
    [SerializeField] private bool m_Vertical;

    private void Awake()
    {
        if (m_Fill != null) UiTheme.PaintMeter(GetComponent<Image>(), m_Fill.GetComponent<Image>(), m_Vertical);
    }

    public void SetValue(float value)
    {
        if (m_Fill == null) return;

        value = Mathf.Clamp01(value);
        Vector2 max = m_Fill.anchorMax;
        if (m_Vertical) max.y = value;
        else max.x = value;
        m_Fill.anchorMax = max;
    }
}
