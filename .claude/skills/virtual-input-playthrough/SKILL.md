---
name: virtual-input-playthrough
description: Run a UI-only play-through of City Game in Play mode with a virtual mouse and keyboard (Input System state events) — clicking HUD/toolbar/panel buttons, clicking and dragging on map cells, keyboard shortcuts — and log pass/fail checks. Use to finish a milestone (its done-when needs a UI-only play-through) or to test any player-facing flow end to end.
---

# UI-only play-through with virtual input

Every action goes through Input System events, so the EventSystem, `InputReader` actions, hover states, placement validation and UI wiring are all exercised. Only the checks read game state. The template **`PlaythroughDriver.cs`** (next to this file) is the M11g ages play-through; copy it into a `Unity_RunCommand`, set `LogPath` to the scratchpad, and edit the scenario in `Run()`.

## Setup (all inside the driver)
1. Enter Play mode in one RunCommand; start the driver in the next. The driver sets `Application.runInBackground = true` (only ever in Play mode).
2. By default device input doesn't reach game actions in an unfocused Editor. Set (in memory only — there is no settings asset) `InputSystem.settings.editorInputBehaviorInPlayMode = AllDeviceInputAlwaysGoesToGameView` and `backgroundBehavior = IgnoreFocus`.
3. `InputSystem.DisableDevice` the real `Mouse.current` / `Keyboard.current` (else the physical mouse overrides), `AddDevice<Mouse>` / `<Keyboard>` virtual ones and `MakeCurrent()` them. Remove them and re-enable the real ones in a `finally`.
4. Run the scenario as a coroutine on any scene MonoBehaviour (`GameManager.StartCoroutine`), log each step to a file in the scratchpad, and wait for it from the shell with a background loop such as `for i in $(seq 1 200); do grep -q "devices restored" log && break; sleep 3; done; cat log`.
5. If the scenario saves, set `SaveSlots.Root` to a scratchpad folder first (see `unity-playmode-check`); the real saves are then untouched.

## Input primitives
- Each step: `InputSystem.QueueStateEvent(device, state)` then `yield return null` ×2.
- Mouse: `new MouseState { position = p }`; press with `.WithButton(MouseButton.Left)`; scroll with `scroll = new Vector2(0, -120)` (negative zooms out).
- Keyboard: `new KeyboardState(Key.F1)` then `new KeyboardState()` to release; hold Shift (`Key.LeftShift`) across a click for shift-click.
- UI targets: `RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center))` (overlay canvas → null camera). Map cells: `Camera.main.WorldToScreenPoint(new Vector3(x + 0.5f, 0, y + 0.5f))`.
- Roads place one cell per click (`ConfirmPressed`); zoning paints while LMB is held (press, move through the cells, release). Escape cancels the tool / clears the selection.

## Pitfalls (each cost a run in M11g)
- **Map clicks are ignored over UI.** The HUD (top), toolbar (bottom), selection panel (right) and side panels (left) cover cells. Zoom out first, measure how W/A/S/D move the map on screen, and pan each target cell into a safe rect before clicking (`EnsureVisible` in the template). Close panels before clicking the map.
- **Visibility:** a path `GameObject.Find` is not proof an element is shown — check `activeInHierarchy` (`Shown()` in the template); locked toolbar buttons and the hidden BUILDINGS section depend on it.
- **Fast-forward through the UI:** F1 opens the debug panel; click its "Skip 30 days" button. Pause first so real time doesn't add days.
- If map input does nothing, log `InputReader.Pointer` against the virtual mouse position: `Pointer` must be a **Pass Through** action (it was a Value action until M11g and stuck to the first pointer device).
- Statics don't carry between RunCommands; write results to the log file.

## Reporting
End with `DONE, n failed checks`. Summarise what was exercised and every FAIL; distinguish driver bugs (fix and rerun) from game bugs (fix in the game, add a test where the sim is involved).

## Notes from the M18 play-through (UI-only, 23 checks, 0 failed)
- Scenario for a **presentation** milestone: New City by dialog, roads / zones by input, Skip by the debug panel, then check *effects* through state the systems already expose: `AudioController.PlayLog` (effects played), the ground `Tilemap`'s sprite names, `Tilemap.color` / `Light.intensity` / the `Kit.mat` emission for the night, `VehicleView.ActiveCount`, `GrowthVisuals` children (kit prefab names, `transform.forward` toward a road), the active `AudioSource` clip names for the ambience.
- **Time-based fades need real time:** `yield return new WaitForSecondsRealtime(1.5f)` (not frames) before reading the night factor or vehicle counts; the Editor in the background runs few frames.
- Debug-panel buttons made at runtime are found by their text (`ButtonWithText(DebugPanel, "Time of day")`); each press cycles noon / dusk / midnight / calendar.
- A DEBUG age jump that keeps the scenario short: `tech.StartNew(age); GameEvents.RaiseAgeChanged(age)` (grants the earlier techs, redevelopment follows with Skip).
- Runtime-built panels (Sound): find the button by name (`UI/HUD/Game/SoundButton`), the panel by component, sliders by `GetComponentsInChildren<Slider>`; click a slider at a fraction of its track from `GetWorldCorners`.
- Put `finally` cleanups behind null checks: when Awake failed once, the driver's `finally` threw and hid the real error.
- Reset PlayerPrefs-backed settings (volumes, mute, Lock to day) at the end; they persist in the Editor.

## Notes from the M15 play-through (UI-only, Budget panel)
- **Find runtime-built UI by what it says, not by order.** `FindObjectsByType` order is unspecified, so "the first Enact button" was a different ordinance each run: find the `TMP_Text` with the row's label and take the `Button` in its parent. Buttons made at runtime are named `<label>Button` (`Take loanButton`, `Repay $18,750Button`: match by prefix).
- **Click a slider track at a fraction of its width** (world corners to screen, inset by the handle half-width): 0 = minimum, 1 = maximum, 0.5 = the middle step. Setup shortcuts are fine when labelled DEBUG: the debug panel's Seed button plus two Skip clicks gave a 220-pop Industrial city.
- Hover target for the happiness tooltip is the `HappinessTooltip` component's own GameObject; read its text by searching all active `TMP_Text`s for `<b>Happiness</b>`.
- The scenario saves and loads: point `SaveSlots.Root` at a scratchpad folder first (see `unity-playmode-check`).

## Notes from the M14 play-through (UI-only, 49 checks, 0 failed)
- Scenario: Medieval 64² (wells, grow without services, tooltip, research the civic techs by clicking the next researchable step of their prerequisite chain, place from the toolbar, step the Services button) then Industrial 64² (plants and towers on the zoned row beside the road **before anything grows**, Constabulary, research Telegraph, read the outdated panel, Police Station by a held home).
- **A rule that shares a gate with a stronger one needs the right cell.** Pollution (−0.2) dwarfs crime (−0.05) in land value and keeps growing with industry, so pick the held home after the city settles (5 skips) and only one whose land value *without* the crime line clears the gate.
- Read the happiness tooltip by hovering the `HappinessTooltip` object and searching `TMP_Text`s for `<b>Happiness</b>`; the Services button is `GameObject.Find("ServicesView")` (made at runtime).
- Signed terms: "the penalty fell" means the (negative) term went *up*; compare per unit of ramp when the population changes between readings.

## Notes from the M13 play-through (UI-only)
- **View buttons toggle.** Clicking the active VIEW button turns the view off, and the chosen view survives New City; set `InfoOverlay.SetView(View.Off)` (or check `Chosen`) before a "click the view button" step, or a rerun in the same Play session fails it.
- **Debug jumps are fine when labelled.** Climbing four ages on a 32² map takes hundreds of days; `tech.Restore(age, TechSystem.StartingTechs(...), "", 0, [])` + `GameEvents.RaiseAgeChanged(age)` jumps there (log it as DEBUG). It grants only the starting techs (no Public Sanitation → no pump button) and skips `GameManager.HandleAgeAdvanced`, so the calendar doesn't move and the HUD water line stays stale until the next grid change.
- Pipes paint while LMB is held, like zones: `Drag(Line(...))`; a drag that starts on a pipe removes.

## Notes from the M12 play-through (UI-only, 0 failed checks)
- Scenario shape that worked for a *rule* milestone: New City (Industrial, 64²) → road, drag zones, place the plant from the toolbar → skip days with the debug panel until the rule bites (a home held at level 2) → open the view by its toolbar button → select the cell and **read the SelectionPanel text** (`GetComponentsInChildren<TMP_Text>(false)` under `UI/SelectionPanel`, then `Contains(...)`) → apply the fix by clicking (park) → skip days → check the level rose → Keep via the panel → `V` cycles the views.
- **Power runs out first.** A grown 64² test city fills one plant in ~90 days, and a home blocked by power looks like a failed rule: place a second plant by click before expecting an upgrade (`blocker PowerAtCapacity` in the log is the tell). Log `Power.Load/Supply` in `State()`.
- Close the F1 debug panel before clicking the map — it covers the left of the screen — and open it again only for `Skip`.
- Write the scenario as a `Run()` with `Check(...)` lines that log the numbers, run it once, fix the *driver* for anything that is not a game bug, and rerun; the log file plus `grep -q "devices restored"` is the completion signal.
- The scenario here did not save, so the save slot was untouched; back it up first only if yours does.


## Notes from M19b (menus)
- The HUD **New** button is hidden since M19b and **Load** is now **Menu**: start a New City by pressing Esc (nothing else open) and clicking the pause menu's `New cityButton` (`UI/PauseMenu/Panel/New cityButton`), and Load through `Load…` (`Load…Button`) and the browser's `Row_<name>` button. `PlaythroughDriver.cs` still has the older HUD paths: update them before reuse.
- Menu windows are code-built under the `UI` canvas (`PauseMenu`, `SaveBrowser`, `ConfirmDialog`, `NewCityDialog`): find a button by its label inside the window (`<label>Button` names collide between windows, so search under one window root, see the 19b driver idea: `ButtonByLabel(root, "Load")`). Text fields: set `TMP_InputField.text` directly (typing through the virtual keyboard was not needed).
- While a window is open the game is paused and `InputReader.Blocked` is true: gameplay keys do nothing, Esc still works. `GameFlow.Instance.State`, `.Browser`, `.Pause`, `EscapeRouter.LastHandled` are the state to check.
