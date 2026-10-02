using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Modal New City panel, opened by the HUD's New button (GameMenu.NewRequested): pick a map size, then
// Create discards the current city (SaveGameController.NewCity). Cancel / Esc closes it. The script sits
// on the container; m_Panel (dimmed full-screen blocker + centred panel) is what gets toggled. M11 adds
// the starting age.
public sealed class NewCityDialog : MonoBehaviour
{
    [SerializeField] private GameMenu m_GameMenu;
    [SerializeField] private SaveGameController m_SaveGame;
    [SerializeField] private GameManager m_GameManager;
    [SerializeField] private InputReader m_InputReader;
    [SerializeField] private GameObject m_Panel;
    [Tooltip("One button per size; the selected one is non-interactable, so its disabled colour is the highlight.")]
    [SerializeField] private Button[] m_SizeButtons;
    [SerializeField] private int[] m_Sizes = { 32, 64, 96 };
    [SerializeField] private int m_DefaultSize = 64;
    [SerializeField] private Button m_CreateButton;
    [SerializeField] private Button m_CancelButton;

    private int m_SelectedSize;

    public bool IsOpen => m_Panel != null && m_Panel.activeSelf;

    private void OnEnable()
    {
        if (m_GameMenu != null) m_GameMenu.NewRequested += Open;
    }

    private void OnDisable()
    {
        if (m_GameMenu != null) m_GameMenu.NewRequested -= Open;
    }

    private void Start()
    {
        for (int i = 0; i < m_SizeButtons.Length && i < m_Sizes.Length; i++)
        {
            int size = m_Sizes[i];
            m_SizeButtons[i].onClick.AddListener(() => Select(size));
            TMP_Text label = m_SizeButtons[i].GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = $"{size} × {size}";
        }
        if (m_CreateButton != null) m_CreateButton.onClick.AddListener(Create);
        if (m_CancelButton != null) m_CancelButton.onClick.AddListener(Close);
        if (m_Panel != null) m_Panel.SetActive(false);
    }

    private void Update()
    {
        if (IsOpen && m_InputReader != null && m_InputReader.CancelPressed) Close();
    }

    public void Open()
    {
        if (m_Panel == null) return;

        // Start from the current map's size when it's one of the options.
        int current = m_GameManager != null ? m_GameManager.MapSize.x : 0;
        Select(System.Array.IndexOf(m_Sizes, current) >= 0 ? current : m_DefaultSize);
        m_Panel.SetActive(true);
    }

    public void Close()
    {
        if (m_Panel != null) m_Panel.SetActive(false);
    }

    private void Select(int size)
    {
        m_SelectedSize = size;
        for (int i = 0; i < m_SizeButtons.Length && i < m_Sizes.Length; i++)
        {
            m_SizeButtons[i].interactable = m_Sizes[i] != size;
        }
    }

    private void Create()
    {
        Close();
        if (m_SaveGame != null) m_SaveGame.NewCity(new Vector2Int(m_SelectedSize, m_SelectedSize));
    }
}
