# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Project conventions and standing rules live in `AGENTS.md` (shared with OpenCode) — keep them there, not here. Per-system notes (what each system does, tuned numbers, tests) live in `Docs/GamePlan.md` §13, not in `AGENTS.md`:

@AGENTS.md

Design intent, balance formulas, the milestone roadmap (§12) and the systems reference (§13) are in `Docs/GamePlan.md` (where an earlier section disagrees with §13 or `AGENTS.md`, those describe the code as built). M0–M18 are done; §12 holds the M10–M19 ages roadmap (M19 next) and each milestone's full plan is `Docs/milestones/Mxx.md` — when starting a milestone, expand its §12 outline into such a file (goal, done-when, lettered steps, risks) before implementing.

## Claude Code specifics

- **Unity MCP** is registered for Claude Code as `unity-mcp` (tools `mcp__unity-mcp__*`; `opencode.json` registers the same relay as `unity` for OpenCode). It only works while the Editor is open.
- **Procedures live in project skills** (`.claude/skills/`, also read by OpenCode) — load the one that fits the task:
  - `milestone-workflow` — planning and implementing a roadmap milestone or step (M12–M19).
  - `unity-test-loop` — RunCommand rules and quirks, running EditMode tests via MCP, offline compile checks, the no-Editor sim harness (`Tools/sim-harness`), domain reloads.
  - `unity-playmode-check` — Play-mode checks, fast-forwarding, game-view / UI screenshots.
  - `virtual-input-playthrough` — UI-only play-throughs with a virtual mouse and keyboard (template driver included).
  - `scene-prefab-editing` — editing `Main.unity`, prefabs and assets from scripts without bad diffs.
  - `perf-benchmark` — development player build + `-perfBenchmark`, baselines, cleaning up build churn.
  - `balance-tuning` — `BalanceConfig` / content tuning with the `SeededCity` and `EngagedCity` harnesses.
- **Saves (M19a):** saves are named files in `%USERPROFILE%/AppData/LocalLow/DefaultCompany/City Game/Saves/` (the old `city.json` is copied in once as "Imported city"; never touched). A Play-mode check that saves must set `SaveSlots.Root` to a scratchpad folder first (after startup: `SaveGameController.Awake` sets it), or pass `-savesDir <path>` to a player, so the real saves stay untouched.
- **Shell edits:** in the Bash tool a heredoc whose body contains apostrophes (e.g. C# strings like `"can't"`) or non-ASCII characters (`×`, `—`) can fail to parse or match even when quoted (`<<'EOF'`). For multi-line patches, write a Python script to the scratchpad with the Write tool and run it, and have it assert that every replacement matched. Most docs and scripts are CRLF: let Python's default text mode write them (never `newline=""`, which turns the whole file into LF), and note that a heredoc also collapses `\n` to `
` inside the script.
