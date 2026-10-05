using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// M19f play-through of the tutorial, UI-only (virtual keyboard and mouse): title -> Tutorial -> the first six objectives by
// clicks and drags -> quick save, advance, quick load (the card returns at the saved objective) -> Skip -> the New City
// dialog's Tutorial toggle. Start it from a one-line RunCommand:
//   FindAnyObjectByType<GameManager>().StartCoroutine(TutorialDriver.Run(logPath, savesDir));
public static class TutorialDriver
{
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

    private static Vector2 UiPos(GameObject go)
    {
        var rt = (RectTransform)go.transform;
        return RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
    }

    private static IEnumerator ClickUi(GameObject go)
    {
        if (go == null || !go.activeInHierarchy) { Check(false, "missing ui object"); yield break; }
        yield return ClickAt(UiPos(go));
    }

    // A button anywhere under the UI canvas by name (the first one that is shown).
    private static GameObject Btn(string name)
    {
        foreach (Button b in GameObject.Find("UI").GetComponentsInChildren<Button>())
        {
            if (b.name == name) return b.gameObject;
        }
        return null;
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

    private static List<Vector2Int> Row(int y, int x0, int x1)
    {
        var list = new List<Vector2Int>();
        for (int x = x0; x <= x1; x++) list.Add(new Vector2Int(x, y));
        return list;
    }

    private static string Current() => s_Flow.Tutorial.Active ? s_Flow.Tutorial.CurrentId : "(none)";
    private static string Pointing() => UiHighlight.Target != null ? UiHighlight.Target.name : "(nothing)";

    private static IEnumerator WaitFor(System.Func<bool> done, float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!done() && Time.realtimeSinceStartup < end) yield return null;
    }

    private static void Cleanup(Mouse realMouse, Keyboard realKeyboard)
    {
        foreach (InputDevice device in InputSystem.devices.ToArray())
        {
            if (device.name.StartsWith("Virtual")) InputSystem.RemoveDevice(device);
        }
        if (realMouse != null) InputSystem.EnableDevice(realMouse);
        if (realKeyboard != null) InputSystem.EnableDevice(realKeyboard);
    }

    public static IEnumerator Run(string logPath, string savesDir)
    {
        s_Log = logPath;
        s_Fails = 0;
        File.WriteAllText(s_Log, string.Empty);
        foreach (InputDevice device in InputSystem.devices.ToArray())
        {
            if (device.name.StartsWith("Virtual")) InputSystem.RemoveDevice(device);
        }
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
        try
        {
            s_Flow = GameFlow.Instance;
            SaveSlots.Root = savesDir;
            var game = Object.FindAnyObjectByType<GameManager>();
            Log($"screen {Screen.width}x{Screen.height}, flow {s_Flow.State}");
            yield return Frames(5);

            // Title -> Tutorial.
            Check(s_Flow.State == GameFlowState.MainMenu, "starts on the title");
            yield return ClickUi(Btn("TutorialButton"));
            yield return Frames(10);
            Check(s_Flow.State == GameFlowState.Playing && s_Flow.Tutorial.Active, "Tutorial starts a game with the card: " + s_Flow.State);
            Check(Current() == "road", "first objective: " + Current());
            Check(game.MapSize.x == 48 && game.CurrentAgeName != null, "48x48 Medieval: " + game.MapSize + " " + game.CurrentAgeName);
            yield return Frames(20);
            Check(s_Flow.Tutorial.Visible, "the card is shown");
            Check(Pointing() == "Road", "the Road button is outlined: " + Pointing());

            // Zoom out so the whole map is in view.
            for (int i = 0; i < 25; i++) { MouseTo(new Vector2(Screen.width * 0.4f, Screen.height * 0.5f), false, -120f); yield return Frames(3); }
            MouseTo(new Vector2(Screen.width * 0.4f, Screen.height * 0.5f), false, 0f);
            yield return Frames(5);
            Log("road start at " + CellPos(new Vector2Int(0, 24)) + ", end " + CellPos(new Vector2Int(10, 24)));

            // 1. A road from the west edge.
            yield return ClickUi(Btn("Road"));
            yield return Drag(Row(24, 0, 10));
            yield return Frames(20);
            yield return WaitFor(() => Current() != "road", 3f);
            Check(Current() == "homes", "road laid -> " + Current());
            Check(Pointing() == "Residential", "now pointing at Residential: " + Pointing());

            // 2. Homes beside it.
            yield return ClickUi(Btn("Residential"));
            var placement = Object.FindAnyObjectByType<PlacementController>();
            Log($"mode {placement.CurrentMode} brush {placement.ZoneBrush}, cell(1,25) {CellPos(new Vector2Int(1, 25))}, (8,25) {CellPos(new Vector2Int(8, 25))}, road cells {game.Grid.CountRoads()}");
            yield return Drag(Row(25, 1, 8));
            yield return Frames(20);
            int zoned = 0;
            for (int x = 0; x < 48; x++) for (int y = 0; y < 48; y++) if (game.Grid.GetZone(new Vector2Int(x, y)) == ZoneType.Residential) zoned++;
            Log($"residential cells {zoned}");
            var diag = new System.Text.StringBuilder();
            for (int x = 0; x < 48; x++) for (int y = 0; y < 48; y++)
            {
                var c = new Vector2Int(x, y);
                if (game.Grid.GetZone(c) == ZoneType.Residential) diag.Append($"({x},{y}:{(game.Roads.HasRoadAccess(c) ? "A" : "-")}) ");
            }
            int conn = 0;
            for (int x = 0; x < 48; x++) for (int y = 0; y < 48; y++) { var c = new Vector2Int(x, y); if (game.Grid.IsRoad(c) && game.Roads.IsConnectedToEntry(c)) conn++; }
            Log($"zones {diag} connected roads {conn}");
            yield return WaitFor(() => Current() != "homes", 3f);
            Check(Current() == "well", "homes zoned -> " + Current());
            Check(Pointing() == "Group_Parks", "pointing at the Parks group: " + Pointing());
            yield return ClickUi(Btn("Group_Parks"));
            yield return WaitFor(() => Pointing() == "Build_well", 3f);
            Check(Pointing() == "Build_well", "pointing at the Well in the flyout: " + Pointing());

            // 3. A well.
            yield return ClickUi(Btn("Build_well"));   // picking it closes the flyout
            yield return ClickAt(CellPos(new Vector2Int(5, 27)));
            yield return Frames(20);
            yield return WaitFor(() => Current() != "well", 3f);
            Check(Current() == "villagers", "well placed -> " + Current());

            // 4. Wait for the first villagers at 4x.
            yield return ClickUi(GameObject.Find("UI/HUD/Time/Speed/Speed4"));
            yield return WaitFor(() => Current() != "villagers", 120f);
            Check(Current() == "work", "first villagers -> " + Current() + ", pop " + game.Simulation.Population.Population);

            // 5. Shops and crafts.
            yield return ClickUi(Btn("Commercial"));
            yield return Drag(Row(23, 1, 4));
            yield return ClickUi(Btn("Industrial"));
            yield return Drag(Row(23, 5, 8));
            yield return Frames(20);
            yield return WaitFor(() => Current() != "work", 3f);
            Check(Current() == "books", "shops and crafts zoned -> " + Current());
            Check(Pointing() == "BudgetButton", "pointing at Budget: " + Pointing());

            // A quick save now, then the Budget panel advances the card; F9 brings it back to "books".
            yield return Press(Key.F5);
            yield return Frames(10);
            yield return ClickUi(Btn("BudgetButton"));
            yield return Frames(20);
            yield return WaitFor(() => Current() != "books", 3f);
            Check(Current() == "research", "Budget opened -> " + Current());
            yield return Press(Key.Escape); yield return Press(Key.Escape);   // the first Esc puts the zone tool down, the second closes the panel
            yield return Frames(5);
            Log($"before F9: budget open {Object.FindAnyObjectByType<BudgetPanel>(FindObjectsInactive.Include).IsOpen}, flow {s_Flow.State}, save.Tutorial {s_Flow.Save.Tutorial}, files {string.Join(",", Directory.GetFiles(savesDir).Select(Path.GetFileName))}");
            yield return Press(Key.F9);
            yield return Frames(10);
            Log($"after F9: confirm {s_Flow.Confirm.IsOpen}, flow {s_Flow.State}, save.Tutorial {s_Flow.Save.Tutorial}, current {Current()}");
            if (s_Flow.Confirm.IsOpen) { yield return ClickUi(Btn("Don't saveButton")); yield return Frames(15); }
            yield return WaitFor(() => Current() == "books", 3f);
            Check(Current() == "books", "quick load -> the card is back at the saved objective: " + Current());

            // Skip, with its confirmation.
            yield return ClickUi(Btn("Skip tutorialButton"));
            yield return Frames(5);
            Check(s_Flow.Confirm.IsOpen, "skipping asks first");
            yield return ClickUi(Btn("Keep goingButton"));
            yield return Frames(5);
            Check(Current() == "books", "Keep going leaves the tutorial");
            yield return ClickUi(Btn("Skip tutorialButton"));
            yield return Frames(5);
            yield return ClickUi(Btn("Skip itButton"));
            yield return Frames(10);
            Check(!s_Flow.Tutorial.Active && !s_Flow.Tutorial.Visible && Pointing() == "(nothing)", "skipped: no card, no outline");
            Check(s_Flow.Save.Tutorial == -1, "the save says no tutorial: " + s_Flow.Save.Tutorial);

            // New City dialog: the Tutorial toggle.
            s_Flow.Menu.RequestNew();
            yield return Frames(10);
            if (s_Flow.Confirm.IsOpen) { yield return ClickUi(Btn("Don't saveButton")); yield return Frames(10); }
            GameObject toggle = null;
            foreach (Toggle t in GameObject.Find("UI").GetComponentsInChildren<Toggle>())
            {
                if (t.name == "TutorialToggle") toggle = t.gameObject;
            }
            Check(toggle != null, "the New City dialog has a Tutorial toggle");
            yield return ClickUi(toggle);
            yield return ClickUi(Btn("CreateButton"));
            yield return Frames(15);
            if (s_Flow.Confirm.IsOpen) { yield return ClickUi(Btn("Don't saveButton")); yield return Frames(15); }
            Check(s_Flow.Tutorial.Active && Current() == "road", "New City with the toggle restarts the tutorial: " + Current());
        }
        finally
        {
            Cleanup(realMouse, realKeyboard);
            Log($"DONE, {s_Fails} failed checks");
            Log("devices restored");
        }
    }
}
