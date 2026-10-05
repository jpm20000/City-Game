using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// M22f play-through of the road layout, UI-only (virtual mouse): a Modern 32 x 32 city with the Highway unlocked, a paired
// Avenue dragged along +x (second lane to the right of the heading), one lane picked and demolished from the selection panel
// (both go), a one-way Highway dragged along +x, Reverse direction, Make two-way, Make one-way, then a save and a load that
// keep the highway's direction. Start it from a one-line RunCommand in Play mode (set Application.runInBackground = true first):
//   FindAnyObjectByType<GameManager>().StartCoroutine(RoadLayoutDriver.Run(logPath, savesDir, shotPath));
public static class RoadLayoutDriver
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

    // A button with that text that is not a toolbar ToolButton (the selection panel's action, not the Demolish tool).
    private static GameObject PanelBtn(string text)
    {
        foreach (Button b in GameObject.Find("UI").GetComponentsInChildren<Button>())
        {
            TMP_Text label = b.GetComponentInChildren<TMP_Text>();
            if (b.gameObject.activeInHierarchy && b.GetComponent<ToolButton>() == null && label != null && label.text.Contains(text)) return b.gameObject;
        }
        return null;
    }

    private static Vector2 CellPos(Vector2Int c)
    {
        Vector3 p = Camera.main.WorldToScreenPoint(new Vector3(c.x + 0.5f, 0f, c.y + 0.5f));
        return new Vector2(p.x, p.y);
    }

    // Press on the first cell, move over each next one with the button held, release on the last.
    private static IEnumerator Drag(params Vector2Int[] cells)
    {
        MouseTo(CellPos(cells[0]), false); yield return Frames(3);
        MouseTo(CellPos(cells[0]), true); yield return Frames(4);
        for (int i = 1; i < cells.Length; i++)
        {
            MouseTo(CellPos(cells[i]), true);
            yield return Frames(4);
        }
        MouseTo(CellPos(cells[cells.Length - 1]), false);
        yield return Frames(4);
    }

    private static Vector2Int V(int x, int y) => new Vector2Int(x, y);

    private static IEnumerator NewModernCity(GameFlow flow)
    {
        flow.Menu.RequestNew();
        yield return Frames(10);
        if (flow.Confirm.IsOpen) { yield return ClickUi(Btn("Don't saveButton")); yield return Frames(10); }
        yield return ClickUi(BtnWithText("Modern"));
        yield return ClickUi(BtnWithText("32"));
        yield return ClickUi(Btn("CreateButton"));
        yield return Frames(20);
        if (flow.Confirm.IsOpen) { yield return ClickUi(Btn("Don't saveButton")); yield return Frames(20); }
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
            var saves = Object.FindAnyObjectByType<SaveGameController>();
            yield return Frames(10);

            yield return NewModernCity(flow);
            Check(flow.State == GameFlowState.Playing, "a city starts: " + flow.State);
            game.Clock.SetSpeed(GameSpeed.Paused);
            for (int i = 0; i < 5; i++) { MouseTo(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false, 120f); yield return Frames(3); }
            MouseTo(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false, 0f);
            yield return Frames(5);
            GridData grid = game.Grid;

            // The Highway needs Automobiles, a Modern tech a Modern start does not have: research it the way a save would.
            TechSystem tech = game.Simulation.Tech;
            var ids = tech.Researched.Select(t => t.Id).ToList();
            ids.Add("automobiles");
            tech.Restore(tech.CurrentAge, ids, "", 0f, new string[0]);
            GameEvents.RaiseCityLoaded();
            yield return Frames(10);
            Check(Btn("Road_avenue") != null && Btn("Road_avenue").activeInHierarchy, "the Avenue button is there");
            Check(Btn("Road_highway") != null && Btn("Road_highway").activeInHierarchy, "the Highway button is there");

            // --- a paired avenue, dragged east along y = 10 ---
            float money = game.Economy.Money;
            yield return ClickUi(Btn("Road_avenue"));
            yield return Frames(3);
            Check(placement.CurrentMode == PlacementController.Mode.Road && placement.RoadToolTier == RoadTiers.Avenue, "the Avenue tool is active");
            MouseTo(CellPos(V(13, 16)), false);
            yield return Frames(4);
            Check(placement.CursorHint.Contains("2 tiles"), "the hint says the avenue is two tiles wide: " + placement.CursorHint);
            yield return Drag(V(13, 16), V(14, 16), V(15, 16), V(16, 16), V(17, 16));
            for (int x = 13; x <= 17; x++)
            {
                Check(grid.GetRoadTier(V(x, 16)) == RoadTiers.Avenue && grid.GetRoadTier(V(x, 15)) == RoadTiers.Avenue, $"avenue lanes at x {x}");
                Check(grid.GetRoadPair(V(x, 16)) == RoadLayout.South && grid.GetRoadPair(V(x, 15)) == RoadLayout.North, $"x {x}: the lanes are a pair (second lane on the right of the heading)");
            }
            float spent = money - game.Economy.Money;
            Check(Mathf.Abs(spent - 10 * game.Simulation.RoadTiers.Cost(RoadTiers.Avenue)) < 1f, $"ten lanes were paid for ({spent})");
            yield return Frames(6);
            ScreenCapture.CaptureScreenshot(shotPath);
            yield return Seconds(1.5f);

            // --- pick one lane, demolish it from the panel: both lanes go ---
            placement.ClearMode();
            yield return Frames(3);
            yield return ClickAt(CellPos(V(15, 15)));
            yield return Frames(6);
            GameObject demolish = PanelBtn("Demolish");
            Check(demolish != null, "selecting a lane offers Demolish");
            yield return ClickUi(demolish);
            yield return Frames(6);
            Check(!grid.IsRoad(V(15, 15)) && !grid.IsRoad(V(15, 16)), "demolishing one lane took the other");
            Check(grid.GetRoadPair(V(14, 16)) == RoadLayout.South && grid.GetRoadPair(V(16, 15)) == RoadLayout.North, "the rest of the avenue is still paired");

            // --- a one-way highway, dragged east along y = 15 ---
            yield return ClickUi(Btn("Road_highway"));
            yield return Frames(3);
            yield return Drag(V(13, 20), V(14, 20), V(15, 20), V(16, 20), V(17, 20));
            bool allEast = true;
            for (int x = 13; x <= 17; x++) allEast &= grid.GetRoadTier(V(x, 20)) == RoadTiers.Highway && grid.GetRoadDirection(V(x, 20)) == RoadLayout.East;
            Check(allEast, "the highway cells point east along the drag");

            // --- Reverse, Make two-way, Make one-way from the panel ---
            placement.ClearMode();
            yield return Frames(3);
            yield return ClickAt(CellPos(V(15, 20)));
            yield return Frames(6);
            Check(BtnWithText("Reverse direction") != null && BtnWithText("Make two-way") != null, "a one-way highway offers Reverse and Make two-way");
            yield return ClickUi(BtnWithText("Reverse direction"));
            yield return Frames(6);
            bool allWest = true;
            for (int x = 13; x <= 17; x++) allWest &= grid.GetRoadDirection(V(x, 20)) == RoadLayout.West;
            Check(allWest, "Reverse turned the whole stretch west");
            yield return ClickUi(BtnWithText("Make two-way"));
            yield return Frames(6);
            Check(!grid.AnyOneWay(), "Make two-way cleared the direction");
            yield return ClickUi(BtnWithText("Make one-way"));
            yield return Frames(6);
            Check(grid.GetRoadDirection(V(13, 20)) == RoadLayout.East && grid.GetRoadDirection(V(17, 20)) == RoadLayout.East, "Make one-way set the run east again");
            ScreenCapture.CaptureScreenshot(shotPath.Replace(".png", "_highway.png"));
            yield return Seconds(1.5f);

            // --- save, change, load: the layout comes back ---
            Check(saves.Save(), "the city saves");
            byte[] directions = grid.ExportRoadDirections();
            byte[] pairs = grid.ExportRoadPairs();
            RoadLayout.ReverseStretch(grid, V(15, 20));
            grid.SetRoadTier(V(14, 16), 0);
            yield return Frames(4);
            Check(grid.GetRoadDirection(V(15, 20)) == RoadLayout.West, "the city was changed after saving");
            Check(saves.Load(), "the city loads");
            yield return Frames(20);
            grid = game.Grid;
            Check(grid.ExportRoadDirections().SequenceEqual(directions), "the highway directions came back");
            Check(grid.ExportRoadPairs().SequenceEqual(pairs), "the avenue pairs came back");
            Check(grid.GetRoadDirection(V(15, 20)) == RoadLayout.East, "the highway points east again");
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
