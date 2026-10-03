---
name: unity-test-loop
description: Compile-check and test City Game C# changes through the Unity MCP bridge — running EditMode tests from a RunCommand, offline compile checks when the Editor is closed or reloading, RunCommand rules and quirks, and handling domain reloads. Use after editing any script under Assets/_Game, or whenever tests need to run.
---

# Unity test loop (City Game)

The Unity Editor is the build system: no CLI build, lint or formatter, never `dotnet test`. Tests run in the Editor's Test Runner (`com.unity.test-framework`); EditMode tests live under `Assets/_Game/Tests/EditMode/` (`CityBuilder.Grid.Tests`, `CityBuilder.Simulation.Tests`).

## The bridge
- Claude Code: the MCP server `unity-mcp` (tools `mcp__unity-mcp__*`). OpenCode: `opencode.json` registers the same relay as `unity`. It only works while the Editor is open.
- Typical loop after editing scripts: `Unity_GetConsoleLogs` (errors/warnings) → `Unity_RunCommand` for tests and scene/asset work.
- Adding or editing **any** `.cs` triggers a domain reload: the bridge answers "Unity not detected" (or, mid-reload, `Could not find type …RunCommandMacroEvaluatorEntryPoint`) for ~30–45 s. Wait with a **background** `sleep 40` (foreground sleeps are blocked), then check `Unity_GetConsoleLogs` for compile errors and retry.
- Asset deletion via MCP pops an editor modal — delete assets in the Editor UI (or with git/the shell while nothing references them).
- Ask the user to confirm the **first** asset generation of a conversation — it blocks until every generation finishes.

## RunCommand rules
- The script must be `internal class CommandScript : IRunCommand` with `public void Execute(ExecutionResult result)`. Game types from `Assembly-CSharp` are usable directly (`Object.FindAnyObjectByType<GameManager>()`, `SerializedObject` to set private `m_` fields).
- Blocked: `System.Reflection`, `System.Diagnostics` (time with `Time.realtimeSinceStartupAsDouble`), and `File.Delete` ("User interactions are not supported") — delete/restore files from the shell.
- Scripts are wrapped in the namespace `Unity.AI.Assistant.Agent.Dynamic.Extension.Editor`, where `Image` resolves to a namespace: alias `using UImage = UnityEngine.UI.Image;`.
- Nested classes inside `CommandScript` get duplicated by the wrapper (CS1527): declare helpers (test callbacks, coroutine drivers) as **top-level `internal`** classes.
- `result.Log` ignores format specifiers like `{0:F2}` — use interpolated strings. `Unity_GetConsoleLogs` doesn't reliably return plain `Debug.Log` lines; report via `result.Log`, or have coroutines append to a log file in the scratchpad.
- Each RunCommand compiles into its own assembly, so statics are not shared between calls — pass state through files or the scene.

## Running EditMode tests
Not in Play mode and not while compiling. Synchronous runs return results in the same call; async runs are unreliable (a pending domain reload drops the callbacks). Batch-mode `Unity.exe -runTests` can't run while the Editor has the project open.

```csharp
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;

internal class TestCollector : ICallbacks
{
    public static StringBuilder Sb = new StringBuilder();
    public void RunStarted(ITestAdaptor t) { }
    public void RunFinished(ITestResultAdaptor r) { Sb.AppendLine($"RUN: pass={r.PassCount} fail={r.FailCount} skip={r.SkipCount}"); }
    public void TestStarted(ITestAdaptor t) { }
    public void TestFinished(ITestResultAdaptor r)
    {
        if (!r.HasChildren && r.TestStatus == TestStatus.Failed) Sb.AppendLine("FAIL " + r.FullName + ": " + r.Message);
    }
}

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        AssetDatabase.Refresh();
        if (EditorApplication.isCompiling) { result.Log("still compiling"); return; }
        TestCollector.Sb.Clear();
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(new TestCollector());
        api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }) { runSynchronously = true });
        result.Log(TestCollector.Sb.ToString());
    }
}
```

- One test: `testNames = new[] { "SimulationTests.Tick_IsDeterministic" }`; one fixture: `groupNames = new[] { "AgeBalanceTests" }`. `[Explicit]` tests are skipped unless named.
- `ITestResultAdaptor.Output` holds `TestContext.WriteLine` text — print it for harness reports (e.g. `AgeBalanceTests`).
- **After a run, check `git status`:** the Test Runner sometimes re-saves `Main.unity`. If the diff is anything other than canonical `0` layout values (see the `scene-prefab-editing` skill), revert it and reopen the scene.

## Compile check without the Editor (closed, or mid-reload)
Use throwaway SDK-style projects in the scratchpad — never in the repo. They catch C# errors fast but do not replace the Test Runner (`ScriptableObject.CreateInstance` and other native calls fail outside Unity).

1. **Runtime:** `netstandard2.1`, `EnableDefaultCompileItems=false`, `LangVersion 9.0`; `<Compile Include>` `Assets/_Game/Scripts/**/*.cs` (exclude `**/Editor/**`); references `C:/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Data/Managed/UnityEngine/*.dll`, `Library/ScriptAssemblies/Unity.*.dll` (minus `Unity.AI.*` and `*Editor*`) and `Library/ScriptAssemblies/UnityEngine.UI.dll`.
2. **asmdef boundaries:** only `Scripts/Grid/**`, `Scripts/Simulation/**` and `Tests/EditMode/Simulation/**` + the `UnityEngine/*.dll` folder (it already contains `UnityEditor.CoreModule`, so tests using `AssetDatabase`/`SerializedObject` compile — **don't** also add `Managed/UnityEditor.dll`, it causes CS0433 duplicates) + `Library/PackageCache/com.unity.ext.nunit@*/net472/unity-custom/nunit.framework.dll`. Set `AssemblyName` to `CityBuilder.Simulation.Tests` so `InternalsVisibleTo` applies. These assemblies must not reference `Assembly-CSharp` types.

Run `dotnet build -v q -nologo 2>&1 | grep -E "error|Build succeeded" | sort -u`.

## No Editor at all (cloud sessions): the sim harness
`Tools/sim-harness/run.sh` builds the pure Grid + Simulation code and the Simulation EditMode tests with Mono and runs them with NUnitLite against stub `UnityEngine` / `UnityEditor` APIs (the asset tests read the real `.asset` YAML). The first run installs the toolchain (`setup.sh`). It reproduces the Editor's numbers exactly, so it is good for the pure-sim steps of a milestone (implement, test, tune, record numbers), but it does not compile `Assembly-CSharp` or run Play mode: say in the commit / step note that the tests ran in the harness and still need a Test Runner pass. Throwaway probes go in the scratchpad and in via `EXTRA="…/Probe.cs"`. Details: `Tools/sim-harness/README.md`.

## Waiting for a reload, and probe tests (added in M12)
- After editing `.cs`, `.asset` or prefab files the bridge answers "Unity not detected" for roughly 30–75 s. A foreground `python -c "import time; time.sleep(40)"` waits fine (plain `sleep` is blocked); retry the RunCommand afterwards — a second short wait is normal after bigger edits.
- Run **several filters in one call**: `new ExecutionSettings(new Filter { testMode = TestMode.EditMode, groupNames = new[] { "AgeBalanceTests" } }, new Filter { testMode = TestMode.EditMode, testNames = new[] { "TuneProbe.Probe" } })`. Print `r.Output` from `TestFinished` to see `TestContext.WriteLine` text.
- **Probe tests:** for tuning or timing, drop a `[Test, Explicit]` class under `Tests/EditMode/Simulation/` that prints a table (population, happiness breakdown, blockers, `Stopwatch` ms) and run it by name. Delete it and its `.meta` before committing (check `git status` for stray `TuneProbe.cs`).
- `Stopwatch` is blocked in RunCommand scripts but fine inside test code (`System.Diagnostics.Stopwatch`).

