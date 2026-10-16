using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// M27 play-through of the drag tools, UI-only (virtual mouse and keyboard): a Modern 32 x 32 city with the Highway
// unlocked. A street line dragged with a wobble (straight along the longer axis, nothing laid until the release, the
// total in the hint), a line dropped with a right click and one with Esc, a line that swings from x to z, a single click,
// a Highway line over a street (upgrade price, one-way along the drag) and one westwards, Avenue lines east and west
// (the second lane on the R side), a line longer than the money (part laid, "short" in the hint), a pipe line and a
// pipe erase line, then a demolish drag across grown blocks, roads and two placed buildings (one confirmation
// on release) and a single demolish click. Start it from a one-line RunCommand in Play mode (set Application.runInBackground = true first):
//   FindAnyObjectByType<GameManager>().StartCoroutine(DragToolsDriver.Run(logPath, savesDir, shotPath));
public static class DragToolsDriver
{
    private static Mouse s_Mouse;
    private static Keyboard s_Keyboard;
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

    private static void MouseTo(Vector2 pos, bool left, float scroll = 0f, bool right = false)
    {
        var state = new MouseState { position = pos, scroll = new Vector2(0f, scroll) };
        if (left) state = state.WithButton(MouseButton.Left);
        if (right) state = state.WithButton(MouseButton.Right);
        InputSystem.QueueStateEvent(s_Mouse, state);
    }

    private static IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(s_Keyboard, new KeyboardState(key)); yield return Frames(2);
        InputSystem.QueueStateEvent(s_Keyboard, new KeyboardState()); yield return Frames(2);
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

    // Press on the first cell and move over the others with the button held; the caller releases (Release) or cancels.
    private static IEnumerator Hold(params Vector2Int[] cells)
    {
        MouseTo(CellPos(cells[0]), false); yield return Frames(3);
        MouseTo(CellPos(cells[0]), true); yield return Frames(4);
        for (int i = 1; i < cells.Length; i++)
        {
            MouseTo(CellPos(cells[i]), true);
            yield return Frames(4);
        }
    }

    private static IEnumerator Release(Vector2Int cell)
    {
        MouseTo(CellPos(cell), false);
        yield return Frames(4);
    }

    private static IEnumerator Drag(params Vector2Int[] cells)
    {
        yield return Hold(cells);
        yield return Release(cells[cells.Length - 1]);
    }

    private static Vector2Int V(int x, int y) => new Vector2Int(x, y);

    private static bool AllRoad(GridData grid, int y, int x0, int x1, byte tier)
    {
        for (int x = x0; x <= x1; x++) if (grid.GetRoadTier(V(x, y)) != tier) return false;
        return true;
    }

    private static int RoadsIn(GridData grid, int y, int x0, int x1)
    {
        int n = 0;
        for (int x = x0; x <= x1; x++) if (grid.IsRoad(V(x, y))) n++;
        return n;
    }

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
        s_Keyboard = InputSystem.AddDevice<Keyboard>("VirtualKeyboard");
        s_Keyboard.MakeCurrent();
        try
        {
            GameFlow flow = GameFlow.Instance;
            SaveSlots.Root = savesDir;
            var game = Object.FindAnyObjectByType<GameManager>();
            var placement = Object.FindAnyObjectByType<PlacementController>();
            yield return Frames(10);

            yield return NewModernCity(flow);
            Check(flow.State == GameFlowState.Playing, "a city starts: " + flow.State);
            game.Clock.SetSpeed(GameSpeed.Paused);
            for (int i = 0; i < 5; i++) { MouseTo(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false, 120f); yield return Frames(3); }
            MouseTo(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false, 0f);
            yield return Frames(5);
            GridData grid = game.Grid;
            EconomySystem economy = game.Economy;
            RoadTiers tiers = game.Simulation.RoadTiers;
            byte street = tiers.BestStreetTier;

            // The Highway needs Automobiles, a Modern tech a Modern start does not have: research it the way a save would.
            TechSystem tech = game.Simulation.Tech;
            var ids = tech.Researched.Select(t => t.Id).ToList();
            ids.Add("automobiles");
            tech.Restore(tech.CurrentAge, ids, "", 0f, new string[0]);
            GameEvents.RaiseCityLoaded();
            yield return Frames(10);

            // --- a street line dragged with a wobble: straight along x, built only on release ---
            yield return ClickUi(Btn("Road"));
            yield return Frames(3);
            Check(placement.CurrentMode == PlacementController.Mode.Road && placement.ActiveRoadTier == street, "the street tool is active");
            float money = economy.Money;
            yield return Hold(V(12, 10), V(13, 10), V(14, 11), V(15, 11), V(16, 11));
            Check(placement.LineActive, "pressing starts a line");
            Check(RoadsIn(grid, 10, 12, 16) == 0 && RoadsIn(grid, 11, 12, 16) == 0, "nothing is laid while the button is held");
            string hint = placement.CursorHint;
            Check(hint.Contains("×5") && hint.Contains($"${5 * tiers.Cost(street):N0}") && placement.CursorHintValid, "the hint gives the run and its total: " + hint);
            yield return Frames(4);
            ScreenCapture.CaptureScreenshot(shotPath);
            yield return Seconds(1.5f);
            yield return Release(V(16, 11));
            Check(!placement.LineActive, "releasing ends the line");
            Check(AllRoad(grid, 10, 12, 16, street), "the release built x 12..16 on y 10");
            Check(RoadsIn(grid, 11, 12, 16) == 0, "the wobble onto y 11 laid nothing there");
            Check(Mathf.Abs(money - economy.Money - 5 * tiers.Cost(street)) < 1f, $"five cells paid for ({money - economy.Money})");

            // --- dropped lines: a right click, then Esc, while held ---
            money = economy.Money;
            yield return Hold(V(12, 13), V(13, 13), V(14, 13), V(15, 13));
            MouseTo(CellPos(V(15, 13)), true, 0f, right: true); yield return Frames(3);
            MouseTo(CellPos(V(15, 13)), true); yield return Frames(3);
            Check(!placement.LineActive, "a right click drops the line");
            Check(placement.CurrentMode == PlacementController.Mode.Road, "and keeps the tool");
            yield return Release(V(15, 13));
            Check(RoadsIn(grid, 13, 12, 15) == 0 && Mathf.Abs(money - economy.Money) < 1f, "a dropped line builds nothing");
            yield return Hold(V(12, 14), V(13, 14), V(14, 14));
            yield return Press(Key.Escape);
            Check(!placement.LineActive && placement.CurrentMode == PlacementController.Mode.Road, "Esc drops the line and keeps the tool");
            yield return Release(V(14, 14));
            Check(RoadsIn(grid, 14, 12, 14) == 0, "the Esc line built nothing");
            yield return Press(Key.Escape);
            Check(placement.CurrentMode == PlacementController.Mode.None, "a second Esc leaves the tool");
            yield return ClickUi(Btn("Road"));
            yield return Frames(3);

            // --- a line that swings from x to z while held ---
            yield return Drag(V(18, 8), V(19, 8), V(20, 8), V(19, 10), V(19, 12));
            bool column = true;
            for (int y = 8; y <= 12; y++) column &= grid.GetRoadTier(V(18, y)) == street;
            Check(column, "the line swung to z: x 18, y 8..12");
            Check(!grid.IsRoad(V(19, 8)) && !grid.IsRoad(V(20, 8)), "the earlier x leg was not built");

            // --- a single click lays one cell ---
            yield return ClickAt(CellPos(V(12, 24)));
            yield return Frames(3);
            Check(grid.IsRoad(V(12, 24)) && !grid.IsRoad(V(13, 24)) && !grid.IsRoad(V(12, 25)), "a click lays one cell");

            // --- Highway over the street: the upgrade price, one-way along the drag; then a line westwards ---
            yield return ClickUi(Btn("Road_highway"));
            yield return Frames(3);
            money = economy.Money;
            yield return Hold(V(12, 10), V(13, 10), V(14, 10));
            Check(placement.CursorHint.Contains($"${3 * tiers.UpgradeCost(street, RoadTiers.Highway):N0}"), "the highway hint prices the upgrade: " + placement.CursorHint);
            yield return Release(V(14, 10));
            Check(AllRoad(grid, 10, 12, 14, RoadTiers.Highway) && AllRoad(grid, 10, 15, 16, street), "x 12..14 upgraded, 15..16 still streets");
            Check(Mathf.Abs(money - economy.Money - 3 * tiers.UpgradeCost(street, RoadTiers.Highway)) < 1f, $"the upgrade paid the difference ({money - economy.Money})");
            Check(grid.GetRoadDirection(V(12, 10)) == RoadLayout.East && grid.GetRoadDirection(V(14, 10)) == RoadLayout.East, "the upgraded highway points east");
            yield return Drag(V(17, 26), V(16, 26), V(15, 26), V(14, 26), V(13, 26));
            bool west = AllRoad(grid, 26, 13, 17, RoadTiers.Highway);
            for (int x = 13; x <= 17; x++) west &= grid.GetRoadDirection(V(x, 26)) == RoadLayout.West;
            Check(west, "a line dragged west is a westbound highway");
            ScreenCapture.CaptureScreenshot(shotPath.Replace(".png", "_highway.png"));
            yield return Seconds(1.5f);

            // --- Avenue lines east and west: the second lane on the chosen side (R), south by default ---
            yield return ClickUi(Btn("Road_avenue"));
            yield return Frames(3);
            money = economy.Money;
            yield return Hold(V(12, 18), V(14, 18), V(16, 18));
            Check(placement.CursorHint.Contains("×5") && placement.CursorHint.Contains($"${10 * tiers.Cost(RoadTiers.Avenue):N0}"), "the avenue hint prices both lanes: " + placement.CursorHint);
            yield return Release(V(16, 18));
            bool east = AllRoad(grid, 18, 12, 16, RoadTiers.Avenue) && AllRoad(grid, 17, 12, 16, RoadTiers.Avenue);
            for (int x = 12; x <= 16; x++) east &= grid.GetRoadPair(V(x, 18)) == RoadLayout.South;
            Check(east, "an avenue dragged east pairs each cell with the one south of it");
            Check(Mathf.Abs(money - economy.Money - 10 * tiers.Cost(RoadTiers.Avenue)) < 1f, $"ten lanes paid for ({money - economy.Money})");
            yield return Drag(V(16, 21), V(14, 21), V(12, 21));
            bool westPair = AllRoad(grid, 21, 12, 16, RoadTiers.Avenue) && AllRoad(grid, 20, 12, 16, RoadTiers.Avenue);
            for (int x = 12; x <= 16; x++) westPair &= grid.GetRoadPair(V(x, 21)) == RoadLayout.South;
            Check(westPair, "an avenue dragged west keeps the chosen side (south, square to the heading)");
            ScreenCapture.CaptureScreenshot(shotPath.Replace(".png", "_avenue.png"));
            yield return Seconds(1.5f);

            // --- not enough money: the preview says the shortfall, the release lays what it can ---
            yield return ClickUi(Btn("Road"));
            yield return Frames(3);
            float saved = economy.Money;
            int cost = tiers.Cost(street);
            economy.Restore(3 * cost + cost * 0.5f, economy.IncomePerDay, economy.ExpensePerDay, economy.TaxResidential, economy.TaxCommercial, economy.TaxIndustrial);
            Log("DEBUG money set to " + economy.Money);
            yield return Hold(V(12, 16), V(14, 16), V(17, 16));
            Check(placement.CursorHint.Contains("short") && !placement.CursorHintValid, "the hint says the money is short: " + placement.CursorHint);
            yield return Release(V(17, 16));
            Check(AllRoad(grid, 16, 12, 14, street) && RoadsIn(grid, 16, 15, 17) == 0, "three cells were laid, then the money ran out");
            economy.Restore(saved, economy.IncomePerDay, economy.ExpensePerDay, economy.TaxResidential, economy.TaxCommercial, economy.TaxIndustrial);

            // --- pipes: a line beside the street, then an erase line from a pipe ---
            yield return Press(Key.P);
            Check(placement.CurrentMode == PlacementController.Mode.Pipe, "P picks the pipe tool (pipes unlocked: " + game.PipesUnlocked + ")");
            money = economy.Money;
            yield return Hold(V(12, 11), V(14, 11), V(16, 11));
            Check(placement.CursorHint.Contains("×5"), "the pipe hint counts the run: " + placement.CursorHint);
            yield return Release(V(16, 11));
            bool pipes = true;
            for (int x = 12; x <= 16; x++) pipes &= grid.IsPipe(V(x, 11));
            Check(pipes, "a pipe line along y 11");
            Check(Mathf.Abs(money - economy.Money - 5 * game.Balance.PipeCost) < 1f, $"five pipes paid for ({money - economy.Money})");
            yield return Hold(V(12, 11), V(13, 11), V(14, 11));
            Check(placement.CursorHint.Contains("Remove 3 pipes"), "a line from a pipe removes: " + placement.CursorHint);
            yield return Release(V(14, 11));
            Check(!grid.IsPipe(V(12, 11)) && !grid.IsPipe(V(14, 11)) && grid.IsPipe(V(15, 11)) && grid.IsPipe(V(16, 11)), "the erase line removed x 12..14 only");
            placement.ClearMode();
            yield return Frames(3);

            // --- demolish drag: grown blocks, roads and two placed buildings, one confirmation on release ---
            for (int x = 13; x <= 15; x++) { grid.SetZone(V(x, 14), ZoneType.Residential); grid.SetBuildingLevel(V(x, 14), 1); }
            BuildingDefinition tower = game.Buildings.GetById("water_tower");
            BuildingDefinition police = game.Buildings.GetById("police_station");
            bool placed = tower != null && police != null && placement.RestoreBuilding(tower, V(18, 14), 0) && placement.RestoreBuilding(police, V(18, 17), 0);
            Log($"DEBUG three grown homes on y 14, a {tower?.DisplayName} at (18,14) and a {police?.DisplayName} at (18,17) placed for free: {placed}");
            int buildings = placement.PlacedBuildings.Count();
            GameSettings.ConfirmDemolish = true;
            yield return ClickUi(Btn("Demolish"));
            yield return Frames(3);
            Check(placement.CurrentMode == PlacementController.Mode.Demolish, "the Demolish tool is active");
            yield return Hold(V(13, 14), V(14, 14), V(15, 14), V(14, 15), V(14, 16), V(14, 17), V(15, 16), V(16, 16), V(17, 15),
                V(18, 14), V(18, 15), V(18, 16), V(18, 17));
            bool grownGone = true;
            for (int x = 13; x <= 15; x++) grownGone &= grid.GetBuildingLevel(V(x, 14)) == 0 && grid.GetZone(V(x, 14)) == ZoneType.Residential;
            Check(grownGone, "holding cleared the grown blocks (the zone stays)");
            Check(!grid.IsRoad(V(14, 16)) && grid.IsRoad(V(13, 16)), "and the street cell it passed, not its neighbour");
            Check(!grid.IsRoad(V(14, 17)) && !grid.IsRoad(V(14, 18)) && grid.IsRoad(V(13, 17)), "an avenue lane it passed took its partner");
            Check(placement.PlacedBuildings.Count() == buildings && placement.PendingDemolishCount == 2, $"the two buildings wait for the release ({placement.PendingDemolishCount}): " + placement.CursorHint);
            yield return Release(V(18, 17));
            yield return Frames(4);
            bool asked = flow.Confirm.IsOpen && GameObject.Find("UI").GetComponentsInChildren<TMP_Text>().Any(x => x.text.Contains("Demolish 2 buildings?"));
            Check(asked, "the release asks once: Demolish 2 buildings?");
            ScreenCapture.CaptureScreenshot(shotPath.Replace(".png", "_demolish.png"));
            yield return Seconds(1.5f);
            yield return ClickUi(Btn("DemolishButton"));
            yield return Frames(6);
            Check(!flow.Confirm.IsOpen && placement.PlacedBuildings.Count() == buildings - 2, "both buildings went with one answer");
            GameSettings.ResetGameplay();
            yield return ClickAt(CellPos(V(13, 16)));
            yield return Frames(4);
            Check(!grid.IsRoad(V(13, 16)) && grid.IsRoad(V(12, 16)), "a click still demolishes one cell");
            placement.ClearMode();
            yield return Frames(3);
        }
        finally
        {
            GameSettings.ResetGameplay();
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
