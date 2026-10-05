using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// M23e play-through of zone density, UI-only (virtual mouse): a Medieval city offers only Low (the Medium and High
// buttons are hidden, the brush paints Low); a Modern city offers all three; High homes, Low shops are dragged out,
// the hint and the selection panel name the density, the Density view lights up from the Views flyout, repainting a
// built High block as Low asks first and keeps its level, and a save and load keep every density. Start it from a
// one-line RunCommand in Play mode (set Application.runInBackground = true first):
//   FindAnyObjectByType<GameManager>().StartCoroutine(DensityDriver.Run(logPath, savesDir, shotPath));
public static class DensityDriver
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
        foreach (Button b in GameObject.Find("UI").GetComponentsInChildren<Button>(true))
        {
            if (b.name == name) return b.gameObject;
        }
        return null;
    }

    private static bool Shown(string name) => Btn(name) != null && Btn(name).activeInHierarchy;

    private static GameObject BtnWithText(string text)
    {
        foreach (Button b in GameObject.Find("UI").GetComponentsInChildren<Button>())
        {
            TMP_Text label = b.GetComponentInChildren<TMP_Text>();
            if (b.gameObject.activeInHierarchy && label != null && label.text.Contains(text)) return b.gameObject;
        }
        return null;
    }

    // Every shown text under the selection panel.
    private static string PanelText()
    {
        Transform panel = GameObject.Find("UI/SelectionPanel")?.transform;
        if (panel == null) return string.Empty;
        return string.Join("\n", panel.GetComponentsInChildren<TMP_Text>(false).Select(t => t.text));
    }

    private static Vector2 CellPos(Vector2Int c)
    {
        Vector3 p = Camera.main.WorldToScreenPoint(new Vector3(c.x + 0.5f, 0f, c.y + 0.5f));
        return new Vector2(p.x, p.y);
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
            var overlay = Object.FindAnyObjectByType<InfoOverlay>();
            yield return Frames(10);

            // --- Medieval: only Low is offered ---
            yield return NewCity(flow, "Medieval");
            Check(flow.State == GameFlowState.Playing, "a Medieval city starts: " + flow.State);
            game.Clock.SetSpeed(GameSpeed.Paused);
            yield return ZoomIn();
            GridData grid = game.Grid;
            Check(Shown("Density_Low"), "Low is offered in the Medieval age");
            Check(!Shown("Density_Medium"), "Medium is hidden in the Medieval age");
            Check(!Shown("Density_High"), "High is hidden in the Medieval age");
            yield return ClickUi(BtnWithText("Residential"));
            yield return Frames(3);
            Check(placement.DensityBrush == Density.Low, "the brush paints Low: " + placement.DensityBrush);
            MouseTo(CellPos(V(14, 16)), false);
            yield return Frames(4);
            Check(placement.CursorHint.Contains("Low density"), "the hint names the density: " + placement.CursorHint);
            yield return Drag(V(14, 16), V(15, 16), V(16, 16), V(17, 16));
            bool allLow = true;
            for (int x = 14; x <= 17; x++) allLow &= grid.GetZone(V(x, 16)) == ZoneType.Residential && grid.GetDensity(V(x, 16)) == Density.Low;
            Check(allLow, "the dragged homes are Low density");
            placement.ClearMode();

            // --- Modern: all three ---
            yield return NewCity(flow, "Modern");
            Check(flow.State == GameFlowState.Playing, "a Modern city starts: " + flow.State);
            game.Clock.SetSpeed(GameSpeed.Paused);
            yield return ZoomIn();
            grid = game.Grid;
            Check(Shown("Density_Low") && Shown("Density_Medium") && Shown("Density_High"), "Low, Medium and High are offered in the Modern age");

            yield return ClickUi(BtnWithText("Residential"));
            yield return ClickUi(Btn("Density_High"));
            yield return Frames(3);
            Check(placement.CurrentMode == PlacementController.Mode.Zone && placement.DensityBrush == Density.High, "High is the brush");
            MouseTo(CellPos(V(14, 14)), false);
            yield return Frames(4);
            Check(placement.CursorHint.Contains("High density"), "the hint names High: " + placement.CursorHint);
            yield return Drag(V(13, 14), V(14, 14), V(15, 14), V(16, 14), V(17, 14));
            bool allHigh = true;
            for (int x = 13; x <= 17; x++) allHigh &= grid.GetZone(V(x, 14)) == ZoneType.Residential && grid.GetDensity(V(x, 14)) == Density.High;
            Check(allHigh, "the dragged homes are High density");

            yield return ClickUi(BtnWithText("Commercial"));
            yield return ClickUi(Btn("Density_Low"));
            yield return Frames(3);
            Check(placement.ZoneBrush == ZoneType.Commercial && placement.DensityBrush == Density.Low, "Commercial with the Low brush");
            yield return Drag(V(13, 18), V(14, 18), V(15, 18), V(16, 18));
            bool lowShops = true;
            for (int x = 13; x <= 16; x++) lowShops &= grid.GetZone(V(x, 18)) == ZoneType.Commercial && grid.GetDensity(V(x, 18)) == Density.Low;
            Check(lowShops, "the dragged shops are Low density");

            yield return ClickUi(Btn("Density_Medium"));
            yield return ClickUi(BtnWithText("Industrial"));
            yield return Drag(V(13, 21), V(14, 21));
            Check(grid.GetZone(V(13, 21)) == ZoneType.Industrial && grid.GetDensity(V(13, 21)) == Density.Medium, "Medium is the plain zone (no density stored)");
            ScreenCapture.CaptureScreenshot(shotPath);
            yield return Seconds(1.5f);

            // --- the selection panel and the Density view ---
            placement.ClearMode();
            yield return Frames(3);
            yield return ClickAt(CellPos(V(15, 14)));
            yield return Frames(6);
            string panel = PanelText();
            Check(panel.Contains("Density") && panel.Contains("High"), "the selection panel names the density");
            Check(panel.Contains("needs power and water to start") || panel.Contains("High density needs it from the start"), "and says High needs its utilities");

            yield return ClickUi(Btn("ViewsButton"));
            yield return Frames(4);
            Check(Shown("DensityView"), "the Views flyout lists Density");
            yield return ClickUi(Btn("DensityView"));
            yield return Frames(6);
            Check(overlay.Shown == InfoOverlay.View.Density, "the Density view is on: " + overlay.Shown);
            ScreenCapture.CaptureScreenshot(shotPath.Replace(".png", "_view.png"));
            yield return Seconds(1.5f);
            yield return ClickUi(Btn("ViewsButton"));
            yield return Frames(4);
            yield return ClickUi(Btn("OffView"));
            yield return Frames(4);
            Check(overlay.Shown == InfoOverlay.View.Off, "Off turns it off");

            // --- repainting a built block asks first and keeps its level ---
            Vector2Int home = V(15, 14);
            grid.SetBuiltAge(home, 3);                       // DEBUG: a grown High home stands here
            grid.SetBuildingLevel(home, 3);
            yield return Frames(4);
            int before = game.Simulation.Capacity.CapacityOf(grid, home);
            placement.ClearMode();
            yield return ClickUi(BtnWithText("Residential"));
            yield return ClickUi(Btn("Density_Low"));
            yield return Frames(3);
            Check(placement.CurrentMode == PlacementController.Mode.Zone && placement.ZoneBrush == ZoneType.Residential && placement.DensityBrush == Density.Low, "Residential with the Low brush");
            yield return Drag(V(15, 14));
            Check(flow.Confirm.IsOpen, "repainting a built High home as Low asks first");
            Check(grid.GetDensity(home) == Density.High, "nothing changed before the answer");
            yield return ClickUi(BtnWithText("Change density"));
            yield return Frames(6);
            Check(grid.GetDensity(home) == Density.Low && grid.GetBuildingLevel(home) == 3, $"the home is Low and still level 3: {grid.GetDensity(home)} L{grid.GetBuildingLevel(home)}");
            int after = game.Simulation.Capacity.CapacityOf(grid, home);
            Check(after < before, $"its capacity was re-fitted ({before} -> {after})");

            // --- save and load keep every density ---
            placement.ClearMode();
            yield return Frames(3);
            Check(saves.Save(), "the city saves");
            byte[] densities = grid.ExportDensities();
            Check(densities.Any(d => d == (byte)Density.High) && densities.Any(d => d == (byte)Density.Low), "the city holds both Low and High");
            grid.SetDensity(V(14, 14), Density.Low);
            yield return Frames(4);
            Check(saves.Load(), "the city loads");
            yield return Frames(20);
            grid = game.Grid;
            Check(grid.ExportDensities().SequenceEqual(densities), "the densities came back");
            Check(grid.GetDensity(V(14, 14)) == Density.High && grid.GetDensity(home) == Density.Low, "the High row and the Low home are as saved");
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
