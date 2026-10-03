---
name: perf-benchmark
description: Measure City Game performance properly — build a development player, run the built-in PerfBenchmark (fully grown 96x96 city with mixed ages), compare against the recorded baselines, and clean up the settings churn a player build leaves behind. Use after changes that add renderers, per-frame work or heavy per-tick sim work, and whenever a milestone plan asks for a benchmark re-run.
---

# Performance benchmark (City Game)

Editor frame times mislead: Hierarchy/editor overhead makes GameObject create/destroy look ~5 ms, and an unfocused player stalls. Measure in a **development player**.

## Run
1. Commit (or at least note) your work first — the build rewrites settings files.
2. Build from a RunCommand (warnings about shaders from packages are normal; check `report.summary.result`):
   ```csharp
   var options = new BuildPlayerOptions {
       scenes = new[] { "Assets/_Game/Scenes/Main.unity" },
       locationPathName = "<scratchpad>/PerfBuild/CityGame.exe",
       target = BuildTarget.StandaloneWindows64,
       options = BuildOptions.Development };
   var report = BuildPipeline.BuildPlayer(options);
   ```
3. Run from the shell: `CityGame.exe -perfBenchmark -screen-width 1920 -screen-height 1080 -screen-fullscreen 0`, then read `%USERPROFILE%/AppData/LocalLow/DefaultCompany/City Game/perf_benchmark.txt` (+ `perf_benchmark_N.png` screenshots). The benchmark never saves the city.
4. **Clean up:** the build (and Play sessions) rewrite `Assets/Settings/*.asset`, `ProjectSettings/*.asset` (shader prefiltering, serialized defaults, Input System `preloadedAssets`) and sometimes `Main.unity` UI layout. `git checkout -- Assets/Settings ProjectSettings` (and the scene if touched), then reopen the scene with `EditorSceneManager.OpenScene`.

## What it measures
`Assets/_Game/Scripts/Core/PerfBenchmark.cs` (development players only, `-perfBenchmark`): New City 96×96, roads every 5 cells, every block level 3 (5,776 cells, ~11.5k blocks) with built ages cycling through every age per 4×4 block; sim tick time; then frame / main thread / render thread / GPU medians (`FrameTimingManager`) for idle, 4×, a rezone every frame, demolish + regrow every frame, a road toggle every frame, and the whole map zoomed out.

## Baselines (Ryzen 9 7950X, RTX 3070, 1080p)
| Run | Default zoom | Zoomed out | Edit every frame | Sim tick |
|---|---|---|---|---|
| M10b (after shared materials + pooling) | ~1.1 ms | 1.5 ms | ~2 ms | 0.5 ms |
| M11e (mixed ages) | 1.14 ms | 1.48 ms | ~2 ms | 1.25 ms* |

\* The stress city starts with a quarter of its blocks outdated, so the redevelop scan runs over the whole map every tick.

Regressions to watch: per-instance materials or `MaterialPropertyBlock`s (they drop renderers out of the SRP Batcher / GPU Resident Drawer — this was 23 ms frames / 110 ms zoomed out before M10b), creating/destroying GameObjects per change instead of pooling, and per-tick full-map scans in new sim systems.

Record new numbers in this table and in GamePlan §12's milestone notes.
