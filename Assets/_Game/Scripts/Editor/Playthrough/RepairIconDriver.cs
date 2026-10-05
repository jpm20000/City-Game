using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// M21d play-through of the repair icon, UI-only (virtual keyboard and mouse): a Modern 32 x 32 city, a Water Tower placed
// from the Utilities flyout, the tower taken down through the sim (what a random breakdown does), the icon over it
// (and over nothing else), a click on the tower opening the Repair button, Repair clearing the icon and the breakdown, a
// second breakdown followed by New City dropping the icon. Start it from a one-line RunCommand in Play mode (set
// Application.runInBackground = true first):
//   FindAnyObjectByType<GameManager>().StartCoroutine(RepairIconDriver.Run(logPath, savesDir, shotPath));
public static class RepairIconDriver
{
    private static Mouse s_Mouse;
    private static string s_Log;
    private static int s_Fails;

    private static void Log(string line) => File.AppendAllText(s_Log, $"[{Time.frameCount}] {line}\n");
    private static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    private static IEnumerator Seconds(float s) { float end = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < end) yield return null; }

    private static void Check(bool ok, string what)
    {
        if (!ok) s_Fails++;
        Log((ok ? "PASS " : "FAIL ") + what);
    }

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

    private static GameObject Btn(string name)
    {
        foreach (Button b in GameObject.Find("UI").GetComponentsInChildren<Button>())
        {
            if (b.name == name) return b.gameObject;
        }
        return null;
    }

    private static GameObject BtnWithText(string text)
    {
        foreach (Button b in GameObject.Find("UI").GetComponentsInChildren<Button>())
        {
            TMP_Text label = b.GetComponentInChildren<TMP_Text>();
            if (b.gameObject.activeInHierarchy && label != null && label.text.Contains(text)) return b.gameObject;
        }
        return null;
    }

    private static Vector2 CellPos(Vector2Int c)
    {
        Vector3 p = Camera.main.WorldToScreenPoint(new Vector3(c.x + 0.5f, 0f, c.y + 0.5f));
        return new Vector2(p.x, p.y);
    }

    public static IEnumerator Run(string logPath, string savesDir, string shotPath)
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
        s_Mouse.MakeCurrent();
        try
        {
            GameFlow flow = GameFlow.Instance;
            SaveSlots.Root = savesDir;
            var game = Object.FindAnyObjectByType<GameManager>();
            var placement = Object.FindAnyObjectByType<PlacementController>();
            yield return Frames(10);

            flow.Menu.RequestNew();
            yield return Frames(10);
            if (flow.Confirm.IsOpen) { yield return ClickUi(Btn("Don't saveButton")); yield return Frames(10); }
            yield return ClickUi(BtnWithText("Modern"));
            yield return ClickUi(BtnWithText("32"));
            yield return ClickUi(Btn("CreateButton"));
            yield return Frames(20);
            if (flow.Confirm.IsOpen) { yield return ClickUi(Btn("Don't saveButton")); yield return Frames(20); }
            Check(flow.State == GameFlowState.Playing, "a city starts: " + flow.State);
            game.Clock.SetSpeed(GameSpeed.Paused);
            for (int i = 0; i < 24; i++) { MouseTo(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false, -120f); yield return Frames(3); }
            MouseTo(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false, 0f);
            yield return Frames(5);

            var icons = Object.FindAnyObjectByType<StatusIcons>();
            Check(icons != null, "the StatusIcons presenter exists");
            Check(icons.ActiveCount == 0, "no icon in a healthy city");

            // A Water Tower from the Utilities flyout.
            yield return ClickUi(Btn("Group_Utilities"));
            yield return Frames(3);
            yield return ClickUi(Btn("Build_water_tower"));
            yield return Frames(3);
            var spot = new Vector2Int(16, 16);
            yield return ClickAt(CellPos(spot));
            yield return Frames(10);
            Check(game.Grid.IsOccupied(spot), "the Water Tower is placed");
            placement.ClearMode();
            yield return Frames(3);
            BuildingInstance tower = placement.GetBuildingAt(spot);
            Check(tower != null, "the tower can be found at its cell");
            Vector2Int origin = tower.Origin;

            // A breakdown (the sim's own entry point) shows the icon over that tower only.
            Check(game.Simulation.Disasters.Breakdowns.Break(origin), "the tower breaks down");
            yield return Seconds(0.6f);
            Check(icons.ActiveCount == 1, $"one icon after the breakdown ({icons.ActiveCount})");
            var icon = icons.GetComponentsInChildren<SpriteRenderer>().FirstOrDefault(r => r.gameObject.activeSelf);
            Check(icon != null && icon.sprite != null && icon.transform.position.y > tower.transform.position.y + 0.5f, "the icon floats above the tower");
            ScreenCapture.CaptureScreenshot(shotPath);
            yield return Seconds(2f);

            // Clicking the tower offers Repair.
            yield return ClickAt(CellPos(spot));
            yield return Frames(6);
            GameObject repair = BtnWithText("Repair");
            Check(repair != null, "selecting the broken tower offers Repair");
            yield return ClickUi(repair);
            yield return Seconds(0.6f);
            Check(!game.Simulation.Disasters.Breakdowns.IsBroken(origin), "Repair mends the tower");
            Check(icons.ActiveCount == 0, $"the icon is gone after the repair ({icons.ActiveCount})");

            // Breaking it again brings the icon back; a new city drops it.
            game.Simulation.Disasters.Breakdowns.Break(origin);
            yield return Seconds(0.6f);
            Check(icons.ActiveCount == 1, "the icon comes back after a second breakdown");
            flow.Menu.RequestNew();
            yield return Frames(10);
            if (flow.Confirm.IsOpen) { yield return ClickUi(Btn("Don't saveButton")); yield return Frames(10); }
            yield return ClickUi(Btn("CreateButton"));
            yield return Frames(20);
            if (flow.Confirm.IsOpen) { yield return ClickUi(Btn("Don't saveButton")); yield return Frames(20); }
            yield return Seconds(0.6f);
            Check(icons.ActiveCount == 0, $"a new city has no icons ({icons.ActiveCount})");
        }
        finally
        {
            foreach (InputDevice device in InputSystem.devices.ToArray())
            {
                if (device.name.StartsWith("Virtual")) InputSystem.RemoveDevice(device);
            }
            if (realMouse != null) InputSystem.EnableDevice(realMouse);
            if (realKeyboard != null) InputSystem.EnableDevice(realKeyboard);
            Log($"DONE, {s_Fails} failed checks");
        }
    }
}
