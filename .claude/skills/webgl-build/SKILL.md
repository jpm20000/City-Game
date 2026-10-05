---
name: webgl-build
description: Build and publish the City Game / Chronopolis WebGL version, keep desktop code paths unchanged, and switch the Editor back to Windows afterwards. Use when building for the browser, touching code that must differ on WebGL, or debugging a web-only problem.
---

# WebGL build (post-M19)

The WebGL version lives on `main` behind `#if UNITY_WEBGL` guards, so the Windows game is unchanged. The first browser build was checked by hand in a browser (title, Settings, saves not yet stress-tested); there is no automated WebGL test.

## Building
- **Menu:** `CityBuilder > Build WebGL` (`Scripts/Editor/Release/WebGLBuild.cs`) writes `Build/WebGL` (gzip, decompression fallback on, so any static host works without response headers).
- **To another folder** (used so far: `D:\Chronopolis web`): a `RunCommand` that queues `BuildPipeline.BuildPlayer` with `EditorApplication.delayCall` (scenes `Assets/_Game/Scenes/Main.unity`, target `BuildTarget.WebGL`, `locationPathName` = the folder), writes `DONE ...` to a log file in `Build/`, and a shell loop that waits for `DONE`. A build takes 2 to 4 minutes; the Editor is busy meanwhile.
- **The build files are named after the output folder** (`Chronopolis web.loader.js`, `.data.unityweb`, `.framework.js.unityweb`, `.wasm.unityweb`). A space in the name is awkward in URLs: build into a folder without one if the host has trouble.
- **A rebuild into an existing folder does not rewrite `index.html`** (or `TemplateData`). The project's page settings (`defaultScreenWidthWeb` / `Height`) only matter for the page Unity generates; the owner's host manages its own `index.html` (it scales the canvas to the browser window), so ship only `Build/` and `StreamingAssets/`.
- The build prints one error from Unity's own package (`FailedDownload.wav` in `com.unity.ai.assistant`, AAC encode). It is harmless: the result is Succeeded.

## Web build profile
`Assets/Settings/Build Profiles/Web - Desktop - Release.asset` is a Unity build profile with **its own copy of the player settings** (`m_PlayerSettingsYaml`): compression Gzip, decompression fallback, 512 MB initial memory, web canvas 1920x1080. A setting changed in Project Settings can be overridden by the profile while it is active: edit both.

## Back to Windows (do this before any desktop work)
The profile stays active and the target stays WebGL after a build. In a `RunCommand`: `BuildProfile.GetActiveBuildProfile()` -> `BuildProfile.SetActiveBuildProfile(null)`, then `EditorUserBuildSettings.SwitchActiveBuildTarget(NamedBuildTarget.Standalone, BuildTarget.StandaloneWindows64)`. Wait about 90 s for the reimport (a `LiberationSans.ttf.meta` version warning is harmless: do not re-save it). Then run the EditMode tests and, for a release, `ReleaseBuild` and `-smokeTest`.
- Web builds and target switches churn `Assets/Settings/*`, `ProjectSettings/GraphicsSettings.asset`, `ProjectSettings/UnityConnectSettings.asset` and sometimes `ProjectSettings.asset` (preloaded assets, the `WebGL:` scripting define). `git status`, then `git checkout --` the churn; commit only intended settings.
- A stray `Data/` folder with Burst `.wasm` output can appear in the repository root after a web build: never commit it.

## What differs on WebGL (all `#if UNITY_WEBGL`)
- **No Quit button** on the title and pause menus (a tab cannot quit).
- **No Display tab** in Settings and `GameSettings.ApplyDisplay` does nothing (the browser owns window, resolution and frame rate). `SettingsPanel` keeps `m_TabIds` so the highlighted tab button follows the tab, not the button index (the first web build lit the wrong tab).
- **The showcase city** is fetched with `UnityWebRequest` (`StreamingAssets` is a URL, `File` can't read it): `SaveGameController.LoadShowcaseWeb`. It arrives after `GameFlow` finished its own showcase load, so it brackets the apply with `GameFlow.ShowcaseApplying(true/false)`; without that `OnCityLoaded` took it for a real load and left the title menu. It also applies only while the player is still on the title.
- The rest is unchanged: saves still use `File` under `persistentDataPath` (the browser keeps them in IndexedDB), `Application.wantsToQuit` never fires.

## Not checked
Save durability across a closed tab and cleared browser data, performance and memory on a large city, mobile browsers, high-DPI sharpness (a page setting). Do not claim them without a browser test.

## Browser caching
After uploading a new build, a stale `.wasm` / `.data` can still be served (browser cache, CDN, Unity's IndexedDB data cache): test in a private window with the cache disabled, or compare the served size of the `.wasm` with the file on disk. A fix that is "not working" in the browser was a stale file the first time.
