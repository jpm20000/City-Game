---
name: milestone-workflow
description: Plan and implement a City Game roadmap milestone or milestone step (M12–M19, e.g. "implement M12", "implement M13b") — writing the plan in Docs/GamePlan.md §12, the per-step implement/verify/document/commit loop, and the done-when checklist. Use at the start of any milestone or step.
---

# Milestone workflow (City Game)

Where things live: standing rules → `AGENTS.md` (**Roadmap M11–M19**); plans, outlines and where each later milestone plugs in → `Docs/GamePlan.md` §12 (M11 and M12 are the worked examples); how each system is built and tuned → `Docs/GamePlan.md` §13 *Systems reference*. `AGENTS.md` stays short: conventions and standing rules only.

## Starting a milestone (no plan yet)
1. Expand its §12 outline into a §11-style plan in `Docs/GamePlan.md`: goal, done-when, design decisions (defaults the user can change), architecture (pure sim vs runtime tables), lettered steps that each fit a session and leave the game playable, content tables, risks / open questions.
2. Check the plan against the standing rules in `AGENTS.md` (age-less baseline, saves + migration, per-cell state, content generator, rendering, UI patterns) and the milestone's entry in §12 *Where M13–M19 plug in*.
3. Commit the plan on its own. Ask the user only about decisions that are genuinely theirs (design trade-offs, accepting a changed baseline — M12 asked exactly once: should the age-less sim change too); offer a recommended default and record the answer in the plan's decision table.

## Each step
1. Re-read the step and its design decisions in §12. Read the code it touches before editing.
2. Implement. Pure logic first (Simulation asmdef) with EditMode tests, then runtime/UI.
3. Verify:
   - compile check + EditMode tests (`unity-test-loop`), and `git status` for unintended scene/settings churn;
   - Play-mode check of the visible change (`unity-playmode-check`), with screenshots for visual work;
   - `perf-benchmark` if the step adds renderers or heavy per-frame/per-tick work;
   - `balance-tuning` if it touches growth, economy, happiness or research.
4. Document, in the same commit as the code: the matching **GamePlan §13** system section(s) (what it does, formulas, tuned numbers, test names — keep it current, fix stale lines you notice) and the step's line in §12 marked "— done (date)" with notes from implementing (decisions taken, numbers measured, what a tuning pass changed and why). Touch `AGENTS.md` only when a convention or standing rule changes (never to describe a system), and add `CLAUDE.md` / skill notes for new tooling gotchas.
5. Commit the step on its own with a descriptive message (and the attribution lines from the system reminder); push when the user has asked for pushes in this workflow.
6. Report to the user: what changed, what was verified (with numbers), anything that deviates from the plan or needs their decision.

## Finishing a milestone (done-when)
- All EditMode tests green.
- A **UI-only virtual-input play-through** of the milestone's player loop passes (`virtual-input-playthrough`).
- Balance targets still met (`AgeBalanceTests`), benchmark re-run if the plan asked for it.
- Docs: GamePlan §13 (systems as built), §12 (milestone marked done, step notes, status paragraph naming the next milestone) and §8 status; `AGENTS.md` Project bullet ("done / next") and `CLAUDE.md` header line ("M0–M1x are done"); update the plug-in notes for later milestones if this one changed the code they build on.

## Lessons from M12 (carry forward)
- **Step order that worked:** plan → pure sim + content + tests (12a/12b) → UI (12c) → harness + balance + play-through + docs (12d). Each step committed green; a baseline-changing step records the old and new numbers in its §12 note.
- **A new sim term shifts the legacy tests.** Run the whole suite right after wiring it in; failures in the seeded-city ranges are the signal to *tune* (M12: pollution per point 0.02 → 0.06), not to loosen the asserts. Re-record `IndustrialStart_RealContent_Baseline` only after tuning settles.
- **Harnesses must mirror the assets.** A new field on a building asset or age (e.g. `Pollution`, `PollutionScale`) needs the same value in `SeededCity` and in `EngagedCity.LoadBuildings`, or the tests play a different game than the shipped one.
- **Probe, don't guess:** a throwaway `[Test, Explicit]` that prints a table with `TestContext.WriteLine` (see `unity-test-loop`) is the fast way to tune; delete it (and its `.meta`) before committing.
- **Colour ramps for views** should be centred on the number the player cares about (the level-3 threshold), not on 0..1 — most cells sit in a narrow band.

