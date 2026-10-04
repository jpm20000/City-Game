---
name: scene-prefab-editing
description: Edit City Game's Main.unity scene, UI and building prefabs, ScriptableObject assets and other Unity assets from scripts (Unity MCP RunCommand) without leaving bad diffs — opening/saving the scene, wiring references, building or extending prefabs, creating assets and hand-written .meta files. Use whenever a change touches a scene, prefab or asset rather than only C#.
---

# Scene, prefab and asset editing (City Game)

Prefer creating and wiring assets through `Unity_RunCommand` while the Editor is open; always check `git diff --stat` afterwards.

## Scene (`Assets/_Game/Scenes/Main.unity`)
- Do the edit in one RunCommand: `var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);` → edit → `EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);`.
- **Load assets after `OpenScene`.** `OpenScene(Single)` unloads assets loaded earlier in the same script; assigning those stale objects serializes `{fileID: 0}`. Check the diff for `{fileID: 0}`.
- Set fields through `SerializedObject` (`FindProperty("m_…")`, `objectReferenceValue`, `ApplyModifiedPropertiesWithoutUndo()`).
- **Driven layout values:** saving the scene serializes layout-driven `RectTransform` values of UI prefab children as overrides with value `0` — Unity's canonical form; the committed scene already has dozens. When a prefab gains children (e.g. new HUD groups), the next scene save adds the same canonical `0` overrides (anchors, size, anchored position) plus TMP `m_TextStyleHashCode` / `m_fontColor32` lines for them — expected and harmless. What's **not** fine: non-zero laid-out values, which appear if you re-save after the canvas has laid out (e.g. saving later in the same session after Play mode). Save right after `OpenScene` + your edit.
- Avoid new cross-prefab references on the HUD instance — put the reference on the new object instead (`NewCityDialog` → `GameMenu.NewRequested`; `TechPanel.m_ToggleButton` → `HUD/Budget/ResearchButton`).
- Reverting `Main.unity` with git while it's open briefly blocks the MCP bridge; afterwards reopen it with `EditorSceneManager.OpenScene` so the Editor doesn't re-save its stale copy. The Test Runner and Play sessions sometimes re-save the scene — check `git status` and revert unintended churn.
- Editing a Tilemap from an editor script: `MarkSceneDirty` + `EditorUtility.SetDirty(tilemap)` before `SaveScene`, else tiles don't persist.

## Prefabs
- Edit: `var root = PrefabUtility.LoadPrefabContents(path); try { …; PrefabUtility.SaveAsPrefabAsset(root, path); } finally { PrefabUtility.UnloadPrefabContents(root); }`.
- New UI prefab in the house style: load an existing one (e.g. `TaxPanel.prefab`), `Object.Instantiate` its parts (panel background, header, close button, text, buttons) into a new root, then `SaveAsPrefabAsset` (see how `TechPanel.prefab` was built in M11f).
- Nested prefab instances (e.g. `ToolButton` in the toolbar or as a template): use `PrefabUtility.InstantiatePrefab(prefabAsset, parent)`, not `Object.Instantiate` of an existing instance (that loses the prefab link).
- UI button colours come from the `Button`'s `ColorBlock` (normal/highlighted/pressed/disabled) multiplied by the `Image` colour — set the `ColorBlock`, keep the image white (cloned Demolish buttons are red through their `ColorBlock`).
- Building prefabs: root has `BuildingInstance` + a collider (selection raycasts hit it), layer 9; decorations go under a collider-less `Decor` child referenced by `BuildingInstance.m_Decor` (authored in world units on top of the base); shared materials only (copy an existing `Art/*.mat` and set `_BaseColor`).
- In editor scripts, `go.GetComponent<T>() ?? go.AddComponent<T>()` is unsafe (Unity fake-null); use an explicit `!= null` check.

## Generated art (M18)
- **Building kit, vehicles:** menu *CityBuilder > Generate Building Kit* (`Scripts/Editor/BuildingKitGenerator.cs`, designs in `KitDesigns.cs`, vehicles in `KitVehicles.cs`) writes the palette textures, `Kit.mat`, meshes, prefabs, the `AgeVisualSet` `Prefabs` slots and `VehicleSet`; deterministic and in place. **Append** palette swatches, never insert (indices are baked into meshes). A hand-made prefab replaces a slot in the Inspector, but a re-run overwrites the slots it owns. `ArtContract` (*Validate Art*) checks any prefab you import.
- **Run it only after the compile finished:** `EditorApplication.ExecuteMenuItem` right after editing the generator runs the *old* code (the Editor has not reloaded yet); check the console for compile errors, wait, then run. The first kit was regenerated that way and missed a fix for a whole session.
- **Tiles and audio:** `Tools/gen_tiles.py`, `Tools/gen_audio.py` (stdlib only); Unity rewrites the `.meta`s on import, keep its version (the scripts then reuse the GUID in it).
- After `OpenScene(Single)` load every asset you assign again (a stale reference serialises as `{fileID: 0}` or throws on `new SerializedObject`).
- Runtime changes to a shared material asset stick in the Editor: `DayNightCycle` restores `Kit.mat`'s emission on destroy / quit; check `git status` after Play for float-format churn in `Art/*.mat` and revert it.

## Assets
- ScriptableObjects: `ScriptableObject.CreateInstance<T>()` + `AssetDatabase.CreateAsset`, set private fields via `SerializedObject`, `EditorUtility.SetDirty`, `AssetDatabase.SaveAssets()`.
- Age / tech / age-visual content is **generated**, not hand-edited: edit the tables in `Tools/gen_age_content.py` and run it (deterministic GUIDs keep references stable), then `AssetDatabase.Refresh()`.
- `AssetDatabase.CreateAsset` can't create `.inputactions` — write `InputActionAsset.ToJson()` to the path, then `ImportAsset` (or edit the JSON directly and reimport).
- **Hand-written assets:** a new `.cs` file referenced from a scene or asset needs a `.meta` with a GUID before Unity imports it (`fileFormatVersion: 2` + `guid: <32 hex>` + the `MonoImporter` block); YAML assets need the script's GUID in `m_Script` and `m_EditorClassIdentifier: <Assembly>::<Class>`.
- Commit every asset's `.meta`; never delete or ignore them.
- Asset deletion via MCP pops a modal — delete in the Editor UI or with git when nothing references it.
