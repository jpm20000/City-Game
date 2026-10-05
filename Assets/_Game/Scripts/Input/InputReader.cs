using UnityEngine;
using UnityEngine.InputSystem;

public sealed class InputReader : MonoBehaviour
{
    [SerializeField] private InputActionAsset m_InputActions;

    private InputActionMap m_Map;
    private InputActionAsset m_Clone;

    // The runtime copy of the actions: the Controls tab (KeyBindings) rebinds on it.
    public static InputReader Instance { get; private set; }
    public InputActionAsset Asset => m_Clone;

    private InputAction m_PanAction;
    private InputAction m_ZoomAction;
    private InputAction m_PointerAction;
    private InputAction m_ConfirmAction;
    private InputAction m_CancelAction;
    private InputAction m_RotateAction;
    private InputAction m_RotateCameraLeftAction;
    private InputAction m_RotateCameraRightAction;
    private InputAction m_DemolishAction;
    private InputAction m_RoadToolAction;
    private InputAction m_PipeToolAction;
    private InputAction m_SpeedDeltaAction;
    private InputAction m_DebugToggleAction;
    private InputAction m_QuickSaveAction;
    private InputAction m_QuickLoadAction;
    private InputAction m_CycleOverlayAction;

    // Set by GameFlow while a menu or dialog is open: every gameplay action reads as idle except Cancel (Esc), which
    // the EscapeRouter needs to close things.
    public bool Blocked { get; set; }

    public Vector2 Pan => Blocked ? Vector2.zero : m_PanAction.ReadValue<Vector2>();
    public float Zoom => Blocked ? 0f : m_ZoomAction.ReadValue<float>();
    public Vector2 Pointer => m_PointerAction.ReadValue<Vector2>();
    public bool ConfirmPressed => !Blocked && m_ConfirmAction.WasPressedThisFrame();
    public bool ConfirmHeld => !Blocked && m_ConfirmAction.IsPressed();
    public bool CancelPressed => m_CancelAction.WasPressedThisFrame();
    public bool RotatePressed => !Blocked && m_RotateAction.WasPressedThisFrame();
    public bool RotateCameraLeftPressed => !Blocked && m_RotateCameraLeftAction.WasPressedThisFrame();
    public bool RotateCameraRightPressed => !Blocked && m_RotateCameraRightAction.WasPressedThisFrame();
    public bool DemolishPressed => !Blocked && m_DemolishAction.WasPressedThisFrame();
    public bool RoadToolPressed => !Blocked && m_RoadToolAction.WasPressedThisFrame();
    public bool PipeToolPressed => !Blocked && m_PipeToolAction.WasPressedThisFrame();
    public bool DebugTogglePressed => !Blocked && m_DebugToggleAction.WasPressedThisFrame();
    public bool QuickSavePressed => !Blocked && m_QuickSaveAction.WasPressedThisFrame();
    public bool QuickLoadPressed => !Blocked && m_QuickLoadAction.WasPressedThisFrame();
    public bool CycleOverlayPressed => !Blocked && m_CycleOverlayAction.WasPressedThisFrame();
    public int SpeedDelta => Blocked ? 0 : Mathf.RoundToInt(m_SpeedDeltaAction.ReadValue<float>());

    private void Awake()
    {
        if (m_InputActions == null)
        {
            Debug.LogError("InputReader: no InputActionAsset assigned.", this);
            return;
        }

        Instance = this;
        InputActionAsset clone = Instantiate(m_InputActions);
        m_Clone = clone;
        KeyBindings.Load(clone);
        m_Map = clone.FindActionMap("Gameplay", throwIfNotFound: true);

        m_PanAction = m_Map.FindAction("Pan", throwIfNotFound: true);
        m_ZoomAction = m_Map.FindAction("Zoom", throwIfNotFound: true);
        m_PointerAction = m_Map.FindAction("Pointer", throwIfNotFound: true);
        m_ConfirmAction = m_Map.FindAction("Confirm", throwIfNotFound: true);
        m_CancelAction = m_Map.FindAction("Cancel", throwIfNotFound: true);
        m_RotateAction = m_Map.FindAction("Rotate", throwIfNotFound: true);
        m_RotateCameraLeftAction = m_Map.FindAction("RotateCameraLeft", throwIfNotFound: true);
        m_RotateCameraRightAction = m_Map.FindAction("RotateCameraRight", throwIfNotFound: true);
        m_DemolishAction = m_Map.FindAction("Demolish", throwIfNotFound: true);
        m_RoadToolAction = m_Map.FindAction("RoadTool", throwIfNotFound: true);
        m_PipeToolAction = m_Map.FindAction("PipeTool", throwIfNotFound: true);
        m_SpeedDeltaAction = m_Map.FindAction("SpeedDelta", throwIfNotFound: true);
        m_DebugToggleAction = m_Map.FindAction("DebugToggle", throwIfNotFound: true);
        m_QuickSaveAction = m_Map.FindAction("QuickSave", throwIfNotFound: true);
        m_QuickLoadAction = m_Map.FindAction("QuickLoad", throwIfNotFound: true);
        m_CycleOverlayAction = m_Map.FindAction("CycleOverlay", throwIfNotFound: true);

        m_Map.Enable();
    }

    private void OnDestroy()
    {
        m_Map?.Disable();
        if (Instance == this) Instance = null;
    }
}
