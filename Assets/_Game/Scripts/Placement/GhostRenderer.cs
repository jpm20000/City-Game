using UnityEngine;

public sealed class GhostRenderer : MonoBehaviour
{
    [SerializeField] private SpriteRenderer m_Renderer;
    [SerializeField] private Color m_ValidColor = new Color(0.2f, 0.9f, 0.3f, 0.5f);
    [SerializeField] private Color m_InvalidColor = new Color(0.9f, 0.2f, 0.2f, 0.5f);

    private void Awake()
    {
        if (m_Renderer == null) m_Renderer = GetComponent<SpriteRenderer>();
    }

    public void Show(Vector3 worldPosition, Vector2Int size, bool valid)
    {
        if (m_Renderer == null) return;

        m_Renderer.enabled = true;
        m_Renderer.color = valid ? m_ValidColor : m_InvalidColor;
        transform.position = worldPosition;
        transform.localScale = new Vector3(size.x, size.y, 1f);
    }

    public void Hide()
    {
        if (m_Renderer == null) return;

        m_Renderer.enabled = false;
    }
}
