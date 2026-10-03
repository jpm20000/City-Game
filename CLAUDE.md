# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Project conventions, architecture and per-system notes live in `AGENTS.md` (shared with OpenCode) — keep them there, not here:

@AGENTS.md

Design intent, balance formulas and the milestone roadmap are in `Docs/GamePlan.md` (sections marked stale in `AGENTS.md` take precedence from `AGENTS.md`). M0–M12 are done; §12 holds the M10–M19 ages roadmap (M13 next) — when starting a milestone, expand its outline there into a §11-style plan (goal, done-when, lettered steps, risks) before implementing.

## Claude Code specifics

- **Unity MCP** is registered for Claude Code as `unity-mcp` (tools `mcp__unity-mcp__*`; `opencode.json` registers the same relay as `unity` for OpenCode). It only works while the Editor is open.
- **Procedures live in project skills** (`.claude/skills/`, also read by OpenCode) — load the one that fits the task:
  - `milestone-workflow` — planning and implementing a roadmap milestone or step (M12–M19).
  - `unity-test-loop` — RunCommand rules and quirks, running EditMode tests via MCP, offline compile checks, domain reloads.
  - `unity-playmode-check` — Play-mode checks, fast-forwarding, game-view / UI screenshots.
  - `virtual-input-playthrough` — UI-only play-throughs with a virtual mouse and keyboard (template driver included).
  - `scene-prefab-editing` — editing `Main.unity`, prefabs and assets from scripts without bad diffs.
  - `perf-benchmark` — development player build + `-perfBenchmark`, baselines, cleaning up build churn.
  - `balance-tuning` — `BalanceConfig` / content tuning with the `SeededCity` and `EngagedCity` harnesses.
- **Save slot:** the HUD Save button / F5 / `SaveGameController.Save()` overwrite the player's real save (`%USERPROFILE%/AppData/LocalLow/DefaultCompany/City Game/city.json`). Back it up to the scratchpad before anything that saves and restore it afterwards.
- **Shell edits:** in the Bash tool a heredoc whose body contains apostrophes (e.g. C# strings like `"can't"`) or non-ASCII characters (`×`, `—`) can fail to parse or match even when quoted (`<<'EOF'`). For multi-line patches, write a Python script to the scratchpad with the Write tool and run it, and have it assert that every replacement matched.
