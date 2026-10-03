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
        saveGame.NewCity(new Vector2Int(k_MapSize, k_MapSize));
        float buildStart = Time.realtimeSinceStartup;
        BuildCity(gameManager.Grid, gameManager.Ages != null ? gameManager.Ages.Count : 1);
        float buildMs = (Time.realtimeSinceStartup - buildStart) * 1000f;
        for (int i = 0; i < 30; i++) yield return null;

        float tickStart = Time.realtimeSinceStartup;
        for (int i = 0; i < 20; i++) gameManager.Simulation.Tick();
        float tickMs = (Time.realtimeSinceStartup - tickStart) * 1000f / 20f;

        m_Report.AppendLine($"PerfBenchmark {DateTime.Now:yyyy-MM-dd HH:mm} — {Application.unityVersion}, {SystemInfo.processorType}, {SystemInfo.graphicsDeviceName}, {Screen.width}x{Screen.height}");
        m_Report.AppendLine($"map {k_MapSize}x{k_MapSize}: built in {buildMs:F0} ms, sim tick {tickMs:F2} ms");

        Camera camera = Camera.main;
        float startZoom = camera.orthographicSize;
        yield return RunScenarios(gameManager, $"default zoom ({startZoom:F0})");

        // Whole map on screen: the worst case for drawing.
        camera.orthographicSize = k_MapSize * 0.45f;
        yield return Measure("zoomed out to the whole map, idle", null);
        yield return Screenshot();

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

    // Blocks of 4x4 cells share a built age, cycling through every age, so all age styles are drawn.
    private static void BuildCity(GridData grid, int ageCount)
    {
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (x % 5 == 0 || y % 5 == 0) grid.SetRoad(new Vector2Int(x, y), true);
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
