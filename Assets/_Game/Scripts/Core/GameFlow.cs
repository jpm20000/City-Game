using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum GameFlowState { Playing, Paused, MainMenu }

// What the player is doing at the top level. Playing; or looking at a menu / dialog (M19b), in which case the game is
// paused and gameplay input is blocked: windows (pause menu, save browser, confirm and New City dialogs) announce
// themselves with WindowOpened / WindowClosed, so the flow is "paused while any window is open" and the speed that was
// running comes back when the last one closes. Or the main menu (M19c): the HUD is hidden, input is blocked and the
// showcase city runs behind the menu with the camera drifting over it; a load, Continue or New City leaves it. It also
// owns the Esc key (EscapeRouter), the unsaved-changes guard and the quit hook. Created at runtime like the other
// code-built UI, so there is no scene edit.
public sealed class GameFlow : MonoBehaviour
{
    private const string SkipMenuEditorPref = "CityGame.SkipMenu";
    private static readonly string[] s_SkipArgs = { "-skipMenu", "-perfBenchmark", "-smokeTest" };
    // Top-level canvas children that stay visible (and usable) over the main menu.
    private static readonly HashSet<string> s_KeepVisible = new()
    {
        "PauseMenu", "SaveBrowser", "ConfirmDialog", "MainMenu", "NewCityDialog", "Notifications", "EventPopup",
    };

    public static GameFlow Instance { get; private set; }

    private GameManager m_Game;
    private SaveGameController m_Save;
    private InputReader m_Input;
    private GameMenu m_Menu;
    private Transform m_Canvas;
    private IsoCameraController m_Camera;
    private PauseMenu m_Pause;
    private MainMenu m_MainMenu;
    private SaveBrowser m_Browser;
    private ConfirmDialog m_Confirm;

    private readonly List<Action> m_Windows = new();
    private readonly List<HiddenGroup> m_Hidden = new();
    private GameSpeed m_ResumeSpeed = GameSpeed.x1;
    private bool m_SkipRestore;
    private bool m_QuitApproved;
    private bool m_InMainMenu;
    private bool m_LoadingShowcase;

    private struct HiddenGroup
    {
        public CanvasGroup Group;
        public float Alpha;
        public bool Interactable;
        public bool BlocksRaycasts;
        public bool Added;
    }

    public GameFlowState State => m_InMainMenu ? GameFlowState.MainMenu : m_Windows.Count > 0 ? GameFlowState.Paused : GameFlowState.Playing;
    public SaveGameController Save => m_Save;
    public SaveBrowser Browser => m_Browser;
    public ConfirmDialog Confirm => m_Confirm;
    public PauseMenu Pause => m_Pause;
    public MainMenu Main => m_MainMenu;
    public GameMenu Menu => m_Menu;
    public bool InMainMenu => m_InMainMenu;

    // The speed to store in a save: while a window has the game paused, the speed that will come back.
    public static GameSpeed SpeedToSave(TimeManager clock)
    {
        if (Instance != null && Instance.State == GameFlowState.Paused) return Instance.m_ResumeSpeed;
        return clock.Speed;
    }

    // The game starts straight in the city, without the main menu: command-line players (-skipMenu, the benchmark and the
    // smoke test) and, in the Editor, the CityBuilder > Skip Main Menu In Play Mode switch.
    private static bool ShouldSkipMenu()
    {
        if (Application.isBatchMode) return true;
        string[] args = Environment.GetCommandLineArgs();
        foreach (string arg in s_SkipArgs)
        {
            if (Array.IndexOf(args, arg) >= 0) return true;
        }
#if UNITY_EDITOR
        return UnityEditor.EditorPrefs.GetBool(SkipMenuEditorPref, false);
#else
        return false;
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        GameManager game = FindAnyObjectByType<GameManager>();
        SaveGameController save = FindAnyObjectByType<SaveGameController>();
        InputReader input = FindAnyObjectByType<InputReader>();
        TaxPanel taxPanel = FindAnyObjectByType<TaxPanel>(FindObjectsInactive.Include);
        if (game == null || save == null || input == null || taxPanel == null) return;

        var go = new GameObject("GameFlow");
        go.AddComponent<GameFlow>().Init(game, save, input, taxPanel.transform.root, FindAnyObjectByType<GameMenu>(FindObjectsInactive.Include));
    }

    private void Init(GameManager game, SaveGameController save, InputReader input, Transform canvas, GameMenu menu)
    {
        Instance = this;
        m_Game = game;
        m_Save = save;
        m_Input = input;
        m_Menu = menu;
        m_Canvas = canvas;
        m_Camera = FindAnyObjectByType<IsoCameraController>();

        m_Confirm = new ConfirmDialog(canvas, this);
        m_Browser = new SaveBrowser(canvas, this);
        m_Pause = new PauseMenu(canvas, this);
        m_MainMenu = new MainMenu(canvas, this);

        if (m_Menu != null)
        {
            m_Menu.MenuRequested += OpenPauseMenu;
            m_Menu.SaveRequested += SaveFromHud;
        }
        GameEvents.CityLoaded += OnCityLoaded;
        Application.wantsToQuit += OnWantsToQuit;

        if (ShouldSkipMenu()) return;
        BeginMainMenu();
        StartCoroutine(Boot());
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (m_Menu != null)
        {
            m_Menu.MenuRequested -= OpenPauseMenu;
            m_Menu.SaveRequested -= SaveFromHud;
        }
        GameEvents.CityLoaded -= OnCityLoaded;
        Application.wantsToQuit -= OnWantsToQuit;
        EscapeRouter.Unregister(m_Confirm);
        EscapeRouter.Unregister(m_Browser);
        EscapeRouter.Unregister(m_Pause);
        if (m_Input != null) m_Input.Blocked = false;
        if (m_InMainMenu) ShowHud();
    }

    private void Update()
    {
        if (m_Input == null || !m_Input.CancelPressed) return;
        if (!EscapeRouter.Dispatch() && !m_InMainMenu) OpenPauseMenu();
    }

    // --- Windows ---

    // closer closes the window (it must call WindowClosed with the same delegate): used to close everything when a
    // different city replaces this one.
    public void WindowOpened(Action closer)
    {
        if (m_Windows.Contains(closer)) return;
        bool first = m_Windows.Count == 0;
        m_Windows.Add(closer);
        if (!first) return;

        m_SkipRestore = false;
        if (!m_InMainMenu)
        {
            // The showcase behind the main menu keeps running under a dialog; a real game pauses.
            m_ResumeSpeed = m_Game.Clock.Speed;
            m_Game.Clock.SetSpeed(GameSpeed.Paused);
            GameEvents.RaiseFlowChanged(GameFlowState.Paused);
        }
        UpdateBlocked();
    }

    public void WindowClosed(Action closer)
    {
        if (!m_Windows.Remove(closer) || m_Windows.Count > 0) return;

        UpdateBlocked();
        if (m_InMainMenu) return;
        if (!m_SkipRestore) m_Game.Clock.SetSpeed(m_ResumeSpeed);
        m_SkipRestore = false;
        GameEvents.RaiseFlowChanged(GameFlowState.Playing);
    }

    private void UpdateBlocked()
    {
        if (m_Input != null) m_Input.Blocked = m_InMainMenu || m_Windows.Count > 0;
    }

    private void CloseAllWindows()
    {
        m_SkipRestore = true;
        foreach (Action closer in new List<Action>(m_Windows)) closer();
    }

    // A load or New City replaced the city: its own speed stands, every window goes away, and the main menu is left.
    private void OnCityLoaded()
    {
        if (m_LoadingShowcase) return;
        if (m_Windows.Count > 0) CloseAllWindows();
        if (m_InMainMenu) LeaveMainMenu();
    }

    // --- Main menu ---

    private IEnumerator Boot()
    {
        // The scene's own Start methods run first; the showcase replaces the startup city after them.
        yield return null;
        yield return null;
        if (!m_InMainMenu) yield break;
        LoadShowcaseCity();
        m_MainMenu.Show();
    }

    private void BeginMainMenu()
    {
        m_InMainMenu = true;
        HideHud();
        UpdateBlocked();
        if (m_Camera != null) m_Camera.SetShowcase(true);
        GameEvents.RaiseFlowChanged(GameFlowState.MainMenu);
    }

    private void LoadShowcaseCity()
    {
        m_LoadingShowcase = true;
        try { m_Save.LoadShowcase(); }
        finally { m_LoadingShowcase = false; }
        m_Game.Clock.SetSpeed(GameSpeed.x1);
    }

    // From the pause menu's Main menu entry (after the unsaved-changes guard): back to the title.
    public void EnterMainMenu()
    {
        if (m_InMainMenu) return;
        CloseAllWindows();
        m_Pause.Hide();
        BeginMainMenu();
        LoadShowcaseCity();
        m_MainMenu.Show();
    }

    private void LeaveMainMenu()
    {
        m_InMainMenu = false;
        m_MainMenu.Hide();
        ShowHud();
        if (m_Camera != null) m_Camera.SetShowcase(false);
        m_SkipRestore = false;
        UpdateBlocked();
        GameEvents.RaiseFlowChanged(GameFlowState.Playing);
    }

    // Leaves the main menu without loading anything (Play-mode checks and tools that start from the current city).
    public void StartPlaying()
    {
        if (m_InMainMenu) LeaveMainMenu();
    }

    private void HideHud()
    {
        m_Hidden.Clear();
        foreach (Transform child in m_Canvas)
        {
            if (s_KeepVisible.Contains(child.name)) continue;
            CanvasGroup group = child.GetComponent<CanvasGroup>();
            bool added = group == null;
            if (added) group = child.gameObject.AddComponent<CanvasGroup>();
            m_Hidden.Add(new HiddenGroup { Group = group, Alpha = group.alpha, Interactable = group.interactable, BlocksRaycasts = group.blocksRaycasts, Added = added });
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }
    }

    private void ShowHud()
    {
        foreach (HiddenGroup hidden in m_Hidden)
        {
            if (hidden.Group == null) continue;
            if (hidden.Added)
            {
                Destroy(hidden.Group);
                continue;
            }
            hidden.Group.alpha = hidden.Alpha;
            hidden.Group.interactable = hidden.Interactable;
            hidden.Group.blocksRaycasts = hidden.BlocksRaycasts;
        }
        m_Hidden.Clear();
    }

    // --- Menus ---

    public void OpenPauseMenu()
    {
        if (m_InMainMenu || m_Pause == null || m_Pause.IsOpen) return;
        EventPopup popup = FindAnyObjectByType<EventPopup>();
        if (popup != null && popup.IsOpen) return;
        m_Pause.Show();
    }

    // The HUD Save button: the city's own save, or the Save as window when it has none yet.
    private void SaveFromHud()
    {
        if (string.IsNullOrEmpty(m_Save.CurrentName)) m_Browser.Open(SaveBrowser.Mode.Save);
        else m_Save.Save();
    }

    // Runs proceed now, or after the player decided what to do about unsaved changes.
    public void GuardDiscard(Action proceed)
    {
        if (m_Save == null || !m_Save.Dirty)
        {
            proceed();
            return;
        }

        string city = string.IsNullOrWhiteSpace(m_Save.CityName) ? "This city" : m_Save.CityName;
        m_Confirm.Ask("Unsaved changes", $"{city} has changes since it was last saved.",
            new ConfirmDialog.Choice("Save", () => { if (m_Save.Save()) proceed(); }, primary: true),
            new ConfirmDialog.Choice("Don't save", proceed),
            new ConfirmDialog.Choice("Cancel", null));
    }

    // --- Quit ---

    public void Quit() => GuardDiscard(DoQuit);

    private void DoQuit()
    {
        m_QuitApproved = true;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // The window's X, Alt+F4 and Application.Quit all pass through here: ask first when there is unsaved progress. (In
    // the Editor this also fires when leaving Play mode, which must never be blocked.)
    private bool OnWantsToQuit()
    {
        if (Application.isEditor || m_QuitApproved || m_Save == null || !m_Save.Dirty) return true;
        GuardDiscard(DoQuit);
        return false;
    }
}
