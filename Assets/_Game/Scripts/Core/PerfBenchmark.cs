using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

// Development builds only: launch the player with -perfBenchmark to build a fully grown 96x96 city
// (roads every 5 cells, every block level 3, built ages mixed across every age), time frames in a few scenarios and write the results
// to <persistentDataPath>/perf_benchmark.txt, then quit. Uses NewCity (in memory) and never saves.
public sealed class PerfBenchmark : MonoBehaviour
{
    private const string k_Arg = "-perfBenchmark";
    private const int k_MapSize = 96;
    private const int k_Frames = 300;

    private readonly float[] m_Samples = new float[k_Frames];
    private readonly float[] m_CpuSamples = new float[k_Frames];
    private readonly float[] m_RenderSamples = new float[k_Frames];
    private readonly float[] m_GpuSamples = new float[k_Frames];
    private readonly FrameTiming[] m_Timing = new FrameTiming[1];
    private readonly StringBuilder m_Report = new();
    private int m_Shots;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!Debug.isDebugBuild || Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(), k_Arg) < 0) return;
        new GameObject(nameof(PerfBenchmark)).AddComponent<PerfBenchmark>();
    }

    private IEnumerator Start()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        Application.runInBackground = true;   // an unfocused player would otherwise stall

        GameManager gameManager = FindAnyObjectByType<GameManager>();
        SaveGameController saveGame = FindAnyObjectByType<SaveGameController>();
        for (int i = 0; i < 10; i++) yield return null;

        gameManager.Clock.SetSpeed(GameSpeed.Paused);
        saveGame.NewCity(new Vector2Int(k_MapSize, k_MapSize), -1, false);
        float buildStart = Time.realtimeSinceStartup;
        BuildCity(gameManager.Grid, gameManager.Ages != null ? gameManager.Ages.Count : 1);
        FeedUtilities(gameManager);
        float buildMs = (Time.realtimeSinceStartup - buildStart) * 1000f;
        for (int i = 0; i < 30; i++) yield return null;

        float tickStart = Time.realtimeSinceStartup;
        for (int i = 0; i < 20; i++) gameManager.Simulation.Tick();
        float tickMs = (Time.realtimeSinceStartup - tickStart) * 1000f / 20f;
        float flowStart = Time.realtimeSinceStartup;
        for (int i = 0; i < 20; i++) gameManager.Simulation.Traffic.Update(1f, 1f, 1f);
        float flowMs = (Time.realtimeSinceStartup - flowStart) * 1000f / 20f;

        m_Report.AppendLine($"PerfBenchmark {DateTime.Now:yyyy-MM-dd HH:mm} — {Application.unityVersion}, {SystemInfo.processorType}, {SystemInfo.graphicsDeviceName}, {Screen.width}x{Screen.height}");
        m_Report.AppendLine($"map {k_MapSize}x{k_MapSize}: built in {buildMs:F0} ms, sim tick {tickMs:F2} ms (traffic flow alone {flowMs:F2} ms)");

        Camera camera = Camera.main;
        float startZoom = camera.orthographicSize;
        yield return RunScenarios(gameManager, $"default zoom ({startZoom:F0})");

        // Whole map on screen: the worst case for drawing.
        camera.orthographicSize = k_MapSize * 0.45f;
        yield return Measure("zoomed out to the whole map, idle", null);
        yield return Screenshot();

        camera.orthographicSize = startZoom;
        yield return RunDisasters(gameManager);
        yield return RunVehicles(gameManager, camera, startZoom);

        camera.orthographicSize = k_MapSize * 0.45f;
        Light sun = FindAnyObjectByType<Light>();
        if (sun != null && sun.shadows != LightShadows.None)
        {
            LightShadows shadows = sun.shadows;
            sun.shadows = LightShadows.None;
            yield return Measure("zoomed out, shadows off", null);
            camera.orthographicSize = startZoom;
            yield return Measure("default zoom, shadows off", null);
            sun.shadows = shadows;
        }
        camera.orthographicSize = startZoom;

        string path = Path.Combine(Application.persistentDataPath, "perf_benchmark.txt");
        File.WriteAllText(path, m_Report.ToString());
        Application.Quit();
    }

    private IEnumerator RunScenarios(GameManager gameManager, string label)
    {
        GridData grid = gameManager.Grid;
        m_Report.AppendLine($"-- {label}");

        gameManager.Clock.SetSpeed(GameSpeed.Paused);
        yield return Measure("idle, paused", null);

        yield return Screenshot();

        gameManager.Clock.SetSpeed(GameSpeed.x4);
        yield return Measure("running 4x", null);
        gameManager.Clock.SetSpeed(GameSpeed.Paused);

        // Like dragging a zone brush across grown blocks.
        yield return Measure("rezone a grown cell every frame", i =>
        {
            Vector2Int cell = new Vector2Int(1 + i % 4, 1);
            grid.SetZone(cell, grid.GetZone(cell) == ZoneType.Residential ? ZoneType.Commercial : ZoneType.Residential);
        });

        // Demolish / regrow: blocks leave and re-enter the pool.
        yield return Measure("demolish + regrow every frame", i =>
        {
            Vector2Int cell = new Vector2Int(6 + i % 4, 6);
            grid.SetBuildingLevel(cell, grid.GetBuildingLevel(cell) == 0 ? (byte)3 : (byte)0);
        });

        yield return Measure("road toggle every frame", i =>
        {
            Vector2Int cell = new Vector2Int(5, 2 + i % 3);
            grid.SetRoad(cell, !grid.IsRoad(cell));
        });
    }

    // M17: a few dozen fires burning and homes infected (flame / marker cubes and the hazard tilemap on screen, paused so
    // they stay), then the daily Disasters.Step timed with fires being re-lit so the spread paths run.
    private IEnumerator RunDisasters(GameManager gameManager)
    {
        SimulationSystem sim = gameManager.Simulation;
        DisasterSystem d = sim.Disasters;
        GridData grid = gameManager.Grid;
        if (sim.Tech == null) yield break;
        d.Enabled = true;
        d.Random.Seed(7);
        int fires = 0, sick = 0;
        for (int i = 0; i < grid.Width * grid.Height; i += 7)
        {
            var cell = new Vector2Int(i % grid.Width, i / grid.Width);
            if (fires < 30 && i % 301 < 7 && d.Fire.Ignite(cell)) fires++;
            else if (sick < 60 && grid.GetZone(cell) == ZoneType.Residential && d.Epidemic.Infect(cell)) sick++;
        }
        m_Report.AppendLine($"-- disasters: {d.Fire.BurningCount} cells burning, {d.Epidemic.InfectedCount} homes infected");
        yield return Measure("fires + plague on screen, idle", null);

        float start = Time.realtimeSinceStartup;
        int steps = 100;
        for (int i = 0; i < steps; i++)
        {
            if (d.Fire.BurningCount < 10)
            {
                for (int k = 0, lit = 0; k < grid.Width * grid.Height && lit < 12; k += 211)
                {
                    if (d.Fire.Ignite(new Vector2Int(k % grid.Width, k / grid.Width))) lit++;
                }
            }
            d.Step();
        }
        float stepMs = (Time.realtimeSinceStartup - start) * 1000f / steps;
        m_Report.AppendLine($"Disasters.Step with fires burning: {stepMs:F3} ms");
        yield return Measure("after the fires, idle", null);
    }

    // M18f: the cosmetic traffic. Every scenario above already ran with vehicles (they spawn while paused too), so this
    // measures the same view with them driving, then with the whole VehicleView switched off, for the difference.
    private IEnumerator RunVehicles(GameManager gameManager, Camera camera, float startZoom)
    {
        VehicleView vehicles = gameManager.Vehicles;
        if (vehicles == null) yield break;
        gameManager.Clock.SetSpeed(GameSpeed.x1);
        for (int i = 0; i < 90; i++) yield return null;
        m_Report.AppendLine($"-- vehicles: {vehicles.ActiveCount} driving at default zoom");
        yield return Measure("vehicles driving, default zoom", null);
        yield return Screenshot();

        camera.orthographicSize = k_MapSize * 0.45f;
        for (int i = 0; i < 60; i++) yield return null;
        m_Report.AppendLine($"-- vehicles: {vehicles.ActiveCount} driving, whole map in view");
        yield return Measure("vehicles driving, zoomed out", null);

        vehicles.ReleaseAll();
        vehicles.gameObject.SetActive(false);
        yield return Measure("vehicles off, zoomed out", null);
        camera.orthographicSize = startZoom;
        yield return Measure("vehicles off, default zoom", null);
        vehicles.gameObject.SetActive(true);
        gameManager.Clock.SetSpeed(GameSpeed.Paused);
    }

    // Proof of what was on screen while measuring.
    private IEnumerator Screenshot()
    {
        string shot = Path.Combine(Application.persistentDataPath, $"perf_benchmark_{m_Shots++}.png");
        ScreenCapture.CaptureScreenshot(shot);
        for (int i = 0; i < 5; i++) yield return null;
        m_Report.AppendLine($"screenshot: {shot}");
    }

    private IEnumerator Measure(string scenario, Action<int> step)
    {
        for (int i = 0; i < 10; i++) yield return null;
        for (int i = 0; i < k_Frames; i++)
        {
            step?.Invoke(i);
            yield return null;
            m_Samples[i] = Time.unscaledDeltaTime * 1000f;
            FrameTimingManager.CaptureFrameTimings();
            bool timed = FrameTimingManager.GetLatestTimings(1, m_Timing) > 0;
            m_CpuSamples[i] = timed ? (float)m_Timing[0].cpuMainThreadFrameTime : 0f;
            m_RenderSamples[i] = timed ? (float)m_Timing[0].cpuRenderThreadFrameTime : 0f;
            m_GpuSamples[i] = timed ? (float)m_Timing[0].gpuFrameTime : 0f;
        }

        Array.Sort(m_Samples);
        Array.Sort(m_CpuSamples);
        Array.Sort(m_RenderSamples);
        Array.Sort(m_GpuSamples);
        float sum = 0f;
        foreach (float sample in m_Samples) sum += sample;
        m_Report.AppendLine($"{scenario}: frame avg {sum / k_Frames:F2} ms, median {m_Samples[k_Frames / 2]:F2}, p95 {m_Samples[k_Frames * 95 / 100]:F2}, max {m_Samples[k_Frames - 1]:F2}"
            + $" | medians: main thread {m_CpuSamples[k_Frames / 2]:F2}, render thread {m_RenderSamples[k_Frames / 2]:F2}, GPU {m_GpuSamples[k_Frames / 2]:F2}");
    }

    // M13: power and water fed in along the west edge (sim-only sources, no buildings drawn) and a
    // water pipe along the middle row of every block, so both networks carry real flow when the
    // tick recomputes them. M14d: plus sim-only civic cover.
    private static void FeedUtilities(GameManager gameManager)
    {
        GridData grid = gameManager.Grid;
        var sources = new System.Collections.Generic.List<ServiceSource>();
        for (int y = 1; y < grid.Height; y += 10)
        {
            sources.Add(new ServiceSource(new Vector2Int(1, y), Vector2Int.one, 0, 20000, waterSupply: 20000));
        }
        // M14: civic cover of every line on a 15-cell grid, so the civic terms read real cover.
        var kinds = new[] { ServiceKind.Order, ServiceKind.Fire, ServiceKind.Health, ServiceKind.Education };
        int k = 0;
        for (int y = 6; y < grid.Height; y += 15)
        {
            for (int x = 6; x < grid.Width; x += 15)
            {
                sources.Add(new ServiceSource(new Vector2Int(x, y), new Vector2Int(2, 2), 0, 0,
                    civicKind: kinds[k++ % kinds.Length], civicRadius: 8, civicStrength: 1f));
            }
        }
        gameManager.Simulation.Sources = sources;
        for (int y = 2; y < grid.Height; y += 5)
        {
            for (int x = 0; x < grid.Width; x++) grid.SetPipe(new Vector2Int(x, y), true);   // ignored on roads
        }
    }

    // Blocks of 4x4 cells share a built age, cycling through every age, so all age styles are drawn.
    private static void BuildCity(GridData grid, int ageCount)
    {
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                // M16: a mix of road tiers (every tier sprite and capacity in play), avenues on the main cross.
                if (x % 5 == 0 || y % 5 == 0)
                {
                    bool cross = x == 45 || y == 45;
                    grid.SetRoadTier(new Vector2Int(x, y), cross ? (byte)4 : (byte)((x / 5 * 3 + y / 5 * 2) % 5 + 1));
                }
            }
        }
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (grid.IsRoad(cell)) continue;
                int block = (x / 5 + y / 5) % 3;
                grid.SetZone(cell, block == 0 ? ZoneType.Residential : block == 1 ? ZoneType.Commercial : ZoneType.Industrial);
                grid.SetBuiltAge(cell, (byte)((x / 5 * 7 + y / 5 * 3) % ageCount));
                grid.SetBuildingLevel(cell, 3);
            }
        }
    }
}
