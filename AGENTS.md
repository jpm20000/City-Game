# AGENTS.md

## Project
- **Chronopolis** (J-man Studios), version **1.2.0**: isometric 2.5D city-builder with an abstract (statistical) simulation, four ages with research. Unity **6000.6.3f1**, URP 17.6.0, Linear, Windows + WebGL, new Input System only.
- **Status: M0–M31 done, no milestone planned** (1.0 = M0–M19, 1.1 = M20–M26, 1.2 = M27–M31). Changelog: `Docs/Changelog.md`.
- Where things are: design, roadmap (§12) and the per-system reference (§13, read the section for the system you touch and update it in the same commit) in `Docs/GamePlan.md`; each milestone's plan in `Docs/milestones/<version>/Mxx.md` (`v1.1/`, `v1.2/` have a `Roadmap.md`); procedures in skills (`.claude/skills/`: `milestone-workflow`, `unity-test-loop`, `unity-playmode-check`, `virtual-input-playthrough`, `scene-prefab-editing`, `perf-benchmark`, `webgl-build`, `balance-tuning`). Keep this file to conventions and standing rules.
- Game code under `Assets/_Game/` (never `Assets/` root). Scene: `Assets/_Game/Scenes/Main.unity`.

## Build / test
- The Unity Editor is the build system: no CLI build, lint or formatter; never `dotnet test`. Tests: EditMode under `Assets/_Game/Tests/EditMode/`, run in the Test Runner (via MCP: `unity-test-loop`). Without an Editor, `Tools/sim-harness/run.sh` runs the pure-sim tests (a Test Runner pass is still owed).
- Never edit or commit `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `obj/`, `Build/`, the untracked `Data/`; `*.csproj` / `City Game.slnx` are generated. Commit every asset's `.meta`.
- Unity MCP (Editor must be open): `unity-mcp` in Claude Code, `unity` in OpenCode (`opencode.json`).

## C# conventions
- Assemblies: runtime → `Assembly-CSharp`; anything under `Editor/` → `Assembly-CSharp-Editor` (or player builds fail). Only asmdefs: `Scripts/Grid/CityBuilder.Grid`, `Scripts/Simulation/CityBuilder.Simulation` (pure sim, references Grid by `GUID:`) and their test asmdefs (one per folder: `Tests/EditMode/`, `Tests/EditMode/Simulation/`). These must not reference `Assembly-CSharp`.
- Input System only (legacy `Input.*` throws). Actions: `Assets/_Game/Input/CityBuilder.inputactions`, map `Gameplay`.
- URP sprites / tilemaps need `Universal Render Pipeline/2D/Sprite-Unlit-Default` (`Sprites/Default` renders pink). `Camera.transparencySortMode` / `Axis` are runtime-only (set in code).
- `Object.GetInstanceID()` is a compile error (use `GetEntityId()`); use `FindAnyObjectByType`, not `FindFirstObjectByType`.
- World: logical cell `(x, z)` = world `[x, x+1]×[z, z+1]`, buildings stand along Y; camera and grid details in GamePlan §13 *Camera* / *Grid*.

## Standing rules (any new work)
- **Workflow:** `milestone-workflow` — plan file first, each step committed on its own with its docs; a milestone is done when EditMode is green, a UI-only virtual-input play-through passes and GamePlan §12 / §13 are updated (this file only if a convention changed).
- **Pure sim.** Rules and data live in `CityBuilder.Simulation` (ScriptableObjects refer to buildings by `Id`; `BuildingDefinition` stays in `Assembly-CSharp`). Runtime code only feeds it (`CityModifiers`, `ServiceSource`) and draws it.
- **Baselines.** The age-less `SimulationSystem` keeps its numbers (legacy seeded-city tests); `AgeBalanceTests.IndustrialStart_RealContent_Baseline` is the accepted real-content baseline. A plan that changes a baseline says which numbers and why.
- **Per-cell state** survives `GridData.Resize` (reallocate on `OnResized`); growth-tied state clears at level 0. Capacity only through `CapacityModel`; growth gates and their text through `GrowthSystem` + `GrowthBlocker` (+ `SelectionPanel`).
- **Saves.** New persisted state bumps `SaveData.CurrentVersion` + one `SaveMigrations` step with a test (`Fixtures/city_v1_24x24.json` keeps migrating); derived state is recomputed. Save → load → continue equals an uninterrupted run (seeded RNG, state saved).
- **Content** (ages, techs, visual sets) from `Tools/gen_age_content.py`; new buildings hang off existing techs (`RequiredTech`); `ContentTests` green. Kit, tiles and audio are generated (`Generate Building Kit`, `Tools/gen_tiles.py`, `Tools/gen_audio.py`): edit the tables, re-run, commit the rewritten `.meta`s.
- **Balance.** Tunables in `BalanceConfig` (code default = asset value, tagged "(tuned, Mxx)"); re-run `AgeBalanceTests` and extend `EngagedCity` when players must build something new (`balance-tuning`).
- **Rendering.** Shared materials only (no per-instance materials / `MaterialPropertyBlock`s), pooled objects; `perf-benchmark` after adding many renderers or per-frame work.
- **Presentation reads the sim, never writes it**: own randomness (never `SimRandom`), settings in `PlayerPrefs`, created at runtime by `GameManager`; a missing asset means the old look or silence, never an error.
- **UI.** Events in `GameEvents` (+ `ResetSubscribers`). Views: `InfoOverlay.View` + `IsAvailable` + a `VIEW` `ToolButton`. Tech-gated features stay hidden until unlocked (`GameManager.PowerUnlocked` pattern). Side panels share `SidePanels`.
- **Platforms.** Desktop is the reference. WebGL-only code behind `#if UNITY_WEBGL`; after a web build switch back to Windows and revert the churn (`webgl-build`). Releases: `CityBuilder > Build Windows Release` (+ `-smokeTest -savesDir <folder>`), `CityBuilder > Build WebGL`.
