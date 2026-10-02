# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Project conventions, architecture and per-system notes live in `AGENTS.md` (shared with OpenCode) — keep them there, not here:

@AGENTS.md

Design intent, balance formulas and the milestone roadmap are in `Docs/GamePlan.md` (sections marked stale in `AGENTS.md` take precedence from `AGENTS.md`).

## Claude Code specifics

- **Unity MCP** is registered for Claude Code as `unity-mcp` (tools `mcp__unity-mcp__*`; `opencode.json` registers the same relay as `unity` for OpenCode). It only works while the Editor is open. Typical loop after editing scripts: `Unity_GetConsoleLogs` (errors/warnings) → `Unity_RunCommand` for scene/asset work. `RunCommand` scripts can use game types from `Assembly-CSharp` directly (e.g. `Object.FindAnyObjectByType<GameManager>()`, `SerializedObject` to set private `m_` fields).
- **Play-mode checks via MCP:** set `EditorApplication.isPlaying = true` in one `RunCommand`, drive the game in the next (e.g. `PlacementController.DebugSeedCity()`, `GameManager.Clock.SetSpeed(...)`), then set it back to `false`. `Unity_Camera_Capture` without an id returns the Scene View, not the game camera; to see the game view, render `Camera.main` into a `RenderTexture` and write a PNG to the scratchpad, then read it.
- **Editor closed, compile check only:** a throwaway SDK-style `.csproj` in the scratchpad (never in the repo) that `<Compile Include>`s `Assets/_Game/Scripts/**` and references `C:/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Data/Managed/UnityEngine/*.dll` plus `Library/ScriptAssemblies/Unity.InputSystem.dll` (and `Library/PackageCache/com.unity.ext.nunit@*/net472/unity-custom/nunit.framework.dll` for tests) catches C# errors. Compile Grid + Simulation sources on their own to catch asmdef-boundary violations (they must not reference `Assembly-CSharp` types). This does not replace the Test Runner — `ScriptableObject.CreateInstance` and other native calls fail outside Unity.
- **Hand-written assets:** new `.cs` files referenced from a scene or asset need a `.meta` with a GUID before Unity imports them (`fileFormatVersion: 2` + `guid: <32 hex>`); prefer creating assets/scene objects through `RunCommand` when the Editor is open.
