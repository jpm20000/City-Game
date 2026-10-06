using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// M26 release play-through: one session from the title screen that touches each 1.1 feature through the virtual mouse
// and keyboard: the version on the title, New City, the camera turn (Q / E), a toolbar flyout, the Build menu search and
// placement of the custom Market Hall, a paired avenue, a one-way highway, High-density homes, the goods meter, and a
// save and load that bring all of it back. Start it from a one-line RunCommand in Play mode:
//   FindAnyObjectByType<GameManager>().StartCoroutine(Release11Driver.Run(logPath, savesDir, shotPath));
public static class Release11Driver
{
    private static Mouse s_Mouse;
    private static Keyboard s_Keyboard;
    private static string s_Log;
    private static int s_Fails;

    private static void Log(string line) => File.AppendAllText(s_Log, $"[{Time.frameCount}] {line}\n");
    private static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

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

    private static IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(s_Keyboard, new KeyboardState(key)); yield return Frames(2);
        InputSystem.QueueStateEvent(s_Keyboard, new KeyboardState()); yield return Frames(4);
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

    private static GameObject Btn(string name, string rootPath = "UI")
    {
        GameObject root = GameObject.Find(rootPath);
        if (root == null) return null;
        foreach (Button b in root.GetComponentsInChildren<Button>())
        {
            if (b.name == name) return b.gameObject;
        }
        return null;
    }

    private static GameObject BtnWithText(string text, string rootPath)
    {
        GameObject root = GameObject.Find(rootPath);
        if (root == null) return null;
        foreach (Button b in root.GetComponentsInChildren<Button>())
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

    private static Vector2Int V(int x, int y) => new Vector2Int(x, y);

    private static IEnumerator Drag(params Vector2Int[] cells)
    {
        MouseTo(CellPos(cells[0]), false); yield return Frames(3);
        MouseTo(CellPos(cells[0]), true); yield return Frames(4);
        for (int i = 1; i < cells.Length; i++) { MouseTo(CellPos(cells[i]), true); yield return Frames(4); }
        MouseTo(CellPos(cells[cells.Length - 1]), false); yield return Frames(4);
    }

    private static IEnumerator ZoomIn()
    {
        for (int i = 0; i < 5; i++) { MouseTo(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false, 120f); yield return Frames(3); }
        MouseTo(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false, 0f);
        yield return Frames(5);
    }

    private static void RemoveVirtualDevices()
    {
        foreach (InputDevice device in InputSystem.devices.ToArray())
        {
            if (device.name.StartsWith("Virtual")) InputSystem.RemoveDevice(device);
        }
    }

    public static IEnumerator Run(string logPath, string savesDir, string shotPath)
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
        s_Mouse.MakeCurrent();
        s_Keyboard = InputSystem.AddDevice<Keyboard>("VirtualKeyboard");
        s_Keyboard.MakeCurrent();
        try
        {
            GameFlow flow = GameFlow.Instance;
            SaveSlots.Root = savesDir;
            KeyBindings.ResetAll();
            var game = Object.FindAnyObjectByType<GameManager>();
            var placement = Object.FindAnyObjectByType<PlacementController>();
            var saves = Object.FindAnyObjectByType<SaveGameController>();
            var camera = Object.FindAnyObjectByType<IsoCameraController>();
            yield return Frames(5);
            if (flow.State != GameFlowState.MainMenu)
            {
                flow.EnterMainMenu();
                yield return Frames(10);
                if (flow.Confirm.IsOpen) { yield return ClickUi(BtnWithText("Don't save", "UI/ConfirmDialog")); yield return Frames(10); }
            }

            // --- the title shows the release version ---
            Check(flow.State == GameFlowState.MainMenu, "starts on the title");
            Check(Application.version == "1.1.0", "the player version is 1.1.0: " + Application.version);
            bool versionShown = GameObject.Find("UI/MainMenu").GetComponentsInChildren<TMP_Text>().Any(t => t.text == "v" + Application.version);
            Check(versionShown, "the title shows v" + Application.version);

            // --- New City (Modern, 32x32) from the title ---
            yield return ClickUi(BtnWithText("New city", "UI/MainMenu"));
            yield return Frames(10);
            yield return ClickUi(BtnWithText("Modern", "UI/NewCityDialog"));
            yield return ClickUi(BtnWithText("32", "UI/NewCityDialog"));
            yield return ClickUi(Btn("CreateButton"));
            yield return Frames(20);
            Check(flow.State == GameFlowState.Playing && game.MapSize.x == 32, $"a 32x32 city starts: {flow.State} {game.MapSize}");
            game.Clock.SetSpeed(GameSpeed.Paused);
            yield return ZoomIn();
            GridData grid = game.Grid;

            // --- camera: E and Q turn the view, the buttons are on the toolbar ---
            Check(camera.View == 0, "starts in view 0");
            yield return Press(Key.E);
            yield return new WaitForSecondsRealtime(0.5f);
            Check(camera.View == 1, "E turns the view: " + camera.View);
            yield return Press(Key.Q);
            yield return new WaitForSecondsRealtime(0.5f);
            Check(camera.View == 0, "Q turns it back: " + camera.View);

            // --- toolbar groups: a flyout lists its buildings and closes on Esc ---
            yield return ClickUi(Btn("Group_Utilities"));
            yield return Frames(4);
            Check(Btn("Build_water_tower") != null, "the Utilities flyout lists the Water Tower");
            yield return Press(Key.Escape);
            Check(Btn("Build_water_tower") == null, "Esc closes the flyout");

            // --- roads: a paired avenue and a one-way highway ---
            yield return ClickUi(Btn("Road_avenue"));
            yield return Frames(3);
            yield return Drag(V(13, 16), V(14, 16), V(15, 16), V(16, 16), V(17, 16));
            bool pair = true;
            for (int x = 13; x <= 17; x++) pair &= grid.GetRoadTier(V(x, 16)) == RoadTiers.Avenue && grid.GetRoadTier(V(x, 15)) == RoadTiers.Avenue;
            Check(pair, "the avenue is laid as two lanes");
            // DEBUG: the Highway needs Automobiles, which a Modern start does not have; grant it the way a save would.
            TechSystem tech = game.Simulation.Tech;
            var ids = tech.Researched.Select(t => t.Id).ToList();
            ids.Add("automobiles");
            tech.Restore(tech.CurrentAge, ids, "", 0f, new string[0]);
            GameEvents.RaiseCityLoaded();
            yield return Frames(10);
            yield return ClickUi(Btn("Road_highway"));
            yield return Frames(3);
            yield return Drag(V(13, 20), V(14, 20), V(15, 20), V(16, 20), V(17, 20));
            bool east = true;
            for (int x = 13; x <= 17; x++) east &= grid.GetRoadTier(V(x, 20)) == RoadTiers.Highway && grid.GetRoadDirection(V(x, 20)) == RoadLayout.East;
            Check(east, "the highway is one-way, east along the drag");
            placement.ClearMode();

            // --- density: High homes ---
            yield return ClickUi(BtnWithText("Residential", "UI"));
            yield return ClickUi(Btn("Density_High"));
            yield return Frames(3);
            yield return Drag(V(14, 13), V(15, 13), V(16, 13), V(17, 13));
            bool high = true;
            for (int x = 14; x <= 17; x++) high &= grid.GetZone(V(x, 13)) == ZoneType.Residential && grid.GetDensity(V(x, 13)) == Density.High;
            Check(high, "the dragged homes are High density");
            placement.ClearMode();

            // --- goods: the Modern city has them, and the G meter and its tooltip exist ---
            Check(game.Simulation.GoodsActive, "goods are in play in a Modern city");
            GameObject goodsGroup = GameObject.Find("UI").GetComponentsInChildren<GoodsTooltip>(true).Select(t => t.gameObject).FirstOrDefault();
            Check(goodsGroup != null && goodsGroup.activeInHierarchy, "the G meter is shown");

            // --- the Build menu: F, search, pick the custom Market Hall, place it ---
            yield return Press(Key.F);
            BuildMenu menu = flow.BuildMenu;
            Check(menu.IsOpen, "F opens the Build menu");
            var input = GameObject.Find("UI").GetComponentsInChildren<TMP_InputField>(false).FirstOrDefault(f => f.name == "BuildSearch");
            Check(input != null, "the search field exists");
            if (input != null) { input.text = "market"; yield return Frames(4); }
            Check(menu.ShownCount == 1 && menu.SelectedId == "market_hall", $"'market' finds the Market Hall: {menu.ShownCount}, {menu.SelectedId}");
            ScreenCapture.CaptureScreenshot(shotPath);
            yield return Frames(6);
            yield return Press(Key.Enter);
            Check(!menu.IsOpen && placement.SelectedBuilding != null && placement.SelectedBuilding.Id == "market_hall", "Enter starts placing it");
            Vector2Int spot = default;
            bool found = false;
            for (int y = 2; y < grid.Height - 3 && !found; y++)
            {
                for (int x = 2; x < grid.Width - 3 && !found; x++)
                {
                    var c = new Vector2Int(x, y);
                    if (grid.IsOccupied(c) || grid.IsOccupied(c + Vector2Int.right) || grid.IsOccupied(c + Vector2Int.up) || grid.IsOccupied(c + Vector2Int.one)) continue;
                    if (grid.GetZone(c) != ZoneType.None || grid.IsRoad(c)) continue;
                    Vector2 s = CellPos(c);
                    if (s.x < Screen.width * 0.25f || s.x > Screen.width * 0.7f || s.y < Screen.height * 0.25f || s.y > Screen.height * 0.7f) continue;
                    spot = c; found = true;
                }
            }
            Check(found, "a free 2x2 spot is on screen: " + spot);
            if (found) yield return ClickAt(CellPos(spot));
            yield return Frames(6);
            int markets = placement.PlacedBuildings.Count(b => b.Definition.Id == "market_hall");
            Check(markets == 1, "the Market Hall is placed: " + markets);
            placement.ClearMode();
            ScreenCapture.CaptureScreenshot(shotPath.Replace(".png", "_city.png"));
            yield return Frames(6);

            // --- save and load bring it all back ---
            Check(saves.Save(), "the city saves");
            Check(saves.Load(), "the city loads");
            yield return Frames(20);
            grid = game.Grid;
            bool back = true;
            for (int x = 13; x <= 17; x++) back &= grid.GetRoadTier(V(x, 16)) == RoadTiers.Avenue && grid.GetRoadTier(V(x, 15)) == RoadTiers.Avenue && grid.GetRoadDirection(V(x, 20)) == RoadLayout.East;
            for (int x = 14; x <= 17; x++) back &= grid.GetDensity(V(x, 13)) == Density.High;
            Check(back, "the avenue, the one-way highway and the High homes are as saved");
            markets = placement.PlacedBuildings.Count(b => b.Definition.Id == "market_hall");
            Check(markets == 1, "the Market Hall is still there: " + markets);
            Check(game.Simulation.GoodsActive, "goods are still in play after loading");
            Check(Directory.GetFiles(savesDir, "*.json").Length >= 1, "a save file was written");

            // --- and back to the title ---
            flow.EnterMainMenu();
            yield return Frames(10);
            if (flow.Confirm.IsOpen) { yield return ClickUi(BtnWithText("Don't save", "UI/ConfirmDialog")); yield return Frames(10); }
            Check(flow.State == GameFlowState.MainMenu, "back on the title");
        }
        finally
        {
            RemoveVirtualDevices();
            if (realMouse != null) InputSystem.EnableDevice(realMouse);
            if (realKeyboard != null) InputSystem.EnableDevice(realKeyboard);
            Log($"DONE, {s_Fails} failed checks");
        }
    }
}
