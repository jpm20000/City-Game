using UnityEngine;

// Sprite-free fill bar: stretches the fill rect's anchor to the 0..1 value.
public sealed class UIMeter : MonoBehaviour
{
    [SerializeField] private RectTransform m_Fill;
    [SerializeField] private bool m_Vertical;

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
