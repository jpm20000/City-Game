using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Research panel (M11), opened by the HUD's Research button; shares the slot under the HUD with the
// tax panel. Laid out from the data: a column per age shown (unresearched earlier-age techs, the
// current age, the next age), rows by prerequisite depth within the age. Click = research now (or
// queue it when a prerequisite is still only planned), shift-click = add to the queue, click a
// planned tech = remove it. Below: the advancement checklist and its Advance project.
public sealed class TechPanel : MonoBehaviour
{
    [SerializeField] private GameManager m_GameManager;
    [SerializeField] private GameObject m_Root;
    [Tooltip("HUD research button (scene reference); hidden when the game has no age data.")]
    [SerializeField] private Button m_ToggleButton;
    [SerializeField] private Button m_CloseButton;
    [SerializeField] private TMP_Text m_Title;
    [SerializeField] private TMP_Text m_Status;
    [SerializeField] private UIMeter m_Progress;
    [SerializeField] private Transform m_Columns;
    [Tooltip("Inactive template: a vertical group whose first child is the heading text.")]
    [SerializeField] private GameObject m_ColumnTemplate;
    [Tooltip("Inactive template for one tech.")]
    [SerializeField] private ToolButton m_TechTemplate;
    [SerializeField] private TMP_Text m_Checklist;
    [SerializeField] private Button m_AdvanceButton;
    [SerializeField] private TMP_Text m_AdvanceLabel;
    [SerializeField] private TMP_Text m_Detail;

    [Header("Tech states")]
    [SerializeField] private Color m_DoneColor = new Color(0.18f, 0.36f, 0.24f);
    [SerializeField] private Color m_ActiveColor = new Color(0.30f, 0.55f, 0.92f);
    [SerializeField] private Color m_QueuedColor = new Color(0.27f, 0.38f, 0.56f);
    [SerializeField] private Color m_AvailableColor = new Color(0.24f, 0.27f, 0.33f);
    [SerializeField] private Color m_LockedColor = new Color(0.16f, 0.17f, 0.20f);

    private const string k_Good = "#73D973";
    private const string k_Bad = "#F2665A";
    private const string k_Muted = "#9AA3B2";

    private readonly Dictionary<TechDefinition, ToolButton> m_Buttons = new();
    private readonly List<GameObject> m_ColumnObjects = new();
    private readonly StringBuilder m_Text = new();
    private int m_LayoutAge = -1;
    private int m_LayoutEarlier = -1;
    private TechDefinition m_Hovered;
    private bool m_Dirty;

    private TechSystem Tech => m_GameManager != null && m_GameManager.Simulation != null ? m_GameManager.Simulation.Tech : null;

    private void Start()
    {
        bool hasAges = Tech != null;
        if (m_ToggleButton != null)
        {
            m_ToggleButton.gameObject.SetActive(hasAges);
            m_ToggleButton.onClick.AddListener(() => SetOpen(!m_Root.activeSelf));
        }
        if (m_CloseButton != null) m_CloseButton.onClick.AddListener(() => SetOpen(false));
        if (m_AdvanceButton != null) m_AdvanceButton.onClick.AddListener(OnAdvanceClicked);
        if (m_ColumnTemplate != null) m_ColumnTemplate.SetActive(false);
        if (m_TechTemplate != null) m_TechTemplate.gameObject.SetActive(false);

        GameEvents.ResearchChanged += MarkDirty;
        GameEvents.CityLoaded += MarkDirty;
        GameEvents.AgeChanged += OnAgeChanged;
        GameEvents.TechCompleted += OnTechCompleted;
        GameEvents.PopulationChanged += OnPopulationChanged;
        SidePanels.Opened += OnSidePanelOpened;
        EscapeRouter.Register(this, EscapeRouter.SidePanel, TryEscape);
        m_Root.SetActive(false);
    }

    private void OnDestroy()
    {
        GameEvents.ResearchChanged -= MarkDirty;
        GameEvents.CityLoaded -= MarkDirty;
        GameEvents.AgeChanged -= OnAgeChanged;
        GameEvents.TechCompleted -= OnTechCompleted;
        GameEvents.PopulationChanged -= OnPopulationChanged;
        SidePanels.Opened -= OnSidePanelOpened;
        EscapeRouter.Unregister(this);
    }

    private void MarkDirty() => m_Dirty = true;
    private void OnAgeChanged(int age) => m_Dirty = true;
    private void OnTechCompleted(string id) => m_Dirty = true;
    private void OnPopulationChanged(int population, int jobs) => m_Dirty = true;

    private void OnSidePanelOpened(object panel)
    {
        if (!ReferenceEquals(panel, this) && m_Root.activeSelf) m_Root.SetActive(false);
    }

    private bool TryEscape()
    {
        if (!m_Root.activeSelf) return false;
        SetOpen(false);
        return true;
    }

    private void SetOpen(bool open)
    {
        if (Tech == null) open = false;
        m_Root.SetActive(open);
        if (!open) return;
        SidePanels.RaiseOpened(this);
        Refresh();
    }

    private void LateUpdate()
    {
        if (!m_Dirty || !m_Root.activeSelf) return;
        m_Dirty = false;
        Refresh();
    }

    private void Refresh()
    {
        TechSystem tech = Tech;
        if (tech == null) return;

        EnsureLayout(tech);
        foreach (KeyValuePair<TechDefinition, ToolButton> pair in m_Buttons) RefreshButton(tech, pair.Key, pair.Value);
        RefreshStatus(tech);
        RefreshChecklist(tech);
        RefreshDetail(tech);
    }

    // --- Layout ---

    // Columns change only with the age or when the set of leftover earlier-age techs changes.
    private void EnsureLayout(TechSystem tech)
    {
        var earlier = new List<TechDefinition>();
        foreach (TechDefinition t in tech.Techs.Techs)
        {
            if (t != null && t.Age < tech.CurrentAge && !tech.IsResearched(t)) earlier.Add(t);
        }
        if (m_LayoutAge == tech.CurrentAge && m_LayoutEarlier == earlier.Count) return;
        m_LayoutAge = tech.CurrentAge;
        m_LayoutEarlier = earlier.Count;

        foreach (GameObject column in m_ColumnObjects) Destroy(column);
        m_ColumnObjects.Clear();
        m_Buttons.Clear();

        AgeDatabase ages = tech.Ages;
        if (earlier.Count > 0) AddColumn("Earlier ages", earlier, tech.Techs);
        AddColumn(ages[tech.CurrentAge].DisplayName, TechsOfAge(tech.Techs, tech.CurrentAge), tech.Techs);
        if (tech.CurrentAge + 1 < ages.Count)
        {
            AddColumn($"<color={k_Muted}>Next: {ages[tech.CurrentAge + 1].DisplayName}</color>",
                TechsOfAge(tech.Techs, tech.CurrentAge + 1), tech.Techs);
        }
        if (m_Title != null) m_Title.text = $"Research  ·  {ages[tech.CurrentAge].DisplayName}";
    }

    private static List<TechDefinition> TechsOfAge(TechDatabase techs, int age)
    {
        var list = new List<TechDefinition>();
        foreach (TechDefinition t in techs.Techs)
        {
            if (t != null && t.Age == age) list.Add(t);
        }
        return list;
    }

    // Rows by prerequisite depth, then database order (a stable sort keeps it).
    private void AddColumn(string heading, List<TechDefinition> techs, TechDatabase database)
    {
        if (m_ColumnTemplate == null || m_TechTemplate == null || m_Columns == null) return;

        GameObject column = Instantiate(m_ColumnTemplate, m_Columns);
        column.SetActive(true);
        m_ColumnObjects.Add(column);
        TMP_Text title = column.transform.GetChild(0).GetComponent<TMP_Text>();
        if (title != null) title.text = heading;

        var order = new List<(int depth, int index, TechDefinition tech)>();
        for (int i = 0; i < techs.Count; i++) order.Add((database.DepthInAge(techs[i]), i, techs[i]));
        order.Sort((a, b) => a.depth != b.depth ? a.depth.CompareTo(b.depth) : a.index.CompareTo(b.index));

        foreach (var (_, _, t) in order)
        {
            ToolButton button = Instantiate(m_TechTemplate, column.transform);
            button.gameObject.SetActive(true);
            button.name = $"Tech_{t.Id}";
            button.Setup(t.DisplayName, string.Empty, Color.clear, string.Empty);
            TechDefinition captured = t;
            button.Button.onClick.AddListener(() => OnTechClicked(captured));
            button.Hovered += _ => { m_Hovered = captured; RefreshDetail(Tech); };
            button.Unhovered += _ => { if (m_Hovered == captured) { m_Hovered = null; RefreshDetail(Tech); } };
            m_Buttons[t] = button;
        }
    }

    // --- States ---

    private void RefreshButton(TechSystem tech, TechDefinition t, ToolButton button)
    {
        string cost;
        Color color;
        float alpha = 1f;
        int queueIndex = QueueIndex(tech, t);

        if (tech.IsResearched(t))
        {
            color = m_DoneColor;
            cost = "Researched";
            alpha = 0.75f;
        }
        else if (tech.Active != null && tech.Active.Tech == t)
        {
            color = m_ActiveColor;
            cost = $"{Mathf.Min(tech.Progress, t.Cost):N0} / {t.Cost:N0} RP";
        }
        else if (queueIndex >= 0)
        {
            color = m_QueuedColor;
            cost = $"Queued #{queueIndex + 1}  ·  {t.Cost:N0} RP";
        }
        else if (t.Age > tech.CurrentAge)
        {
            color = m_LockedColor;
            cost = "Later age";
            alpha = 0.45f;
        }
        else if (tech.CanResearch(t) || tech.CanEnqueue(t))
        {
            color = m_AvailableColor;
            cost = $"{t.Cost:N0} RP";
        }
        else
        {
            color = m_LockedColor;
            cost = "Needs " + MissingPrerequisites(tech, t);
            alpha = 0.55f;
        }

        button.SetBackground(color);
        if (button.Label != null) button.Label.alpha = alpha;
        if (button.Cost != null)
        {
            button.Cost.text = cost;
            button.Cost.gameObject.SetActive(true);
        }
    }

    private static int QueueIndex(TechSystem tech, TechDefinition t)
    {
        IReadOnlyList<ResearchProject> queue = tech.Queue;
        for (int i = 0; i < queue.Count; i++)
        {
            if (queue[i].Tech == t) return i;
        }
        return -1;
    }

    private static string MissingPrerequisites(TechSystem tech, TechDefinition t)
    {
        var names = new List<string>();
        foreach (TechDefinition prerequisite in t.Prerequisites)
        {
            if (prerequisite != null && !tech.IsResearched(prerequisite)) names.Add(prerequisite.DisplayName);
        }
        return string.Join(", ", names);
    }

    private void RefreshStatus(TechSystem tech)
    {
        m_Text.Clear();
        float rate = m_GameManager.Simulation.ResearchIncome();
        ResearchProject active = tech.Active;
        if (active != null)
        {
            float remaining = Mathf.Max(0f, active.Cost - tech.Progress);
            string eta = rate > 0f ? $"~{Mathf.CeilToInt(remaining / rate)} days" : "no research income";
            m_Text.Append($"<b>Researching</b> {active.DisplayName} — {Mathf.Min(tech.Progress, active.Cost):N0} / {active.Cost:N0} RP  ·  {rate:0.#} RP/day  ·  {eta}");
        }
        else
        {
            float bankDays = m_GameManager.Balance.ResearchBankDays;
            m_Text.Append($"<color={k_Muted}>Nothing being researched</color> — {tech.Progress:N0} RP banked ({rate:0.#} RP/day, saved up to {bankDays:0} days' worth). Pick a tech.");
        }
        // Where the RP come from (M14), before the techs' multiplier.
        ResearchBreakdown parts = m_GameManager.Simulation.ResearchBreakdown();
        string multiplier = parts.Multiplier > 1.001f ? $" · ×{parts.Multiplier:0.##} from techs" : string.Empty;
        m_Text.Append($"\n<size=85%><color={k_Muted}>From shops {parts.Commercial:0.#} · buildings {parts.Buildings:0.#} · " +
                      $"schooled residents {parts.Education:0.#}{multiplier}</color></size>");
        if (tech.Queue.Count > 0)
        {
            var names = new List<string>();
            foreach (ResearchProject project in tech.Queue) names.Add(project.DisplayName);
            m_Text.Append($"\n<color={k_Muted}>Then:</color> {string.Join(", ", names)}");
        }
        if (m_Status != null) m_Status.text = m_Text.ToString();
        if (m_Progress != null) m_Progress.SetValue(active != null && active.Cost > 0f ? Mathf.Clamp01(tech.Progress / active.Cost) : 0f);
    }

    private void RefreshChecklist(TechSystem tech)
    {
        int population = m_GameManager.Population.Population;
        AdvanceStatus status = tech.GetAdvanceStatus(population);
        if (!status.HasNextAge)
        {
            if (m_Checklist != null) m_Checklist.text = $"<b>{tech.CurrentAgeDefinition.DisplayName}</b> — the last age. Research what's left to strengthen the city.";
            if (m_AdvanceButton != null) m_AdvanceButton.gameObject.SetActive(false);
            return;
        }

        AgeDefinition next = tech.Ages[status.NextAge];
        AgeDefinition current = tech.CurrentAgeDefinition;
        m_Text.Clear();
        m_Text.Append($"<b>Advance to the {next.DisplayName}</b>  <color={k_Muted}>(the year becomes at least {next.StartYear})</color>\n");
        m_Text.Append($"{Mark(status.TechsDone >= status.TechsNeeded)} {Mathf.Min(status.TechsDone, status.TechsNeeded)} / {status.TechsNeeded} {current.DisplayName} techs researched\n");
        foreach (TechDefinition required in next.RequiredTechs)
        {
            if (required != null) m_Text.Append($"{Mark(tech.IsResearched(required))} {required.DisplayName}\n");
        }
        m_Text.Append($"{Mark(status.PopulationMet)} Population {population:N0} / {status.PopulationNeeded:N0}\n");
        m_Text.Append($"<color={k_Muted}>Then {status.RpCost:N0} RP of research. New buildings grow in the new style; older ones are rebuilt unless kept historic.</color>");
        if (m_Checklist != null) m_Checklist.text = m_Text.ToString();

        if (m_AdvanceButton == null) return;
        m_AdvanceButton.gameObject.SetActive(true);
        ResearchProject advance = tech.NextAdvance();
        bool planned = tech.IsPlanned(advance);
        if (planned)
        {
            bool active = advance.Equals(tech.Active);
            SetAdvanceLabel(active ? $"Advancing…  {Mathf.Clamp01(tech.Progress / Mathf.Max(1f, advance.Cost)):P0}  (click to cancel)" : "Advance queued  (click to cancel)");
            m_AdvanceButton.interactable = true;
        }
        else
        {
            SetAdvanceLabel(status.Ready ? $"Start advancing — {status.RpCost:N0} RP" : "Not ready to advance");
            m_AdvanceButton.interactable = status.Ready;
        }
    }

    private void SetAdvanceLabel(string text)
    {
        if (m_AdvanceLabel != null) m_AdvanceLabel.text = text;
    }

    private static string Mark(bool done) => done ? $"<color={k_Good}>•</color>" : $"<color={k_Bad}>•</color>";

    private void RefreshDetail(TechSystem tech)
    {
        if (m_Detail == null || tech == null) return;
        TechDefinition t = m_Hovered;
        if (t == null)
        {
            m_Detail.text = $"<color={k_Muted}>Click: research now  ·  Shift-click: add to the queue (up to {m_GameManager.Balance.ResearchQueueMax})  ·  Click a planned tech to drop it.  Hover a tech for details.</color>";
            return;
        }

        m_Text.Clear();
        m_Text.Append($"<b>{t.DisplayName}</b>  <color={k_Muted}>{tech.Ages[Mathf.Clamp(t.Age, 0, tech.Ages.Count - 1)].DisplayName}  ·  {t.Cost:N0} RP</color>\n");
        if (!string.IsNullOrEmpty(t.Description)) m_Text.Append(t.Description).Append('\n');
        if (t.Prerequisites.Length > 0)
        {
            var parts = new List<string>();
            foreach (TechDefinition prerequisite in t.Prerequisites)
            {
                if (prerequisite != null) parts.Add($"{Mark(tech.IsResearched(prerequisite))} {prerequisite.DisplayName}");
            }
            m_Text.Append("Needs: ").Append(string.Join("   ", parts)).Append('\n');
        }
        string unlocks = Unlocks(t);
        if (unlocks.Length > 0) m_Text.Append($"Unlocks: <color={k_Good}>{unlocks}</color>");
        m_Detail.text = m_Text.ToString().TrimEnd();
    }

    // Buildings whose RequiredTech is this tech.
    private string Unlocks(TechDefinition t)
    {
        if (m_GameManager.Buildings == null) return string.Empty;
        var names = new List<string>();
        foreach (BuildingDefinition def in m_GameManager.Buildings.Entries)
        {
            if (def != null && def.RequiredTech == t.Id) names.Add(def.DisplayName);
        }
        return string.Join(", ", names);
    }

    // --- Actions ---

    private static bool ShiftHeld => Keyboard.current != null && Keyboard.current.shiftKey.isPressed;

    private void OnTechClicked(TechDefinition t)
    {
        TechSystem tech = Tech;
        if (tech == null || tech.IsResearched(t)) return;

        if (tech.IsPlanned(t)) tech.Remove(t);
        else if (t.Age > tech.CurrentAge) GameEvents.RaiseNotification($"{t.DisplayName} belongs to a later age — advance first.");
        else if (!ShiftHeld && tech.CanResearch(t)) tech.SetActive(t);
        else if (!tech.Enqueue(t)) GameEvents.RaiseNotification(QueueProblem(tech, t));
        AfterPlanChange();
    }

    private string QueueProblem(TechSystem tech, TechDefinition t)
    {
        return PrerequisitesPlannedOrDone(tech, t)
            ? $"The research queue is full ({m_GameManager.Balance.ResearchQueueMax} max)."
            : $"{t.DisplayName} needs {MissingPrerequisites(tech, t)} first.";
    }

    private static bool PrerequisitesPlannedOrDone(TechSystem tech, TechDefinition t)
    {
        foreach (TechDefinition prerequisite in t.Prerequisites)
        {
            if (!tech.IsResearched(prerequisite) && !tech.IsPlanned(prerequisite)) return false;
        }
        return true;
    }

    private void OnAdvanceClicked()
    {
        TechSystem tech = Tech;
        if (tech == null) return;

        ResearchProject advance = tech.NextAdvance();
        int population = m_GameManager.Population.Population;
        if (advance == null) return;
        if (tech.IsPlanned(advance)) tech.Remove(advance);
        else if (ShiftHeld ? !tech.EnqueueAdvance(population) : !tech.SetActiveAdvance(population))
        {
            GameEvents.RaiseNotification("Not ready to advance yet — see the checklist.");
        }
        AfterPlanChange();
    }

    private void AfterPlanChange()
    {
        GameEvents.RaiseResearchChanged();
        Refresh();
    }
}
