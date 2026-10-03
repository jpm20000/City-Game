// Template for a UI-only play-through driven by a virtual mouse and keyboard (City Game).
// Paste into a Unity_RunCommand while in Play mode (after Application.runInBackground = true),
// edit the scenario in Run(), and watch the log file. This is the M11g ages play-through:
// New (Medieval, 64x64) -> roads + zones by input -> research by click / shift-click ->
// Monastery from the toolbar -> Advance x2 -> Keep a block -> Save / play on / Load -> New (Industrial).
// Every action goes through input; only the Check() lines read game state.
// Back up city.json before running (the scenario saves) and restore it afterwards.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

internal static class Driver
{
    // Point this at the session scratchpad.
    public const string LogPath = "C:/path/to/scratchpad/playthrough.log";
    static Mouse s_Mouse;
    static Keyboard s_Keyboard;
    static Mouse s_RealMouse;
    static Keyboard s_RealKeyboard;
    static GameManager gm;
    static PlacementController pc;
    static readonly Dictionary<Key, Vector2> s_Pan = new Dictionary<Key, Vector2>();
    static int s_Fails;

    public static void Log(string line) { File.AppendAllText(LogPath, $"[{Time.frameCount}] {line}\n"); }
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    // Path GameObject.Find can return objects under inactive parents: check activeInHierarchy.
    static bool Shown(string path) { var g = GameObject.Find(path); return g != null && g.activeInHierarchy; }

    // ---------- input primitives ----------
    static void MouseTo(Vector2 pos, bool down, float scroll = 0f)
    {
        var state = new MouseState { position = pos, scroll = new Vector2(0, scroll) };
        if (down) state = state.WithButton(MouseButton.Left);
        InputSystem.QueueStateEvent(s_Mouse, state);
    }

    static void Keys(params Key[] keys) { InputSystem.QueueStateEvent(s_Keyboard, new KeyboardState(keys)); }
    static IEnumerator Press(Key key) { Keys(key); yield return Frames(2); Keys(); yield return Frames(2); }

    static IEnumerator ClickAt(Vector2 pos, bool shift = false)
    {
        if (shift) { Keys(Key.LeftShift); yield return Frames(2); }
        MouseTo(pos, false); yield return Frames(2);
        MouseTo(pos, true); yield return Frames(2);
        MouseTo(pos, false); yield return Frames(2);
        if (shift) { Keys(); yield return Frames(2); }
    }

    static Vector2 UiPos(GameObject go)
    {
        var rt = (RectTransform)go.transform;
        return RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
    }

    static IEnumerator ClickUi(string path, bool shift = false)
    {
        var go = GameObject.Find(path);
        if (go == null || !go.activeInHierarchy) { Log("MISSING ui " + path); yield break; }
        yield return ClickAt(UiPos(go), shift);
    }

    static IEnumerator ClickUi(GameObject go, bool shift = false)
    {
        if (go == null || !go.activeInHierarchy) { Log("MISSING ui object"); yield break; }
        yield return ClickAt(UiPos(go), shift);
    }

    static Vector2 CellPos(Vector2Int c)
    {
        Vector3 p = Camera.main.WorldToScreenPoint(new Vector3(c.x + 0.5f, 0f, c.y + 0.5f));
        return new Vector2(p.x, p.y);
    }

    // ---------- keeping map targets clear of the UI ----------
    // Map clicks are ignored over UI (HUD on top, toolbar at the bottom, panels on the sides).
    static Rect Safe => Rect.MinMaxRect(Screen.width * 0.04f, Screen.height * 0.16f, Screen.width * 0.70f, Screen.height * 0.88f);

    // How each pan key moves the map on screen per frame held (don't assume directions).
    static IEnumerator MeasurePan()
    {
        foreach (var key in new[] { Key.W, Key.A, Key.S, Key.D })
        {
            Vector2 before = CellPos(new Vector2Int(gm.MapSize.x / 2, gm.MapSize.y / 2));
            Keys(key); yield return Frames(8); Keys(); yield return Frames(20);
            s_Pan[key] = (CellPos(new Vector2Int(gm.MapSize.x / 2, gm.MapSize.y / 2)) - before) / 8f;
        }
    }

    static IEnumerator EnsureVisible(Vector2Int cell)
    {
        for (int i = 0; i < 60; i++)
        {
            Vector2 p = CellPos(cell);
            Rect safe = Safe;
            if (safe.Contains(p)) yield break;
            Vector2 target = new Vector2(Mathf.Clamp(p.x, safe.xMin + 40, safe.xMax - 40), Mathf.Clamp(p.y, safe.yMin + 40, safe.yMax - 40));
            Vector2 want = target - p;
            Key best = Key.W; float bestScore = 0f;
            foreach (var pair in s_Pan)
            {
                float score = Vector2.Dot(pair.Value.normalized, want.normalized);
                if (score > bestScore) { bestScore = score; best = pair.Key; }
            }
            int frames = Mathf.Clamp(Mathf.CeilToInt(want.magnitude / Mathf.Max(1f, s_Pan[best].magnitude)), 1, 6);
            Keys(best); yield return Frames(frames); Keys(); yield return Frames(6);
        }
        Log($"WARN cell {cell} still off the safe area at {CellPos(cell)}");
    }

    static IEnumerator ClickCell(int x, int y)
    {
        yield return EnsureVisible(new Vector2Int(x, y));
        yield return ClickAt(CellPos(new Vector2Int(x, y)));
    }

    // Zoning paints while LMB is held: press, move through the cells, release.
    static IEnumerator Drag(List<Vector2Int> cells)
    {
        yield return EnsureVisible(cells[cells.Count - 1]);
        yield return EnsureVisible(cells[0]);
        MouseTo(CellPos(cells[0]), false); yield return Frames(2);
        MouseTo(CellPos(cells[0]), true); yield return Frames(2);
        foreach (var c in cells) { MouseTo(CellPos(c), true); yield return Frames(2); }
        MouseTo(CellPos(cells[cells.Count - 1]), false); yield return Frames(2);
    }

    static List<Vector2Int> Line(int x0, int y0, int x1, int y1)
    {
        var list = new List<Vector2Int>();
        int dx = Math.Sign(x1 - x0), dy = Math.Sign(y1 - y0);
        for (int x = x0, y = y0; ; x += dx, y += dy) { list.Add(new Vector2Int(x, y)); if (x == x1 && y == y1) break; }
        return list;
    }

    // ---------- game-specific helpers ----------
    static GameObject TechButton(string id)
    {
        foreach (var b in UnityEngine.Object.FindObjectsByType<ToolButton>(FindObjectsInactive.Exclude))
            if (b.name == "Tech_" + id) return b.gameObject;
        return null;
    }

    // Debug panel (F1) button "Skip 30 days": UI-only fast-forward.
    static GameObject SkipButton()
    {
        foreach (var t in GameObject.Find("UI/DebugPanel").GetComponentsInChildren<TMP_Text>(true))
            if (t.text.Contains("Skip")) return t.GetComponentInParent<Button>().gameObject;
        return null;
    }

    static string State()
    {
        var tech = gm.Simulation.Tech;
        return $"age={tech.CurrentAgeDefinition.Id} {gm.Clock.Day}/{gm.Clock.Month}/{gm.Clock.Year} pop={gm.Population.Population} ${gm.Economy.Money:N0} rp/day={gm.Simulation.ResearchIncome():F1} active={tech.Active?.Id ?? "-"} progress={tech.Progress:F1} researched={tech.ResearchedCount} queue={tech.Queue.Count} ready={tech.GetAdvanceStatus(gm.Population.Population).Ready}";
    }

    static IEnumerator Skip() { yield return ClickUi(SkipButton()); Log("skip 30 -> " + State()); }
    static void Check(bool ok, string what) { if (!ok) s_Fails++; Log((ok ? "PASS " : "FAIL ") + what); }
    static IEnumerator OpenResearch() { yield return ClickUi("UI/HUD/Budget/ResearchButton"); yield return Frames(3); }

    static IEnumerator ResearchSomething(TechSystem tech)
    {
        yield return OpenResearch();
        foreach (var t in tech.Techs.Techs) if (tech.CanResearch(t)) { yield return ClickUi(TechButton(t.Id)); break; }
        yield return OpenResearch();
    }

    static IEnumerator PlaceWhenShown(string button, int x, int y, string label)
    {
        if (!Shown(button)) yield break;
        float before = gm.Simulation.Modifiers.ResearchPerDay;
        yield return ClickUi(button);
        yield return ClickCell(x, y);
        yield return Press(Key.Escape);
        Check(gm.Simulation.Modifiers.ResearchPerDay > before, $"{label} placed from the toolbar");
    }

    // ---------- scenario ----------
    public static IEnumerator Run()
    {
        gm = UnityEngine.Object.FindAnyObjectByType<GameManager>();
        pc = UnityEngine.Object.FindAnyObjectByType<PlacementController>();
        s_RealMouse = Mouse.current;
        s_RealKeyboard = Keyboard.current;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        if (s_RealMouse != null) InputSystem.DisableDevice(s_RealMouse);
        if (s_RealKeyboard != null) InputSystem.DisableDevice(s_RealKeyboard);
        s_Mouse = InputSystem.AddDevice<Mouse>("VirtualMouse");
        s_Keyboard = InputSystem.AddDevice<Keyboard>("VirtualKeyboard");
        s_Mouse.MakeCurrent();
        s_Keyboard.MakeCurrent();
        try
        {
            Log($"screen {Screen.width}x{Screen.height}");
            yield return Frames(5);
            var tech = gm.Simulation.Tech;

            // New city through the dialog, then pause (fast-forward only with Skip).
            yield return ClickUi("UI/HUD/Game/NewButton");
            yield return ClickUi("UI/NewCityDialog/Blocker/Panel/AgeRow/Age0");
            yield return ClickUi("UI/NewCityDialog/Blocker/Panel/SizeRow/Size1");
            yield return ClickUi("UI/NewCityDialog/Blocker/Panel/Actions/CreateButton");
            yield return Frames(5);
            yield return ClickUi("UI/HUD/Time/Speed/Pause");
            Check(tech.CurrentAge == 0 && gm.MapSize.x == 64, "New City dialog: Medieval 64x64 -> " + State());

            // Zoom out (scroll wheel) and learn the pan keys.
            Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            for (int i = 0; i < 12; i++) { MouseTo(centre, false, -120f); yield return Frames(2); }
            MouseTo(centre, false); yield return Frames(20);
            yield return MeasurePan();

            // Roads (one click per cell; must reach the map edge) and zones (drag).
            yield return ClickUi("UI/Toolbar/Transport/Row/Road");
            foreach (var c in Line(0, 32, 44, 32)) yield return ClickCell(c.x, c.y);
            foreach (var c in Line(32, 20, 32, 44)) if (c.y != 32) yield return ClickCell(c.x, c.y);
            Check(gm.Roads.IsConnectedToEntry(new Vector2Int(32, 44)), $"roads clicked: {gm.Grid.CountRoads()} cells, connected to the edge");
            yield return ClickUi("UI/Toolbar/Zoning/Row/Residential");
            yield return Drag(Line(0, 33, 31, 33));
            yield return Drag(Line(31, 34, 31, 44));
            yield return Drag(Line(33, 33, 33, 44));
            yield return Drag(Line(34, 33, 44, 33));
            yield return ClickUi("UI/Toolbar/Zoning/Row/Commercial");
            yield return Drag(Line(0, 31, 20, 31));
            yield return Drag(Line(31, 20, 31, 30));
            yield return ClickUi("UI/Toolbar/Zoning/Row/Industrial");
            yield return Drag(Line(21, 31, 30, 31));
            yield return Drag(Line(33, 20, 33, 30));
            yield return Drag(Line(34, 31, 44, 31));
            yield return Press(Key.Escape);

            // Research: click = research now, shift-click = queue.
            yield return OpenResearch();
            yield return ClickUi(TechButton("masonry"));
            yield return ClickUi(TechButton("monasticism"), shift: true);
            yield return ClickUi(TechButton("commons"), shift: true);
            yield return ClickUi(TechButton("crop_rotation"), shift: true);
            yield return ClickUi(TechButton("smithing"), shift: true);
            Check(tech.Active?.Id == "masonry" && tech.Queue.Count == 4, "research planned -> " + State());
            yield return OpenResearch();

            yield return Press(Key.F1);   // debug panel for Skip 30 days
            yield return Frames(3);

            bool monastery = false;
            for (int i = 0; i < 12 && !tech.GetAdvanceStatus(gm.Population.Population).Ready; i++)
            {
                yield return Skip();
                if (!monastery && Shown("UI/Toolbar/Buildings/Row/Build_monastery"))
                {
                    yield return PlaceWhenShown("UI/Toolbar/Buildings/Row/Build_monastery", 2, 29, "Monastery");
                    monastery = true;
                }
                if (tech.Active == null) yield return ResearchSomething(tech);
            }
            yield return OpenResearch();
            yield return ClickUi("UI/TechPanel/Panel/AdvanceButton");
            yield return OpenResearch();
            for (int i = 0; i < 6 && tech.CurrentAge == 0; i++) yield return Skip();
            Check(tech.CurrentAge == 1, "advanced to the Renaissance -> " + State());

            // Keep a block historic, then watch the rest redevelop.
            Vector2Int kept = new Vector2Int(-1, -1);
            for (int y = 0; y < gm.MapSize.y; y++) for (int x = 0; x < gm.MapSize.x; x++)
            {
                var cell = new Vector2Int(x, y);
                if (gm.Simulation.Growth.IsOutdated(cell) && gm.Grid.GetZone(cell) == ZoneType.Residential) kept = cell;
            }
            yield return ClickCell(kept.x, kept.y);
            yield return ClickUi("UI/SelectionPanel/Panel/KeepButton");
            yield return Press(Key.Escape);
            yield return Skip();
            yield return Skip();
            Check(gm.Grid.IsHistoric(kept) && gm.Grid.GetBuiltAge(kept) == 0, "kept block stayed in its age");

            // Save mid-research, play on, load.
            if (tech.Active == null) yield return ResearchSomething(tech);
            yield return ClickUi("UI/HUD/Game/SaveButton");
            string savedActive = tech.Active?.Id; float savedProgress = tech.Progress;
            yield return Skip();
            yield return ClickUi("UI/HUD/Game/LoadButton");
            yield return Frames(5);
            Check(tech.Active?.Id == savedActive && Mathf.Approximately(tech.Progress, savedProgress), "Load restored research -> " + State());

            // New city in the Industrial age.
            yield return ClickUi("UI/HUD/Game/NewButton");
            yield return ClickUi("UI/NewCityDialog/Blocker/Panel/AgeRow/Age2");
            yield return ClickUi("UI/NewCityDialog/Blocker/Panel/Actions/CreateButton");
            yield return Frames(5);
            Check(tech.CurrentAge == 2 && gm.PowerUnlocked && Shown("UI/HUD/Power"), "New Industrial city -> " + State());
            Log($"DONE, {s_Fails} failed checks");
        }
        finally
        {
            InputSystem.RemoveDevice(s_Mouse);
            InputSystem.RemoveDevice(s_Keyboard);
            if (s_RealMouse != null) InputSystem.EnableDevice(s_RealMouse);
            if (s_RealKeyboard != null) InputSystem.EnableDevice(s_RealKeyboard);
            Log("devices restored");
        }
    }
}

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (!EditorApplication.isPlaying) { result.Log("not playing"); return; }
        Application.runInBackground = true;
        File.WriteAllText(Driver.LogPath, "");
        UnityEngine.Object.FindAnyObjectByType<GameManager>().StartCoroutine(Driver.Run());
        result.Log("play-through started");
    }
}
