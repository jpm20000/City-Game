using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The Settings window (M19d): tabs for Audio, Display, Interface, Gameplay and Controls (M19e, the rebindable keys). It is a counted
// window, so opened in a game it pauses it, and opened from the title it leaves the showcase running. Every control
// writes GameSettings at once and applies itself (the screen, the UI scale); a Reset button restores one tab's defaults.
public sealed class SettingsPanel
{
    public enum Tab { Audio, Display, Interface, Gameplay, Controls }

    private static readonly string[] TabNames = { "Audio", "Display", "Interface", "Gameplay", "Controls" };
    private static readonly string[] ModeNames = { "Full screen", "Borderless window", "Windowed" };
    private const float LabelWidth = 250f;

    private readonly UiKit.Window m_Window;
    private readonly GameFlow m_Flow;
    private readonly Action m_Closer;
    private readonly RectTransform m_Content;
    private readonly List<Button> m_TabButtons = new();
    private readonly List<Tab> m_TabIds = new();   // the tab each button opens (the Display tab has none on WebGL)
    private Tab m_Tab;
    private string m_Status = string.Empty;

    public bool IsOpen => m_Window.IsOpen;
    public Tab CurrentTab => m_Tab;

    public SettingsPanel(Transform canvas, GameFlow flow)
    {
        m_Flow = flow;
        m_Closer = Hide;
        m_Window = UiKit.CreateWindow(canvas, "SettingsPanel", "Settings", 780f);

        RectTransform tabs = UiKit.Row(m_Window.Body, 36f);
        for (int i = 0; i < TabNames.Length; i++)
        {
            var tab = (Tab)i;
#if UNITY_WEBGL
            if (tab == Tab.Display) continue;   // the browser owns the window
#endif
            Button button = UiKit.MakeButton(tabs, TabNames[i], () => SelectTab(tab), 0f);
            button.GetComponent<LayoutElement>().flexibleWidth = 1f;
            m_TabButtons.Add(button);
            m_TabIds.Add(tab);
        }

        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        content.transform.SetParent(m_Window.Body, false);
        m_Content = (RectTransform)content.transform;
        var layout = content.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.padding = new RectOffset(4, 4, 6, 6);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.GetComponent<LayoutElement>().minHeight = 290f;

        RectTransform footer = UiKit.Row(m_Window.Body, 38f);
        Button reset = UiKit.MakeButton(footer, "Reset this tab", ResetTab, 0f);
        Button close = UiKit.MakeButton(footer, "Close", Hide, 0f, UiKit.AccentColor);
        reset.GetComponent<LayoutElement>().flexibleWidth = 1f;
        close.GetComponent<LayoutElement>().flexibleWidth = 1f;

        EscapeRouter.Register(this, EscapeRouter.Window, () =>
        {
            if (!IsOpen) return false;
            if (KeyBindings.SwallowEscape) return true;
            Hide();
            return true;
        });
    }

    public void Open(Tab? tab = null)
    {
        m_Window.Show();
        m_Flow.WindowOpened(m_Closer);
        SelectTab(tab ?? m_Tab);
        AudioController.Play(SfxId.Click);
    }

    public void Hide()
    {
        m_Window.Hide();
        m_Flow.WindowClosed(m_Closer);
    }

    public void SelectTab(Tab tab)
    {
        m_Tab = tab;
        for (int i = 0; i < m_TabButtons.Count; i++)
        {
            m_TabButtons[i].GetComponent<Image>().color = m_TabIds[i] == tab ? UiKit.AccentColor : UiKit.ButtonColor;
        }

        foreach (Transform child in m_Content)
        {
            child.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(child.gameObject);
        }
        switch (tab)
        {
            case Tab.Audio: BuildAudio(); break;
            case Tab.Display: BuildDisplay(); break;
            case Tab.Interface: BuildInterface(); break;
            case Tab.Controls: BuildControls(); break;
            default: BuildGameplay(); break;
        }
    }

    private void ResetTab()
    {
        switch (m_Tab)
        {
            case Tab.Audio: GameSettings.ResetAudio(); break;
            case Tab.Display:
                GameSettings.ResetDisplay();
                GameSettings.ApplyDisplay(force: true);
                break;
            case Tab.Interface:
                GameSettings.ResetInterface();
                UiScaling.Apply();
                break;
            case Tab.Controls: KeyBindings.ResetAll(); m_Status = string.Empty; break;
            default: GameSettings.ResetGameplay(); break;
        }
        SelectTab(m_Tab);
    }

    // ---- tabs -------------------------------------------------------------------------------------------

    private void BuildAudio()
    {
        SliderRow("Master volume", GameSettings.Master, v => GameSettings.Master = v);
        SliderRow("Music", GameSettings.Get(SoundChannel.Music), v => GameSettings.Set(SoundChannel.Music, v));
        SliderRow("Ambience", GameSettings.Get(SoundChannel.Ambience), v => GameSettings.Set(SoundChannel.Ambience, v));
        SliderRow("Effects", GameSettings.Get(SoundChannel.Sfx), v => GameSettings.Set(SoundChannel.Sfx, v));
        SwitchRow("Mute all sound", () => GameSettings.Mute, v => GameSettings.Mute = v);
    }

    private void BuildDisplay()
    {
        StepRow("Window mode", () => ModeNames[GameSettings.DisplayMode], step =>
        {
            GameSettings.DisplayMode = Wrap(GameSettings.DisplayMode + step, ModeNames.Length);
            GameSettings.ApplyDisplay(force: true);
        });

        List<Vector2Int> resolutions = ResolutionOptions();
        StepRow("Resolution", () => ResolutionLabel(resolutions), step =>
        {
            int index = Wrap(CurrentResolutionIndex(resolutions) + step, resolutions.Count);
            GameSettings.Resolution = resolutions[index];
            GameSettings.ApplyDisplay(force: true);
        });

        SwitchRow("Vertical sync", () => GameSettings.VSync, v =>
        {
            GameSettings.VSync = v;
            GameSettings.ApplyDisplay(force: true);
        });
        StepRow("Frame rate cap (VSync off)", () => GameSettings.FrameCap == 0 ? "Unlimited" : GameSettings.FrameCap + " fps", step =>
        {
            int index = Wrap(NearestIndex(GameSettings.FrameCaps, GameSettings.FrameCap) + step, GameSettings.FrameCaps.Length);
            GameSettings.FrameCap = GameSettings.FrameCaps[index];
            GameSettings.ApplyDisplay(force: true);
        });
    }

    private void BuildInterface()
    {
        StepRow("UI scale", () => Mathf.RoundToInt(GameSettings.UiScale * 100f) + "%", step =>
        {
            int index = Mathf.Clamp(NearestIndex(GameSettings.UiScales, GameSettings.UiScale) + step, 0, GameSettings.UiScales.Length - 1);
            GameSettings.UiScale = GameSettings.UiScales[index];
            UiScaling.Apply();
        });
        SwitchRow("Always day (no night)", () => GameSettings.LockToDay, v => GameSettings.LockToDay = v);
        StepRow("Camera pan speed", () => Mathf.RoundToInt(GameSettings.PanSpeed * 100f) + "%", step =>
        {
            int index = Mathf.Clamp(NearestIndex(GameSettings.PanSpeeds, GameSettings.PanSpeed) + step, 0, GameSettings.PanSpeeds.Length - 1);
            GameSettings.PanSpeed = GameSettings.PanSpeeds[index];
        });
        SwitchRow("Scroll at the screen edge", () => GameSettings.EdgeScroll, v => GameSettings.EdgeScroll = v);
        StepRow("Tooltip delay", () => GameSettings.TooltipDelay <= 0f ? "None" : GameSettings.TooltipDelay.ToString("0.0") + " s", step =>
        {
            int index = Mathf.Clamp(NearestIndex(GameSettings.TooltipDelays, GameSettings.TooltipDelay) + step, 0, GameSettings.TooltipDelays.Length - 1);
            GameSettings.TooltipDelay = GameSettings.TooltipDelays[index];
        });
    }

    private void BuildGameplay()
    {
        StepRow("Autosave", () => AutosaveLabel(GameSettings.AutosaveMonths), step =>
        {
            int index = Mathf.Clamp(NearestIndex(GameSettings.AutosaveChoices, GameSettings.AutosaveMonths) + step, 0, GameSettings.AutosaveChoices.Length - 1);
            GameSettings.AutosaveMonths = GameSettings.AutosaveChoices[index];
        });
        SwitchRow("Disasters & events in new cities", () => GameSettings.DisastersByDefault, v => GameSettings.DisastersByDefault = v);
        SwitchRow("Pause the game on a random event", () => GameSettings.PauseOnEvents, v => GameSettings.PauseOnEvents = v);
        SwitchRow("Confirm before demolishing a building", () => GameSettings.ConfirmDemolish, v => GameSettings.ConfirmDemolish = v);
    }

    private void BuildControls()
    {
        foreach (KeyBindings.Entry entry in KeyBindings.Entries)
        {
            KeyBindings.Entry captured = entry;
            RectTransform row = NewRow(entry.Label);
            Button key = null;
            key = UiKit.MakeButton(row, KeyBindings.Label(entry), () =>
            {
                UiKit.SetLabel(key, "Press a key…");
                key.GetComponent<Image>().color = UiKit.AccentColor;
                m_Status = "Press the new key, or Esc to cancel.";
                KeyBindings.Rebind(captured, message =>
                {
                    m_Status = message;
                    if (m_Window.IsOpen && m_Tab == Tab.Controls) SelectTab(Tab.Controls);
                });
            }, 170f);
            UiKit.MakeButton(row, "Reset", () =>
            {
                KeyBindings.Reset(captured);
                m_Status = string.Empty;
                SelectTab(Tab.Controls);
            }, 80f, KeyBindings.IsDefault(entry) ? UiKit.DisabledColor : (Color?)null);
        }
        TMP_Text status = UiKit.Text(m_Content, string.IsNullOrEmpty(m_Status) ? "Esc, F1, 1-4 (speed), the arrow keys and the mouse are fixed." : m_Status, 14, UiKit.MutedColor);
        status.name = "ControlsStatus";
    }

    // ---- rows -------------------------------------------------------------------------------------------

    private RectTransform NewRow(string label)
    {
        RectTransform row = UiKit.Row(m_Content, 34f);
        TMP_Text text = UiKit.Text(row, label, 16, UiKit.BodyColor);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.GetComponent<LayoutElement>().minWidth = LabelWidth;
        return row;
    }

    private void SliderRow(string label, float value, UnityEngine.Events.UnityAction<float> onChanged)
    {
        UiKit.MakeSlider(NewRow(label), value, 0f, 1f, onChanged);
    }

    private void SwitchRow(string label, Func<bool> get, Action<bool> set)
    {
        UiKit.SwitchButton.Create(NewRow(label), get, set);
    }

    private void StepRow(string label, Func<string> text, Action<int> move)
    {
        UiKit.Stepper.Create(NewRow(label), text, move);
    }

    // ---- helpers ----------------------------------------------------------------------------------------

    private static int Wrap(int value, int count) => count <= 0 ? 0 : ((value % count) + count) % count;

    private static int NearestIndex(float[] values, float value)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++)
        {
            if (Mathf.Abs(values[i] - value) < Mathf.Abs(values[best] - value)) best = i;
        }
        return best;
    }

    private static int NearestIndex(int[] values, int value)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++)
        {
            if (Mathf.Abs(values[i] - value) < Mathf.Abs(values[best] - value)) best = i;
        }
        return best;
    }

    private static string AutosaveLabel(int months)
    {
        return months <= 0 ? "Off" : months == 1 ? "Every month" : $"Every {months} months";
    }

    // The screen's resolutions from 1280x720 up (one entry per size), always with the current one.
    public static List<Vector2Int> ResolutionOptions()
    {
        var set = new SortedSet<(int w, int h)>();
        foreach (Resolution r in Screen.resolutions)
        {
            if (r.width >= 1280 && r.height >= 720) set.Add((r.width, r.height));
        }
        if (Screen.width > 0 && Screen.height > 0) set.Add((Screen.width, Screen.height));
        Vector2Int chosen = GameSettings.Resolution;
        if (chosen.x > 0 && chosen.y > 0) set.Add((chosen.x, chosen.y));
        if (set.Count == 0) set.Add((1920, 1080));
        var list = new List<Vector2Int>();
        foreach ((int w, int h) in set) list.Add(new Vector2Int(w, h));
        return list;
    }

    private static int CurrentResolutionIndex(List<Vector2Int> options)
    {
        Vector2Int chosen = GameSettings.Resolution;
        Vector2Int target = chosen.x > 0 && chosen.y > 0 ? chosen : new Vector2Int(Screen.width, Screen.height);
        int index = options.IndexOf(target);
        return index >= 0 ? index : 0;
    }

    private static string ResolutionLabel(List<Vector2Int> options)
    {
        Vector2Int size = options[CurrentResolutionIndex(options)];
        return $"{size.x} × {size.y}";
    }
}
