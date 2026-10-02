using System.Collections.Generic;
using TMPro;
using UnityEngine;

// "-$50" popups that rise and fade from where money was spent (GameEvents.MoneySpent). Labels track
// their world point so they stay put while the camera pans. Pooled; unscaled time so they also fade
// while paused. The template's parent must be a full-screen rect on an overlay canvas.
public sealed class FloatingTextController : MonoBehaviour
{
    [SerializeField] private TMP_Text m_Template;
    [SerializeField] private float m_Duration = 1.1f;
    [SerializeField] private float m_RisePixels = 60f;
    [SerializeField] private float m_WorldLift = 0.6f;
    [SerializeField] private Color m_SpendColor = new Color(0.95f, 0.40f, 0.35f);

    private sealed class Popup
    {
        public TMP_Text Text;
        public Vector3 World;
        public float Age;
    }

    private readonly List<Popup> m_Active = new();
    private readonly Stack<TMP_Text> m_Pool = new();
    private RectTransform m_Parent;
    private Camera m_Camera;

    private void Awake()
    {
        m_Camera = Camera.main;
        if (m_Template != null)
        {
            m_Parent = m_Template.rectTransform.parent as RectTransform;
            m_Template.gameObject.SetActive(false);
        }
    }

    private void OnEnable()
    {
        GameEvents.MoneySpent += OnMoneySpent;
    }

    private void OnDisable()
    {
        GameEvents.MoneySpent -= OnMoneySpent;
    }

    private void OnMoneySpent(float amount, Vector3 worldPosition)
    {
        Show($"-${amount:N0}", worldPosition + Vector3.up * m_WorldLift, m_SpendColor);
    }

    public void Show(string message, Vector3 worldPosition, Color color)
    {
        if (m_Template == null || m_Camera == null) return;

        TMP_Text text = m_Pool.Count > 0 ? m_Pool.Pop() : Instantiate(m_Template, m_Parent);
        text.text = message;
        text.color = color;
        text.gameObject.SetActive(true);
        Popup popup = new Popup { Text = text, World = worldPosition };
        m_Active.Add(popup);
        Place(popup);
    }

    private void LateUpdate()
    {
        for (int i = m_Active.Count - 1; i >= 0; i--)
        {
            Popup popup = m_Active[i];
            popup.Age += Time.unscaledDeltaTime;
            if (popup.Age >= m_Duration)
            {
                popup.Text.gameObject.SetActive(false);
                m_Pool.Push(popup.Text);
                m_Active.RemoveAt(i);
                continue;
            }
            Place(popup);
        }
    }

    private void Place(Popup popup)
    {
        float t = popup.Age / m_Duration;
        Vector2 screen = m_Camera.WorldToScreenPoint(popup.World);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Parent, screen, null, out Vector2 local))
        {
            popup.Text.rectTransform.anchoredPosition = local + Vector2.up * (m_RisePixels * t);
        }

        Color color = popup.Text.color;
        color.a = 1f - t * t;   // hold, then fade
        popup.Text.color = color;
    }
}
