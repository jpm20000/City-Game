using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The Esc menu (M19b): Resume, Save, Save as, Load, New city, Settings, Quit. Opening it pauses the game (GameFlow); a
// load or New City from it closes it again. The main menu entry joins in M19c.
public sealed class PauseMenu
{
    private readonly UiKit.Window m_Window;
    private readonly GameFlow m_Flow;
    private readonly TMP_Text m_City;
    private readonly System.Action m_Closer;

    public bool IsOpen => m_Window.IsOpen;

    public PauseMenu(Transform canvas, GameFlow flow)
    {
        m_Flow = flow;
        m_Closer = Hide;
        m_Window = UiKit.CreateWindow(canvas, "PauseMenu", "Paused", 360f);
        m_Window.Title.alignment = TextAlignmentOptions.Center;
        m_City = UiKit.Text(m_Window.Body, "", 16, UiKit.MutedColor);
        m_City.alignment = TextAlignmentOptions.Center;

        AddButton("Resume", Hide, primary: true);
        AddButton("Save", SaveClicked);
        AddButton("Save as…", () => m_Flow.Browser.Open(SaveBrowser.Mode.Save));
        AddButton("Load…", () => m_Flow.Browser.Open(SaveBrowser.Mode.Load));
        if (m_Flow.Menu != null) AddButton("New city", () => m_Flow.Menu.RequestNew());
        AddButton("Settings", SettingsClicked);
        AddButton("Main menu", () => m_Flow.GuardDiscard(m_Flow.EnterMainMenu));
#if !UNITY_WEBGL
        AddButton("Quit", () => m_Flow.Quit());   // a browser tab cannot quit
#endif

        EscapeRouter.Register(this, EscapeRouter.PauseMenu, () =>
        {
            if (!IsOpen) return false;
            Hide();
            return true;
        });
    }

    public void Show()
    {
        SaveGameController save = m_Flow.Save;
        string city = save != null && !string.IsNullOrWhiteSpace(save.CityName) ? save.CityName : SaveSlots.UnnamedCity;
        string saved = save != null && !string.IsNullOrEmpty(save.CurrentName) ? $"saved as {save.CurrentName}" : "not saved yet";
        m_City.text = $"{city} — {saved}{(save != null && save.Dirty ? ", unsaved changes" : "")}";
        m_Window.Show();
        m_Flow.WindowOpened(m_Closer);
        AudioController.Play(SfxId.Click);
    }

    public void Hide()
    {
        m_Window.Hide();
        m_Flow.WindowClosed(m_Closer);
    }

    private void AddButton(string label, UnityEngine.Events.UnityAction onClick, bool primary = false)
    {
        Button button = UiKit.MakeButton(m_Window.Body, label, onClick, 0f, primary ? UiKit.AccentColor : (Color?)null);
        button.GetComponent<LayoutElement>().minHeight = 38f;
    }

    // Save: the city's own file, or the Save as window when it has none yet.
    private void SaveClicked()
    {
        SaveGameController save = m_Flow.Save;
        if (string.IsNullOrEmpty(save.CurrentName))
        {
            m_Flow.Browser.Open(SaveBrowser.Mode.Save);
            return;
        }
        if (save.Save()) Show();
    }

    // The Settings window opens over the pause menu; Esc closes it first.
    private void SettingsClicked() => m_Flow.Settings.Open();
}
