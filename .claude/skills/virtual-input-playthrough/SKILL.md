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
5. Back up `city.json` first if the scenario saves (see `unity-playmode-check`), and restore it afterwards.

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
