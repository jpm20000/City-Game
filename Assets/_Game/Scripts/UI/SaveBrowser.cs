using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The Save as / Load window (M19b): every named save with its thumbnail and a one-line summary, newest first. Save mode
// has a name field (a click on a row copies its name there, to overwrite it); Load mode loads the selected save. Rename
// and Delete work on the selected save in both. The list reads only the sidecar summaries, never the cities.
public sealed class SaveBrowser
{
    public enum Mode { Save, Load }

    private static readonly Color RowColor = new Color(0.16f, 0.18f, 0.23f);
    private static readonly Color RowSelectedColor = new Color(0.20f, 0.36f, 0.62f);
    private static readonly Color ThumbBackColor = new Color(0.07f, 0.08f, 0.10f);
    private const float ListHeight = 380f;

    private readonly UiKit.Window m_Window;
    private readonly GameFlow m_Flow;
    private readonly Action m_Closer;
    private readonly TMP_InputField m_Name;
    private readonly RectTransform m_Content;
    private readonly TMP_Text m_Empty;
    private readonly TMP_Text m_Status;
    private readonly Button m_Primary;
    private readonly Button m_Rename;
    private readonly Button m_Delete;
    private readonly List<Row> m_Rows = new();
    private readonly List<Texture2D> m_Textures = new();

    private Mode m_Mode;
    private string m_Selected = "";

    private sealed class Row
    {
        public SaveSummary Summary;
        public Image Background;
    }

    public bool IsOpen => m_Window.IsOpen;
    public string SelectedName => m_Selected;
    public Mode CurrentMode => m_Mode;
    public int RowCount => m_Rows.Count;

    public SaveBrowser(Transform canvas, GameFlow flow)
    {
        m_Flow = flow;
        m_Closer = Hide;
        m_Window = UiKit.CreateWindow(canvas, "SaveBrowser", "Save city", 860f);

        RectTransform nameRow = UiKit.Row(m_Window.Body, 34f);
        UiKit.Text(nameRow, "Name", 16, UiKit.BodyColor).GetComponent<LayoutElement>().minWidth = 60f;
        m_Name = UiKit.InputField(nameRow, "Name of the save", SaveSlots.MaxNameLength);

        BuildList(out m_Content, out m_Empty);
        m_Status = UiKit.Text(m_Window.Body, "", 14, UiKit.MutedColor);

        RectTransform buttons = UiKit.Row(m_Window.Body, 38f);
        m_Primary = UiKit.MakeButton(buttons, "Save", PrimaryClicked, 0f, UiKit.AccentColor);
        m_Rename = UiKit.MakeButton(buttons, "Rename", RenameClicked, 0f);
        m_Delete = UiKit.MakeButton(buttons, "Delete", DeleteClicked, 0f);
        Button close = UiKit.MakeButton(buttons, "Close", Hide, 0f);
        foreach (Button b in new[] { m_Primary, m_Rename, m_Delete, close }) b.GetComponent<LayoutElement>().flexibleWidth = 1f;

        EscapeRouter.Register(this, EscapeRouter.Window, () =>
        {
            if (!IsOpen) return false;
            Hide();
            return true;
        });
    }

    private void BuildList(out RectTransform content, out TMP_Text empty)
    {
        var scroll = new GameObject("List", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
        scroll.transform.SetParent(m_Window.Body, false);
        scroll.GetComponent<Image>().color = new Color(0.07f, 0.08f, 0.11f, 1f);
        var element = scroll.GetComponent<LayoutElement>();
        element.preferredHeight = ListHeight;
        element.minHeight = ListHeight;
        element.flexibleWidth = 1f;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(scroll.transform, false);
        var viewportRect = (RectTransform)viewport.transform;
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = viewportRect.offsetMax = Vector2.zero;

        var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewport.transform, false);
        content = (RectTransform)contentGo.transform;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;
        var layout = contentGo.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scrollRect = scroll.GetComponent<ScrollRect>();
        scrollRect.viewport = viewportRect;
        scrollRect.content = content;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;

        empty = UiKit.Text(scroll.transform, "No saved cities yet.", 18, UiKit.MutedColor);
        empty.alignment = TextAlignmentOptions.Center;
        var emptyRect = (RectTransform)empty.transform;
        emptyRect.anchorMin = Vector2.zero;
        emptyRect.anchorMax = Vector2.one;
        emptyRect.offsetMin = emptyRect.offsetMax = Vector2.zero;
        UnityEngine.Object.Destroy(empty.GetComponent<LayoutElement>());
    }

    public void Open(Mode mode)
    {
        m_Mode = mode;
        m_Window.Title.text = mode == Mode.Save ? "Save city" : "Load city";
        UiKit.SetLabel(m_Primary, mode == Mode.Save ? "Save" : "Load");
        m_Selected = "";
        m_Status.text = "";

        SaveGameController save = m_Flow.Save;
        if (mode == Mode.Save)
        {
            string city = string.IsNullOrWhiteSpace(save.CityName) ? $"City {DateTime.Now:yyyy-MM-dd}" : save.CityName;
            m_Name.text = !string.IsNullOrEmpty(save.CurrentName) ? save.CurrentName : SaveSlots.UniqueName(city);
        }
        else
        {
            m_Name.text = "";
        }

        Refresh();
        if (mode == Mode.Save && SaveSlots.Exists(m_Name.text)) Select(m_Name.text, copyName: false);
        m_Window.Show();
        m_Flow.WindowOpened(m_Closer);
        AudioController.Play(SfxId.Click);
    }

    public void Hide()
    {
        m_Window.Hide();
        ClearRows();
        m_Flow.WindowClosed(m_Closer);
    }

    // --- The list ---

    private void Refresh()
    {
        GameManager game = FindGame();
        List<SaveSummary> saves = SaveSlots.List(game != null ? game.Ages : null, game != null ? game.Techs : null);
        ClearRows();
        foreach (SaveSummary summary in saves) AddRow(summary);
        m_Empty.gameObject.SetActive(saves.Count == 0);
        UpdateButtons();
    }

    private static GameManager FindGame() => UnityEngine.Object.FindAnyObjectByType<GameManager>();

    private void ClearRows()
    {
        foreach (Transform child in m_Content)
        {
            child.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(child.gameObject);
        }
        foreach (Texture2D texture in m_Textures) UnityEngine.Object.Destroy(texture);
        m_Textures.Clear();
        m_Rows.Clear();
    }

    private void AddRow(SaveSummary summary)
    {
        var go = new GameObject("Row_" + summary.Name, typeof(RectTransform), typeof(Image), typeof(Button),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        go.transform.SetParent(m_Content, false);
        var image = go.GetComponent<Image>();
        image.color = RowColor;
        go.GetComponent<LayoutElement>().minHeight = 84f;
        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(6, 8, 6, 6);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleLeft;

        var thumb = new GameObject("Thumbnail", typeof(RectTransform), typeof(RawImage), typeof(LayoutElement));
        thumb.transform.SetParent(go.transform, false);
        var thumbElement = thumb.GetComponent<LayoutElement>();
        thumbElement.preferredWidth = 128f;
        thumbElement.preferredHeight = 72f;
        var raw = thumb.GetComponent<RawImage>();
        raw.raycastTarget = false;
        raw.color = ThumbBackColor;
        Texture2D texture = LoadThumbnail(summary);
        if (texture != null)
        {
            raw.texture = texture;
            raw.color = Color.white;
            m_Textures.Add(texture);
        }

        var info = new GameObject("Info", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        info.transform.SetParent(go.transform, false);
        info.GetComponent<LayoutElement>().flexibleWidth = 1f;
        var infoLayout = info.GetComponent<VerticalLayoutGroup>();
        infoLayout.spacing = 2f;
        infoLayout.childControlWidth = true;
        infoLayout.childControlHeight = true;
        infoLayout.childForceExpandWidth = true;
        infoLayout.childForceExpandHeight = false;
        UiKit.Text(info.transform, summary.Name + Tag(summary), 19, Color.white);
        UiKit.Text(info.transform, Describe(summary), 14, UiKit.BodyColor);
        UiKit.Text(info.transform, When(summary), 13, UiKit.MutedColor);

        var row = new Row { Summary = summary, Background = image };
        go.GetComponent<Button>().onClick.AddListener(() => Select(summary.Name, copyName: true));
        m_Rows.Add(row);
    }

    private static string Tag(SaveSummary s)
    {
        if (s.Damaged) return "  [damaged]";
        return s.SaveKind == SaveKind.Quick ? "  [quicksave]" : s.SaveKind == SaveKind.Auto ? "  [autosave]" : "";
    }

    private static string Describe(SaveSummary s)
    {
        if (s.Damaged) return "This save can't be read. It can only be deleted.";
        string city = string.IsNullOrWhiteSpace(s.CityName) ? SaveSlots.UnnamedCity : s.CityName;
        string age = string.IsNullOrEmpty(s.AgeName) ? "" : s.AgeName + ", ";
        string tutorial = s.Tutorial ? " · tutorial" : "";
        return $"{city} · {age}year {s.Year} · {s.Population:N0} residents · ${s.Money:N0} · {s.Width}×{s.Height}{tutorial}";
    }

    private static string When(SaveSummary s)
    {
        return s.SavedUtcTicks > 0 ? "Saved " + s.SavedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "";
    }

    private Texture2D LoadThumbnail(SaveSummary summary)
    {
        if (!summary.HasThumbnail) return null;
        try
        {
            byte[] bytes = System.IO.File.ReadAllBytes(SaveSlots.ThumbnailPath(summary.Name));
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (texture.LoadImage(bytes)) return texture;
            UnityEngine.Object.Destroy(texture);
        }
        catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
        {
        }
        return null;
    }

    public void Select(string name, bool copyName)
    {
        m_Selected = name;
        foreach (Row row in m_Rows) row.Background.color = row.Summary.Name == name ? RowSelectedColor : RowColor;
        if (copyName) m_Name.text = name;
        m_Status.text = "";
        UpdateButtons();
    }

    private SaveSummary SelectedSummary()
    {
        foreach (Row row in m_Rows)
        {
            if (row.Summary.Name == m_Selected) return row.Summary;
        }
        return null;
    }

    private void UpdateButtons()
    {
        SaveSummary selected = SelectedSummary();
        m_Primary.interactable = m_Mode == Mode.Save || (selected != null && !selected.Damaged);
        m_Rename.interactable = selected != null && !selected.Damaged;
        m_Delete.interactable = selected != null;
    }

    // --- Actions ---

    private void PrimaryClicked()
    {
        if (m_Mode == Mode.Load) LoadSelected();
        else SaveToName();
    }

    private void SaveToName()
    {
        string name = SaveSlots.SanitizeName(m_Name.text);
        if (!SaveSlots.Exists(name))
        {
            DoSave(name);
            return;
        }
        m_Flow.Confirm.Ask("Replace this save?", $"A save called '{name}' already exists. Replace it with this city?",
            new ConfirmDialog.Choice("Replace", () => DoSave(name), primary: true),
            new ConfirmDialog.Choice("Cancel", null));
    }

    private void DoSave(string name)
    {
        if (m_Flow.Save.SaveAs(name)) Hide();
        else m_Status.text = "Couldn't save. See the log for details.";
    }

    private void LoadSelected()
    {
        SaveSummary selected = SelectedSummary();
        if (selected == null || selected.Damaged) return;
        string name = selected.Name;
        m_Flow.GuardDiscard(() =>
        {
            if (!m_Flow.Save.Load(name)) m_Status.text = "Couldn't load that save.";
        });
    }

    private void RenameClicked()
    {
        SaveSummary selected = SelectedSummary();
        if (selected == null) return;
        if (!m_Flow.Save.Rename(selected.Name, m_Name.text, out string finalName, out string error))
        {
            m_Status.text = error;
            return;
        }
        Refresh();
        Select(finalName, copyName: true);
        m_Status.text = "Renamed.";
    }

    private void DeleteClicked()
    {
        SaveSummary selected = SelectedSummary();
        if (selected == null) return;
        string name = selected.Name;
        m_Flow.Confirm.Ask("Delete this save?", $"'{name}' will be deleted. This can't be undone.",
            new ConfirmDialog.Choice("Delete", () =>
            {
                m_Flow.Save.Delete(name);
                m_Selected = "";
                Refresh();
                m_Status.text = $"Deleted {name}.";
            }, primary: true),
            new ConfirmDialog.Choice("Cancel", null));
    }
}
