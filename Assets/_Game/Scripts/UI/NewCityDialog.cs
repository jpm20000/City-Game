using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Modal New City panel, opened by the HUD's New button (GameMenu.NewRequested): pick a map size and a
// starting age (M11), then Create discards the current city (SaveGameController.NewCity). Cancel / Esc
// closes it. The script sits on the container; m_Panel (dimmed full-screen blocker + centred panel) is
// what gets toggled.
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
    [Tooltip("One button per age in AgeDatabase order (extra buttons are hidden); same highlight convention.")]
    [SerializeField] private Button[] m_AgeButtons;
    [Tooltip("Hidden when the game has no age data.")]
    [SerializeField] private GameObject[] m_AgeSection;
    [SerializeField] private TMP_Text m_AgeHint;
    [SerializeField] private Button m_CreateButton;
    [SerializeField] private Button m_CancelButton;

    private Toggle m_DisastersToggle;      // (M17) built at runtime under the age hint
    private bool m_Disasters = true;
    private bool m_Tutorial;
    private Toggle m_TutorialToggle;
    private int m_SelectedSize;
    private int m_SelectedAge = -1;
    private System.Action m_Closer;

    public bool IsOpen => m_Panel != null && m_Panel.activeSelf;

    private void OnEnable()
    {
        if (m_GameMenu != null) m_GameMenu.NewRequested += Open;
        EscapeRouter.Register(this, EscapeRouter.NewCity, TryEscape);
    }

    private void OnDisable()
    {
        if (m_GameMenu != null) m_GameMenu.NewRequested -= Open;
        EscapeRouter.Unregister(this);
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
        AgeDatabase ages = m_GameManager != null ? m_GameManager.Ages : null;
        foreach (GameObject part in m_AgeSection) if (part != null) part.SetActive(ages != null);
        for (int i = 0; m_AgeButtons != null && i < m_AgeButtons.Length; i++)
        {
            bool used = ages != null && i < ages.Count;
            m_AgeButtons[i].gameObject.SetActive(used);
            if (!used) continue;
            int age = i;
            m_AgeButtons[i].onClick.AddListener(() => SelectAge(age));
            TMP_Text label = m_AgeButtons[i].GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = ages[i].DisplayName;
        }
        BuildDisastersToggle(ages != null);
        if (m_CreateButton != null) m_CreateButton.onClick.AddListener(Create);
        if (m_CancelButton != null) m_CancelButton.onClick.AddListener(Close);
        if (m_Panel != null) m_Panel.SetActive(false);
    }

    // The Disasters & events switch (M17): a toggle created under the age hint, so there is no prefab edit.
    private void BuildDisastersToggle(bool hasAges)
    {
        if (m_AgeHint == null || !hasAges) return;
        m_DisastersToggle = BuildToggle("DisastersToggle", "Disasters & events (fires, plague, breakdowns, choices)", 1, m_Disasters, value => m_Disasters = value);
        // M19f: the guided tutorial city (Medieval, 48 x 48, no disasters) replaces the choices above.
        m_TutorialToggle = BuildToggle("TutorialToggle", "Guided tutorial (Medieval, 48 × 48, no disasters)", 2, m_Tutorial, value => m_Tutorial = value);
    }

    private Toggle BuildToggle(string name, string text, int offset, bool isOn, UnityEngine.Events.UnityAction<bool> onChanged)
    {
        GameObject go = DefaultControls.CreateToggle(new DefaultControls.Resources());
        go.name = name;
        Transform parent = m_AgeHint.transform.parent;
        go.transform.SetParent(parent, false);
        go.transform.SetSiblingIndex(m_AgeHint.transform.GetSiblingIndex() + offset);
        // A clear Image on the row makes all of it clickable (the stock toggle only reacts on its small box).
        var hit = go.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        var element = go.AddComponent<LayoutElement>();
        element.minHeight = 28f;
        element.preferredHeight = 28f;
        var legacyLabel = go.GetComponentInChildren<Text>();
        TMP_Text label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.transform.SetParent(go.transform, false);
        label.text = text;
        label.fontSize = m_AgeHint.fontSize;
        label.color = m_AgeHint.color;
        label.raycastTarget = false;
        var labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(30f, 0f);
        labelRect.offsetMax = Vector2.zero;
        if (legacyLabel != null) Destroy(legacyLabel.gameObject);
        var toggle = go.GetComponent<Toggle>();
        toggle.isOn = isOn;
        toggle.onValueChanged.AddListener(onChanged);
        return toggle;
    }

    public void Open()
    {
        if (m_Panel == null) return;

        // Start from the current map's size when it's one of the options.
        int current = m_GameManager != null ? m_GameManager.MapSize.x : 0;
        Select(System.Array.IndexOf(m_Sizes, current) >= 0 ? current : m_DefaultSize);
        m_Disasters = GameSettings.DisastersByDefault;
        if (m_DisastersToggle != null) m_DisastersToggle.isOn = m_Disasters;
        m_Tutorial = false;
        if (m_TutorialToggle != null) m_TutorialToggle.isOn = false;
        TechSystem tech = m_GameManager != null && m_GameManager.Simulation != null ? m_GameManager.Simulation.Tech : null;
        if (tech != null) SelectAge(tech.CurrentAge);
        m_Panel.SetActive(true);
        transform.SetAsLastSibling();
        m_Closer ??= Close;
        if (GameFlow.Instance != null) GameFlow.Instance.WindowOpened(m_Closer);
    }

    private bool TryEscape()
    {
        if (!IsOpen) return false;
        Close();
        return true;
    }

    public void Close()
    {
        if (m_Panel != null) m_Panel.SetActive(false);
        if (m_Closer != null && GameFlow.Instance != null) GameFlow.Instance.WindowClosed(m_Closer);
    }

    private void Select(int size)
    {
        m_SelectedSize = size;
        for (int i = 0; i < m_SizeButtons.Length && i < m_Sizes.Length; i++)
        {
            m_SizeButtons[i].interactable = m_Sizes[i] != size;
        }
    }

    private void SelectAge(int age)
    {
        m_SelectedAge = age;
        for (int i = 0; m_AgeButtons != null && i < m_AgeButtons.Length; i++)
        {
            m_AgeButtons[i].interactable = i != age;
        }

        AgeDatabase ages = m_GameManager != null ? m_GameManager.Ages : null;
        if (m_AgeHint == null || ages == null || !ages.IsValidIndex(age)) return;
        AgeDefinition def = ages[age];
        string earlier = age > 0 ? $"Every tech up to the {ages[age - 1].DisplayName} is researched. " : string.Empty;
        string power = def.UpgradesNeedPower ? "Buildings need power to grow past level 1. " : "No power needed. ";
        m_AgeHint.text = $"Starts in {def.StartYear} with ${def.StartingMoney:N0}. {earlier}{power}" +
                         "<color=#9AA3B2>Later ages need more residents to reach, so small maps suit early ages.</color>";
    }

    private void Create()
    {
        int size = m_SelectedSize;
        int age = m_SelectedAge;
        bool disasters = m_Disasters;
        bool tutorial = m_Tutorial;
        void Make()
        {
            Close();
            if (m_SaveGame == null) return;
            if (tutorial) m_SaveGame.StartTutorial();
            else m_SaveGame.NewCity(new Vector2Int(size, size), age, disasters);
        }

        // Unsaved changes in the current city are asked about first.
        if (GameFlow.Instance != null) GameFlow.Instance.GuardDiscard(Make);
        else Make();
    }
}
