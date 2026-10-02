using UnityEngine;
using UnityEngine.UI;

// Developer panel toggled with F1 (DebugToggle). Only available in the Editor and development builds.
public sealed class DebugPanel : MonoBehaviour
{
    [SerializeField] private InputReader m_InputReader;
    [SerializeField] private PlacementController m_Placement;
    [SerializeField] private GameManager m_GameManager;
    [SerializeField] private GameObject m_Root;
    [SerializeField] private Button m_SeedCityButton;
    [SerializeField] private Button m_AddMoneyButton;
    [SerializeField] private Button m_SkipDaysButton;
    [SerializeField] private float m_MoneyAmount = 10000f;
    [SerializeField] private int m_SkipDays = 30;

    private void Start()
    {
        m_Root.SetActive(false);
        if (!Debug.isDebugBuild)
        {
            enabled = false;
            return;
        }

        Bind(m_SeedCityButton, () => m_Placement.DebugSeedCity());
        Bind(m_AddMoneyButton, () => m_GameManager.Economy.Refund(m_MoneyAmount));
        Bind(m_SkipDaysButton, () => m_GameManager.Clock.DebugAdvanceDays(m_SkipDays));
    }

    private void Update()
    {
        if (m_InputReader != null && m_InputReader.DebugTogglePressed)
        {
            m_Root.SetActive(!m_Root.activeSelf);
        }
    }

    private static void Bind(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null) button.onClick.AddListener(action);
    }
}
