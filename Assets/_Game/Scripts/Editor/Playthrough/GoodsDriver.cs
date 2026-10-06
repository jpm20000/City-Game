using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// M24e play-through of goods, UI-only (virtual mouse): an Industrial city shows the G meter and the Goods view,
// the selection panel says what a factory makes, a shop sells and a home buys, the budget ledger lists imports; with the
// factories switched off in memory (DEBUG: BalanceConfig through SerializedObject, restored at the end) the
// supply falls, a home held at level 2 says the city is short of goods, the happiness tooltip names it and the G
// meter turns red; with output back the home is released; with a surplus the ledger lists exports, and a save
// and load keep the stock. Setup shortcuts (seeded city, skipped days) are labelled DEBUG. Start it from a one-line
// RunCommand in Play mode (set Application.runInBackground = true first):
//   FindAnyObjectByType<GameManager>().StartCoroutine(GoodsDriver.Run(logPath, savesDir, shotPath));
public static class GoodsDriver
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

    private static bool Shown(string name) => Btn(name) != null && Btn(name).activeInHierarchy;

    private static GameObject BtnWithText(string text, GameObject root = null)
    {
        foreach (Button b in (root != null ? root : GameObject.Find("UI")).GetComponentsInChildren<Button>())
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

    private static string TextContaining(string needle)
    {
        foreach (TMP_Text t in GameObject.Find("UI").GetComponentsInChildren<TMP_Text>(false))
        {
            if (t.text.Contains(needle)) return t.text;
        }
        return null;
    }

    private static Vector2Int V(int x, int y) => new Vector2Int(x, y);

    private static Vector2 CellPos(Vector2Int c)
    {
        Vector3 p = Camera.main.WorldToScreenPoint(new Vector3(c.x + 0.5f, 0f, c.y + 0.5f));
        return new Vector2(p.x, p.y);
    }

    private static IEnumerator NewCity(GameFlow flow, string age)
    {
        flow.Menu.RequestNew();
        yield return Frames(10);
        if (flow.Confirm.IsOpen) { yield return ClickUi(BtnWithText("Don't save")); yield return Frames(10); }
        // Inside the dialog: the toolbar also has an "Industrial" zone button.
        yield return ClickUi(BtnWithText(age, GameObject.Find("UI/NewCityDialog")));
        yield return ClickUi(BtnWithText("32", GameObject.Find("UI/NewCityDialog")));
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

    // DEBUG: a BalanceConfig value changed in memory (never saved; restored at the end).
    private static void SetBalance(BalanceConfig config, string field, float value)
    {
        var so = new SerializedObject(config);
        so.FindProperty(field).floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // The grown cell of a zone closest to the middle of the map (on screen after zooming in), within the level range.
    private static bool Nearest(GridData grid, ZoneType zone, int minLevel, int maxLevel, System.Func<Vector2Int, bool> also, out Vector2Int found)
    {
        found = default;
        float best = float.MaxValue;
        var centre = new Vector2(grid.Width * 0.5f, grid.Height * 0.5f);
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                var c = new Vector2Int(x, y);
                if (grid.GetZone(c) != zone || grid.IsRoad(c)) continue;
                int level = grid.GetBuildingLevel(c);
                if (level < minLevel || level > maxLevel || (also != null && !also(c))) continue;
                float d = Vector2.Distance(c, centre);
                if (d < best) { best = d; found = c; }
            }
        }
        return best < float.MaxValue;
    }

    private static IEnumerator SelectByClick(Vector2Int cell)
    {
        yield return ClickAt(CellPos(cell));
        yield return Frames(6);
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
        BalanceConfig balance = null;
        try
        {
            GameFlow flow = GameFlow.Instance;
            SaveSlots.Root = savesDir;
            var game = Object.FindAnyObjectByType<GameManager>();
            var placement = Object.FindAnyObjectByType<PlacementController>();
            var saves = Object.FindAnyObjectByType<SaveGameController>();
            var overlay = Object.FindAnyObjectByType<InfoOverlay>();
            balance = game.Balance;
            yield return Frames(10);

            // --- a Medieval city has no goods ---
            yield return NewCity(flow, "Medieval");
            Check(flow.State == GameFlowState.Playing, "a Medieval city starts: " + flow.State);
            game.Clock.SetSpeed(GameSpeed.Paused);
            Check(!game.Simulation.GoodsActive, "goods are off in the Medieval age");
            GameObject gMeter = GameObject.Find("UI/HUD/Game/DemandG") ?? GameObject.Find("DemandG");
            Check(gMeter == null || !gMeter.activeInHierarchy, "the G meter is hidden in the Medieval age");

            // --- Industrial: the meter, the view, the selection lines, the ledger ---
            yield return NewCity(flow, "Industrial");
            Check(flow.State == GameFlowState.Playing, "an Industrial city starts: " + flow.State);
            game.Clock.SetSpeed(GameSpeed.Paused);
            yield return ZoomIn();
            GridData grid = game.Grid;
            placement.DebugSeedCity();                       // DEBUG: cross of roads, a plant and R / C / I strips
            // DEBUG: a free water tower on the road (the Industrial age is piped; roads carry the water), or nothing grows past level 1.
            Check(placement.RestoreBuilding(game.Buildings.GetById("water_tower"), V(14, 15), 0), "a water tower is placed");
            game.Clock.DebugAdvanceDays(8);                  // DEBUG: let the strips grow
            yield return Frames(10);
            Check(game.Simulation.GoodsActive, "goods are on in the Industrial age");
            gMeter = GameObject.Find("DemandG");
            Check(gMeter != null && gMeter.activeInHierarchy, "the G meter is shown");
            // The G meter explains itself on hover.
            var goodsTip = gMeter.GetComponent<GoodsTooltip>();
            Check(goodsTip != null, "the G meter has a tooltip");
            if (goodsTip != null)
            {
                MouseTo(UiPos(gMeter), false);
                yield return Frames(6);
                Check(goodsTip.IsShown && goodsTip.Text.Contains("Factories make goods") && goodsTip.Text.Contains("Supply"), "hovering the G meter shows what goods are: " + goodsTip.Text.Replace("\n", " | "));
                ScreenCapture.CaptureScreenshot(shotPath.Replace(".png", "_tip.png"));
                yield return Seconds(1.5f);
                MouseTo(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false);
                yield return Frames(4);
                Check(!goodsTip.IsShown, "the tooltip hides when the pointer leaves");
            }
            // DEBUG: the age event the real advance raises; the toast names goods.
            GameEvents.RaiseAgeChanged(game.Simulation.Tech.CurrentAge);
            yield return Frames(4);
            Check(TextContaining("are now in play") != null, "entering the goods age toasts what goods are");
            Check(game.Simulation.Goods.Last.Demanded > 0f, $"the city wants goods: {game.Simulation.Goods.Last.Demanded:F1} (made {game.Simulation.Goods.Last.Produced:F1}, imported {game.Simulation.Goods.Last.Imported:F1})");

            yield return ClickUi(Btn("ViewsButton"));
            yield return Frames(4);
            Check(Shown("GoodsView"), "the Views flyout lists Goods");
            yield return ClickUi(Btn("GoodsView"));
            yield return Frames(6);
            Check(overlay.Shown == InfoOverlay.View.Goods, "the Goods view is on: " + overlay.Shown);
            ScreenCapture.CaptureScreenshot(shotPath);
            yield return Seconds(1.5f);
            yield return ClickUi(Btn("ViewsButton"));
            yield return Frames(4);
            yield return ClickUi(Btn("OffView"));
            yield return Frames(4);
            Check(overlay.Shown == InfoOverlay.View.Off, "Off turns it off");

            if (Nearest(grid, ZoneType.Industrial, 1, 3, null, out Vector2Int factory))
            {
                yield return SelectByClick(factory);
                string text = PanelText();
                Check(text.Contains("Goods") && text.Contains("makes up to"), "a factory says what it makes: " + factory);
            }
            else Check(false, "an industrial cell has grown");
            if (Nearest(grid, ZoneType.Commercial, 1, 3, null, out Vector2Int shop))
            {
                yield return SelectByClick(shop);
                Check(PanelText().Contains("sells up to"), "a shop says what it sells: " + shop);
            }
            else Check(false, "a commercial cell has grown");
            if (Nearest(grid, ZoneType.Residential, 1, 3, null, out Vector2Int home))
            {
                yield return SelectByClick(home);
                Check(PanelText().Contains("buys about"), "a home says what it buys: " + home);
            }
            else Check(false, "a residential cell has grown");

            yield return ClickUi(Btn("BudgetButton"));
            yield return Frames(6);
            string ledger = TextContaining("Costs -$");
            Check(ledger != null && ledger.Contains("imports $"), "the budget ledger lists imports: " + (ledger ?? "no ledger").Replace("\n", " | "));
            yield return ClickUi(Btn("BudgetButton"));
            yield return Frames(4);

            // --- a shortage: factories off (DEBUG) ---
            placement.ClearMode();
            SetBalance(balance, "m_GoodsPerIndustrialJob", 0f);
            game.Clock.DebugAdvanceDays(30);                 // DEBUG
            yield return Frames(10);
            var skipped = new System.Collections.Generic.HashSet<Vector2Int>();
            float supply = game.Simulation.GoodsSupply();
            Check(supply < balance.GoodsLevel3Supply, $"supply falls below the level-3 line: {supply:F2}");
            Check(gMeter.activeInHierarchy, "the G meter is still shown");
            // DEBUG: a small city rarely has a level 2 home with free land value at this point; promote the nearest level 1 homes
            // one by one until one is held by goods (and not by land value first).
            Vector2Int heldHome = default;
            bool held = false;
            for (int attempt = 0; attempt < 12 && !held; attempt++)
            {
                if (!Nearest(grid, ZoneType.Residential, 1, 1, c => !skipped.Contains(c), out Vector2Int candidate)) break;
                grid.SetBuiltAge(candidate, 2);
                grid.SetBuildingLevel(candidate, 2);
                if (game.Simulation.Growth.GetBlocker(candidate, game.Simulation.Demand.Snapshot) == GrowthBlocker.NoGoods) { held = true; heldHome = candidate; }
                else skipped.Add(candidate);
            }
            yield return Frames(4);
            Check(held, "a level 2 home is held by goods" + (held ? ": " + heldHome : ""));
            if (held)
            {
                yield return SelectByClick(heldHome);
                string text = PanelText();
                Check(text.Contains("short of goods"), "its panel says the city is short of goods");
                Check(text.Contains("build more factories"), "and what to do about it");
            }
            var tooltip = Object.FindAnyObjectByType<HappinessTooltip>();
            if (tooltip != null)
            {
                MouseTo(UiPos(tooltip.gameObject), false);
                yield return Frames(6);
                string tip = TextContaining("<b>Happiness</b>");
                Check(tip != null && tip.Contains("Short of goods"), "the happiness tooltip names the shortage");
                MouseTo(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false);
                yield return Frames(4);
            }
            else Check(false, "the happiness tooltip exists");
            ScreenCapture.CaptureScreenshot(shotPath.Replace(".png", "_short.png"));
            yield return Seconds(1.5f);

            // --- output back: the held home goes up ---
            SetBalance(balance, "m_GoodsPerIndustrialJob", 3f);   // DEBUG: a surplus, so the stock fills
            game.Clock.DebugAdvanceDays(25);                 // DEBUG
            yield return Frames(10);
            Check(game.Simulation.GoodsSupply() >= 1f, $"supply is back to 100%: {game.Simulation.GoodsSupply():F2}");
            if (held)
            {
                GrowthBlocker now = game.Simulation.Growth.GetBlocker(heldHome, game.Simulation.Demand.Snapshot);
                Check(now != GrowthBlocker.NoGoods, $"the home is no longer held by goods: {now}");
            }
            Check(game.Simulation.Goods.Stock > 0f, $"a stock builds up: {game.Simulation.Goods.Stock:F0}");
            yield return ClickUi(Btn("BudgetButton"));
            yield return Frames(6);
            ledger = TextContaining("Income +$");
            Check(ledger != null && ledger.Contains("exports $"), "the budget ledger lists exports: " + (ledger ?? "no ledger").Replace("\n", " | "));
            yield return ClickUi(Btn("BudgetButton"));
            yield return Frames(4);

            // --- save and load keep the stock ---
            Check(saves.Save(), "the city saves");
            float stock = game.Simulation.Goods.Stock;
            game.Clock.DebugAdvanceDays(3);                  // DEBUG: the stock moves on
            yield return Frames(4);
            Check(saves.Load(), "the city loads");
            yield return Frames(20);
            Check(Mathf.Abs(game.Simulation.Goods.Stock - stock) < 0.01f, $"the stock came back: saved {stock:F1}, loaded {game.Simulation.Goods.Stock:F1}");
            Check(game.Simulation.GoodsActive, "goods are still on after loading");
        }
        finally
        {
            if (balance != null)
            {
                SetBalance(balance, "m_GoodsPerIndustrialJob", 0.2f);
            }
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
