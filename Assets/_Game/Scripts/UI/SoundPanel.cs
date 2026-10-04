using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Volumes, mute and "Lock to day" (M18e). Built at runtime like the Budget panel (no scene or prefab edit):
// it sits in the SidePanels slot under the HUD and opens from a Sound button next to Save / Load / New.
public sealed class SoundPanel : MonoBehaviour
{
    private const float PanelWidth = 340f;
    private static readonly Color PanelColor = new Color(0.10f, 0.12f, 0.16f, 0.96f);
    private static readonly Color ButtonColor = new Color(0.24f, 0.27f, 0.33f);
    private static readonly Color OnColor = new Color(0.30f, 0.55f, 0.92f);

    private GameObject m_Root;
    private Button m_MuteButton, m_DayButton;
    private Slider m_Master, m_Music, m_Ambience, m_Sfx;
    private bool m_Open;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        GameManager game = FindAnyObjectByType<GameManager>();
        TaxPanel taxPanel = FindAnyObjectByType<TaxPanel>(FindObjectsInactive.Include);
        if (game == null || taxPanel == null) return;

        Button save = null;
        foreach (Button b in taxPanel.transform.root.GetComponentsInChildren<Button>(true))
        {
            if (b.name == "SaveButton") save = b;
        }
        if (save == null) return;

        var go = new GameObject("SoundPanel", typeof(RectTransform));
        go.transform.SetParent(taxPanel.transform.parent, false);
        var panel = go.AddComponent<SoundPanel>();
        panel.Init(taxPanel.GetComponent<RectTransform>());

        Button toggle = Instantiate(save, save.transform.parent);
        toggle.name = "SoundButton";
        toggle.onClick.RemoveAllListeners();
        toggle.GetComponentInChildren<TMP_Text>().text = "Sound";
        toggle.onClick.AddListener(panel.Toggle);
        toggle.interactable = true;
    }

    private void Init(RectTransform slot)
    {
        var rt = (RectTransform)transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = slot.anchoredPosition;
        rt.sizeDelta = new Vector2(PanelWidth, 0f);

        m_Root = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        m_Root.transform.SetParent(transform, false);
        var rootRect = (RectTransform)m_Root.transform;
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(1f, 1f);
        rootRect.pivot = new Vector2(0.5f, 1f);
        rootRect.anchoredPosition = Vector2.zero;
        rootRect.sizeDelta = Vector2.zero;
        m_Root.GetComponent<Image>().color = PanelColor;
        var layout = m_Root.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 12);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        m_Root.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform header = Row(m_Root.transform, 28f);
        Text(header, "Sound", 22, Color.white, flexible: true);
        MakeButton(header, "x", Close, 28f).GetComponent<LayoutElement>().minWidth = 28f;

        m_Master = VolumeRow("Master", SoundSettings.Master, v => SoundSettings.Master = v);
        m_Music = VolumeRow("Music", SoundSettings.Get(SoundChannel.Music), v => SoundSettings.Set(SoundChannel.Music, v));
        m_Ambience = VolumeRow("Ambience", SoundSettings.Get(SoundChannel.Ambience), v => SoundSettings.Set(SoundChannel.Ambience, v));
        m_Sfx = VolumeRow("Effects", SoundSettings.Get(SoundChannel.Sfx), v => SoundSettings.Set(SoundChannel.Sfx, v));

        RectTransform toggles = Row(m_Root.transform, 30f);
        m_MuteButton = MakeButton(toggles, "Mute", () => SoundSettings.Mute = !SoundSettings.Mute, 0f);
        m_MuteButton.GetComponent<LayoutElement>().flexibleWidth = 1f;
        m_DayButton = MakeButton(toggles, "Always day", () => DayNightCycle.LockToDay = !DayNightCycle.LockToDay, 0f);
        m_DayButton.GetComponent<LayoutElement>().flexibleWidth = 1f;
        // Both toggles repaint themselves after a click (the settings are PlayerPrefs, no event for the day lock).
        m_MuteButton.onClick.AddListener(RefreshToggles);
        m_DayButton.onClick.AddListener(RefreshToggles);

        m_Root.SetActive(false);
        SidePanels.Opened += OnSidePanelOpened;
    }

    private void OnDestroy()
    {
        SidePanels.Opened -= OnSidePanelOpened;
    }

    private void Toggle()
    {
        if (m_Open) Close();
        else Open();
    }

    private void Open()
    {
        m_Open = true;
        m_Root.SetActive(true);
        SidePanels.RaiseOpened(this);
        m_Master.SetValueWithoutNotify(SoundSettings.Master);
        m_Music.SetValueWithoutNotify(SoundSettings.Get(SoundChannel.Music));
        m_Ambience.SetValueWithoutNotify(SoundSettings.Get(SoundChannel.Ambience));
        m_Sfx.SetValueWithoutNotify(SoundSettings.Get(SoundChannel.Sfx));
        RefreshToggles();
        AudioController.Play(SfxId.Click);
    }

    private void Close()
    {
        m_Open = false;
        m_Root.SetActive(false);
    }

    private void OnSidePanelOpened(object panel)
    {
        if (!ReferenceEquals(panel, this) && m_Open) Close();
    }

    private void RefreshToggles()
    {
        Paint(m_MuteButton, "Mute", SoundSettings.Mute);
        Paint(m_DayButton, "Always day", DayNightCycle.LockToDay);
    }

    private static void Paint(Button button, string label, bool on)
    {
        button.GetComponent<Image>().color = on ? OnColor : ButtonColor;
        button.GetComponentInChildren<TMP_Text>().text = on ? label + ": on" : label + ": off";
    }

    // ---- widgets ----------------------------------------------------------------------------------------

    private Slider VolumeRow(string label, float value, UnityEngine.Events.UnityAction<float> onChanged)
    {
        RectTransform row = Row(m_Root.transform, 22f);
        TMP_Text name = Text(row, label, 15, new Color(0.85f, 0.88f, 0.94f));
        name.GetComponent<LayoutElement>().minWidth = 80f;

        GameObject go = DefaultControls.CreateSlider(new DefaultControls.Resources());
        go.transform.SetParent(row, false);
        var element = go.AddComponent<LayoutElement>();
        element.flexibleWidth = 1f;
        element.minHeight = 20f;
        foreach (Image image in go.GetComponentsInChildren<Image>())
        {
            image.color = image.name == "Background" ? new Color(0.20f, 0.23f, 0.28f)
                : image.name == "Fill" ? new Color(0.30f, 0.55f, 0.95f)
                : Color.white;
        }
        var slider = go.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.SetValueWithoutNotify(value);
        slider.onValueChanged.AddListener(onChanged);
        // Let go of the slider = a click so the new level is audible (the effects channel is the one to judge).
        var trigger = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        var up = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp };
        up.callback.AddListener(_ => AudioController.Play(SfxId.Click));
        trigger.triggers.Add(up);
        return slider;
    }

    private static RectTransform Row(Transform parent, float height)
    {
        var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        layout.childAlignment = TextAnchor.MiddleLeft;
        go.GetComponent<LayoutElement>().minHeight = height;
        return (RectTransform)go.transform;
    }

    private static TMP_Text Text(Transform parent, string text, float size, Color color, bool flexible = false)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        if (flexible) go.GetComponent<LayoutElement>().flexibleWidth = 1f;
        return tmp;
    }

    private static Button MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, float width)
    {
        var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = ButtonColor;
        var element = go.GetComponent<LayoutElement>();
        if (width > 0f) element.preferredWidth = width;
        var button = go.GetComponent<Button>();
        button.onClick.AddListener(onClick);
        var text = Text(go.transform, label, 15, Color.white);
        text.alignment = TextAlignmentOptions.Center;
        var rect = (RectTransform)text.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        Destroy(text.GetComponent<LayoutElement>());
        return button;
    }
}
