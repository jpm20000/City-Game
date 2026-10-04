---
name: milestone-workflow
description: Plan and implement a City Game roadmap milestone or milestone step (M12–M19, e.g. "implement M12", "implement M13b") — writing the plan in Docs/GamePlan.md §12, the per-step implement/verify/document/commit loop, and the done-when checklist. Use at the start of any milestone or step.
---

# Milestone workflow (City Game)

Where things live: standing rules → `AGENTS.md` (**Roadmap M11–M19**); the roadmap, outlines of milestones not yet started and where each later milestone plugs in → `Docs/GamePlan.md` §12; **each milestone's full plan lives in its own file, `Docs/milestones/Mxx.md`** (read only the one you need; M15 is the live example, M11 / M12 / M14 the finished worked examples); how each system is built and tuned → `Docs/GamePlan.md` §13 *Systems reference*. `AGENTS.md` stays short: conventions and standing rules only.

## Starting a milestone (no plan yet)
1. Expand its §12 outline into a plan in a new `Docs/milestones/Mxx.md` (model it on `M15.md`; leave a short stub with a link in GamePlan §12): goal, done-when, design decisions (defaults the user can change), architecture (pure sim vs runtime tables), lettered steps that each fit a session and leave the game playable, content tables, risks / open questions.
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
- Docs: GamePlan §13 (systems as built), §12 (stub + status paragraph naming the next milestone) and the milestone's own `Docs/milestones/Mxx.md` (marked done, step notes) and §8 status; `AGENTS.md` Project bullet ("done / next") and `CLAUDE.md` header line ("M0–M1x are done"); update the plug-in notes for later milestones if this one changed the code they build on.

## Lessons from M14 (carry forward)
- **Steps done without the Editor owe an Editor pass.** 14b / 14c ran in the sim harness; 14d started by compiling, running the Test Runner and taking the owed screenshots before any new work. List what is owed in the step note so the next session can't miss it.
- **A must-build the harness player never builds is a balance bug in the harness first.** Trace it day by day (why was each line skipped: cash, upkeep, no spot) before touching prices; the Hospital needed a player rule (build on cash when the deficit is covered) *and* a price cut.
- **Measure the hot path, not the recompute.** Civic cover recomputes in 0.02 ms, but the per-home needs read on every tick cost 0.22 ms until they got a fast path; time `ServiceStats.Measure` with and without the new term.

## Lessons from M13 (carry forward)
- **Check plan assumptions against validators before content work.** M13's plan made Waterworks a required tech of the Industrial age; `TechDatabase.Validate` only allows required techs from an earlier age, so it followed the Electricity precedent (a starting tech) instead.
- **Every new must-build needs the harness player to respond — and to save for it.** The engaged player that kept buying parks and roads while a tower was blocking growth stalled the Industrial age for 100+ days (see `balance-tuning`).
- **The toolbar and HUD are full at 1920.** New buttons need room: building buttons narrow, view buttons are 88 px and the toolbar scales itself down when it overflows; put a new readout inside an existing HUD group (water sits under power).
- **Measure networks with flow.** A benchmark city without sources hides per-cell network cost; `PerfBenchmark` now feeds power and water.

## Lessons from M12 (carry forward)
- **Step order that worked:** plan → pure sim + content + tests (12a/12b) → UI (12c) → harness + balance + play-through + docs (12d). Each step committed green; a baseline-changing step records the old and new numbers in its §12 note.
- **A new sim term shifts the legacy tests.** Run the whole suite right after wiring it in; failures in the seeded-city ranges are the signal to *tune* (M12: pollution per point 0.02 → 0.06), not to loosen the asserts. Re-record `IndustrialStart_RealContent_Baseline` only after tuning settles.
- **Harnesses must mirror the assets.** A new field on a building asset or age (e.g. `Pollution`, `PollutionScale`) needs the same value in `SeededCity` and in `EngagedCity.LoadBuildings`, or the tests play a different game than the shipped one.
- **Probe, don't guess:** a throwaway `[Test, Explicit]` that prints a table with `TestContext.WriteLine` (see `unity-test-loop`) is the fast way to tune; delete it (and its `.meta`) before committing.
- **Colour ramps for views** should be centred on the number the player cares about (the level-3 threshold), not on 0..1 — most cells sit in a narrow band.

