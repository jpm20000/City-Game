using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The tutorial's objective card (M19f): a small non-blocking panel at the left of the screen with the current objective,
// its progress and the last few ticked ones. The game keeps running and every tool stays usable. The objectives are pure
// (Simulation/Tutorial); this class measures the city about four times a second, outlines the control the objective needs
// (UiHighlight), adds the top growth blocker on the steps that wait on growth, and keeps SaveGameController.Tutorial in step.
public sealed class TutorialCard
{
    private const float CheckInterval = 0.25f;

    private readonly GameFlow m_Flow;
    private readonly GameManager m_Game;
    private readonly SaveGameController m_Save;
    private readonly Transform m_Canvas;
    private readonly TutorialProgress m_Progress = new();
    private readonly Dictionary<string, Button> m_Buttons = new();

    private GameObject m_Root;
    private RectTransform m_Body;
    private TMP_Text m_Header;
    private TMP_Text m_Title;
    private TMP_Text m_Text;
    private TMP_Text m_Count;
    private TMP_Text m_Help;
    private TMP_Text m_Done;
    private Button m_Minimise;
    private bool m_Minimised;

    private BudgetPanel m_Budget;
    private InfoOverlay m_Overlay;
    private SelectionPanel m_Selection;
    private PlacementController m_Placement;
    private bool m_BudgetOpened;
    private bool m_ViewUsed;
    private float m_NextCheck;
    private int m_ShownIndex = -2;

    public bool Active => m_Progress.Active;
    public int Index => m_Progress.Index;
    public string CurrentId => m_Progress.Current?.Id;
    public bool Visible => m_Root != null && m_Root.activeSelf;

    public TutorialCard(Transform canvas, GameFlow flow, GameManager game, SaveGameController save)
    {
        m_Canvas = canvas;
        m_Flow = flow;
        m_Game = game;
        m_Save = save;
        Build();
    }

    // ---- life cycle -------------------------------------------------------------------------------------

    // A load or New City replaced the city: the saved index says whether a tutorial is running.
    public void CityLoaded()
    {
        m_Progress.Restore(m_Save != null ? m_Save.Tutorial : TutorialProgress.None);
        ResetFlags();
        m_ShownIndex = -2;
        m_NextCheck = 0f;
        Refresh(null);
    }

    public void Update()
    {
        bool show = m_Progress.Active && m_Flow.State != GameFlowState.MainMenu;
        if (m_Root.activeSelf != show) m_Root.SetActive(show);
        if (!show)
        {
            UiHighlight.Clear();
            return;
        }
        if (Time.unscaledTime < m_NextCheck) return;
        m_NextCheck = Time.unscaledTime + CheckInterval;
        Check();
    }

    public void Skip()
    {
        m_Progress.Skip();
        m_Save.TutorialChanged(m_Progress.Index);
        UiHighlight.Clear();
        m_Root.SetActive(false);
    }

    // ---- checking ---------------------------------------------------------------------------------------

    private void Check()
    {
        FindParts();
        if (m_Budget != null && m_Budget.IsOpen) m_BudgetOpened = true;
        if (m_Overlay != null && m_Overlay.Shown != InfoOverlay.View.Off) m_ViewUsed = true;

        var placed = new List<string>();
        if (m_Placement != null)
        {
            foreach (BuildingInstance building in m_Placement.PlacedBuildings)
            {
                if (building != null && building.Definition != null) placed.Add(building.Definition.Id);
            }
        }
        TutorialSnapshot snapshot = TutorialSnapshot.Measure(m_Game.Grid, m_Game.Roads, m_Game.Simulation, placed, m_BudgetOpened, m_ViewUsed);

        int before = m_Progress.Index;
        int advanced = m_Progress.Evaluate(snapshot);
        if (advanced > 0)
        {
            ResetFlags();
            m_Save.TutorialChanged(m_Progress.Index);
            for (int i = before; i < before + advanced; i++)
            {
                GameEvents.RaiseNotification($"<color=#F2CF59>Tutorial:</color> {TutorialContent.Objectives[i].Title} — done");
            }
            AudioController.Play(SfxId.Click);
        }

        if (m_Progress.Finished)
        {
            Finish();
            return;
        }
        Refresh(snapshot);
    }

    private void FindParts()
    {
        if (m_Budget == null) m_Budget = Object.FindAnyObjectByType<BudgetPanel>(FindObjectsInactive.Include);
        if (m_Overlay == null) m_Overlay = Object.FindAnyObjectByType<InfoOverlay>(FindObjectsInactive.Include);
        if (m_Selection == null) m_Selection = Object.FindAnyObjectByType<SelectionPanel>(FindObjectsInactive.Include);
        if (m_Placement == null) m_Placement = Object.FindAnyObjectByType<PlacementController>();
    }

    private void ResetFlags()
    {
        m_BudgetOpened = false;
        m_ViewUsed = false;
    }

    private void Finish()
    {
        UiHighlight.Clear();
        m_Root.SetActive(false);
        const string message = "You have a village, a research programme and a new age. Fires, plague and random events " +
            "come with disasters on; the Renaissance and the ages after it bring new buildings, needs and techs. " +
            "Keep this city going, or start a new one from the menu.";
        m_Flow.Confirm.Ask("Tutorial complete", message,
            new ConfirmDialog.Choice("Turn disasters on", () =>
            {
                m_Game.Simulation.Disasters.Enabled = true;
                GameEvents.RaiseNotification("Disasters and events are on.");
            }),
            new ConfirmDialog.Choice("Keep them off", null, primary: true));
    }

    // ---- display ----------------------------------------------------------------------------------------

    private void Refresh(TutorialSnapshot snapshot)
    {
        if (!m_Progress.Active)
        {
            UiHighlight.Clear();
            m_ShownIndex = -2;
            return;
        }

        TutorialObjective current = m_Progress.Current;
        m_Header.text = $"Tutorial  {m_Progress.Index + 1} / {TutorialContent.Count}";
        m_Title.text = current.Title;
        m_Body.gameObject.SetActive(!m_Minimised);
        UiKit.SetLabel(m_Minimise, m_Minimised ? "+" : "–");

        if (m_ShownIndex != m_Progress.Index)
        {
            m_ShownIndex = m_Progress.Index;
            m_Text.text = current.Body;
            var done = new StringBuilder();
            int first = Mathf.Max(0, m_Progress.Index - 3);
            if (first > 0) done.Append($"<color=#8A93A3>+ {first} earlier</color>\n");
            for (int i = first; i < m_Progress.Index; i++) done.Append($"<color=#73D973><s>{TutorialContent.Objectives[i].Title}</s></color>\n");
            m_Done.text = done.ToString().TrimEnd();
            m_Done.gameObject.SetActive(m_Progress.Index > 0);
        }

        if (snapshot != null)
        {
            string count = current.Progress(snapshot);
            m_Count.text = count;
            m_Count.gameObject.SetActive(count.Length > 0);
            string help = current.Help ? GrowthHelp() : string.Empty;
            m_Help.text = help;
            m_Help.gameObject.SetActive(help.Length > 0);
            Highlight(current, snapshot);
        }
    }

    private void Highlight(TutorialObjective objective, TutorialSnapshot s)
    {
        string key = objective.Highlight;
        // Two steps point at the Research button first and at the building once its tech is in.
        if (objective.Id == "green" && s.Has(TutorialContent.CommonsTech)) key = "Park";
        if (objective.Id == "learning" && s.Has("monasticism")) key = "Monastery";
        if (objective.Id == "age") key = "Research";
        string[] names = key switch
        {
            "Road" => new[] { "Road" },
            "Residential" => new[] { "Residential" },
            "Commercial" => new[] { s.CommercialCells < 4 ? "Commercial" : "Industrial" },
            "Well" => new[] { "Build_well", "Group_Utilities" },   // the group button until its flyout is open
            "Park" => new[] { "Build_park", "Group_Parks" },
            "Monastery" => new[] { "Build_monastery", "Group_Education" },
            "Budget" => new[] { "BudgetButton" },
            "Research" => new[] { "ResearchButton" },
            "Views" => new[] { "WaterView", "CoverageView", "PowerView", "PollutionView", "ViewsButton" },
            _ => System.Array.Empty<string>(),
        };
        // A step that is waiting on the player's panel needs no pointer once it is open.
        foreach (string name in names)
        {
            Button button = FindButton(name);
            if (button != null && button.gameObject.activeInHierarchy)
            {
                UiHighlight.Show((RectTransform)button.transform);
                return;
            }
        }
        UiHighlight.Clear();
    }

    private Button FindButton(string name)
    {
        if (m_Buttons.TryGetValue(name, out Button cached) && cached != null) return cached;
        foreach (Button button in m_Canvas.GetComponentsInChildren<Button>(true))
        {
            if (button.name == name)
            {
                m_Buttons[name] = button;
                return button;
            }
        }
        return null;
    }

    // The most common thing holding the zoned, still empty cells back, in the Selection panel's wording.
    private string GrowthHelp()
    {
        if (m_Selection == null) return string.Empty;
        GridData grid = m_Game.Grid;
        DemandSnapshot demand = m_Game.Demand.Snapshot;
        var counts = new Dictionary<GrowthBlocker, int>();
        var sample = new Dictionary<GrowthBlocker, Vector2Int>();
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                var cell = new Vector2Int(x, y);
                if (grid.GetZone(cell) == ZoneType.None || grid.GetBuildingLevel(cell) > 0 || grid.IsOccupied(cell)) continue;
                GrowthBlocker blocker = m_Game.Simulation.Growth.GetBlocker(cell, demand);
                if (blocker == GrowthBlocker.None || blocker == GrowthBlocker.NotZoned) continue;
                counts[blocker] = counts.TryGetValue(blocker, out int n) ? n + 1 : 1;
                sample[blocker] = cell;
            }
        }
        GrowthBlocker top = GrowthBlocker.None;
        int best = 0;
        foreach (KeyValuePair<GrowthBlocker, int> pair in counts)
        {
            if (pair.Value > best) { best = pair.Value; top = pair.Key; }
        }
        if (top == GrowthBlocker.None) return "<color=#9AA3B2>Growth happens a little at a time. Give it a few days.</color>";
        Vector2Int at = sample[top];
        return m_Selection.BlockerText(at, grid.GetZone(at), "Growth");
    }

    // ---- building the card ------------------------------------------------------------------------------

    private void Build()
    {
        m_Root = new GameObject("TutorialCard", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        m_Root.transform.SetParent(m_Canvas, false);
        var rect = (RectTransform)m_Root.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(16f, 40f);
        rect.sizeDelta = new Vector2(310f, 0f);
        m_Root.GetComponent<Image>().color = UiKit.PanelColor;
        var layout = m_Root.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 10, 12);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        m_Root.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform top = UiKit.Row(m_Root.transform, 24f);
        m_Header = UiKit.Text(top, "Tutorial", 14, UiKit.MutedColor, flexible: true);
        m_Minimise = UiKit.MakeButton(top, "–", () =>
        {
            m_Minimised = !m_Minimised;
            m_ShownIndex = -2;
            Refresh(null);
        }, 28f);
        m_Title = UiKit.Text(m_Root.transform, "", 20, UiKit.TitleColor);

        var body = new GameObject("Body", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        body.transform.SetParent(m_Root.transform, false);
        m_Body = (RectTransform)body.transform;
        var bodyLayout = body.GetComponent<VerticalLayoutGroup>();
        bodyLayout.spacing = 6f;
        bodyLayout.childControlWidth = true;
        bodyLayout.childControlHeight = true;
        bodyLayout.childForceExpandWidth = true;
        bodyLayout.childForceExpandHeight = false;

        m_Text = UiKit.Text(m_Body, "", 15, UiKit.BodyColor);
        m_Count = UiKit.Text(m_Body, "", 16, new Color(0.45f, 0.75f, 1f));
        m_Help = UiKit.Text(m_Body, "", 14, UiKit.BodyColor);
        m_Done = UiKit.Text(m_Body, "", 13, UiKit.MutedColor);
        Button skip = UiKit.MakeButton(m_Body, "Skip tutorial", AskSkip, 0f);
        skip.GetComponent<LayoutElement>().minHeight = 30f;
        m_Root.SetActive(false);
    }

    private void AskSkip()
    {
        m_Flow.Confirm.Ask("Skip the tutorial?", "The objectives go away and the city carries on as it is.",
            new ConfirmDialog.Choice("Skip it", Skip),
            new ConfirmDialog.Choice("Keep going", null, primary: true));
    }
}
