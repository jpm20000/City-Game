using UnityEngine;

[RequireComponent(typeof(Camera))]
public sealed class IsoCameraController : MonoBehaviour
{
    [Header("Pan")]
    [SerializeField] private float m_PanSpeed = 12f;
    [SerializeField] private float m_EdgePanZonePx = 20f;
    [SerializeField] private float m_DiagonalDamping = 0.7f;

    [Header("Zoom")]
    [SerializeField] private float m_ZoomSpeed = 0.006f;
    [SerializeField] private float m_MinZoom = 4f;
    [SerializeField] private float m_MaxZoom = 20f;

    [Header("Bounds")]
    [SerializeField] private float m_GridWidth = 24f;
    [SerializeField] private float m_GridHeight = 24f;
    [SerializeField] private float m_SoftSpringForce = 40f;
    [SerializeField] private float m_SpringDamping = 0.88f;

    private Camera m_Camera;
    private InputReader m_InputReader;
    private Vector3 m_Velocity;

    private void Awake()
    {
        m_Camera = GetComponent<Camera>();
        m_InputReader = FindAnyObjectByType<InputReader>();
    }

    private void LateUpdate()
    {
        if (m_InputReader == null) return;

        ApplyZoom();
        ApplyPan();
        ApplyBounds();
    }

    private void ApplyZoom()
    {
        float scroll = m_InputReader.Zoom;
        if (Mathf.Approximately(scroll, 0f)) return;

        float oldSize = m_Camera.orthographicSize;
        float newSize = Mathf.Clamp(oldSize - scroll * m_ZoomSpeed, m_MinZoom, m_MaxZoom);
        if (Mathf.Approximately(oldSize, newSize)) return;

        Vector3 groundPoint = ScreenToGround(m_InputReader.Pointer);

        m_Camera.orthographicSize = newSize;

        Vector3 screenDelta = GroundToScreen(groundPoint) - (Vector3)m_InputReader.Pointer;
        Vector3 worldDelta = ScreenVectorToWorldDelta(screenDelta);
        transform.position -= worldDelta;
    }

    private void ApplyPan()
    {
        Vector2 inputPan = m_InputReader.Pan;
        Vector2 edgeDirection = GetEdgePanDirection();
        Vector2 totalInput = inputPan + edgeDirection;

        if (totalInput.sqrMagnitude < 0.0001f) return;
        if (totalInput.sqrMagnitude > 1f) totalInput.Normalize();

        bool diagonal = Mathf.Abs(totalInput.x) > 0.001f && Mathf.Abs(totalInput.y) > 0.001f;
        Vector2 clampedInput = diagonal ? totalInput * m_DiagonalDamping : totalInput;

        Vector3 rightXZ = transform.right;
        rightXZ.y = 0f;
        Vector3 forwardXZ = transform.forward;
        forwardXZ.y = 0f;

        if (rightXZ.sqrMagnitude > 0.0001f) rightXZ.Normalize();
        if (forwardXZ.sqrMagnitude > 0.0001f) forwardXZ.Normalize();

        Vector3 move = rightXZ * clampedInput.x + forwardXZ * clampedInput.y;
        float speed = m_PanSpeed * (m_Camera.orthographicSize / 10f) * Time.unscaledDeltaTime;
        transform.position += move * speed;
    }

    private Vector2 GetEdgePanDirection()
    {
        Vector2 mouse = m_InputReader.Pointer;
        float w = Screen.width;
        float h = Screen.height;
        Vector2 dir = Vector2.zero;

        if (mouse.x <= m_EdgePanZonePx) dir.x = -1f;
        else if (mouse.x >= w - m_EdgePanZonePx) dir.x = 1f;

        if (mouse.y <= m_EdgePanZonePx) dir.y = -1f;
        else if (mouse.y >= h - m_EdgePanZonePx) dir.y = 1f;

        return dir;
    }

    private void ApplyBounds()
    {
        float halfHeight = m_Camera.orthographicSize;
        float halfWidth = halfHeight * m_Camera.aspect;

        float camMinX = halfWidth;
        float camMaxX = m_GridWidth - halfWidth;
        float camMinZ = halfHeight;
        float camMaxZ = m_GridHeight - halfHeight;

        Vector3 pos = transform.position;
        float overflowX = 0f;
        float overflowZ = 0f;

        if (pos.x < camMinX) overflowX = camMinX - pos.x;
        else if (pos.x > camMaxX) overflowX = camMaxX - pos.x;

        if (pos.z < camMinZ) overflowZ = camMinZ - pos.z;
        else if (pos.z > camMaxZ) overflowZ = camMaxZ - pos.z;

        if (Mathf.Approximately(overflowX, 0f) && Mathf.Approximately(overflowZ, 0f))
        {
            m_Velocity = Vector3.zero;
            return;
        }

        Vector3 overflow = new Vector3(overflowX, 0f, overflowZ);
        m_Velocity += overflow * m_SoftSpringForce * Time.unscaledDeltaTime;
        m_Velocity *= m_SpringDamping;

        transform.position += m_Velocity * Time.unscaledDeltaTime;
    }

    private Vector3 ScreenToGround(Vector2 screenPoint)
    {
        Ray ray = m_Camera.ScreenPointToRay(screenPoint);
        Plane plane = new Plane(Vector3.up, Vector3.zero);
        if (plane.Raycast(ray, out float enter))
            return ray.GetPoint(enter);
        return Vector3.zero;
    }

    private Vector3 GroundToScreen(Vector3 worldPoint)
    {
        return m_Camera.WorldToScreenPoint(worldPoint);
    }

    private Vector3 ScreenVectorToWorldDelta(Vector3 screenDelta)
    {
        float pixelsPerWorldUnit = m_Camera.pixelHeight / (2f * m_Camera.orthographicSize);
        return new Vector3(screenDelta.x / pixelsPerWorldUnit, 0f, screenDelta.y / pixelsPerWorldUnit);
    }
}
