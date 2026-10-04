---
name: balance-tuning
description: Tune City Game's simulation balance — BalanceConfig values, age/tech content costs and thresholds — using pure-sim harnesses (SeededCity, EngagedCity) and lock the results into EditMode balance tests. Use when changing tunables, adding a system that affects growth/economy/happiness/research, or when a milestone's balance targets need checking.
---

# Balance tuning (City Game)

## Where the numbers live
- `BalanceConfig` (`Scripts/Simulation/BalanceConfig.cs`): every sim tunable. **The code default and `Scriptables/Balance/BalanceConfig.asset` must hold the same value** — tests use `ScriptableObject.CreateInstance<BalanceConfig>()` defaults, the game uses the asset. Tag changed fields "(tuned, M1x)" in the tooltip with the reason.
- Age/tech content (entry population, advance costs, tech costs and effects, starting money): the tables in `Tools/gen_age_content.py` — edit, run it, `AssetDatabase.Refresh()`. Research-building output and costs: the `Scriptables/Buildings/*.asset` definitions.
- Current values and the reasoning behind them: `Docs/GamePlan.md` §13 (Simulation, Services & power, Ages & research → Balance, Land value & pollution) and §7 (the original formulas, partly stale).

## Baselines that must not drift by accident
- **The age-less sim** (`SimulationSystem` without age/tech databases) is the legacy baseline: `SimulationTests.RunSeededCity` numbers (e.g. 212 pop / 0.60 happiness at day 60 with the plant). Growth pace (`MaxGrowthPerDay`) is shared with it — fit new content to the pace rather than changing it, unless the plan decides to rebaseline.
- **Industrial start with real content**: 220 pop / 0.647 happiness / +0.05 Technology at day 60 (`AgeBalanceTests.IndustrialStart_RealContent_Baseline`, accepted in M11g).

## Harnesses
- **`SeededCity`** (`Tests/EditMode/Simulation/SeededCity.cs`): the fixed 24² seeded layout (road cross, R/C/I strips, plant at `(0,9)`, optional parks at `(5,14)`/`(14,16)`), `Run(grid, config, days, plantSupply, parks, jobTax, ages, techs, startAge)`. Same layout as `PlacementController.DebugSeedCity`, so numbers translate directly into test assertions.
- **`EngagedCity`** (`Tests/EditMode/Simulation/EngagedCity.cs`): the shipped assets played by an engaged player on a 64² map — a road from the map edge, blocks opened nearest-first and zoned for the zone in demand once existing blocks can't grow or upgrade (only the 12 road-facing cells of each 4×4 block), research kept going (advance as soon as allowed; research-building and power techs first), Monasteries/Academies/parks/plants built as they unlock and are needed, all paid for with a cash cushion. `Report()` prints the age timeline, money/happiness minima, buildings and per-zone level/blocker diagnostics. **When a milestone adds something a player must build or manage (water, services, roads…), extend its player model** or the balance tests will under-play the city.
- **Fast iteration:** `AgeBalanceTests.Report_FromMedievalWithAsset` (`[Explicit]`, run it by name) uses the **asset**, so edit the asset values (and regenerate content) and rerun without a domain reload; copy the final values into the code defaults afterwards. Alternatively run the pure sim in a RunCommand on a `CreateInstance<BalanceConfig>()` with private `m_` fields set through `SerializedObject` — never edit the asset just to try values outside this explicit report loop.
- Print reports with `TestContext.WriteLine` and read them from `ITestResultAdaptor.Output` (see `unity-test-loop`).

## Targets (GamePlan §12) and tests
- About 45–90 in-game days per age with engaged play; no age where the city stalls; never in debt; happiness ≥ 0.55. M11g result from Medieval: 86 / 62 / 63 days, Modern on day 211; M13b: 86 / 62 / 69, Modern on day 217; M14d: 84 / 61 / 73, Modern on day 218.
- `AgeBalanceTests` assert these with `CreateInstance` defaults (`FromMedieval_EachAgeTakes45To90Days_AndReachesModern`, `LaterStart_KeepsGrowing(1..3)`, the Industrial baseline). Update their numbers and comments deliberately when a plan changes balance, and record the new numbers in `AGENTS.md` and GamePlan §7/§12.

## Debugging a stalled harness
Look at the diagnostics line first: demand per zone, housing/jobs, cells per level and the `GrowthBlocker` counts. In M11g every "the game is too slow" turned out to be the harness (whole-map road upkeep, unreachable inner cells counted as free land, new land starving upgrades) until the diagnostics proved otherwise.

## Keeping the harnesses honest (added in M12)
- `SeededCity` and `EngagedCity` build their sources by hand: when a building asset or age gains a sim field (power supply, pollution, …) copy it into `SeededCity.Build` / `EngagedCity.LoadBuildings` (it reads the building assets through `SerializedObject`).
- When a rule holds players back (e.g. the land-value gate), teach `EngagedCity` the response a player would make (it places a park for held homes) before judging the balance — otherwise the harness measures a player who ignores the game.
- A run's `Diagnostics()` line lists the `GrowthBlocker` per zone and level; a new blocker that dominates it (e.g. `LowLandValue:10`) shows what the rule costs.
- Current M12 numbers: seeded 24² city, day 60, 10% taxes, with plant: 212 pop, 0.62 happiness (0.70 with the two parks); 20% C/I taxes without parks stalls at ~22 pop. Industrial start (real content): 220 pop / 0.660. From Medieval: 86 / 62 / 63 days per age (M13b: 86 / 62 / 69).
- **Harness players must save for blocking utilities** (M13b): an `EngagedCity` that keeps buying parks and roads while a plant or tower is what's blocking growth never affords it — the Industrial age took 100+ days until the player stopped other spending while power or water supply was below demand. When a new must-build arrives, check the probe timeline for a city hovering just below a purchase threshold.
- **Current M14d numbers:** seeded 24² city (no civic buildings) happiness 0.589 / 0.556 / 0.504 at day 60 / 90 / 120; Industrial start (real content) 220 pop / 0.631; later starts at day 120: Renaissance / Industrial / Modern 447 / 557 / 630 pop, happiness 0.71 / 0.72 / 0.83.
- **Cash vs. daily deficit (M14d):** a later-age start begins rich but loses money each day until it grows, so "the surplus must carry the upkeep" refuses expensive services until the cash is gone. Let the player build on cash when it covers ~60 days of the deficit the building leaves, but only for a need that costs real happiness (≥ 0.03); without that floor the Modern start bought a Hospital early and went into debt. Trace why each purchase was skipped (cash, upkeep, no spot) with a throwaway `Debug` string in the harness before changing prices.

