using UnityEngine;
using UnityEngine.InputSystem;

public sealed class InputReader : MonoBehaviour
{
    [SerializeField] private InputActionAsset m_InputActions;

    private InputActionMap m_Map;

    private InputAction m_PanAction;
    private InputAction m_ZoomAction;
    private InputAction m_PointerAction;
    private InputAction m_ConfirmAction;
    private InputAction m_CancelAction;
    private InputAction m_RotateAction;
    private InputAction m_DemolishAction;
    private InputAction m_SpeedDeltaAction;

    public Vector2 Pan => m_PanAction.ReadValue<Vector2>();
    public float Zoom => m_ZoomAction.ReadValue<float>();
    public Vector2 Pointer => m_PointerAction.ReadValue<Vector2>();
    public bool ConfirmPressed => m_ConfirmAction.WasPressedThisFrame();
    public bool CancelPressed => m_CancelAction.WasPressedThisFrame();
    public bool RotatePressed => m_RotateAction.WasPressedThisFrame();
    public bool DemolishPressed => m_DemolishAction.WasPressedThisFrame();
    public int SpeedDelta => Mathf.RoundToInt(m_SpeedDeltaAction.ReadValue<float>());

    private void Awake()
    {
        if (m_InputActions == null)
        {
            Debug.LogError("InputReader: no InputActionAsset assigned.", this);
            return;
        }

        InputActionAsset clone = Instantiate(m_InputActions);
        m_Map = clone.FindActionMap("Gameplay", throwIfNotFound: true);

        m_PanAction = m_Map.FindAction("Pan", throwIfNotFound: true);
        m_ZoomAction = m_Map.FindAction("Zoom", throwIfNotFound: true);
        m_PointerAction = m_Map.FindAction("Pointer", throwIfNotFound: true);
        m_ConfirmAction = m_Map.FindAction("Confirm", throwIfNotFound: true);
        m_CancelAction = m_Map.FindAction("Cancel", throwIfNotFound: true);
        m_RotateAction = m_Map.FindAction("Rotate", throwIfNotFound: true);
        m_DemolishAction = m_Map.FindAction("Demolish", throwIfNotFound: true);
        m_SpeedDeltaAction = m_Map.FindAction("SpeedDelta", throwIfNotFound: true);

        m_Map.Enable();
    }

    private void OnDestroy()
    {
        m_Map?.Disable();
    }
}
