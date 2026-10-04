using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The title screen (M19c): a panel on the left of the screen, the showcase city running behind it. Continue loads the
// newest save; New city, Load and Quit work as in the pause menu. It is not a counted window: GameFlow's MainMenu state
// is what blocks input and hides the HUD. The Tutorial starts a guided Medieval city (M19f).
public sealed class MainMenu
{
    private readonly UiKit.Window m_Window;
    private readonly GameFlow m_Flow;
    private readonly Button m_Continue;
    private readonly TMP_Text m_Newest;

    public bool IsOpen => m_Window.IsOpen;

    public MainMenu(Transform canvas, GameFlow flow)
    {
        m_Flow = flow;
        m_Window = UiKit.CreateWindow(canvas, "MainMenu", Application.productName, 380f);
        m_Window.Root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

        var panel = m_Window.Body;
        panel.anchorMin = panel.anchorMax = new Vector2(0f, 0.5f);
        panel.pivot = new Vector2(0f, 0.5f);
        panel.anchoredPosition = new Vector2(120f, 0f);
        m_Window.Title.fontSize = 54;
        m_Window.Title.alignment = TextAlignmentOptions.Center;
        AddLogo(panel);
        TMP_Text tagline = UiKit.Text(panel, "From the Middle Ages to the Modern Era", 15, UiKit.MutedColor);
        tagline.alignment = TextAlignmentOptions.Center;

        m_Continue = AddButton("Continue", Continue, primary: true);
        m_Newest = UiKit.Text(panel, "", 13, UiKit.MutedColor);
        m_Newest.alignment = TextAlignmentOptions.Center;
        if (flow.Menu != null) AddButton("New city", () => flow.Menu.RequestNew());
        AddButton("Tutorial", () => flow.StartTutorial());
        AddButton("Load…", () => flow.Browser.Open(SaveBrowser.Mode.Load));
        AddButton("Settings", () => flow.Settings.Open());
        AddButton("Quit", () => flow.Quit());

        TMP_Text version = UiKit.Text(m_Window.Root.transform, "v" + Application.version, 14, new Color(1f, 1f, 1f, 0.55f));
        var versionRect = (RectTransform)version.transform;
        versionRect.anchorMin = versionRect.anchorMax = Vector2.zero;
        versionRect.pivot = Vector2.zero;
        versionRect.anchoredPosition = new Vector2(16f, 12f);
        versionRect.sizeDelta = new Vector2(200f, 24f);
        Object.Destroy(version.GetComponent<LayoutElement>());
    }

    // The logo (Resources/Logo) replaces the title text; without the asset the text stays.
    private void AddLogo(RectTransform panel)
    {
        Sprite logo = Resources.Load<Sprite>("Logo");
        if (logo == null) return;
        m_Window.Title.gameObject.SetActive(false);
        var go = new GameObject("Logo", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(panel, false);
        go.transform.SetSiblingIndex(m_Window.Title.transform.GetSiblingIndex());
        var image = go.GetComponent<Image>();
        image.sprite = logo;
        image.preserveAspect = true;
        image.raycastTarget = false;
        var element = go.GetComponent<LayoutElement>();
        element.preferredWidth = 380f;
        element.preferredHeight = 380f * logo.rect.height / logo.rect.width;
    }

    public void Show()
    {
        Refresh();
        m_Window.Show();
    }

    public void Hide() => m_Window.Hide();

    // Continue needs a save: the newest one is named under the button.
    private void Refresh()
    {
        GameManager game = Object.FindAnyObjectByType<GameManager>();
        SaveSummary newest = null;
        foreach (SaveSummary save in SaveSlots.List(game != null ? game.Ages : null, game != null ? game.Techs : null))
        {
            if (save.Damaged) continue;
            newest = save;
            break;
        }
        m_Continue.interactable = newest != null;
        m_Newest.text = newest != null ? $"{newest.Name} · {newest.SavedUtc.ToLocalTime():yyyy-MM-dd HH:mm}" : "No saved cities yet";
    }

    private void Continue()
    {
        if (!m_Flow.Save.LoadNewest()) Refresh();
    }

    private Button AddButton(string label, UnityEngine.Events.UnityAction onClick, bool primary = false)
    {
        Button button = UiKit.MakeButton(m_Window.Body, label, onClick, 0f, primary ? UiKit.AccentColor : (Color?)null);
        button.GetComponent<LayoutElement>().minHeight = 46f;
        return button;
    }
}
