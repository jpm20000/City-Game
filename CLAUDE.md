# CLAUDE.md

Conventions and standing rules are in `AGENTS.md` (shared with OpenCode; keep them there), per-system notes in `Docs/GamePlan.md` §13, procedures in `.claude/skills/`:

@AGENTS.md

## Claude Code specifics
- **Unity MCP** tools are `mcp__unity-mcp__*` (Editor must be open).
- **Saves:** real saves are in `%USERPROFILE%/AppData/LocalLow/J-man Studios/Chronopolis/Saves/`. A Play-mode check that saves sets `SaveSlots.Root` to a scratchpad folder after startup; a player gets `-savesDir <path>`.
- **Shell edits:** Bash heredocs with apostrophes or non-ASCII (`×`, `—`) can fail to parse or match. For multi-line patches write a Python script to the scratchpad with the Write tool that asserts every replacement matched. Docs and scripts are mostly CRLF: preserve it (read with `newline=""`, normalise, write the original endings back).
