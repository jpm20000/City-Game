using TMPro;
using UnityEngine;

// Small label that follows the pointer while a tool is active, showing PlacementController.CursorHint
// (the cost, or why the action is blocked). Its parent must be a full-screen rect on an overlay canvas.
public sealed class CursorHint : MonoBehaviour
{
    [SerializeField] private PlacementController m_Placement;
    [SerializeField] private InputReader m_InputReader;
    [SerializeField] private RectTransform m_Panel;
    [SerializeField] private TMP_Text m_Text;
    [SerializeField] private Vector2 m_Offset = new Vector2(18f, -18f);
    [SerializeField] private Color m_ValidColor = Color.white;
    [SerializeField] private Color m_InvalidColor = new Color(0.95f, 0.40f, 0.35f);

    private RectTransform m_Parent;

    private void Awake()
    {
        if (m_Panel != null) m_Parent = m_Panel.parent as RectTransform;
    }

    private void LateUpdate()
    {
        if (m_Panel == null || m_Text == null) return;

        string hint = m_Placement != null ? m_Placement.CursorHint : string.Empty;
        bool show = !string.IsNullOrEmpty(hint) && m_InputReader != null;
        if (m_Panel.gameObject.activeSelf != show) m_Panel.gameObject.SetActive(show);
        if (!show) return;

        if (m_Text.text != hint) m_Text.text = hint;
        m_Text.color = m_Placement.CursorHintValid ? m_ValidColor : m_InvalidColor;

        // Overlay canvas: no camera for the screen -> local conversion.
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Parent, m_InputReader.Pointer, null, out Vector2 local))
        {
            m_Panel.anchoredPosition = local + m_Offset;
        }
    }
}
