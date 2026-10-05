using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// M20e play-through of the camera and toolbar, UI-only (virtual keyboard and mouse): the New City dialog's switches
// (visible state, the tutorial greying Disasters), a Modern 32 x 32 city, then in each of the four views a road and a
// zone drag (so picking works in every view), Q / E and the Turn buttons, building from two groups with the flyouts,
// the Views flyout and the V key, Esc closing a flyout, pan relative to the screen, New City returning to the default
// view and the showcase drifting after a turn. Start it from a one-line RunCommand in Play mode (the Editor window may
// stall unfocused: set Application.runInBackground = true first):
//   FindAnyObjectByType<GameManager>().StartCoroutine(CameraToolbarDriver.Run(logPath, savesDir));
public static class CameraToolbarDriver
{
    private static Mouse s_Mouse;
    private static Keyboard s_Keyboard;
    private static string s_Log;
    private static int s_Fails;
    private static GameFlow s_Flow;
    private static GameManager s_Game;
    private static IsoCameraController s_Camera;

    private static void Log(string line) => File.AppendAllText(s_Log, $"[{Time.frameCount}] {line}\n");
    private static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    private static IEnumerator Seconds(float s) { float end = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < end) yield return null; }

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

    // A button (or toggle) anywhere under the UI canvas by name; only the ones shown.
    private static GameObject Btn(string name)
    {
        foreach (Button b in GameObject.Find("UI").GetComponentsInChildren<Button>())
        {
            if (b.name == name) return b.gameObject;
        }
        return null;
    }

    private static Toggle FindToggle(string name)
    {
        foreach (Toggle t in GameObject.Find("UI").GetComponentsInChildren<Toggle>())
        {
            if (t.name == name) return t;
        }
        return null;
    }

    // The first button under the UI whose label contains the text.
    private static GameObject BtnWithText(string text)
    {
        foreach (Button b in GameObject.Find("UI").GetComponentsInChildren<Button>())
        {
            TMP_Text label = b.GetComponentInChildren<TMP_Text>();
            if (label != null && label.text.Contains(text)) return b.gameObject;
        }
        return null;
    }

    private static string LabelOf(GameObject go)
    {
        TMP_Text label = go != null ? go.GetComponentInChildren<TMP_Text>() : null;
        return label != null ? label.text : string.Empty;
    }

    private static Vector2 CellPos(Vector2Int c)
    {
        Vector3 p = Camera.main.WorldToScreenPoint(new Vector3(c.x + 0.5f, 0f, c.y + 0.5f));
        return new Vector2(p.x, p.y);
    }

    private static bool OnScreen(Vector2 p) => p.x > 30f && p.x < Screen.width - 30f && p.y > 160f && p.y < Screen.height - 130f;

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

    private static Vector3 LookAt()
    {
        Camera cam = Camera.main;
        Ray ray = cam.ScreenPointToRay(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
        new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float t);
        return ray.GetPoint(t);
    }

    // Opens a Dialog -> New city with the given age / size, and checks the switches on the way.
    private static IEnumerator OpenNewCity()
    {
        s_Flow.Menu.RequestNew();
        yield return Frames(10);
        if (s_Flow.Confirm.IsOpen) { yield return ClickUi(Btn("Don't saveButton")); yield return Frames(10); }
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
            s_Game = Object.FindAnyObjectByType<GameManager>();
            s_Camera = Camera.main.GetComponent<IsoCameraController>();
            var placement = Object.FindAnyObjectByType<PlacementController>();
            var overlay = Object.FindAnyObjectByType<InfoOverlay>();
            Log($"screen {Screen.width}x{Screen.height}, flow {s_Flow.State}");
            yield return Frames(10);

            // 1. The New City dialog's switches.
            yield return OpenNewCity();
            Toggle disasters = FindToggle("DisastersToggle");
            Toggle tutorial = FindToggle("TutorialToggle");
            Check(disasters != null && tutorial != null, "the dialog has both switches");
            bool disastersStart = disasters.isOn;
            Check(LabelOf(disasters.gameObject).Contains(disastersStart ? "— On" : "— Off") && LabelOf(tutorial.gameObject).Contains("— Off"), "both show their state: " + LabelOf(disasters.gameObject) + " | " + LabelOf(tutorial.gameObject));
            yield return ClickUi(tutorial.gameObject);
            yield return Frames(3);
            Check(tutorial.isOn && !disasters.isOn && !disasters.interactable && LabelOf(tutorial.gameObject).Contains("— On") && LabelOf(disasters.gameObject).Contains("— Off"),
                "Tutorial on: it shows On, Disasters shows Off and is greyed");
            yield return ClickUi(tutorial.gameObject);
            yield return Frames(3);
            Check(!tutorial.isOn && disasters.interactable && disasters.isOn == disastersStart, "Tutorial off restores the Disasters choice");
            yield return ClickUi(disasters.gameObject);
            yield return Frames(3);
            Check(disasters.isOn == !disastersStart && LabelOf(disasters.gameObject).Contains(disasters.isOn ? "— On" : "— Off"), "Disasters toggles and its label follows");
            yield return ClickUi(BtnWithText("Modern"));
            yield return ClickUi(BtnWithText("32"));
            yield return ClickUi(Btn("CreateButton"));
            yield return Frames(20);
            if (s_Flow.Confirm.IsOpen) { yield return ClickUi(Btn("Don't saveButton")); yield return Frames(20); }
            Check(s_Flow.State == GameFlowState.Playing && s_Game.MapSize.x == 32, $"a 32 x 32 city starts: {s_Flow.State} {s_Game.MapSize}");
            Check(s_Game.CurrentAgeName != null && s_Game.CurrentAgeName.Contains("Modern"), "Modern age: " + s_Game.CurrentAgeName);
            s_Game.Clock.SetSpeed(GameSpeed.Paused);

            // Zoom out so the working area is in view.
            for (int i = 0; i < 30; i++) { MouseTo(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false, -120f); yield return Frames(3); }
            MouseTo(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false, 0f);
            yield return Frames(5);

            // 2. Toolbar shape: five groups, one Views button, the two turn buttons.
            foreach (string group in new[] { "Group_Utilities", "Group_Services", "Group_Health", "Group_Education", "Group_Parks", "ViewsButton", "RotateLeft", "RotateRight" })
            {
                Check(Btn(group) != null, group + " is on the toolbar");
            }
            Check(Btn("Build_water_tower") == null, "building buttons stay in their flyouts");

            // 3. Four views: Q / E, road and zone drags, and the pivot.
            Vector3 pivot = LookAt();
            Check(s_Camera.View == 0, "starts in view 0");
            for (int turn = 0; turn < 4; turn++)
            {
                int view = s_Camera.View;
                int y = 6 + view * 5;
                yield return ClickUi(Btn("Road"));
                var road = Row(y, 0, 8);
                bool visible = road.All(c => OnScreen(CellPos(c)));
                Check(visible, $"view {view}: the drag cells are on screen");
                yield return Drag(road);
                int laid = road.Count(c => s_Game.Grid.IsRoad(c));
                Check(laid == road.Count, $"view {view}: road laid by a drag ({laid}/{road.Count})");
                yield return ClickUi(Btn("Residential"));
                var zone = Row(y + 1, 1, 6);
                yield return Drag(zone);
                int zoned = zone.Count(c => s_Game.Grid.GetZone(c) == ZoneType.Residential);
                Check(zoned == zone.Count, $"view {view}: zone painted by a drag ({zoned}/{zone.Count})");
                placement.ClearMode();
                yield return Frames(3);

                // Turn: even turns with E, odd with the Turn R button.
                if (turn % 2 == 0) yield return Press(Key.E);
                else yield return ClickUi(Btn("RotateRight"));
                yield return Seconds(0.5f);
                Check(s_Camera.View == (view + 1) % 4, $"turned to view {s_Camera.View}");
                Vector3 now = LookAt();
                Check(Vector2.Distance(new Vector2(now.x, now.z), new Vector2(pivot.x, pivot.z)) < 0.05f, $"the screen centre stays on {pivot} ({now})");
                float lightYaw = Mathf.DeltaAngle(0f, Object.FindAnyObjectByType<Light>().transform.eulerAngles.y);
                Log($"camera yaw {Camera.main.transform.eulerAngles.y:F0}, light yaw {lightYaw:F0}, offset {IsoCameraController.YawOffset:F0}");
            }
            Check(s_Camera.View == 0, "four turns come back to view 0");
            yield return Press(Key.Q);
            yield return Seconds(0.5f);
            Check(s_Camera.View == 3, "Q turns the other way: " + s_Camera.View);
            yield return ClickUi(Btn("RotateLeft"));
            yield return Seconds(0.5f);
            Check(s_Camera.View == 2, "the Turn L button: " + s_Camera.View);

            // 4. Pan is relative to the screen: W moves the look-at point up the screen in this (turned) view.
            Vector3 before = LookAt();
            Keys(Key.W);
            yield return Frames(20);
            Keys();
            yield return Frames(4);
            Vector3 after = LookAt();
            Vector3 forwardXZ = Camera.main.transform.forward;
            forwardXZ.y = 0f;
            forwardXZ.Normalize();
            Vector3 moved = after - before;
            moved.y = 0f;
            Check(moved.magnitude > 0.1f && Vector3.Dot(moved.normalized, forwardXZ) > 0.9f, $"W pans up the screen in view 2 (moved {moved.magnitude:F2}, dot {Vector3.Dot(moved.normalized, forwardXZ):F2})");

            // 5. Building groups, in a turned view: open a flyout, pick, place.
            yield return ClickUi(Btn("Group_Utilities"));
            yield return Frames(3);
            GameObject tower = Btn("Build_water_tower");
            Check(tower != null, "the Utilities flyout lists the Water Tower");
            yield return ClickUi(tower);
            yield return Frames(3);
            Check(placement.CurrentMode == PlacementController.Mode.Building && placement.SelectedBuilding != null && placement.SelectedBuilding.Id == "water_tower", "picking a building starts placement");
            Check(Btn("Build_water_tower") == null, "the flyout closed after the pick");
            Vector2Int spot = new Vector2Int(14, 14);
            Vector3 centre = LookAt();
            spot = new Vector2Int(Mathf.Clamp(Mathf.RoundToInt(centre.x), 4, 26), Mathf.Clamp(Mathf.RoundToInt(centre.z), 4, 26));
            yield return ClickAt(CellPos(spot));
            yield return Frames(10);
            Check(s_Game.Grid.IsOccupied(spot), $"the Water Tower is placed at {spot} in view {s_Camera.View}");
            placement.ClearMode();
            yield return ClickUi(Btn("Group_Services"));
            yield return Frames(3);
            GameObject police = Btn("Build_police_station");
            Check(police != null, "the Services flyout lists the Police Station");
            yield return ClickUi(police);
            yield return Frames(3);
            Vector2Int spot2 = new Vector2Int(Mathf.Clamp(spot.x + 4, 4, 28), spot.y);
            yield return ClickAt(CellPos(spot2));
            yield return Frames(10);
            Check(s_Game.Grid.IsOccupied(spot2), $"the Police Station is placed at {spot2}");
            placement.ClearMode();

            // 6. Esc closes a flyout; a click elsewhere too.
            yield return ClickUi(Btn("Group_Health"));
            yield return Frames(3);
            Check(Btn("Build_hospital") != null, "the Health flyout opens");
            yield return Press(Key.Escape);
            Check(Btn("Build_hospital") == null, "Esc closes the flyout");
            yield return ClickUi(Btn("Group_Health"));
            yield return Frames(3);
            yield return ClickAt(new Vector2(Screen.width * 0.5f, Screen.height * 0.4f));
            yield return Frames(3);
            Check(Btn("Build_hospital") == null, "a click elsewhere closes the flyout");
            placement.ClearMode();

            // 7. The Views flyout and the V key.
            yield return ClickUi(Btn("ViewsButton"));
            yield return Frames(3);
            Check(Btn("WaterView") != null && Btn("OffView") != null, "the Views flyout lists the views and Off");
            yield return ClickUi(Btn("WaterView"));
            yield return Frames(3);
            Check(overlay.Shown == InfoOverlay.View.Water && LabelOf(Btn("ViewsButton")) == "Water", $"Water view from the flyout: {overlay.Shown}, button '{LabelOf(Btn("ViewsButton"))}'");
            yield return ClickUi(Btn("ViewsButton"));
            yield return Frames(3);
            yield return ClickUi(Btn("OffView"));
            yield return Frames(3);
            Check(overlay.Shown == InfoOverlay.View.Off && LabelOf(Btn("ViewsButton")) == "Views", "Off turns the view off");
            yield return Press(Key.V);
            yield return Frames(3);
            Check(overlay.Shown != InfoOverlay.View.Off, "V still cycles the views: " + overlay.Shown);
            yield return Press(Key.V);
            overlay.SetView(InfoOverlay.View.Off);

            // 8. A turned view survives until a new city.
            Check(s_Camera.View == 2, "still in view 2");
            yield return OpenNewCity();
            yield return ClickUi(Btn("CreateButton"));
            yield return Frames(20);
            if (s_Flow.Confirm.IsOpen) { yield return ClickUi(Btn("Don't saveButton")); yield return Frames(20); }
            yield return Seconds(0.3f);
            Check(s_Camera.View == 0 && Mathf.Approximately(IsoCameraController.YawOffset, 0f), "New City returns to the default view");

            // 9. The showcase still drifts after a turn.
            yield return Press(Key.E);
            yield return Seconds(0.5f);
            s_Flow.EnterMainMenu();
            yield return Seconds(0.5f);
            Vector3 p0 = Camera.main.transform.position;
            yield return Seconds(1f);
            Vector3 p1 = Camera.main.transform.position;
            Check(s_Flow.State == GameFlowState.MainMenu && Vector3.Distance(p0, p1) > 0.01f, $"the showcase drifts behind the title ({Vector3.Distance(p0, p1):F3})");
        }
        finally
        {
            Cleanup(realMouse, realKeyboard);
            Log($"DONE, {s_Fails} failed checks");
            Log("devices restored");
        }
    }
}
