using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

// `-smokeTest [-savesDir <folder>]` (M19g): a scripted check that a built player works end to end, for release builds
// too. It loads the showcase city (StreamingAssets), starts a fresh Medieval city, builds a little through the grid and
// the placement controller, runs 60 days, saves, loads and compares, does the same for the tutorial city, writes
// smoke_test.txt next to the player log and exits with 0 (all passed) or 1. Never runs in the Editor. Pass -savesDir so
// the player's own saves stay untouched.
public sealed class SmokeTest : MonoBehaviour
{
    private const string k_Arg = "-smokeTest";
    private readonly StringBuilder m_Report = new();
    private int m_Failed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(), k_Arg) < 0) return;
        new GameObject(nameof(SmokeTest)).AddComponent<SmokeTest>();
    }

    private void Check(bool ok, string what)
    {
        if (!ok) m_Failed++;
        m_Report.AppendLine((ok ? "PASS " : "FAIL ") + what);
    }

    private IEnumerator Start()
    {
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        string failure = null;
        try
        {
            m_Report.AppendLine($"SmokeTest {DateTime.Now:yyyy-MM-dd HH:mm} — {Application.productName} {Application.version}, Unity {Application.unityVersion}, " +
                $"{Screen.width}x{Screen.height}, debug build {Debug.isDebugBuild}");
        }
        catch (Exception e) { failure = e.ToString(); }

        GameManager game = null;
        SaveGameController save = null;
        PlacementController placement = null;
        for (int i = 0; i < 20; i++) yield return null;
        game = FindAnyObjectByType<GameManager>();
        save = FindAnyObjectByType<SaveGameController>();
        placement = FindAnyObjectByType<PlacementController>();
        GameFlow flow = GameFlow.Instance;
        if (game == null || save == null || placement == null || flow == null) failure = "the scene is missing GameManager / SaveGameController / PlacementController / GameFlow";

        if (failure == null)
        {
            // Two coroutine-free stretches, each guarded: an exception is a failed run, not a hang.
            IEnumerator run = Run(game, save, placement, flow);
            while (true)
            {
                bool more;
                try { more = run.MoveNext(); }
                catch (Exception e) { failure = e.ToString(); break; }
                if (!more) break;
                yield return run.Current;
            }
        }

        if (failure != null)
        {
            m_Failed++;
            m_Report.AppendLine("FAIL exception: " + failure);
        }
        m_Report.AppendLine(m_Failed == 0 ? "RESULT PASS" : $"RESULT FAIL ({m_Failed})");
        try { File.WriteAllText(Path.Combine(Application.persistentDataPath, "smoke_test.txt"), m_Report.ToString()); } catch (Exception) { }
        Application.Quit(m_Failed == 0 ? 0 : 1);
    }

    private IEnumerator Run(GameManager game, SaveGameController save, PlacementController placement, GameFlow flow)
    {
        Check(flow.State == GameFlowState.Playing, "the title is skipped: " + flow.State);
        game.Clock.SetSpeed(GameSpeed.Paused);

        // The showcase city comes from StreamingAssets in a player.
        bool showcase = save.LoadShowcase();
        yield return null;
        Check(showcase && save.ShowcaseActive, "the showcase city loads from StreamingAssets");
        Check(game.Simulation.Population.Population > 100, $"the showcase has people: {game.Simulation.Population.Population}");
        Check(!save.Dirty, "the showcase is never dirty");

        // A fresh Medieval city, built through the grid and the placement controller.
        save.NewCity(new Vector2Int(32, 32), 0, false, "Smoke city");
        yield return null;
        Check(game.MapSize.x == 32, "New City is 32x32: " + game.MapSize);
        GridData grid = game.Grid;
        for (int x = 0; x < 32; x++) grid.SetRoad(new Vector2Int(x, 16), true);
        for (int x = 2; x < 30; x++)
        {
            grid.SetZone(new Vector2Int(x, 17), ZoneType.Residential);
            grid.SetZone(new Vector2Int(x, 15), x < 16 ? ZoneType.Commercial : ZoneType.Industrial);
        }
        BuildingDefinition well = game.Buildings != null ? game.Buildings.GetById("well") : null;
        Check(well != null && placement.RestoreBuilding(well, new Vector2Int(8, 19), 0), "a well is placed");
        Check(placement.RestoreBuilding(well, new Vector2Int(20, 19), 0), "a second well is placed");

        game.Clock.DebugAdvanceDays(60);
        yield return null;
        int pop = game.Simulation.Population.Population;
        float money = game.Simulation.Economy.Money;
        int day = game.Clock.Day, month = game.Clock.Month, year = game.Clock.Year;
        int buildings = CountBuildings(placement);
        Check(pop > 0, $"60 days later there are people: {pop}");
        m_Report.AppendLine($"after 60 days: pop {pop}, money {money:F0}, {day}/{month}/{year}, {buildings} placed buildings");

        // Save, change the city, load, compare.
        Check(save.SaveAs("Smoke test", SaveKind.Manual), "save");
        save.NewCity(new Vector2Int(24, 24), 0, false, "Other");
        yield return null;
        Check(game.MapSize.x == 24, "another city replaced it");
        Check(save.Load("Smoke test"), "load");
        yield return null;
        Check(game.MapSize.x == 32, "the loaded city is 32x32 again");
        Check(Mathf.Abs(game.Simulation.Economy.Money - money) < 0.5f, $"money equal: {game.Simulation.Economy.Money:F0} vs {money:F0}");
        Check(game.Clock.Day == day && game.Clock.Month == month && game.Clock.Year == year, "date equal");
        Check(CountBuildings(placement) == buildings, $"placed buildings equal: {CountBuildings(placement)} vs {buildings}");
        Check(game.Simulation.Population.Population == pop, $"population equal: {game.Simulation.Population.Population} vs {pop}");
        Check(save.CityName == "Smoke city", "the city name survives: " + save.CityName);
        Check(!save.Dirty, "a loaded city is not dirty");

        // The tutorial city, and its progress through a save.
        save.StartTutorial();
        yield return null;
        yield return null;
        Check(flow.Tutorial != null && flow.Tutorial.Active && flow.Tutorial.Index == 0, "the tutorial starts at objective 1");
        Check(game.MapSize.x == 48, "the tutorial city is 48x48");
        save.TutorialChanged(3);
        Check(save.SaveAs("Smoke tutorial", SaveKind.Manual), "save the tutorial city");
        save.NewCity(new Vector2Int(24, 24), 0, false, "Other");
        yield return null;
        Check(!flow.Tutorial.Active, "a plain new city has no tutorial");
        Check(save.Load("Smoke tutorial"), "load the tutorial city");
        yield return null;
        yield return null;
        Check(flow.Tutorial.Active && flow.Tutorial.Index == 3, "the tutorial resumes at objective 4: " + flow.Tutorial.Index);

        // Nothing for developers in a release player.
        DebugPanel debugPanel = FindAnyObjectByType<DebugPanel>(FindObjectsInactive.Include);
        Check(Debug.isDebugBuild || debugPanel == null || !debugPanel.enabled, "the debug panel is switched off in a release player");

        SaveSlots.Delete("Smoke test");
        SaveSlots.Delete("Smoke tutorial");
    }

    private static int CountBuildings(PlacementController placement)
    {
        int count = 0;
        foreach (BuildingInstance building in placement.PlacedBuildings)
        {
            if (building != null) count++;
        }
        return count;
    }
}
