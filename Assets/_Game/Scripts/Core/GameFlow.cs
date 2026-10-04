using System;
using System.Collections.Generic;
using UnityEngine;

public enum GameFlowState { Playing, Paused }

// What the player is doing at the top level (M19b): playing, or looking at a menu / dialog, in which case the game is
// paused and gameplay input is blocked. Windows (pause menu, save browser, confirm and New City dialogs) announce
// themselves with WindowOpened / WindowClosed, so the flow is "paused while any window is open" and the speed that was
// running comes back when the last one closes. It also owns the Esc key (EscapeRouter), the unsaved-changes guard and
// the quit hook. The main menu joins in M19c. Created at runtime like the other code-built UI, so there is no scene edit.
public sealed class GameFlow : MonoBehaviour
{
    public static GameFlow Instance { get; private set; }

    private GameManager m_Game;
    private SaveGameController m_Save;
    private InputReader m_Input;
    private GameMenu m_Menu;
    private PauseMenu m_Pause;
    private SaveBrowser m_Browser;
    private ConfirmDialog m_Confirm;

    private readonly List<Action> m_Windows = new();
    private GameSpeed m_ResumeSpeed = GameSpeed.x1;
    private bool m_SkipRestore;
    private bool m_QuitApproved;

    public GameFlowState State => m_Windows.Count > 0 ? GameFlowState.Paused : GameFlowState.Playing;
    public SaveGameController Save => m_Save;
    public SaveBrowser Browser => m_Browser;
    public ConfirmDialog Confirm => m_Confirm;
    public PauseMenu Pause => m_Pause;
    public GameMenu Menu => m_Menu;

    // The speed to store in a save: while a window has the game paused, the speed that will come back.
    public static GameSpeed SpeedToSave(TimeManager clock)
    {
        if (Instance != null && Instance.State == GameFlowState.Paused) return Instance.m_ResumeSpeed;
        return clock.Speed;
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

        m_Confirm = new ConfirmDialog(canvas, this);
        m_Browser = new SaveBrowser(canvas, this);
        m_Pause = new PauseMenu(canvas, this);

        if (m_Menu != null)
        {
            m_Menu.MenuRequested += OpenPauseMenu;
            m_Menu.SaveRequested += SaveFromHud;
        }
        GameEvents.CityLoaded += OnCityLoaded;
        Application.wantsToQuit += OnWantsToQuit;
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
        if (m_Input != null) m_Input.Blocked = false;
    }

    private void Update()
    {
        if (m_Input == null || !m_Input.CancelPressed) return;
        if (!EscapeRouter.Dispatch()) OpenPauseMenu();
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

        m_ResumeSpeed = m_Game.Clock.Speed;
        m_SkipRestore = false;
        m_Game.Clock.SetSpeed(GameSpeed.Paused);
        m_Input.Blocked = true;
        GameEvents.RaiseFlowChanged(GameFlowState.Paused);
    }

    public void WindowClosed(Action closer)
    {
        if (!m_Windows.Remove(closer) || m_Windows.Count > 0) return;

        m_Input.Blocked = false;
        if (!m_SkipRestore) m_Game.Clock.SetSpeed(m_ResumeSpeed);
        m_SkipRestore = false;
        GameEvents.RaiseFlowChanged(GameFlowState.Playing);
    }

    // A load or New City replaced the city: its own speed stands, and every window goes away.
    private void OnCityLoaded()
    {
        if (m_Windows.Count == 0) return;
        m_SkipRestore = true;
        foreach (Action closer in new List<Action>(m_Windows)) closer();
    }

    // --- Menus ---

    public void OpenPauseMenu()
    {
        if (m_Pause == null || m_Pause.IsOpen) return;
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
