---
name: milestone-workflow
description: Plan and implement a City Game roadmap milestone or milestone step (M12–M19, e.g. "implement M12", "implement M13b") — writing the plan in Docs/GamePlan.md §12, the per-step implement/verify/document/commit loop, and the done-when checklist. Use at the start of any milestone or step.
---

# Milestone workflow (City Game)

Roadmap and standing rules: `AGENTS.md` → **Roadmap M11–M19**; detailed plans and outlines: `Docs/GamePlan.md` §12 (M11's full plan and step notes are the worked example).

## Starting a milestone (no plan yet)
1. Expand its §12 outline into a §11-style plan in `Docs/GamePlan.md`: goal, done-when, design decisions (defaults the user can change), architecture (pure sim vs runtime tables), lettered steps that each fit a session and leave the game playable, content tables, risks / open questions.
2. Check the plan against the standing rules in `AGENTS.md` (age-less baseline, saves + migration, per-cell state, content generator, rendering, UI patterns).
3. Commit the plan on its own. Ask the user only about decisions that are genuinely theirs (design trade-offs, accepting a changed baseline); offer a recommended default.

## Each step
1. Re-read the step and its design decisions in §12. Read the code it touches before editing.
2. Implement. Pure logic first (Simulation asmdef) with EditMode tests, then runtime/UI.
3. Verify:
   - compile check + EditMode tests (`unity-test-loop`), and `git status` for unintended scene/settings churn;
   - Play-mode check of the visible change (`unity-playmode-check`), with screenshots for visual work;
   - `perf-benchmark` if the step adds renderers or heavy per-frame/per-tick work;
   - `balance-tuning` if it touches growth, economy, happiness or research.
4. Document: `AGENTS.md` Systems section(s) for what changed (keep it current, fix stale lines you notice), and the step's line in GamePlan §12 marked "— done (date)" with notes from implementing (decisions taken, numbers measured). Add `CLAUDE.md` / skill notes for new tooling gotchas.
5. Commit the step on its own with a descriptive message (and the attribution lines from the system reminder); push when the user has asked for pushes in this workflow.
6. Report to the user: what changed, what was verified (with numbers), anything that deviates from the plan or needs their decision.

## Finishing a milestone (done-when)
- All EditMode tests green.
- A **UI-only virtual-input play-through** of the milestone's player loop passes (`virtual-input-playthrough`).
- Balance targets still met (`AgeBalanceTests`), benchmark re-run if the plan asked for it.
- Docs: `AGENTS.md` (Systems + the Roadmap section's status line / plug-in notes), GamePlan §8 status, §12 milestone status (and next milestone), `CLAUDE.md` header line ("M0–M1x are done").
