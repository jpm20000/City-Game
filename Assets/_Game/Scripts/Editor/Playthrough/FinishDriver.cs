using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// M19h play-through of the whole front end, UI-only (virtual keyboard and mouse), from the title screen: Settings (mute,
// UI scale, autosave, a key rebind) -> Tutorial -> first objective by clicks -> the rebound key in play -> pause menu ->
// Save as (file, info and thumbnail on disk) -> Main menu -> Load from the title -> the same tutorial objective, the
// road and the rebound key are back. Start it from a one-line RunCommand in Play mode:
//   FindAnyObjectByType<GameManager>().StartCoroutine(FinishDriver.Run(logPath, savesDir));
public static class FinishDriver
{
    private const string SaveName = "Finish test";
    private static Mouse s_Mouse;
    private static Keyboard s_Keyboard;
    private static string s_Log;
    private static int s_Fails;
    private static GameFlow s_Flow;

    private static void Log(string line) => File.AppendAllText(s_Log, $"[{Time.frameCount}] {line}\n");
    private static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    private static void Check(bool ok, string what)
    {
        if (!ok) s_Fails++;
        Log((ok ? "PASS " : "FAIL ") + what);
    }

    private static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(s_Keyboard, new KeyboardState(keys));
    private static IEnumerator Press(Key key) { Keys(key); yield return Frames(2); Keys(); yield return Frames(4); }

    private static void MouseTo(Vector2 pos, bool down, float scroll = 0f)
    {
        var state = new MouseState { position = pos, scroll = new Vector2(0f, scroll) };
        if (down) state = state.WithButton(MouseButton.Left);
        InputSystem.QueueStateEvent(s_Mouse, state);
    }

    private static IEnumerator ClickAt(Vector2 pos)
    {
        MouseTo(pos, false); yield return Frames(2);
        MouseTo(pos, true); yield return Frames(2);
        MouseTo(pos, false); yield return Frames(2);
    }

    private static IEnumerator ClickUi(GameObject go)
    {
        if (go == null || !go.activeInHierarchy) { Check(false, "missing ui object"); yield break; }
        var rt = (RectTransform)go.transform;
        yield return ClickAt(RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center)));
    }

    // A shown button by name under one window root (names such as "SaveButton" repeat between windows).
    private static GameObject Btn(string root, string name)
    {
        GameObject window = GameObject.Find("UI/" + root);
        if (window == null) return null;
        foreach (Button b in window.GetComponentsInChildren<Button>())
        {
            if (b.name == name) return b.gameObject;
        }
        return null;
    }

    // The Settings row with this label: its n-th button (stepper: 0 = <, 1 = >; switch: 0).
    private static GameObject RowButton(string label, int n)
    {
        foreach (TMP_Text text in GameObject.Find("UI/SettingsPanel/Panel").GetComponentsInChildren<TMP_Text>())
        {
            if (text.text != label || text.GetComponent<LayoutElement>() == null) continue;
            Button[] buttons = text.transform.parent.GetComponentsInChildren<Button>();
            return n < buttons.Length ? buttons[n].gameObject : null;
        }
        return null;
    }

    private static IEnumerator Tab(string tab)
    {
        yield return ClickUi(Btn("SettingsPanel", tab + "Button"));
        yield return Frames(3);
    }

    private static Vector2 CellPos(Vector2Int c)
    {
        Vector3 p = Camera.main.WorldToScreenPoint(new Vector3(c.x + 0.5f, 0f, c.y + 0.5f));
        return new Vector2(p.x, p.y);
    }

    private static IEnumerator Drag(List<Vector2Int> cells)
    {
        MouseTo(CellPos(cells[0]), false); yield return Frames(2);
        MouseTo(CellPos(cells[0]), true); yield return Frames(2);
        foreach (Vector2Int c in cells) { MouseTo(CellPos(c), true); yield return Frames(2); }
        MouseTo(CellPos(cells[cells.Count - 1]), false); yield return Frames(2);
    }

    private static string Current() => s_Flow.Tutorial.Active ? s_Flow.Tutorial.CurrentId : "(none)";

    private static IEnumerator WaitFor(System.Func<bool> done, float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!done() && Time.realtimeSinceStartup < end) yield return null;
    }

    private static float ScalerWidth()
    {
        foreach (CanvasScaler scaler in Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include))
        {
            if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) return scaler.referenceResolution.x;
        }
        return -1f;
    }

    private static void RemoveVirtualDevices()
    {
        foreach (InputDevice device in InputSystem.devices.ToArray())
        {
            if (device.name.StartsWith("Virtual")) InputSystem.RemoveDevice(device);
        }
    }

    public static IEnumerator Run(string logPath, string savesDir)
    {
        s_Log = logPath;
        s_Fails = 0;
        File.WriteAllText(s_Log, string.Empty);
        RemoveVirtualDevices();
        Mouse realMouse = Mouse.current;
        Keyboard realKeyboard = Keyboard.current;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        if (realMouse != null) InputSystem.DisableDevice(realMouse);
        if (realKeyboard != null) InputSystem.DisableDevice(realKeyboard);
        s_Mouse = InputSystem.AddDevice<Mouse>("VirtualMouse");
        s_Keyboard = InputSystem.AddDevice<Keyboard>("VirtualKeyboard");
        s_Mouse.MakeCurrent();
        s_Keyboard.MakeCurrent();
        bool wasMute = GameSettings.Mute;
        float wasScale = GameSettings.UiScale;
        int wasAutosave = GameSettings.AutosaveMonths;
        try
        {
            s_Flow = GameFlow.Instance;
            SaveSlots.Root = savesDir;
            KeyBindings.ResetAll();
            var game = Object.FindAnyObjectByType<GameManager>();
            if (s_Flow.State != GameFlowState.MainMenu)
            {
                s_Flow.EnterMainMenu();
                yield return Frames(10);
                if (s_Flow.Confirm.IsOpen) { yield return ClickUi(Btn("ConfirmDialog", "Don't saveButton")); yield return Frames(10); }
            }
            Log($"screen {Screen.width}x{Screen.height}, flow {s_Flow.State}");
            yield return Frames(5);
            Check(s_Flow.State == GameFlowState.MainMenu, "starts on the title");

            // Settings from the title: sound, interface scale, autosave, a key.
            yield return ClickUi(Btn("MainMenu", "SettingsButton"));
            yield return Frames(5);
            Check(s_Flow.Settings.IsOpen, "Settings opens from the title");
            yield return Tab("Audio");
            bool muteBefore = GameSettings.Mute;
            yield return ClickUi(RowButton("Mute all sound", 0));
            Check(GameSettings.Mute != muteBefore, "the Mute switch changes the setting: " + GameSettings.Mute);
            yield return ClickUi(RowButton("Mute all sound", 0));
            Check(GameSettings.Mute == muteBefore, "and switches back");

            yield return Tab("Interface");
            float width = ScalerWidth();
            yield return ClickUi(RowButton("UI scale", 1));
            yield return Frames(5);
            float widthAfter = ScalerWidth();
            Check(GameSettings.UiScale > wasScale + 0.01f && widthAfter < width - 1f, $"a bigger UI scale shrinks the reference width: {width} -> {widthAfter}, scale {GameSettings.UiScale}");

            yield return Tab("Gameplay");
            int autosaveBefore = GameSettings.AutosaveMonths;
            yield return ClickUi(RowButton("Autosave", 1));
            Check(GameSettings.AutosaveMonths != autosaveBefore, $"Autosave stepper: {autosaveBefore} -> {GameSettings.AutosaveMonths}");

            yield return Tab("Controls");
            yield return ClickUi(RowButton("Road tool", 0));
            yield return Frames(2);
            Keys(Key.N);
            yield return new WaitForSecondsRealtime(0.3f);
            Keys();
            yield return Frames(4);
            Check(KeyBindings.Label("RoadTool") == "N", "Road tool rebound to N: " + KeyBindings.Label("RoadTool"));
            yield return Press(Key.Escape);
            Check(!s_Flow.Settings.IsOpen && s_Flow.State == GameFlowState.MainMenu, "Esc closes Settings and leaves the title");

            // Tutorial from the title.
            yield return ClickUi(Btn("MainMenu", "TutorialButton"));
            yield return Frames(10);
            Check(s_Flow.State == GameFlowState.Playing && s_Flow.Tutorial.Active, "Tutorial starts a game with the card: " + s_Flow.State);
            Check(Current() == "road", "first objective: " + Current());
            yield return Frames(20);
            Check(s_Flow.Tutorial.Visible, "the card is shown");

            for (int i = 0; i < 25; i++) { MouseTo(new Vector2(Screen.width * 0.4f, Screen.height * 0.5f), false, -120f); yield return Frames(3); }
            MouseTo(new Vector2(Screen.width * 0.4f, Screen.height * 0.5f), false, 0f);
            yield return Frames(5);

            // The rebound key is the road tool in play; B is no longer.
            var placement = Object.FindAnyObjectByType<PlacementController>();
            yield return Press(Key.B);
            Check(placement.CurrentMode != PlacementController.Mode.Road, "B no longer selects the road tool");
            yield return Press(Key.N);
            Check(placement.CurrentMode == PlacementController.Mode.Road, "N selects the road tool: " + placement.CurrentMode);
            var road = new List<Vector2Int>();
            for (int x = 0; x <= 10; x++) road.Add(new Vector2Int(x, 24));
            yield return Drag(road);
            yield return Frames(20);
            yield return WaitFor(() => Current() != "road", 3f);
            Check(Current() == "homes", "road laid by the rebound key -> " + Current());
            int roads = game.Grid.CountRoads();
            Check(roads >= 8, "road cells: " + roads);

            // Pause menu -> Save as.
            for (int i = 0; i < 3 && s_Flow.State != GameFlowState.Paused; i++) yield return Press(Key.Escape);
            Check(s_Flow.State == GameFlowState.Paused && Btn("PauseMenu", "ResumeButton") != null && Btn("PauseMenu", "ResumeButton").activeInHierarchy, "Esc opens the pause menu: " + s_Flow.State);
            Check(game.Clock.Speed == GameSpeed.Paused, "the game clock is paused: " + game.Clock.Speed);
            yield return ClickUi(Btn("PauseMenu", "Save as…Button"));
            yield return Frames(5);
            Check(s_Flow.Browser.IsOpen && s_Flow.Browser.CurrentMode == SaveBrowser.Mode.Save, "Save as opens the browser in save mode");
            GameObject window = GameObject.Find("UI/SaveBrowser");
            TMP_InputField field = window.GetComponentInChildren<TMP_InputField>();
            field.text = SaveName;
            yield return Frames(3);
            yield return ClickUi(Btn("SaveBrowser", "SaveButton"));
            yield return Frames(15);
            string json = Path.Combine(savesDir, SaveName + ".json");
            string info = Path.Combine(savesDir, SaveName + ".info.json");
            string png = Path.Combine(savesDir, SaveName + ".png");
            yield return WaitFor(() => File.Exists(png), 3f);
            Check(File.Exists(json) && File.Exists(info), "the save and its summary are on disk");
            Check(File.Exists(png) && new FileInfo(png).Length > 1000, "the thumbnail is on disk: " + (File.Exists(png) ? new FileInfo(png).Length : 0) + " bytes");
            Check(!s_Flow.Save.Dirty, "saved means clean");
            if (s_Flow.Browser.IsOpen) { yield return ClickUi(Btn("SaveBrowser", "CloseButton")); yield return Frames(5); }

            // Back to the title, with the pause menu.
            if (Btn("PauseMenu", "Main menuButton") == null || !Btn("PauseMenu", "Main menuButton").activeInHierarchy)
            {
                yield return Press(Key.Escape);
            }
            yield return ClickUi(Btn("PauseMenu", "Main menuButton"));
            yield return Frames(10);
            if (s_Flow.Confirm.IsOpen) { Log("note: Main menu asked to confirm"); yield return ClickUi(Btn("ConfirmDialog", "Don't saveButton")); yield return Frames(10); }
            Check(s_Flow.State == GameFlowState.MainMenu, "Main menu shows the title: " + s_Flow.State);

            // Load from the title.
            yield return ClickUi(Btn("MainMenu", "Load…Button"));
            yield return Frames(5);
            Check(s_Flow.Browser.IsOpen && s_Flow.Browser.CurrentMode == SaveBrowser.Mode.Load && s_Flow.Browser.RowCount >= 1, "Load lists the save: " + s_Flow.Browser.RowCount);
            yield return ClickUi(Btn("SaveBrowser", "Row_" + SaveName));
            yield return Frames(3);
            Check(s_Flow.Browser.SelectedName == SaveName, "row selected: " + s_Flow.Browser.SelectedName);
            yield return ClickUi(Btn("SaveBrowser", "SaveButton"));   // the primary button keeps its first name; its label says Load
            yield return Frames(20);
            yield return WaitFor(() => s_Flow.State == GameFlowState.Playing, 3f);
            Check(s_Flow.State == GameFlowState.Playing, "loading starts the game: " + s_Flow.State);
            Check(s_Flow.Tutorial.Active && Current() == "homes", "the tutorial card is back at objective 2: " + Current());
            Check(game.Grid.CountRoads() == roads, $"the road is back: {game.Grid.CountRoads()} vs {roads}");
            Check(KeyBindings.Label("RoadTool") == "N", "the rebound key survived");
            Check(s_Flow.Save.CityName == "Tutorial town", "city name: " + s_Flow.Save.CityName);
        }
        finally
        {
            KeyBindings.ResetAll();
            GameSettings.Mute = wasMute;
            GameSettings.UiScale = wasScale;
            GameSettings.AutosaveMonths = wasAutosave;
            UiScaling.Apply();
            RemoveVirtualDevices();
            if (realMouse != null) InputSystem.EnableDevice(realMouse);
            if (realKeyboard != null) InputSystem.EnableDevice(realKeyboard);
            Log($"DONE, {s_Fails} failed checks");
            Log("devices restored");
        }
    }
}
