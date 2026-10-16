using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// M30 play-through of the low-density look, UI-only (virtual mouse): a Medieval city lays a street, zones Low homes,
// shops and industry beside it, the Low brush's tooltip says industry is farmland, the blocks grow (DEBUG: levels set
// directly) into their own Low art (cottages, markets, farms: the prefab under each cell is a *_Low one), the
// selection panel calls a Low factory a Farm that pollutes little, and it emits LowDensityPollution x what a Medium
// factory of the same level would; a Modern city repeats the farm check with the Modern art. Start it from a one-line
// RunCommand in Play mode (set Application.runInBackground = true first):
//   FindAnyObjectByType<GameManager>().StartCoroutine(LowDensityDriver.Run(logPath, shotPath));
public static class LowDensityDriver
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

    private static GameObject Btn(string name)
    {
        foreach (Button b in GameObject.Find("UI").GetComponentsInChildren<Button>(true))
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

    private static string PanelText()
    {
        Transform panel = GameObject.Find("UI/SelectionPanel")?.transform;
        if (panel == null) return string.Empty;
        return string.Join("\n", panel.GetComponentsInChildren<TMP_Text>(false).Select(t => t.text));
    }

    private static bool AnyShownText(string text)
    {
        return Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Any(t => t.isActiveAndEnabled && t.text.Contains(text));
    }

    private static Vector2 CellPos(Vector2Int c)
    {
        Vector3 p = Camera.main.WorldToScreenPoint(new Vector3(c.x + 0.5f, 0f, c.y + 0.5f));
        return new Vector2(p.x, p.y);
    }

    // The prefab drawn on a cell: what a camera ray through its centre hits on the Buildings layer.
    private static string PrefabAt(Vector2Int c)
    {
        Ray ray = Camera.main.ScreenPointToRay(CellPos(c));
        return Physics.Raycast(ray, out RaycastHit hit, 500f, 1 << 9) ? hit.collider.name : "(nothing)";
    }

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

    private static IEnumerator NewCity(GameFlow flow, string age)
    {
        flow.Menu.RequestNew();
        yield return Frames(10);
        if (flow.Confirm.IsOpen) { yield return ClickUi(BtnWithText("Don't save")); yield return Frames(10); }
        yield return ClickUi(BtnWithText(age));
        yield return ClickUi(BtnWithText("32"));
        yield return ClickUi(Btn("CreateButton"));
        yield return Frames(20);
        if (flow.Confirm.IsOpen) { yield return ClickUi(BtnWithText("Don't save")); yield return Frames(20); }
    }

    private static IEnumerator ZoomIn()
    {
        for (int i = 0; i < 5; i++) { MouseTo(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false, 120f); yield return Frames(3); }
        MouseTo(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false, 0f);
        yield return Frames(5);
    }

    // A street along y = 14 from x 13 to 18, Low homes / shops / industry two cells each on y = 15 (by the zone buttons).
    private static IEnumerator LayOut(PlacementController placement, GridData grid)
    {
        yield return ClickUi(Btn("Road"));
        yield return Drag(V(13, 14), V(15, 14), V(18, 14));
        Check(Enumerable.Range(13, 6).All(x => grid.IsRoad(V(x, 14))), "the street is laid by a drag");
        placement.ClearMode();
        string[] zones = { "Residential", "Commercial", "Industrial" };
        for (int z = 0; z < 3; z++)
        {
            yield return ClickUi(BtnWithText(zones[z]));
            yield return ClickUi(Btn("Density_Low"));
            yield return Frames(3);
            yield return Drag(V(13 + z * 2, 15), V(14 + z * 2, 15));
        }
        placement.ClearMode();
        bool zoned = true;
        for (int x = 13; x <= 18; x++) zoned &= grid.GetDensity(V(x, 15)) == Density.Low && grid.GetZone(V(x, 15)) == (ZoneType)(1 + (x - 13) / 2);
        Check(zoned, "homes, shops and industry are zoned Low beside it");
    }

    public static IEnumerator Run(string logPath, string shotPath)
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
            var game = Object.FindAnyObjectByType<GameManager>();
            var placement = Object.FindAnyObjectByType<PlacementController>();
            yield return Frames(10);

            // --- Medieval ---
            yield return NewCity(flow, "Medieval");
            Check(flow.State == GameFlowState.Playing, "a Medieval city starts: " + flow.State);
            game.Clock.SetSpeed(GameSpeed.Paused);
            yield return ZoomIn();
            GridData grid = game.Grid;

            // the Low brush's tooltip names farmland
            yield return ClickUi(BtnWithText("Industrial"));
            MouseTo(UiPos(Btn("Density_Low")), false);
            yield return Seconds(1.5f);
            Check(AnyShownText("industry is farmland"), "hovering the Low brush says industry is farmland");
            placement.ClearMode();
            MouseTo(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false);
            yield return Frames(4);

            yield return LayOut(placement, grid);

            // DEBUG: the blocks grow (levels set directly; growth itself is the sim's, tested in EditMode)
            for (int x = 13; x <= 18; x++) grid.SetBuildingLevel(V(x, 15), (byte)(1 + (x - 13) % 2));
            yield return Seconds(1.2f);                     // the pop animation
            string[] expect = { "Medieval_R1", "Medieval_R2", "Medieval_C1", "Medieval_C2", "Medieval_I1", "Medieval_I2" };
            for (int i = 0; i < 6; i++)
            {
                string drawn = PrefabAt(V(13 + i, 15));
                Check(drawn.StartsWith(expect[i]) && drawn.Contains("_Low"), $"({13 + i}, 15) draws its own Low art: {drawn}");
            }
            ScreenCapture.CaptureScreenshot(shotPath);
            yield return Seconds(1.5f);

            // the selection panel calls the factory a Farm
            Vector2Int farm = V(18, 15);
            yield return ClickAt(CellPos(farm));
            yield return Frames(6);
            string panel = PanelText();
            Check(panel.Contains("Farm"), "the selection panel's title is Farm");
            Check(panel.Contains("farmland"), "its density line says farmland");
            Check(panel.Contains("a farm pollutes little"), "its pollution line says a farm pollutes little");
            yield return ClickAt(CellPos(V(13, 15)));
            yield return Frames(6);
            Check(PanelText().Contains("Residential building"), "a Low home keeps its old title");

            // a farm emits LowDensityPollution x a Medium factory of the same level
            float farmEmission = game.Simulation.Pollution.EmissionOf(farm);
            int farmJobs = game.Simulation.Capacity.CapacityOf(grid, farm);
            grid.SetDensity(farm, Density.Medium);                                   // DEBUG: the same block at Medium
            float factoryEmission = game.Simulation.Pollution.EmissionOf(farm);
            int factoryJobs = game.Simulation.Capacity.CapacityOf(grid, farm);
            grid.SetDensity(farm, Density.Low);
            float expected = factoryEmission * farmJobs / Mathf.Max(1, factoryJobs) * game.Balance.LowDensityPollution;
            Check(farmEmission > 0f && Mathf.Abs(farmEmission - expected) < 1e-3f,
                $"the farm emits {farmEmission:0.###} = {game.Balance.LowDensityPollution} x its capacity's share of a factory's {factoryEmission:0.###}");

            // --- Modern ---
            yield return NewCity(flow, "Modern");
            Check(flow.State == GameFlowState.Playing, "a Modern city starts: " + flow.State);
            game.Clock.SetSpeed(GameSpeed.Paused);
            yield return ZoomIn();
            grid = game.Grid;
            yield return LayOut(placement, grid);
            for (int x = 13; x <= 18; x++) { grid.SetBuiltAge(V(x, 15), 3); grid.SetBuildingLevel(V(x, 15), (byte)(2 + (x - 13) % 2)); }    // DEBUG
            yield return Seconds(1.2f);
            string[] expectModern = { "Modern_R2", "Modern_R3", "Modern_C2", "Modern_C3", "Modern_I2", "Modern_I3" };
            for (int i = 0; i < 6; i++)
            {
                string drawn = PrefabAt(V(13 + i, 15));
                Check(drawn.StartsWith(expectModern[i]) && drawn.Contains("_Low"), $"Modern ({13 + i}, 15) draws its own Low art: {drawn}");
            }
            ScreenCapture.CaptureScreenshot(shotPath.Replace(".png", "_modern.png"));
            yield return Seconds(1.5f);
            yield return ClickAt(CellPos(V(18, 15)));
            yield return Frames(6);
            Check(PanelText().Contains("Farm"), "a Modern Low factory is a Farm too");
            placement.ClearMode();
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
