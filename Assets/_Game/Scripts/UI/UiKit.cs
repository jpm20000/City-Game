using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Small helpers for the panels and dialogs that are built in code (Budget, Sound, the event popup, M19's menus), so
// each of them doesn't carry its own copy of the same few widgets.
public static class UiKit
{
    public static readonly Color PanelColor = new Color(0.10f, 0.12f, 0.16f, 0.97f);
    public static readonly Color DimColor = new Color(0f, 0f, 0f, 0.55f);
    public static readonly Color ButtonColor = new Color(0.24f, 0.27f, 0.33f);
    public static readonly Color AccentColor = new Color(0.30f, 0.55f, 0.92f);
    public static readonly Color DisabledColor = new Color(0.17f, 0.18f, 0.21f);
    public static readonly Color TitleColor = new Color(0.95f, 0.80f, 0.35f);
    public static readonly Color BodyColor = new Color(0.85f, 0.88f, 0.94f);
    public static readonly Color MutedColor = new Color(0.60f, 0.64f, 0.70f);

    public static RectTransform Row(Transform parent, float height)
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

    public static TMP_Text Text(Transform parent, string text, float size, Color color, bool flexible = false)
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

    // A text button. width 0 = as wide as the layout gives it. The label is the first child's text.
    public static Button MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, float width, Color? color = null)
    {
        var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color ?? ButtonColor;
        var element = go.GetComponent<LayoutElement>();
        if (width > 0f) element.preferredWidth = width;
        var button = go.GetComponent<Button>();
        if (onClick != null) button.onClick.AddListener(onClick);
        var text = Text(go.transform, label, 15, Color.white);
        text.alignment = TextAlignmentOptions.Center;
        var rect = (RectTransform)text.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        Object.Destroy(text.GetComponent<LayoutElement>());
        return button;
    }

    public static void SetLabel(Button button, string label)
    {
        TMP_Text text = button.GetComponentInChildren<TMP_Text>();
        if (text != null) text.text = label;
    }

    // A single-line text field with dark styling.
    public static TMP_InputField InputField(Transform parent, string placeholder, int maxLength)
    {
        GameObject go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        go.name = "InputField";
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.10f, 1f);
        var element = go.AddComponent<LayoutElement>();
        element.minHeight = 32f;
        element.flexibleWidth = 1f;
        var field = go.GetComponent<TMP_InputField>();
        field.characterLimit = maxLength;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.textComponent.color = Color.white;
        field.textComponent.fontSize = 16;
        if (field.placeholder is TMP_Text hint)
        {
            hint.text = placeholder;
            hint.fontSize = 16;
            hint.color = MutedColor;
            hint.fontStyle = FontStyles.Normal;
        }
        return field;
    }

    // A modal window: a dimmed full-screen blocker with a centred, auto-height panel. Root is the blocker (toggle it
    // with SetActive); Body is the panel's vertical layout, with the title already in it.
    public sealed class Window
    {
        public GameObject Root;
        public RectTransform Body;
        public TMP_Text Title;

        public void Show()
        {
            Root.SetActive(true);
            Root.transform.SetAsLastSibling();
        }

        public void Hide() => Root.SetActive(false);
        public bool IsOpen => Root != null && Root.activeSelf;
    }

    public static Window CreateWindow(Transform canvas, string name, string title, float width)
    {
        var window = new Window();
        window.Root = new GameObject(name, typeof(RectTransform), typeof(Image));
        window.Root.transform.SetParent(canvas, false);
        var blocker = (RectTransform)window.Root.transform;
        blocker.anchorMin = Vector2.zero;
        blocker.anchorMax = Vector2.one;
        blocker.offsetMin = blocker.offsetMax = Vector2.zero;
        window.Root.GetComponent<Image>().color = DimColor;

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panel.transform.SetParent(window.Root.transform, false);
        var panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(width, 0f);
        panel.GetComponent<Image>().color = PanelColor;
        var layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 16, 18);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        window.Body = panelRect;
        window.Title = Text(panel.transform, title, 26, TitleColor);
        window.Root.SetActive(false);
        return window;
    }

    // A horizontal slider (dark track, blue fill); a click sound when it is let go, so the new level can be judged.
    public static Slider MakeSlider(Transform parent, float value, float min, float max, UnityEngine.Events.UnityAction<float> onChanged)
    {
        GameObject go = DefaultControls.CreateSlider(new DefaultControls.Resources());
        go.transform.SetParent(parent, false);
        var element = go.AddComponent<LayoutElement>();
        element.flexibleWidth = 1f;
        element.minHeight = 20f;
        foreach (Image image in go.GetComponentsInChildren<Image>())
        {
            image.color = image.name == "Background" ? new Color(0.20f, 0.23f, 0.28f)
                : image.name == "Fill" ? AccentColor
                : Color.white;
        }
        var slider = go.GetComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify(value);
        slider.onValueChanged.AddListener(onChanged);
        var trigger = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        var up = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp };
        up.callback.AddListener(_ => AudioController.Play(SfxId.Click));
        trigger.triggers.Add(up);
        return slider;
    }

    // A checkbox row whose state is plainly visible (M20a): a dark box with an outline, an accent tick when on, and the
    // label suffixed "- On" / "- Off". The stock DefaultControls toggle draws white on white without sprites. The whole
    // row is clickable. SetToggleEnabled greys a row to half alpha and makes it unclickable.
    public static Toggle MakeToggle(Transform parent, string name, string text, bool isOn, float fontSize, UnityEngine.Events.UnityAction<bool> onChanged)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Toggle), typeof(LayoutElement), typeof(CanvasGroup));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        var element = go.GetComponent<LayoutElement>();
        element.minHeight = 28f;
        element.preferredHeight = 28f;

        var box = new GameObject("Box", typeof(RectTransform), typeof(Image), typeof(Outline));
        box.transform.SetParent(go.transform, false);
        var boxRect = (RectTransform)box.transform;
        boxRect.anchorMin = boxRect.anchorMax = new Vector2(0f, 0.5f);
        boxRect.pivot = new Vector2(0f, 0.5f);
        boxRect.anchoredPosition = new Vector2(2f, 0f);
        boxRect.sizeDelta = new Vector2(20f, 20f);
        box.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.10f, 1f);
        var outline = box.GetComponent<Outline>();
        outline.effectColor = MutedColor;
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        var tick = new GameObject("Tick", typeof(RectTransform), typeof(Image));
        tick.transform.SetParent(box.transform, false);
        var tickRect = (RectTransform)tick.transform;
        tickRect.anchorMin = Vector2.zero;
        tickRect.anchorMax = Vector2.one;
        tickRect.offsetMin = new Vector2(4f, 4f);
        tickRect.offsetMax = new Vector2(-4f, -4f);
        tick.GetComponent<Image>().color = AccentColor;
        tick.GetComponent<Image>().raycastTarget = false;

        TMP_Text label = Text(go.transform, text, fontSize, BodyColor);
        Object.Destroy(label.GetComponent<LayoutElement>());
        var labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(30f, 0f);
        labelRect.offsetMax = Vector2.zero;
        label.verticalAlignment = VerticalAlignmentOptions.Middle;

        var toggle = go.GetComponent<Toggle>();
        toggle.targetGraphic = go.GetComponent<Image>();
        toggle.graphic = tick.GetComponent<Image>();
        toggle.transition = Selectable.Transition.None;
        void Paint(bool on)
        {
            label.text = $"{text} <color=#{ColorUtility.ToHtmlStringRGB(on ? AccentColor : MutedColor)}>— {(on ? "On" : "Off")}</color>";
        }
        toggle.SetIsOnWithoutNotify(isOn);
        Paint(isOn);
        toggle.onValueChanged.AddListener(on => { Paint(on); AudioController.Play(SfxId.Click); });
        if (onChanged != null) toggle.onValueChanged.AddListener(onChanged);
        return toggle;
    }

    // Greys a MakeToggle row (50% alpha, not clickable).
    public static void SetToggleEnabled(Toggle toggle, bool enabled)
    {
        toggle.interactable = enabled;
        toggle.GetComponent<CanvasGroup>().alpha = enabled ? 1f : 0.5f;
    }

    // An on / off switch drawn as a button: "label: on" (blue) or "label: off". Refresh repaints it from get().
    public sealed class SwitchButton
    {
        public Button Button;
        private System.Func<bool> m_Get;

        public void Refresh()
        {
            bool on = m_Get();
            Button.GetComponent<Image>().color = on ? AccentColor : ButtonColor;
            SetLabel(Button, on ? "On" : "Off");
        }

        public static SwitchButton Create(Transform parent, System.Func<bool> get, System.Action<bool> set)
        {
            var sw = new SwitchButton { m_Get = get };
            sw.Button = MakeButton(parent, "Off", () =>
            {
                set(!get());
                sw.Refresh();
                AudioController.Play(SfxId.Click);
            }, 120f);
            sw.Refresh();
            return sw;
        }
    }

    // A value picker: "<  value  >". text() gives the shown value, move(+1 / -1) steps it (the caller wraps or clamps).
    public sealed class Stepper
    {
        public TMP_Text Value;
        private System.Func<string> m_Text;

        public void Refresh() => Value.text = m_Text();

        public static Stepper Create(Transform parent, System.Func<string> text, System.Action<int> move)
        {
            var stepper = new Stepper { m_Text = text };
            MakeButton(parent, "<", () => { move(-1); stepper.Refresh(); AudioController.Play(SfxId.Click); }, 34f);
            stepper.Value = Text(parent, "", 15, Color.white);
            stepper.Value.alignment = TextAlignmentOptions.Center;
            stepper.Value.GetComponent<LayoutElement>().minWidth = 190f;
            MakeButton(parent, ">", () => { move(1); stepper.Refresh(); AudioController.Play(SfxId.Click); }, 34f);
            stepper.Refresh();
            return stepper;
        }
    }
}
