using UnityEngine;

[RequireComponent(typeof(Camera))]
public sealed class IsoCameraController : MonoBehaviour
{
    [Header("Pan")]
    [SerializeField] private float m_PanSpeed = 12f;
    [SerializeField] private float m_EdgePanZonePx = 20f;
    [SerializeField] private float m_DiagonalDamping = 0.7f;
    [SerializeField] private bool m_EnableEdgePan = false;

    [Header("Zoom")]
    [SerializeField] private float m_ZoomSpeed = 0.006f;
    [SerializeField] private float m_MinZoom = 4f;
    [SerializeField] private float m_MaxZoom = 20f;
    [Tooltip("Max zoom also grows with the map so the whole of a large map fits on screen.")]
    [SerializeField] private float m_MaxZoomPerCell = 0.45f;

    [Header("Bounds")]
    [SerializeField] private float m_SoftSpringForce = 40f;
    [SerializeField] private float m_SpringDamping = 0.88f;

    private Camera m_Camera;
    private InputReader m_InputReader;
    private Vector3 m_Velocity;
    private float m_GridWidth = 24f;
    private float m_GridHeight = 24f;
    private float m_BaseDistance;   // scene distance from the camera to its ground-look-at point

    // --- Rotation (M20d): four views, 90 degrees apart, turning around the ground-look-at point. The turn is animated
    // with unscaled time (so it works while paused); one further press during a turn queues one more step. ---

    private const float RotateSeconds = 0.25f;
    private float m_Offset;       // current yaw offset from the default view, degrees
    private float m_AnimFrom;
    private float m_AnimTo;       // target offset, a multiple of 90
    private float m_AnimTime = RotateSeconds;

    // The yaw offset of the camera (animated): the directional light adds it so the lit faces stay the same.
    public static float YawOffset { get; private set; }

    // 0 = the default view (azimuth 45), then each 90 degrees; the target view while a turn is running.
    public int View => ((Mathf.RoundToInt(m_AnimTo / 90f) % 4) + 4) % 4;

    // dir -1 = turn the view left (Q), +1 = right (E).
    public void Rotate(int dir)
    {
        if (m_Showcase || dir == 0) return;
        if (Mathf.Abs(m_AnimTo - m_Offset) >= 180f) return;   // already one step queued
        m_AnimFrom = m_Offset;
        m_AnimTo += dir > 0 ? 90f : -90f;
        m_AnimTime = 0f;
        GameEvents.RaiseCameraRotated(View);
    }

    // Back to the default view at once (a new or loaded city).
    public void ResetView()
    {
        if (Mathf.Approximately(m_Offset, 0f) && Mathf.Approximately(m_AnimTo, 0f)) return;
        transform.RotateAround(GroundLookAt(), Vector3.up, -m_Offset);
        m_Offset = m_AnimFrom = m_AnimTo = 0f;
        m_AnimTime = RotateSeconds;
        YawOffset = 0f;
        GameEvents.RaiseCameraRotated(0);
    }

    private void AdvanceRotation()
    {
        if (m_AnimTime >= RotateSeconds && Mathf.Approximately(m_Offset, m_AnimTo)) return;
        m_AnimTime = Mathf.Min(RotateSeconds, m_AnimTime + Time.unscaledDeltaTime);
        float k = Mathf.SmoothStep(0f, 1f, m_AnimTime / RotateSeconds);
        float next = m_AnimTime >= RotateSeconds ? m_AnimTo : Mathf.Lerp(m_AnimFrom, m_AnimTo, k);
        float delta = next - m_Offset;
        if (Mathf.Approximately(delta, 0f)) return;
        transform.RotateAround(GroundLookAt(), Vector3.up, delta);
        m_Offset = next;
        YawOffset = next;
    }

    private void Awake()
    {
        m_Camera = GetComponent<Camera>();
        m_InputReader = FindAnyObjectByType<InputReader>();
        m_BaseDistance = Vector3.Distance(transform.position, GroundLookAt());
    }

    private void OnEnable()
    {
        GameEvents.WorldResized += FrameMap;
        GameEvents.CityLoaded += ResetView;
    }

    private void OnDisable()
    {
        GameEvents.WorldResized -= FrameMap;
        GameEvents.CityLoaded -= ResetView;
    }

    // Start, not Awake: GameManager.Awake creates the map.
    private void Start()
    {
        GameManager gameManager = FindAnyObjectByType<GameManager>();
        if (gameManager != null && gameManager.Grid != null) FrameMap(gameManager.MapSize);
    }

    // Adopts a new map size and looks at its centre.
    private void FrameMap(Vector2Int size)
    {
        m_GridWidth = size.x;
        m_GridHeight = size.y;
        m_Velocity = Vector3.zero;
        ResetView();

        // Orthographic, so distance doesn't change the framing, but the camera must sit far enough back
        // that the map's near corner isn't behind the near clip plane (and the far corner within far).
        float side = Mathf.Max(size.x, size.y);
        float distance = Mathf.Max(m_BaseDistance, side * 0.75f + 10f);
        Vector3 center = new Vector3(size.x * 0.5f, 0f, size.y * 0.5f);
        transform.position = center - transform.forward * distance;
        m_Camera.farClipPlane = Mathf.Max(m_Camera.farClipPlane, distance + side * 1.5f + 20f);
        m_Camera.orthographicSize = m_Showcase ? Mathf.Min(MaxZoom, ShowcaseZoom) : Mathf.Clamp(m_Camera.orthographicSize, m_MinZoom, MaxZoom);
    }

    private float MaxZoom => Mathf.Max(m_MaxZoom, Mathf.Max(m_GridWidth, m_GridHeight) * m_MaxZoomPerCell);

    // --- Showcase (M19c): behind the main menu the camera drifts slowly over the map and ignores input. ---

    private const float ShowcaseZoom = 11f;
    private bool m_Showcase;
    private float m_SavedZoom;

    public void SetShowcase(bool on)
    {
        if (m_Showcase == on) return;
        m_Showcase = on;
        if (on)
        {
            m_SavedZoom = m_Camera.orthographicSize;
            m_Camera.orthographicSize = Mathf.Min(MaxZoom, ShowcaseZoom);
        }
        else
        {
            m_Camera.orthographicSize = m_SavedZoom;
            FrameMap(new Vector2Int(Mathf.RoundToInt(m_GridWidth), Mathf.RoundToInt(m_GridHeight)));
        }
    }

    private void DriftOverMap()
    {
        float t = Time.unscaledTime * 0.035f;
        float radius = Mathf.Min(m_GridWidth, m_GridHeight) * 0.18f;
        var target = new Vector3(m_GridWidth * 0.5f + Mathf.Cos(t) * radius, 0f, m_GridHeight * 0.5f + Mathf.Sin(t * 1.3f) * radius * 0.8f);
        float side = Mathf.Max(m_GridWidth, m_GridHeight);
        float distance = Mathf.Max(m_BaseDistance, side * 0.75f + 10f);
        Vector3 want = target - transform.forward * distance;
        transform.position = Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-2f * Time.unscaledDeltaTime));
    }

    // Where the view's centre ray meets the ground (the camera is tilted, so not its XZ position).
    private Vector3 GroundLookAt()
    {
        Vector3 forward = transform.forward;
        if (Mathf.Abs(forward.y) < 0.0001f) return new Vector3(transform.position.x, 0f, transform.position.z);
        float t = -transform.position.y / forward.y;
        return transform.position + forward * t;
    }

    private void LateUpdate()
    {
        if (m_Showcase)
        {
            DriftOverMap();
            return;
        }
        if (m_InputReader == null) return;

        if (m_InputReader.RotateCameraLeftPressed) Rotate(-1);
        if (m_InputReader.RotateCameraRightPressed) Rotate(1);
        AdvanceRotation();
        ApplyZoom();
        ApplyPan();
        ApplyBounds();
    }

    private void ApplyZoom()
    {
        float scroll = m_InputReader.Zoom;
        if (Mathf.Approximately(scroll, 0f)) return;

        float oldSize = m_Camera.orthographicSize;
        float newSize = Mathf.Clamp(oldSize - scroll * m_ZoomSpeed, m_MinZoom, MaxZoom);
        if (Mathf.Approximately(oldSize, newSize)) return;

        Vector3 groundPoint = ScreenToGround(m_InputReader.Pointer);

        m_Camera.orthographicSize = newSize;

        // Slide the camera so the same ground point stays under the pointer (right in every view).
        transform.position += groundPoint - ScreenToGround(m_InputReader.Pointer);
    }

    private void ApplyPan()
    {
        if (m_InputReader.Blocked) return;
        Vector2 inputPan = m_InputReader.Pan;
        Vector2 edgeDirection = m_EnableEdgePan || GameSettings.EdgeScroll ? GetEdgePanDirection() : Vector2.zero;
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
        float speed = m_PanSpeed * GameSettings.PanSpeed * (m_Camera.orthographicSize / 10f) * Time.unscaledDeltaTime;
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

        if (Mathf.Abs(transform.forward.y) < 0.0001f) return;
        Vector3 groundHit = GroundLookAt();

        float targetGroundX = ClampAxis(groundHit.x, halfWidth, m_GridWidth);
        float targetGroundZ = ClampAxis(groundHit.z, halfHeight, m_GridHeight);

        float overflowX = targetGroundX - groundHit.x;
        float overflowZ = targetGroundZ - groundHit.z;

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

    private float ClampAxis(float value, float halfView, float gridSize)
    {
        float min = halfView;
        float max = gridSize - halfView;
        if (max <= min) return gridSize * 0.5f;
        return Mathf.Clamp(value, min, max);
    }

    private Vector3 ScreenToGround(Vector2 screenPoint)
    {
        Ray ray = m_Camera.ScreenPointToRay(screenPoint);
        Plane plane = new Plane(Vector3.up, Vector3.zero);
        if (plane.Raycast(ray, out float enter))
            return ray.GetPoint(enter);
        return Vector3.zero;
    }

}
