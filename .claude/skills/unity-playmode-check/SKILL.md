---
name: unity-playmode-check
description: Verify City Game changes in Play mode through the Unity MCP bridge — entering/leaving Play mode, driving the game from scripts, fast-forwarding days, capturing game-view and UI screenshots, and protecting the player's save slot. Use when a change needs checking in the running game rather than (or as well as) EditMode tests.
---

# Play-mode checks (City Game)

## Basic loop
1. `RunCommand`: `EditorApplication.isPlaying = true;` (one call).
2. Next `RunCommand`: set `Application.runInBackground = true;` **now** — while unfocused the player loop stalls (the sim won't tick, `ScreenCapture` never writes). Never set it in Edit mode: that writes `PlayerSettings.runInBackground` into `ProjectSettings.asset`.
3. Drive the game, e.g. `PlacementController.DebugSeedCity()`, `GameManager.Clock.DebugAdvanceDays(n)` (instant days through the normal tick + calendar path), `GameManager.Clock.SetSpeed(GameSpeed.Paused)`, `SaveGameController.NewCity(size, startAge)`, `PlacementController.SelectCell(cell)`, `PlacementController.RestoreBuilding(def, origin, rot)` (free placement), or invoke UI with `Button.onClick.Invoke()`. Things that need frames (pop animations, tilemap repaints in `LateUpdate`) only show up in a **later** RunCommand.
4. Check `Unity_GetConsoleLogs` for errors, then `EditorApplication.isPlaying = false;`.
- Exit Play mode **before** editing scripts or prefabs; a recompile during Play mode leaves the bridge unreachable until it settles.
- After Play mode, `git status`: Play sessions can rewrite `Assets/Settings/*.asset`, `ProjectSettings/*.asset` and sometimes `Main.unity` — revert unintended churn (see `perf-benchmark` and `scene-prefab-editing`).

## Save slot — protect it
`SaveGameController.Save()`, the HUD Save button and F5 write the player's real save: `%USERPROFILE%/AppData/LocalLow/DefaultCompany/City Game/city.json`. Before any check that saves, copy it to the scratchpad; copy it back afterwards and confirm (`head -c 15` shows the version). `SaveGameController.NewCity()` is in-memory only.

## Seeing the game
- `Unity_Camera_Capture` without an id returns the **Scene View**, not the game camera.
- World only: render `Camera.main` into a `RenderTexture` (set `cam.targetTexture`, `cam.Render()`, `ReadPixels` into a `Texture2D`, `EncodeToPNG`), write it to the scratchpad and Read it. Temporarily change `orthographicSize` to frame what you need, then restore it.
- With the UI (Screen Space Overlay canvas): temporarily set the `UI` canvas to `RenderMode.ScreenSpaceCamera` with `worldCamera = Camera.main`, `planeDistance = 1` **and `sortingOrder = 1000`** (otherwise the sprite tilemaps sort above it and bleed through panels), `Canvas.ForceUpdateCanvases()`, render as above, then restore mode and sorting order.
- Game view resolution in the Editor can differ from 1920×1080 (e.g. 2560×1440) — use `Screen.width/height`.

## Typical checks
- New city in an age: `SaveGameController.NewCity(new Vector2Int(64, 64), ageIndex)`; or click through the New City dialog's buttons.
- Research without waiting: `tech.SetActive(tech.Techs.GetById(id)); tech.Step(1000f);` — advancing: `tech.SetActiveAdvance(population)` + `Step` (raises `AgeAdvanced`, the calendar moves).
- Seeded city without the plant (for pre-power ages): `DebugSeedCity()` then `ClearAllBuildings()`.
- For a UI-only check driven by real input events, use the `virtual-input-playthrough` skill instead.
