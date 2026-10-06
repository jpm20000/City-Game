using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// M25d play-through of the Build menu, UI-only (virtual mouse + keyboard): F opens it, a search narrows it, a locked
// building is refused, Custom only shows the sample Market Hall, Enter / a click starts placing it, a click on the map
// places it, a save and load bring it back, and Esc closes the menu. Setup shortcuts (seeded city) are labelled DEBUG.
// Start it from a one-line RunCommand in Play mode (set Application.runInBackground = true first):
//   FindAnyObjectByType<GameManager>().StartCoroutine(CustomAssetsDriver.Run(logPath, savesDir, shotPath));
public static class CustomAssetsDriver
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

    private static void MouseTo(Vector2 pos, bool down)
    {
        var state = new MouseState { position = pos };
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
        InputSystem.QueueStateEvent(s_Keyboard, new KeyboardState()); yield return Frames(2);
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
        foreach (Button b in root.GetComponentsInChildren<Button>(true))
        {
            if (b.name == name) return b.gameObject;
        }
        return null;
    }

    private static GameObject BtnWithText(string text, GameObject root)
    {
        foreach (Button b in root.GetComponentsInChildren<Button>())
        {
            TMP_Text label = b.GetComponentInChildren<TMP_Text>();
            if (b.gameObject.activeInHierarchy && label != null && label.text.Contains(text)) return b.gameObject;
        }
        return null;
    }

    private static IEnumerator NewCity(GameFlow flow, string age)
    {
        flow.Menu.RequestNew();
        yield return Frames(10);
        if (flow.Confirm.IsOpen) { yield return ClickUi(BtnWithText("Don't save", GameObject.Find("UI/ConfirmDialog"))); yield return Frames(10); }
        GameObject dialog = GameObject.Find("UI/NewCityDialog");
        yield return ClickUi(BtnWithText(age, dialog));
        yield return ClickUi(BtnWithText("32", dialog));
        yield return ClickUi(Btn("CreateButton"));
        yield return Frames(20);
        if (flow.Confirm.IsOpen) { yield return ClickUi(BtnWithText("Don't save", GameObject.Find("UI/ConfirmDialog"))); yield return Frames(20); }
    }

    private static Vector2 CellPos(Vector2Int c)
    {
        Vector3 p = Camera.main.WorldToScreenPoint(new Vector3(c.x + 0.5f, 0f, c.y + 0.5f));
        return new Vector2(p.x, p.y);
    }

    private static IEnumerator Search(BuildMenu menu, string text)
    {
        GameObject field = Btn("BuildSearch") != null ? Btn("BuildSearch") : GameObject.Find("BuildSearch");
        var input = GameObject.Find("UI").GetComponentsInChildren<TMP_InputField>(false).FirstOrDefault(f => f.name == "BuildSearch");
        if (input == null) { Check(false, "the search field exists"); yield break; }
        input.text = text;      // the field's own change event drives the filter
        yield return Frames(4);
    }

    private static int Market(Vector2Int cell, PlacementController placement)
    {
        return placement.PlacedBuildings.Count(b => b.Definition.Id == "market_hall");
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
        s_Keyboard = InputSystem.AddDevice<Keyboard>("VirtualKeyboard");
        s_Keyboard.MakeCurrent();
        try
        {
            GameFlow flow = GameFlow.Instance;
            SaveSlots.Root = savesDir;
            var game = Object.FindAnyObjectByType<GameManager>();
            var placement = Object.FindAnyObjectByType<PlacementController>();
            var saves = Object.FindAnyObjectByType<SaveGameController>();
            yield return Frames(10);

            yield return NewCity(flow, "Industrial");
            Check(flow.State == GameFlowState.Playing, "an Industrial city starts: " + flow.State);
            game.Clock.SetSpeed(GameSpeed.Paused);
            placement.DebugSeedCity();                       // DEBUG: cross of roads, a plant and R / C / I strips
            yield return Frames(10);
            BuildMenu menu = flow.BuildMenu;

            // --- open with F, and with the toolbar button ---
            yield return Press(Key.F);
            Check(menu.IsOpen, "F opens the Build menu");
            Check(menu.ShownCount > 0, "it lists buildings: " + menu.ShownCount);
            ScreenCapture.CaptureScreenshot(shotPath);
            yield return Frames(6);
            yield return Press(Key.Escape);
            Check(!menu.IsOpen, "Esc closes it");
            yield return ClickUi(Btn("BuildMenuButton"));
            Check(menu.IsOpen, "the Find button opens it");

            // --- search ---
            yield return Search(menu, "market");
            Check(menu.ShownCount == 1 && menu.SelectedId == "market_hall", $"'market' finds the Market Hall: {menu.ShownCount} row(s), selected {menu.SelectedId}");
            ScreenCapture.CaptureScreenshot(shotPath.Replace(".png", "_search.png"));
            yield return Frames(6);
            yield return Search(menu, "zzzz");
            Check(menu.ShownCount == 0, "'zzzz' finds nothing");

            // --- a locked building is refused and the menu stays open ---
            yield return Search(menu, "university");
            Check(menu.ShownCount >= 1, "a locked building is listed: " + menu.SelectedId);
            yield return Press(Key.Enter);
            Check(menu.IsOpen && placement.CurrentMode != PlacementController.Mode.Building, "a locked pick is refused and the menu stays open");

            // --- Custom only ---
            yield return Search(menu, "");
            yield return ClickUi(Btn("CustomFilter", "UI/BuildMenu"));
            yield return Frames(4);
            Check(menu.ShownCount == 1 && menu.SelectedId == "market_hall", $"Custom only shows just the Market Hall: {menu.ShownCount}");

            // --- pick it by click, place it on the map ---
            yield return ClickUi(Btn("Row_market_hall", "UI/BuildMenu"));
            Check(!menu.IsOpen, "a click on the row closes the menu");
            Check(placement.CurrentMode == PlacementController.Mode.Building && placement.SelectedBuilding != null && placement.SelectedBuilding.Id == "market_hall",
                "placement started with the Market Hall");
            Check(placement.SelectedBuilding != null && placement.SelectedBuilding.IsCustom, "and it is flagged custom");
            yield return Frames(4);

            Vector2Int spot = default;
            bool found = false;
            GridData grid = game.Grid;
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
            int before = Market(spot, placement);
            float cash = game.Economy.Money;
            if (found) yield return ClickAt(CellPos(spot));
            yield return Frames(6);
            Check(Market(spot, placement) == before + 1, "clicking the map places a Market Hall");
            Check(game.Economy.Money < cash, $"it cost money: {cash:F0} -> {game.Economy.Money:F0}");
            yield return Press(Key.Escape);
            yield return Frames(4);

            // --- save and load bring it back ---
            Check(saves.Save(), "the city saves");
            Check(saves.Load(), "the city loads");
            yield return Frames(20);
            Check(Market(spot, placement) == before + 1, "the Market Hall is still there after loading");

            // --- the menu still works after loading; Locked can be hidden ---
            yield return Press(Key.F);
            Check(menu.IsOpen, "F opens the menu again after loading");
            int withLocked = menu.ShownCount;
            yield return ClickUi(Btn("LockedFilter", "UI/BuildMenu"));
            yield return Frames(4);
            Check(menu.ShownCount <= withLocked, $"hiding Locked does not add rows: {withLocked} -> {menu.ShownCount}");
            // --- layout: the window must fit a 1280x720 screen (read from the canvas scaler, not by resizing the Game view) ---
            GameObject windowRoot = GameObject.Find("UI/BuildMenu");
            Canvas canvas = GameObject.Find("UI").GetComponent<Canvas>();
            var scaler = canvas.GetComponent<CanvasScaler>();
            var rect = (RectTransform)windowRoot.transform.GetChild(0);
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            float heightPx = corners[1].y - corners[0].y, widthPx = corners[2].x - corners[1].x;
            // pixels at the current screen -> pixels at 720 high (the canvas scales with the screen height, or not at all)
            float scaleAt720 = scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize ? 720f / Screen.height : 1f;
            Log($"layout: window {widthPx:F0}x{heightPx:F0} px on {Screen.width}x{Screen.height}, scaler {(scaler != null ? scaler.uiScaleMode.ToString() : "none")}, at 720p {widthPx * scaleAt720:F0}x{heightPx * scaleAt720:F0}");
            Check(heightPx * scaleAt720 <= 720f && widthPx * scaleAt720 <= 1280f, "the Build menu fits a 1280x720 screen");
            yield return Press(Key.Escape);
            Check(!menu.IsOpen, "Esc closes the menu at the end");

            // --- rebinding F: the real rebind path, then back ---
            KeyBindings.Entry entry = KeyBindings.Entries.First(e => e.Action == "BuildMenu");
            string rebound = null;
            KeyBindings.Rebind(entry, m => rebound = m);
            yield return Frames(3);
            yield return Press(Key.G);
            float until = Time.realtimeSinceStartup + 1f;       // the rebind op waits a moment in real time for another key
            while (rebound == null && Time.realtimeSinceStartup < until) yield return null;
            Check(rebound != null && KeyBindings.Label(entry) == "G", "Build menu rebound to G: " + rebound);
            yield return Press(Key.F);
            Check(!menu.IsOpen, "F no longer opens the menu");
            yield return Press(Key.G);
            Check(menu.IsOpen, "G opens it");
            yield return Press(Key.Escape);
            KeyBindings.Reset(entry);
            yield return Frames(3);
            yield return Press(Key.F);
            Check(menu.IsOpen && KeyBindings.IsDefault(entry), "reset puts F back");
            yield return Press(Key.Escape);
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
