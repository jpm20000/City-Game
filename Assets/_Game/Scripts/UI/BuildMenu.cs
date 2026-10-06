using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The Build menu (M25c): every placeable building in one searchable list, opened with its key (F by default) or the
// toolbar's Build button. Type to search (name, id, group, age, effect words, "custom"), narrow by group, age or
// Custom, arrows + Enter or a click pick one, and placement starts exactly as from the toolbar flyouts. Buildings whose
// tech isn't researched are listed greyed at the end with the tech they need. A window like the save browser: the game
// pauses while it is open and gameplay keys are blocked, so typing never moves the camera. The matching itself is
// BuildMenuFilter (pure, tested).
public sealed class BuildMenu
{
    private static readonly Color RowColor = new Color(0.16f, 0.18f, 0.23f);
    private static readonly Color RowSelectedColor = new Color(0.20f, 0.36f, 0.62f);
    private static readonly Color RowLockedColor = new Color(0.12f, 0.13f, 0.16f);
    private static readonly Color CustomColor = new Color(0.45f, 0.85f, 0.60f);
    private const float ListHeight = 400f;
    private const float RowHeight = 52f;
    private const float RowSpacing = 4f;
    private const float ListPadding = 4f;
    private const string AllGroups = "All";

    private readonly UiKit.Window m_Window;
    private readonly GameFlow m_Flow;
    private readonly Action m_Closer;
    private readonly TMP_InputField m_Search;
    private readonly RectTransform m_Content;
    private readonly ScrollRect m_Scroll;
    private readonly TMP_Text m_Empty;
    private readonly TMP_Text m_Status;
    private readonly Button m_AgeButton;
    private readonly Button m_CustomButton;
    private readonly Button m_LockedButton;
    private readonly Dictionary<string, Button> m_GroupButtons = new();
    private readonly List<BuildMenuEntry> m_Entries = new();
    private readonly List<BuildingDefinition> m_Defs = new();
    private readonly List<string> m_LockReasons = new();
    private readonly List<Image> m_RowImages = new();
    private List<int> m_Shown = new();

    private string m_Group = AllGroups;
    private int m_Age = -1;
    private bool m_CustomOnly;
    private bool m_ShowLocked = true;
    private int m_Selected;

    public bool IsOpen => m_Window.IsOpen;
    public int ShownCount => m_Shown.Count;
    public int SelectedIndex => m_Selected;
    public string SelectedId => m_Selected >= 0 && m_Selected < m_Shown.Count ? m_Entries[m_Shown[m_Selected]].Id : "";
    public string Query => m_Search.text;

    public BuildMenu(Transform canvas, GameFlow flow)
    {
        m_Flow = flow;
        m_Closer = Hide;
        m_Window = UiKit.CreateWindow(canvas, "BuildMenu", "Build", 820f);

        RectTransform searchRow = UiKit.Row(m_Window.Body, 36f);
        m_Search = UiKit.InputField(searchRow, "Search buildings: name, group, age, effect (jobs, power, police...)", 40);
        m_Search.name = "BuildSearch";
        m_Search.onValueChanged.AddListener(_ => Refresh(resetSelection: true));
        m_Search.onSubmit.AddListener(_ => PickSelected());

        RectTransform filters = UiKit.Row(m_Window.Body, 32f);
        foreach (string group in new[] { AllGroups, "Utilities", "Services", "Health", "Education", "Parks" })
        {
            string captured = group;
            Button b = UiKit.MakeButton(filters, group, () => { m_Group = captured; Refresh(true); }, 0f);
            b.name = "Group_" + group;
            b.GetComponent<LayoutElement>().flexibleWidth = 1f;
            m_GroupButtons[group] = b;
        }
        RectTransform filters2 = UiKit.Row(m_Window.Body, 32f);
        m_AgeButton = UiKit.MakeButton(filters2, "Any age", CycleAge, 0f);
        m_AgeButton.name = "AgeFilter";
        m_CustomButton = UiKit.MakeButton(filters2, "Custom only", () => { m_CustomOnly = !m_CustomOnly; Refresh(true); }, 0f);
        m_CustomButton.name = "CustomFilter";
        m_LockedButton = UiKit.MakeButton(filters2, "Locked: shown", () => { m_ShowLocked = !m_ShowLocked; Refresh(true); }, 0f);
        m_LockedButton.name = "LockedFilter";
        foreach (Button b in new[] { m_AgeButton, m_CustomButton, m_LockedButton }) b.GetComponent<LayoutElement>().flexibleWidth = 1f;

        BuildList(out m_Content, out m_Scroll, out m_Empty);
        m_Status = UiKit.Text(m_Window.Body, "", 14, UiKit.MutedColor);
        Button close = UiKit.MakeButton(UiKit.Row(m_Window.Body, 36f), "Close", Hide, 0f);
        close.GetComponent<LayoutElement>().flexibleWidth = 1f;

        m_Window.Root.AddComponent<Driver>().Owner = this;
        EscapeRouter.Register(this, EscapeRouter.Window, () =>
        {
            if (!IsOpen) return false;
            Hide();
            return true;
        });
    }

    private void BuildList(out RectTransform content, out ScrollRect scrollRect, out TMP_Text empty)
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
        layout.padding = new RectOffset((int)ListPadding, (int)ListPadding, (int)ListPadding, (int)ListPadding);
        layout.spacing = RowSpacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect = scroll.GetComponent<ScrollRect>();
        scrollRect.viewport = viewportRect;
        scrollRect.content = content;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;

        empty = UiKit.Text(scroll.transform, "No building matches.", 18, UiKit.MutedColor);
        empty.alignment = TextAlignmentOptions.Center;
        var emptyRect = (RectTransform)empty.transform;
        emptyRect.anchorMin = Vector2.zero;
        emptyRect.anchorMax = Vector2.one;
        emptyRect.offsetMin = emptyRect.offsetMax = Vector2.zero;
        UnityEngine.Object.Destroy(empty.GetComponent<LayoutElement>());
    }

    // ---- open / close ---------------------------------------------------------------------------------

    public void Open()
    {
        if (IsOpen) return;
        GameManager game = UnityEngine.Object.FindAnyObjectByType<GameManager>();
        if (game == null || game.Buildings == null) return;
        Collect(game);

        m_Search.SetTextWithoutNotify("");
        m_Group = AllGroups;
        m_Age = -1;
        m_CustomOnly = false;
        m_ShowLocked = true;
        Refresh(resetSelection: true);
        m_Window.Show();
        m_Flow.WindowOpened(m_Closer);
        m_Search.ActivateInputField();
        m_Search.Select();
        AudioController.Play(SfxId.Click);
    }

    public void Hide()
    {
        if (!IsOpen) return;
        m_Window.Hide();
        ClearRows();
        m_Flow.WindowClosed(m_Closer);
    }

    // ---- data ----------------------------------------------------------------------------------------

    // Flattens the database: every placeable (service, utility, decoration) that is available now or still locked.
    // Obsolete or replaced ones (a well once water is piped, an old police station) are left out, as in the toolbar.
    private void Collect(GameManager game)
    {
        m_Entries.Clear();
        m_Defs.Clear();
        m_LockReasons.Clear();
        SimulationSystem sim = game.Simulation;
        foreach (BuildingDefinition def in game.Buildings.Entries)
        {
            if (def == null) continue;
            if (def.Category != BuildingCategory.Service && def.Category != BuildingCategory.Utility && def.Category != BuildingCategory.Decoration) continue;
            bool unlocked = game.IsUnlocked(def);
            if (unlocked && !game.CanBuild(def)) continue;

            int age = -1;
            string ageName = "";
            string reason = "";
            if (!string.IsNullOrEmpty(def.RequiredTech) && sim != null && sim.Tech != null)
            {
                TechDefinition tech = sim.Tech.Techs.GetById(def.RequiredTech);
                if (tech != null)
                {
                    age = tech.Age;
                    if (sim.Tech.Ages != null && sim.Tech.Ages.IsValidIndex(age)) ageName = sim.Tech.Ages[age].DisplayName;
                    if (!unlocked) reason = $"Needs {tech.DisplayName}";
                }
            }
            m_Entries.Add(new BuildMenuEntry
            {
                Id = def.Id,
                Name = def.DisplayName,
                Group = game.ToolbarGroupFor(def).ToString(),
                AgeName = ageName,
                Age = age,
                Effects = SearchWords(def),
                Custom = def.IsCustom,
                Locked = !unlocked,
            });
            m_Defs.Add(def);
            m_LockReasons.Add(reason);
        }
    }

    // Words a player might type for what a building does (the display text below is separate).
    private static string SearchWords(BuildingDefinition def)
    {
        var sb = new StringBuilder();
        if (def.HousingCapacity > 0) sb.Append("housing homes residents ");
        if (def.JobsProvided > 0) sb.Append("jobs work ");
        if (def.PowerSupply > 0) sb.Append("power electricity ");
        if (def.WaterSupply > 0 || def.WaterRadius > 0) sb.Append("water ");
        if (def.HappinessEffect > 0f) sb.Append("happiness park ");
        if (def.ResearchPerDay > 0f) sb.Append("research science ");
        switch (def.CivicKind)
        {
            case ServiceKind.Order: sb.Append("police order crime "); break;
            case ServiceKind.Fire: sb.Append("fire "); break;
            case ServiceKind.Health: sb.Append("health hospital clinic "); break;
            case ServiceKind.Education: sb.Append("school education "); break;
        }
        return sb.ToString();
    }

    private static string Describe(BuildingDefinition def)
    {
        var parts = new List<string>();
        if (def.HousingCapacity > 0) parts.Add($"housing {def.HousingCapacity}");
        if (def.JobsProvided > 0) parts.Add($"jobs {def.JobsProvided}");
        if (def.PowerSupply > 0) parts.Add($"power +{def.PowerSupply}");
        if (def.WaterSupply > 0) parts.Add($"water +{def.WaterSupply}");
        else if (def.WaterRadius > 0) parts.Add($"waters {def.WaterRadius} cells");
        switch (def.CivicKind)
        {
            case ServiceKind.Order: parts.Add("police"); break;
            case ServiceKind.Fire: parts.Add("fire cover"); break;
            case ServiceKind.Health: parts.Add("health care"); break;
            case ServiceKind.Education: parts.Add("education"); break;
        }
        if (def.ResearchPerDay > 0f) parts.Add($"research {def.ResearchPerDay:0.#}/day");
        if (def.HappinessEffect > 0f) parts.Add("happiness");
        parts.Add($"{def.Size.x}x{def.Size.y}");
        if (def.UpkeepPerDay > 0f) parts.Add($"upkeep ${def.UpkeepPerDay:0.#}/day");
        return string.Join("  ·  ", parts);
    }

    // ---- the list ------------------------------------------------------------------------------------

    private void Refresh(bool resetSelection)
    {
        string group = m_Group == AllGroups ? null : m_Group;
        m_Shown = BuildMenuFilter.Apply(m_Entries, m_Search.text, group, m_Age, m_CustomOnly, m_ShowLocked);
        if (resetSelection) m_Selected = FirstPickable();
        m_Selected = Mathf.Clamp(m_Selected, 0, Mathf.Max(0, m_Shown.Count - 1));

        ClearRows();
        foreach (int index in m_Shown) AddRow(index);
        m_Empty.gameObject.SetActive(m_Shown.Count == 0);
        m_Scroll.verticalNormalizedPosition = 1f;
        UpdateFilterLabels();
        HighlightSelection();
        EnsureVisible();
    }

    private int FirstPickable()
    {
        for (int i = 0; i < m_Shown.Count; i++) if (!m_Entries[m_Shown[i]].Locked) return i;
        return 0;
    }

    private void UpdateFilterLabels()
    {
        foreach (KeyValuePair<string, Button> pair in m_GroupButtons)
        {
            pair.Value.GetComponent<Image>().color = pair.Key == m_Group ? UiKit.AccentColor : UiKit.ButtonColor;
        }
        string ageLabel = "Any age";
        GameManager game = UnityEngine.Object.FindAnyObjectByType<GameManager>();
        if (m_Age >= 0 && game != null && game.Ages != null && game.Ages.IsValidIndex(m_Age)) ageLabel = game.Ages[m_Age].DisplayName;
        UiKit.SetLabel(m_AgeButton, ageLabel);
        m_AgeButton.GetComponent<Image>().color = m_Age >= 0 ? UiKit.AccentColor : UiKit.ButtonColor;
        m_CustomButton.GetComponent<Image>().color = m_CustomOnly ? UiKit.AccentColor : UiKit.ButtonColor;
        UiKit.SetLabel(m_CustomButton, m_CustomOnly ? "Custom only: on" : "Custom only");
        UiKit.SetLabel(m_LockedButton, m_ShowLocked ? "Locked: shown" : "Locked: hidden");
        int unlocked = 0;
        foreach (int index in m_Shown) if (!m_Entries[index].Locked) unlocked++;
        m_Status.text = $"{unlocked} available" + (m_Shown.Count > unlocked ? $", {m_Shown.Count - unlocked} locked" : "")
            + "   ·   type to search   ·   Up / Down and Enter pick   ·   Esc closes";
    }

    private void CycleAge()
    {
        GameManager game = UnityEngine.Object.FindAnyObjectByType<GameManager>();
        int count = game != null && game.Ages != null ? game.Ages.Count : 0;
        m_Age = m_Age + 1 >= count ? -1 : m_Age + 1;
        Refresh(true);
    }

    private void ClearRows()
    {
        foreach (Transform child in m_Content)
        {
            child.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(child.gameObject);
        }
        m_RowImages.Clear();
    }

    private void AddRow(int entryIndex)
    {
        BuildMenuEntry entry = m_Entries[entryIndex];
        BuildingDefinition def = m_Defs[entryIndex];
        int row = m_RowImages.Count;

        var go = new GameObject("Row_" + entry.Id, typeof(RectTransform), typeof(Image), typeof(Button),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        go.transform.SetParent(m_Content, false);
        var image = go.GetComponent<Image>();
        image.color = entry.Locked ? RowLockedColor : RowColor;
        go.GetComponent<LayoutElement>().minHeight = RowHeight;
        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 12, 4, 4);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        layout.childAlignment = TextAnchor.MiddleLeft;

        var texts = new GameObject("Texts", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        texts.transform.SetParent(go.transform, false);
        texts.GetComponent<LayoutElement>().flexibleWidth = 1f;
        var vertical = texts.GetComponent<VerticalLayoutGroup>();
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;
        vertical.childAlignment = TextAnchor.MiddleLeft;

        string tags = $"<color=#9AA3B2>{entry.Group}{(entry.AgeName.Length > 0 ? " · " + entry.AgeName : "")}</color>";
        if (entry.Custom) tags += $"  <color=#{ColorUtility.ToHtmlStringRGB(CustomColor)}><b>Custom</b></color>";
        Color nameColor = entry.Locked ? UiKit.MutedColor : Color.white;
        UiKit.Text(texts.transform, $"{entry.Name}   {tags}", 17, nameColor);
        string detail = entry.Locked ? $"<color=#F2C14E>{m_LockReasons[entryIndex]}</color>" : Describe(def);
        UiKit.Text(texts.transform, detail, 13, UiKit.MutedColor);

        TMP_Text cost = UiKit.Text(go.transform, $"${def.Cost:N0}", 17, entry.Locked ? UiKit.MutedColor : UiKit.TitleColor);
        cost.alignment = TextAlignmentOptions.MidlineRight;
        cost.GetComponent<LayoutElement>().minWidth = 80f;

        var button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        int captured = row;
        button.onClick.AddListener(() =>
        {
            m_Selected = captured;
            PickSelected();
        });
        m_RowImages.Add(image);
    }

    private void HighlightSelection()
    {
        for (int i = 0; i < m_RowImages.Count; i++)
        {
            bool locked = m_Entries[m_Shown[i]].Locked;
            m_RowImages[i].color = i == m_Selected ? RowSelectedColor : locked ? RowLockedColor : RowColor;
        }
    }

    // Rows are a fixed height, so the scroll offset follows from the index.
    private void EnsureVisible()
    {
        if (m_Shown.Count == 0) return;
        float top = ListPadding + m_Selected * (RowHeight + RowSpacing);
        float bottom = top + RowHeight;
        float offset = m_Content.anchoredPosition.y;
        if (top < offset) offset = top - ListPadding;
        else if (bottom > offset + ListHeight) offset = bottom - ListHeight + ListPadding;
        m_Content.anchoredPosition = new Vector2(m_Content.anchoredPosition.x, Mathf.Max(0f, offset));
    }

    private void Move(int delta)
    {
        if (m_Shown.Count == 0) return;
        m_Selected = Mathf.Clamp(m_Selected + delta, 0, m_Shown.Count - 1);
        HighlightSelection();
        EnsureVisible();
    }

    // Starts placing the selected building and closes the menu. A locked one only says what it needs.
    public bool PickSelected()
    {
        if (m_Selected < 0 || m_Selected >= m_Shown.Count) return false;
        int entryIndex = m_Shown[m_Selected];
        if (m_Entries[entryIndex].Locked)
        {
            m_Status.text = $"<color=#F2C14E>{m_Entries[entryIndex].Name} is locked. {m_LockReasons[entryIndex]}.</color>";
            return false;
        }
        BuildingDefinition def = m_Defs[entryIndex];
        Hide();
        var placement = UnityEngine.Object.FindAnyObjectByType<PlacementController>();
        if (placement == null) return false;
        placement.SelectBuilding(def);
        return true;
    }

    // Arrow keys while open (the search field keeps the focus, and a single-line field ignores up / down).
    private sealed class Driver : MonoBehaviour
    {
        public BuildMenu Owner;

        private void Update()
        {
            if (Owner == null || !Owner.IsOpen) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.downArrowKey.wasPressedThisFrame) Owner.Move(1);
            else if (keyboard.upArrowKey.wasPressedThisFrame) Owner.Move(-1);
            else if (keyboard.pageDownKey.wasPressedThisFrame) Owner.Move(6);
            else if (keyboard.pageUpKey.wasPressedThisFrame) Owner.Move(-6);
        }
    }
}
