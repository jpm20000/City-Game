# Archive — milestones M10–M14 (done)

Full plans, design decisions, step notes and "Original plan" text of the finished milestones M10–M14,
moved out of `GamePlan.md` §12 on 2026-10-04 (verbatim) because the file had outgrown a single read.
Nothing here is current guidance: the code as built is described in `GamePlan.md` §13 and `AGENTS.md`.
Read this file for the *why* behind a number or a decision, or as a worked example of a milestone plan
(M11, M12 and M14 are the longest). Section references such as "§12" and "§7" inside it mean `GamePlan.md`.

---

### M10 — Scale

**Goal:** a map big enough for a city that lives through four ages, with the size chosen per
city.
**Done when:** New City offers 32/64/96; a 96² city seeded and run at 4× keeps 60 fps; v1 saves
load as 24² cities; all tests pass.

- **10a Variable map size — done (2026-10-02).** Instead of rebuilding the world, the one
  `GridData` is **resized in place** (`GridData.Resize` → `OnResized`), so nothing rebinds and no
  handlers can leak. `RoadNetwork`, `PowerSystem`, `CoverageSystem` (via `SimulationSystem`),
  `GridTilemapView`s, `InfoOverlay` and `GrowthVisuals` reallocate their per-cell state on
  `OnResized`. `GameManager` repaints the ground (`GridSystem.PaintGround`) and raises
  `GameEvents.WorldResized`, and the camera reframes the map's centre (max zoom grows with the map).
  `SaveSystem.ApplyGrid` resizes to the save's size, and `SaveGameController.NewCity(size)` exists.
  The scene now starts at 64². Play-mode check: 64² seeded city = 212 pop at day 60, the same as
  24² (growth is limited by the daily budget, not the area); 96² sim tick 0.35 ms; the 24² v1 save
  loads with the original camera framing. 82 EditMode tests green.
- **10b Performance — done (2026-10-03).** Measured in a development player (`PerfBenchmark`,
  `-perfBenchmark`) on a fully grown 96² city (5,776 level-3 cells, 11.5k blocks). The sim was never
  the problem (tick 0.5 ms), and neither were the tilemap views (~1 ms main thread for a per-frame
  edit). The cost was **drawing**: `GrowthVisuals` tinted every block with a `MaterialPropertyBlock`,
  which opts each renderer out of the SRP Batcher and the GPU Resident Drawer — 23 ms frames at the
  default zoom (render thread 22 ms) and 110 ms with the whole map on screen. Switching to shared
  per-colour materials brought that to **1.1 ms / 1.5 ms** (≈2 ms with an edit every frame). Blocks
  are also pooled now (hidden, not destroyed). The camera backs off further on big maps, which
  were clipping their near corner. Editor-only numbers were misleading (GameObject churn looks like
  ~5 ms there), so performance is checked in a player build.
- **10c Save versioning — moved to M11.** v1 saves already store `Width`/`Height`, so M10 needs
  no format change. The migration chain is built in M11 with its first real step (v1 → v2).
  `TryFromJson` now rejects sizes above `SaveSystem.MaxMapSize` (256).
- **10d New City dialog — done (2026-10-03).** `NewCityDialog` prefab: dimmed blocker + panel with
  32 / 64 / 96 size buttons (current size preselected), Cancel / Esc and Create; replaces the
  double-click "New" (`GameMenu.NewRequested`). Verified with virtual input in Play mode: New opens
  it, 96 + Create discards the seeded 64² city and reframes the camera, map clicks under the open
  dialog place nothing, Cancel / Esc leave the city alone. 82 EditMode tests green.

**Risks:** anything new that caches per-cell state must handle `GridData.OnResized`. Note that
`MaxGrowthPerDay` doesn't scale with the area, so big maps fill at the same pace as small ones.

### M11 — Ages & technology (plan, 2026-10-03)

**Goal:** the age/tech framework with enough thin content to play from Medieval to
Modern on placeholder art: research points, a tech tree, player-triggered age advancement,
a starting age, per-age growth rules, automatic redevelopment of outdated blocks, and "Keep
historical building".

**Done when:**
- A city started in Medieval researches techs, meets the advancement checklist and advances
  through all 4 ages; its outdated blocks redevelop unless kept.
- A city started in Industrial plays exactly like today (same seeded-city numbers).
- Save v2 round-trips age, techs, research progress and per-cell built age / historic flags, and
  v1 saves migrate (Industrial age, year shifted).
- All EditMode tests green, plus a UI-only virtual-input play-through.

#### Design decisions (defaults — change any before 11a starts)

| Topic | Decision |
|---|---|
| Advancing | Player-triggered. An age can require specific techs (`AgeDefinition.RequiredTechs`) on top of the count. "Advance to *next age*" is a research project you can start only once the next age's tech count and population are met; its RP cost is paid like any tech. Completing it changes the age immediately. |
| Research | One active project plus a queue (max 5). RP accrue daily; leftover RP carries into the next project. No RP while nothing is queued (stored up to a cap of one day's worth × 30). |
| Tech tree | A small DAG per age, 4–6 techs, all eventually researchable (no exclusive choices in M11). A tech can need techs from its own or earlier ages. |
| Level rules | Levels stay 1..3 in code. An age caps the level its blocks can reach (`MaxLevel`) and scales their capacity (`CapacityScale`); capacity per cell = `round(CapacityForLevel(level) × CapacityScale(builtAge))`, min 1. Power draw uses the same scaled capacity. |
| Upgrade requirements | Read from the **current** age (it's the city's technology): in Medieval and Renaissance, upgrades need no power; from Industrial on, the M9 rules apply. Electricity is Industrial's first tech (cheap, no prerequisites), so a city arriving in Industrial researches it and builds a plant before its blocks can upgrade or redevelop — the new age's opening task. The Power happiness term only applies when the current age requires power. |
| Redevelopment | Automatic, separate budget `RedevelopPerDay` (default 3, row-major, deterministic), so it doesn't steal growth from demand. Same level, new built age; it's an upgrade-like change (pop animation, power reserved for any extra draw). Needs road access; not demand-gated. |
| Keep historical | Free per-cell toggle on grown cells. A kept cell keeps its built age (style + capacity), is never redeveloped, and can still upgrade up to *its own* age's `MaxLevel`. Demolish / rezone clears it. Payoff (heritage land value and happiness) arrives in M12. |
| Locked buildings | `BuildingDefinition.RequiredTech` (tech Id, empty = always). Locked defs get no toolbar button; the tech panel lists what each tech unlocks; a toast says "*X* unlocked". Placement refuses locked defs. Loads restore locked buildings anyway. `UnlockPopulation` is removed. |
| Starting age | New City dialog adds an age picker. Starting in age N marks every tech of ages < N **plus the age's `StartingTechs`** as researched, sets the year to the age's start year, and uses the age's `StartingMoney`. Industrial's `StartingTechs` = Electricity, so a new Industrial city = today's game. |
| Calendar | New city year = starting age's `StartYear`. Advancing sets `Year = max(Year, nextAge.StartYear)`. v1 saves: `Year += 1759` (Industrial). |
| Currency | `$` in every age for M11. |
| No age data | `SimulationSystem` built without an `AgeDatabase` uses `AgeRules.Legacy` (= Industrial: max level 3, scale 1, power required) and has no research; with no databases assigned, `GameManager` treats every building as unlocked. All 82 existing tests keep this path unchanged. |

#### Architecture

**Pure, in the Simulation asmdef** (`Scripts/Simulation/Ages/`):

| New / changed | Notes |
|---|---|
| `AgeDefinition` (SO) | `Id`, `DisplayName`, `StartYear`, `MaxLevel`, `CapacityScale`, `UpgradesNeedPower` (and `UpgradesNeedWater`, unused until M13), `TechsToAdvance` (count of the previous age's techs needed to enter), `RequiredTechs`, `StartingTechs` (granted only when a city *starts* in this age), `PopulationToEnter`, `AdvanceCost` (RP), `StartingMoney`, `ZoneNames[3]`. |
| `AgeDatabase` (SO) | Ordered `AgeDefinition` list; index = age number used everywhere (bytes in `GridData`, saves). `IndexOf(id)`, `Legacy` index (the `industrial` Id). Validates order (ascending years, non-decreasing scale). |
| `AgeRules` (struct) | What the sim needs from an age: `MaxLevel`, `CapacityScale`, `UpgradesNeedPower`. `AgeRules.Legacy` for the no-ages path. |
| `TechDefinition` (SO) | `Id`, `DisplayName`, `Description`, `Age` (index), `Cost` (RP), `Prerequisites` (`TechDefinition[]`), `Effects` (`TechEffect[]`). |
| `TechEffect` (struct) | `Type` (`UnlockBuilding`, `ResearchMultiplier`, `DemandMultiplier`, `HappinessBonus`, `UpkeepMultiplier`), `Target` (building Id or zone), `Value`. |
| `TechDatabase` (SO) | All techs; validation: unique Ids, no cycles, prerequisites in the same or earlier age, every age has ≥ its `TechsToAdvance`. |
| `TechModifiers` (struct) | Effects of researched techs folded together: research ×, per-zone demand ×, happiness +, upkeep ×, unlocked building Ids. Recomputed when a tech completes or on load. |
| `TechSystem` | `CurrentAge`, `Researched`, `Active`, `Progress`, `Queue`, `ResearchPerDay`. `CanResearch(tech)`, `Enqueue/Remove/SetActive`, `AdvanceStatus(population)` → `{RpCost, TechsDone, TechsNeeded, Population, PopulationNeeded, Ready}`, `Step(rp)` → completed tech / advancement. Events: `TechCompleted`, `AgeAdvanced`. `Restore(...)` for loads. |
| `CapacityModel` | One place for scaled capacity: `CapacityOf(grid, cell)`, `UpgradeDraw(cell)`, `RedevelopDraw(cell)`. Replaces the 5 direct `BalanceConfig.CapacityForLevel` call sites (`PopulationSystem`, `PowerSystem`, `GrowthSystem`, `ServiceStats`, `SelectionPanel`). |
| `GridData` | Per-cell `BuiltAge` (byte) and `Historic` (bool): `Get/SetBuiltAge`, `Get/SetHistoric`, raise `OnCellChanged`; `Export/Import` gain the two arrays; `Resize` clears them. Growth 0→1 stamps the current age; demolish / rezone clears both. |
| `GrowthSystem` | Scan per zone: new cells → upgrades (cap = current age `MaxLevel`, or the built age's for historic cells; power per current age) → separate redevelop pass. New blockers: `AgeMaxLevel` ("max level for *age*"), `Outdated` ("waiting to be rebuilt"), `KeptHistoric` (at its own age's cap). |
| `SimulationSystem` | Optional `AgeDatabase`/`TechDatabase` in the constructor; owns `TechSystem` and `CapacityModel`. Tick: Demand → Growth (incl. redevelop) → Population → Economy → **Research**. `Rules` = current age's `AgeRules`. |
| `BalanceConfig` | `ResearchPerCommercialJob`, `RedevelopPerDay`, `ResearchQueueMax`, `ResearchBankDays`. Tagged "(M11)", mirrored in the `.asset`. |

**Runtime (`Assembly-CSharp`):**

| New / changed | Notes |
|---|---|
| `BuildingDefinition` | `+ RequiredTech` (string), `+ ResearchPerDay`; `- UnlockPopulation`. `CityModifiers.ResearchPerDay` sums placed buildings. |
| `GameManager` | Holds `AgeDatabase`/`TechDatabase` refs, passes them to the sim, forwards `TechCompleted` / `AgeAdvanced` to `GameEvents` (`ResearchChanged`, `TechCompleted(id)`, `AgeChanged(index)`). `IsUnlocked(def)`. |
| `AgeVisualSet` (SO, per age) | Per zone × level: optional prefab slots (array = variants, picked by cell hash) + fallback style (body tint, roof style: flat / pitched / none, height ×). `GrowthVisuals` picks the set by the cell's `BuiltAge`; pools per prefab. |
| `SaveData` v2 + `SaveMigrations` | See 11c. |
| UI | `TechPanel`, HUD age/research readout, New City age picker, SelectionPanel age lines + Keep toggle, `InfoOverlay.View.Age`, toasts. See 11f. |

**Asset contract for your hand-made buildings (M18, defined now):** one prefab per slot (or
several variants); pivot at the ground centre of a 1×1 cell, +Y up, 1 unit = 1 cell, fits inside
the cell; layer 9; a collider on the root (selection raycasts land on it); **shared materials
only** — no per-instance materials or `MaterialPropertyBlock`s (that was the 23 ms/frame problem
in M10b); tinting for info views is done by swapping to shared tinted materials, so keep to one or
two materials per prefab.

#### Steps (each one fits a session and is committed on its own)

- **11a Tech core (pure, no gameplay change) — done (2026-10-03).** Notes from implementing: the RP
  `Progress` is a pool held toward the active project (switching projects is free); a replaced
  active project returns to the front of the queue; removing a planned tech drops queued techs that
  needed it; `TechModifiers` is a class (identity default) folded in database order; the sim has no
  calendar, so advancing raises `AgeAdvanced` and the runtime applies `AgeDefinition.YearOnEntering`
  (11d/11f). 104 EditMode tests green (22 new in `TechSystemTests`).
  Original plan: `AgeDefinition`, `AgeDatabase`, `TechDefinition`,
  `TechEffect`, `TechDatabase` + validation, `TechModifiers`, `TechSystem`, `AgeRules`.
  `SimulationSystem` gets the optional databases and the Research tick step, but nothing reads
  the age yet. Tests: validation (cycle, prerequisite from a later age, duplicate Id, too few
  techs for an age), `CanResearch`, queue order and cap, RP carry-over and bank cap, advancement
  checklist (each condition alone blocks it), advancing raises `AgeAdvanced` and sets the year,
  modifiers fold correctly, determinism.
- **11b Ages in the sim — done (2026-10-03).** Notes from implementing: `SetBuildingLevel(0)` clears
  the built age and historic flag (every demolish / rezone path goes through it); an outdated cell
  may still upgrade up to the current age's cap at its old capacity scale, and a cell that upgraded
  this tick waits a day before it can be redeveloped; redevelopment in a powered age needs power
  even when the extra draw is 0; `AgeDatabase` validation rejects a falling `MaxLevel`; the tech
  happiness bonus is its own `HappinessBreakdown.Technology` term. Industrial start reproduces the
  no-age seeded city exactly (levels, population, money, power) with and without a plant/parks; the
  in-game seeded 64² city is still 212 pop at day 60. 117 EditMode tests green.
  Original plan: `CapacityModel` (replace the 5 call sites), `GridData` built age /
  historic, growth level cap per age, power gate per age, Power happiness term per age, the
  redevelop pass, the new blockers, `TechModifiers` applied (demand, happiness, upkeep, research).
  Tests: no-age path reproduces `RunSeededCity` numbers exactly; Medieval seeded city
  stops at level 2 with ×0.5 capacity and no power needed; advancing makes outdated cells
  redevelop at `RedevelopPerDay`, row-major; historic cells never redevelop and cap at their own
  age; demolish / rezone clears the flags; redevelopment reserves power headroom in powered ages.
- **11c Save v2 + migration — done (2026-10-03).** Notes from implementing: the v1 → v2 step only
  marks the city as saved without ages (`Age = NoAge`); a separate adoption step turns any `NoAge`
  city into an Industrial one when the game has age data, so v2 saves written by the ageless build
  (until 11d wires the databases) migrate the same way as v1 files. `GameManager` got the
  (unassigned) `AgeDatabase` / `TechDatabase` slots so save/load/new city already pass them. The
  real 24² v1 save is the test fixture; in-game, it loads, saves as v2 and reloads unchanged.
  126 EditMode tests green.
  Original plan: `SaveData` v2: `Age`, `Researched`, `ActiveResearch`,
  `ResearchProgress`, `ResearchQueue`, `BuiltAges`, `Historic`. `SaveMigrations.Migrate(data,
  ages, techs)` runs one step per version (v1 → v2: Industrial age, all Medieval and Renaissance
  techs + Electricity researched, every grown cell built in Industrial, `Year += 1759`); `TryFromJson` migrates
  older files and rejects newer ones. `CreateNew(width, height, config, ages, techs, startAge)`.
  Tests: v1 fixture JSON (a real 24² save) migrates and runs identically to a native Industrial city;
  v2 round trip mid-research and mid-redevelopment = uninterrupted run; unknown tech Ids in a save
  are dropped with a warning, not a failure.
- **11d Content — done (2026-10-03).** 30 techs (8/6/8/8) and 4 ages as assets generated from the
  tables below; tech RP costs are a first pass (Medieval 40–120, Renaissance 180–260, Industrial
  150–550, Modern 800–1,400) for 11g to tune. Unlocks use `BuildingDefinition.RequiredTech` only.
  Monastery ($2,000, $10/day, 2 RP/day) and Academy ($5,000, $25/day, 5 RP/day) placeholder
  prefabs. The databases are wired into `GameManager`, so the game now runs with ages (startup and
  New City = Industrial until the 11f picker). **Open question for 11g:** an Industrial start
  keeps every earlier tech's modifier (R ×1.1, C ×1.39, I ×1.21, happiness +0.05, upkeep ×0.9,
  research ×1.25), so it is slightly ahead of the pre-ages game (seeded city day 60: 220 pop,
  0.65 happiness vs 212 / 0.60); the "plays exactly like today" check holds for the age-less sim
  and the test ages, not for the real content. Either accept it (rebaseline) or move those
  modifiers' weight elsewhere during the balance pass. 130 EditMode tests green.
  Original plan: 4 `AgeDefinition` assets, ~28 `TechDefinition` assets (table below),
  `AgeDatabase` / `TechDatabase` assets wired into `GameManager`. Research buildings as placeholder
  prefabs: Monastery (Medieval, 2×2, research 2/day) and Academy (Renaissance, 2×2, research 5/day).
  `RequiredTech` on Park (Medieval "Commons") and Power Plant (Industrial "Electricity", granted to
  cities that start in Industrial). Toolbar hides locked
  defs and adds buttons when a tech unlocks them; `PlacementController` refuses locked defs.
- **11e Visual slots — done (2026-10-03).** `AgeVisualSet` (runtime SO, matched to ages by Id) with
  per zone × level prefab variants and a fallback style (tint blend, roof style default / pitched /
  none, roof colour, height ×); four generated sets (Medieval timber + thatch gables, Renaissance
  sandstone + terracotta, Industrial plain = today, Modern taller concrete / glass). `GrowthVisuals`
  styles by built age, pools prefabs per prefab, pops on redevelopment, tints prefabs for info views
  via shared materials. Player benchmark on the 96² stress city with mixed ages: 1.14 ms frames at
  default zoom, 1.48 ms zoomed out, ~2 ms with an edit per frame (M10b: 1.1 / 1.5 / 2); sim tick
  1.25 ms (was 0.5) because a quarter of that city is outdated and scanned for redevelopment each
  tick. 131 EditMode tests green.
  Original plan: `AgeVisualSet` + one asset per age with fallback styles only (tint +
  roof style + height ×, so each age reads differently on placeholder blocks); `GrowthVisuals`
  per-age style and prefab pooling; redevelopment pops like an upgrade. Player-build benchmark
  re-run (must stay ≈1–2 ms on the 96² stress city with mixed ages).
- **11f UI — done (2026-10-03).** Everything below is in; notes from implementing: the Research
  panel shares the HUD slot with Taxes (opening one closes the other); a click on a planned tech
  removes it, and a tech whose prerequisite is only planned is queued instead of started; the whole
  BUILDINGS toolbar section hides while nothing is unlocked; the Power HUD group, Power view and
  power toasts appear with the first unlocked power source. Checked in Play mode (scripted, not yet
  with virtual input — that's 11g): Medieval start, research plan via the panel, advance to the
  Renaissance (toast, year 1450), redevelopment toast, Keep on an outdated block (kept Medieval
  while the other 82 were rebuilt), Age view, New City with Modern / 32² (year 1945, $80k, power UI
  back). **For 11g:** Medieval research is very slow (≈1 RP/day at 80 residents: Masonry, 60 RP,
  takes ~2 months), so the RP rate or the Medieval costs need the balance pass. 132 EditMode tests
  green.
  - **Tech panel** (HUD "Research" button next to Taxes; TaxPanel-style): current age's and next
    age's techs with state (done / available / locked by prerequisite / later age), cost,
    unlocks; click = set active, shift-click = queue; progress bar; the advancement checklist
    with its "Advance" project.
  - **HUD:** age name next to the date, RP/day and a thin progress bar for the active project.
  - **New City dialog:** starting-age row (4 buttons, current age preselected) under map size.
  - **SelectionPanel:** "Built in *age*", "Outdated — will be rebuilt" / "Historic (kept)",
    **Keep historical building** toggle; capacity uses `CapacityModel`; blockers' new texts.
  - **Info view Age** (third VIEW button; `V` cycles it): cells tinted by built age (old = warm,
    new = cool), historic cells highlighted, outdated cells striped.
  - **Toasts:** research complete, "*X* unlocked", "Ready to advance to *age*", "Welcome to the
    *age*", first redevelopment. Power HUD group / Power view / power nudge hidden until Electricity is researched.
- **11g Balance, play-through, docs — done (2026-10-03).** An `EngagedCity` harness (EditMode
  test helper) plays the shipped content like an engaged player; tuning toward 45–90 days per age
  set research per filled commercial job to 0.25 (was 0.05), the Monastery to 3 RP/day, Medieval
  techs to 30–90 RP and the age gates to 120 / 350 / 650 residents and 250 / 1,200 / 3,000 RP
  (the placeholder 300 / 900 / 2,500 and 800 / 2,500 / 8,000 assumed a much faster growth pace than
  `MaxGrowthPerDay` gives, and that constant is shared with the age-less game). Result from
  Medieval: 86 / 62 / 63 days per age, Modern on day 211, never in debt; later starts keep growing.
  `AgeBalanceTests` lock it in. **Decision:** an Industrial start keeps the earlier ages' tech
  bonuses (220 pop / 0.65 at day 60 vs 212 / 0.60) — accepted and recorded as the new baseline; the
  age-less sim is unchanged. **UI-only play-through** with virtual mouse/keyboard (scripted, all
  actions through input): New (Medieval, 64²) → click roads from the map edge, drag zones → plan
  research (click / shift-click) → Monastery from the toolbar once unlocked → Advance → Renaissance
  (1450) → Keep an outdated block (it stayed Medieval while all others were rebuilt) → Save
  mid-research, play on, Load (research, date, population and the kept block restored exactly) →
  Advance → Industrial (1760, power UI still hidden without Electricity) → New (Industrial): 1760,
  $50k, Electricity, power UI shown. 0 failed checks. It found one real bug: the `Pointer` action
  was a Value action and stuck to the first mouse's position when another pointer device was added
  — now Pass Through. 137 EditMode tests green.
  Original plan: Tune RP and thresholds with a pure harness (seeded city per
  starting age) toward **~45–90 in-game days per age at engaged play** and no age where the city
  stalls. Seeded-city balance tests per age. UI-only play-through: New (Medieval, 64²) →
  research → advance twice → keep a block historic → watch the rest redevelop → save / load
  mid-research → New (Industrial) behaves like today. `AGENTS.md` Systems, §7/§8 status.

#### Content (first pass, numbers are tunables)

| Age | Starts | Advance needs (to enter) | Techs (effects) |
|---|---|---|---|
| 1 Medieval | 750 | — | Commons (unlock Park as village green), Crop Rotation (R demand ×1.1), Smithing (I demand ×1.1), Markets (C demand ×1.1), Masonry, Monasticism (unlock Monastery), Guilds (C/I demand ×1.1), Charters (happiness +0.02) |
| 2 Renaissance | 1450 | 5 Medieval techs, pop 300, 800 RP | Printing Press (research ×1.25), Academies (unlock Academy), Banking (C demand ×1.15), Architecture, Civic Planning (happiness +0.03), Watermills (I upkeep ×0.9) |
| 3 Industrial | 1760 | 4 Renaissance techs, pop 900, 2,500 RP | **Electricity** (first, cheap ~150 RP, no prerequisites; unlock Power Plant), Steam Power (I demand ×1.2), Factories, Railways, Public Sanitation (happiness +0.03), Telegraph (research ×1.2), Steel Frames, Electric Trams |
| 4 Modern | 1945 | 5 Industrial techs, pop 2,500, 8,000 RP | Automobiles, Computing (research ×1.3), Suburbs (R demand ×1.15), Mass Media, Internet (research ×1.3), Renewables, Green Building (upkeep ×0.9), Smart Grid |

**Tech tree (first pass; `A → B` = A is a prerequisite of B).** Every age has at least one tech
with no prerequisites, so a city that has just arrived is never stuck; prerequisites may come from
earlier ages, which gives a few chains through the whole game (building: Masonry → Architecture →
Steel Frames → Green Building; knowledge: Monasticism → Printing Press → Telegraph → Computing →
Internet; power: Electricity → Renewables). No exclusive choices.

| Age | Prerequisites |
|---|---|
| Medieval | Commons, Masonry, Crop Rotation, Smithing: none. Masonry → Monasticism. Crop Rotation → Markets. Masonry + Markets + Smithing → Guilds. Markets → Charters. |
| Renaissance | Monasticism → Printing Press → Academies. Guilds → Banking. Masonry → Architecture → Civic Planning. Smithing → Watermills. |
| Industrial | Electricity: none. Watermills → Steam Power → Factories, Railways. Factories → Steel Frames. Civic Planning → Public Sanitation. Electricity + Printing Press → Telegraph. Electricity → Electric Trams. |
| Modern | Railways → Automobiles → Suburbs. Telegraph → Computing → Internet. Telegraph → Mass Media. Electricity → Renewables. Computing + Renewables → Smart Grid. Steel Frames → Green Building. |

**Built to be extended.** The tree is pure data: one `TechDefinition` asset per tech (prerequisites
are asset references, effects a typed list), collected in `TechDatabase`. Adding a tech, a branch or
a new effect target needs no code; a new *kind* of effect is one `TechEffectType` value plus where
`TechModifiers` applies it. `TechDatabase` validation (unique Ids, no cycles, prerequisites in the
same or an earlier age, enough techs per age to advance) runs in an EditMode test, so a broken edit
fails the tests rather than the game. The tech panel lays out from the data (columns by age, rows by
prerequisite depth), so it grows with the tree. M12–M17 will add their buildings and road tiers as
new techs on these branches.

Research income (starting point): `0.05 × commercial jobs filled` + research buildings, ×
research multipliers. Starting money: $20k (Medieval), $30k (Renaissance), $50k (Industrial, the
current value), $80k (Modern). Most techs are modifiers in M11; M12–M17 hang their buildings and
road tiers on these techs (water tower → Public Sanitation, fire station → Steam Power, etc.).

#### Risks / open questions

- **Balance across ages** is the biggest unknown; 11g has a harness for it, but expect a second
  tuning pass after M12–M14 add real per-age content.
- **Population thresholds vs map size:** 2,500 pop needs room; a 32² map may not reach
  Modern. Either scale thresholds with map area or state that small maps are for early ages.
  Default: leave them fixed and say so in the New City dialog hint.
- **Medieval economy:** `$` and taxes as today. If early ages feel wrong, add per-age income /
  upkeep multipliers on `AgeDefinition` (cheap to add later).
- **Scope:** ~28 tech assets + 4 ages + 2 buildings + 4 visual sets + 5 UI pieces is the largest
  milestone so far; the steps above are ordered so the game stays playable after each one
  (11a–11c change nothing visible for an Industrial-start city).

### M12 — Land value & local pollution (plan, 2026-10-03) — done (2026-10-03)

**Goal:** where things stand matters. Pollution is local: industry (and the power plant) dirties
the cells around it, by an amount and radius that depend on the age a block was built in. Every
cell gets a land value from parks, kept historic blocks, technology and pollution. Homes and shops
need enough land value to reach level 3, and polluted homes are unhappy. Kept historic blocks
finally pay off with a heritage bonus.

**Done when:**
- Homes beside industry are less happy and stop at level 2; the same homes with a park nearby (or
  away from industry) reach level 3. Industry itself is never land-value gated.
- Medieval workshops pollute little and close by; Industrial factories pollute a lot and further.
  A kept historic block raises land value and happiness around it.
- Pollution and Land value info views and the SelectionPanel explain all of it (contributors,
  the level-3 threshold, the new blocker).
- `AgeBalanceTests` still meet their targets (45–90 days per age, never in debt, happiness ≥ 0.55);
  all EditMode tests green, plus a UI-only virtual-input play-through.

#### Design decisions (defaults — change any before the step that uses them)

| Topic | Decision |
|---|---|
| Baseline | **Changes everywhere, the age-less sim included** (decided 2026-10-03): one pollution model for every age. The city-wide `IndustrialJobs / (Housing + Jobs)` term is gone. The legacy seeded-city tests stay as qualitative checks (retuned only if a check fails), the two formula unit tests are rewritten, and `IndustrialStart_RealContent_Baseline` is re-recorded. The no-age sim still equals the Industrial age's rules. |
| Pollution sources | Grown **industrial** cells emit `IndustrialPollution × capacity × PollutionScale(built age) × tech multiplier` points (a level-1 Industrial-age shed = 1, level 3 = 4). Placed buildings with `BuildingDefinition.Pollution` > 0 emit that many points (the Power Plant: 6, radius 5). Commercial and residential cells don't emit. |
| Spread | Each source adds `points × (1 − d / (r + 1))` to every cell within Chebyshev distance `d ≤ r` of its footprint (linear falloff, full at the source). `r` = the built age's `PollutionRadius` (Medieval 1, Renaissance 2, Industrial 3, Modern 3; `BalanceConfig.PollutionRadius` 3 without ages). Pollution is a float per cell, derived and never saved. |
| Pollution happiness | The `Pollution` term becomes the housing-weighted average over homes of `min(pollution(home) × PollutionPenaltyPerPoint, PollutionPenaltyCap)` (0.02 per point, cap 0.25 per home), still ramped in with `SmallTownGracePopulation`. Measured in `ServiceStats` like the Services and Power terms. |
| Land value | Per cell, 0..1: `LandValueBase` (0.5) + parks `min(coverage × 0.10, 0.20)` + heritage `min(kept historic cells within HeritageRadius 3 × 0.05, 0.20)` + tech `LandValueBonus` − `pollution × LandValuePerPollution` (0.04), clamped. Derived, never saved. |
| Level-3 gate | Residential and commercial cells need land value ≥ `LandValueForLevel3` (0.40) to upgrade 2 → 3 — new `GrowthBlocker.LowLandValue`. Industry is exempt. Cells already at level 3 never drop. Redevelopment (same level) is not gated. Medieval (max level 2) never meets the gate; its pollution still costs happiness. |
| Heritage happiness | New `HappinessBreakdown.Heritage` term: housing-weighted average of `min(kept historic cells within HeritageRadius × 0.02, 0.06)` per home. 0 in a city without kept blocks, so the age-less baseline is unaffected by it. |
| Age data | `AgeDefinition` gains `PollutionScale` (Medieval 0.3, Renaissance 0.5, Industrial 1, Modern 0.6 — "workshops low, factories high, clean energy low") and `PollutionRadius`. A block keeps its built age's pollution until it is redeveloped, so advancing raises pollution gradually. |
| Tech effects | Two new `TechEffectType`s: `PollutionMultiplier` (all emissions × Value) and `LandValueBonus` (+ Value on every cell). Content: Renewables pollution ×0.75, Architecture land value +0.05 (both had no effect so far). |
| Saves | No new persisted state (pollution and land value are derived; historic flags are already saved), so **no version bump**. |
| UI | Two info views, `Pollution` and `LandValue` (VIEW buttons, `V` cycles; always available). SelectionPanel: pollution + land value lines with contributors and the level-3 threshold on zoned / grown cells, emissions on industrial cells and polluting buildings, "Heritage: +x land value within 3 cells" on kept blocks. Happiness tooltip: "Pollution (nearby industry)" and "Heritage". One-time toast the first time a home or shop is held at level 2 by land value. |

#### Architecture

**Pure, in the Simulation asmdef:**

| New / changed | Notes |
|---|---|
| `PollutionSystem` | Per-cell float field. Inputs: `GridData` (industrial cells, levels, built ages), `CapacityModel`, `AgeDatabase` (scale/radius per built age; legacy values without), `ServiceSource.Pollution`/`PollutionRadius`, `TechModifiers.PollutionMultiplier`. Lazy like `PowerSystem`: `OnCellChanged`, `SetSources` and a tech change mark it dirty; `GetPollution(cell)`, `EmissionOf(cell)`, `RadiusOf(cell)`. Reallocates on `OnResized`. |
| `LandValueSystem` | Per-cell float field from `CoverageSystem`, historic counts (own per-cell heritage count, recomputed with the field), `PollutionSystem` and `TechModifiers.LandValueBonus`. Lazy; `GetLandValue(cell)` + `Explain(cell)` → `{Base, Parks, Heritage, Tech, Pollution}` for the UI; `HeritageCount(cell)`. |
| `ServiceSource` | `+ Pollution` (points), `+ PollutionRadius` (old constructor kept). |
| `ServiceStats` | `+ PollutionPenalty` (average per-home, before the grace ramp), `+ HeritageBonus`; `Measure` reads the two new systems. |
| `PopulationSystem` / `HappinessBreakdown` | Pollution term from `ServiceStats`; `+ Heritage` term (tooltip line). |
| `GrowthSystem` | Level 2 → 3 for R/C checks `LandValue ≥ LandValueForLevel3`; `GetBlocker` → `LowLandValue`. |
| `SimulationSystem` | Owns `Pollution` and `LandValue`; passes `TechModifiers` changes on. |
| `AgeDefinition` / `AgeRules` | `PollutionScale`, `PollutionRadius` (read per built age, like `CapacityScale`). |
| `TechEffectType` / `TechModifiers` | `PollutionMultiplier`, `LandValueBonus` (appended — enum values are serialized in the tech assets). |
| `BalanceConfig` | `IndustrialPollution` 0.25, `PollutionRadius` 3, `PollutionPenaltyPerPoint` 0.02, `PollutionPenaltyCap` 0.25, `LandValueBase` 0.5, `LandValuePerService` 0.10, `LandValueServiceCap` 0.20, `HeritageRadius` 3, `HeritageLandValueEach` 0.05, `HeritageLandValueCap` 0.20, `HeritageHappinessEach` 0.02, `HeritageHappinessCap` 0.06, `LandValuePerPollution` 0.04, `LandValueForLevel3` 0.40 — tagged "(M12)", mirrored in the asset; `PollutionPenalty` removed. |

**Runtime (`Assembly-CSharp`):** `BuildingDefinition.Pollution` + `PollutionRadius` (Power Plant 6 / 5) → `GameManager` sources; `InfoOverlay` views; `SelectionPanel`, `HappinessTooltip`, `NotificationController` lines; toolbar VIEW buttons.

#### Steps (each one fits a session and is committed on its own)

- **12a Local pollution (pure + content) — done (2026-10-03).** Notes from implementing: at the planned
  0.02 per point the seeded city lost only 0.03 happiness to pollution (the old city-wide term cost
  0.10), so `PollutionPenaltyPerPoint` is 0.06 (tuned): seeded city day 60 = 212 pop, 0.62 happiness
  (0.70 with parks; pre-M12 0.60 / 0.68), half its homes polluted (the east ones face industry, the
  west ones near the plant); 20% C/I without parks stalls at 22 pop. `EngagedCity` pacing is unchanged
  (86 / 62 / 63 days). Industrial-start baseline re-recorded: 220 pop / 0.660 (was 0.647). Tests
  seed the plant's pollution like the asset. 145 EditMode tests green.
  Original plan: `PollutionSystem`, the new `AgeDefinition`, `ServiceSource`,
  `BuildingDefinition` and `BalanceConfig` fields, the per-home Pollution term, `PollutionMultiplier`.
  Generator + Power Plant asset. Tests: falloff and radius, emission by level and built age, plant
  source, resize, laziness (recomputes after a level change), tech multiplier; per-home term (a home
  far from industry pays nothing); the rewritten formula tests. Re-run the seeded-city and age
  balance tests and record what moved.
- **12b Land value, the level-3 gate and heritage (pure + content) — done (2026-10-03).** Notes from
  implementing: the gate is checked after road access and before power (so no power is reserved for a
  blocked upgrade); a kept block counts toward its own heritage; only the heritage counts are cached,
  the rest of the value is read live. Planned numbers kept. Effect on the harnesses: the seeded city
  has no level-3 homes by day 60 anyway, so it is unchanged; `EngagedCity` Industrial / Modern starts
  hold 10 + 4 / 7 + 4 R + C cells at level 2 by land value at day 120 (pop 564 / 859, was 600 / 887)
  and still pass; the Medieval run is unchanged (86 / 62 / 63 days). 152 EditMode tests green.
  Original plan: `LandValueSystem`, the gate and
  `LowLandValue`, the Heritage happiness term, `LandValueBonus` + Architecture. Tests: contributors
  add up and clamp; polluted homes stop at level 2 and a park lets them through; industry ungated;
  historic cells raise neighbours' value and happiness; the blocker; Medieval unaffected by the gate.
- **12c UI — done (2026-10-03).** Notes from implementing: the Land value colours are centred on the
  level-3 threshold (a plain 0..1 ramp made almost all land the same yellow, since most cells sit at
  50–55%); `GrowthSystem.IsHeldByLandValue` is shared by the view and the toast. Checked in Play mode
  on the seeded 64² Industrial city (day 125, 480 pop): homes facing industry 2.8 pollution
  (−17% happiness), land value 44%; a park lifts its side to 56%, two kept blocks give their row
  65% (+10% heritage); the toolbar fits 1920 with five VIEW buttons. 151 EditMode tests green.
  Original plan: Pollution and Land value info views (ground shading + building tints, through
  `GrowthVisuals.SetColorOverride`), two VIEW buttons, SelectionPanel lines, tooltip lines, the
  land-value toast. Play-mode screenshots of both views on a seeded city.
- **12d Balance, play-through, docs — done (2026-10-03).** Notes from implementing: `EngagedCity` places a
  park in the free 2×2 middle of any block with a home or shop held by land value (one a day, paid);
  zone separation wasn't needed — every `AgeBalanceTests` target holds without retuning (Medieval start
  86 / 62 / 63 days, never in debt; Industrial / Modern starts 588 / 888 pop at day 120, happiness
  0.80 / 0.83 with the extra parks). Tick on a full 96² city (EditMode, all cells level 3): 0.76 ms, of
  which the pollution + heritage recompute is 0.38 ms — inside the budget, no renderers added, so no
  player benchmark. UI-only play-through (virtual mouse / keyboard, 0 failed checks): New (Industrial,
  64²) → road from the edge, homes across from industry, a plant from the toolbar → skip 90 days → a
  home held at level 2 (pollution 4.5, land value 37%, −13.5% city pollution happiness) → Pollution
  view, the panel explains it → park behind it (47%) + a second plant → level 3 → Keep historic
  (neighbour 55% → 60%, heritage happiness +0.5%) → Value view, V cycles to Ages and Off. One run
  failed only because the first plant was full (the scenario now adds a second). 151 EditMode tests
  green.
  Original plan: `EngagedCity` learns to place parks for homes blocked by land
  value and to keep zones apart (industry blocks away from homes); retune toward the `AgeBalanceTests`
  targets and re-record the Industrial-start baseline. UI-only play-through: industry next to homes →
  Pollution view → a home held at level 2 (panel says why) → park → it reaches level 3 → keep a block
  historic → Land value view shows the heritage ring. Measure the tick on a full 96² city (must stay
  ≈1–2 ms; `perf-benchmark` only if it doesn't). `AGENTS.md`, §8/§12 status.

#### Risks / open questions

- **Seeded-city layout:** the seeded test city puts homes directly across a road from industry, so it
  will lose some happiness and level-3 homes. That is the point of the milestone; the legacy checks
  are ranges and relative comparisons, so only the re-recorded baseline should need new numbers.
- **Balance:** pollution and the gate slow growth; if `AgeBalanceTests` go over 90 days per age, the
  first knobs are `LandValueForLevel3`, `PollutionPenaltyPerPoint` and the player model's zoning.
- **Tick cost:** pollution recomputes after any grid change, which is almost every day; cost is
  sources × (2r + 1)² (~0.1 M adds on a full 96² map). Fine, but measure it.

### M13 — Water (plan, 2026-10-03) — done (2026-10-03)

**Goal:** every age needs water to grow. In the Medieval and Renaissance ages, wells and fountains
water the blocks around them (coverage). From the Industrial age on, water towers and pumping
stations feed a pipe network: every road carries water, the way roads carry power, and the player
can draw pipes where no road runs (to reach a pump outside town, or to join two road networks). The
water gate sits beside the power gate. Water gets an info view, a HUD readout and toasts, following
the power pattern.

**Done when:**
- Medieval: blocks stay at level 1 until a well's radius covers them, and a well lets them reach
  level 2. Renaissance: a fountain (bigger radius, and it also counts as a park) lets them reach
  level 3 (the land-value gate still applies).
- Industrial / Modern: upgrades need power **and** piped water with headroom for the extra draw. A
  water tower beside the road network waters it. A pumping station built away from any road works
  once a drawn pipe links it to the roads. Two road networks joined by a pipe share their supply.
- Advancing into the Industrial age needs Waterworks. On arrival wells stop counting, and the toast
  says to build a water tower. A city that starts in Industrial or Modern has Waterworks researched.
- The age-less sim needs piped water like the Industrial age (decided, see the table). The seeded
  test city gets a water tower, and the legacy checks still hold.
- Save v3 stores pipes. v2 saves and the v1 fixture migrate (no pipes), and save → load → continue
  equals an uninterrupted run.
- `AgeBalanceTests` still meet their targets (45–90 days per age, never in debt, happiness ≥ 0.55).
  All EditMode tests are green, and a UI-only virtual-input play-through passes.

#### Design decisions (defaults — change any before the step that uses them)

| Topic | Decision |
|---|---|
| Baseline | **Water gates every age, the age-less sim included** (decided 2026-10-03: "everywhere, so long as all ages require water"). `AgeRules.Legacy` = Industrial = piped water. The no-age sim still equals the Industrial age's rules, so `IndustrialStart_PlaysExactlyLikeTheNoAgeSim` keeps holding (`TestAges` gives Industrial / Modern piped water). `SeededCity` seeds a water tower (free, like the plant) with supply ≥ the plant's, so water never binds before power. The legacy checks are ranges, so only upkeep-driven money numbers should move; the step records what moved, and `IndustrialStart_RealContent_Baseline` is re-recorded. |
| Water rule per age | `AgeDefinition.m_UpgradesNeedWater` (bool, unused) becomes `WaterRule Water` = `None / Coverage / Piped` (an `AgeRules` field). Medieval and Renaissance use `Coverage`; Industrial and Modern use `Piped`. `None` is used only by tests. The gate covers every zone and every upgrade past level 1, like power. Level 0 → 1 never needs water. |
| Coverage (early ages) | A cell has water when a source with `WaterRadius` > 0 reaches it (Chebyshev distance from the footprint, like parks). There is no capacity: a well serves everything in range. `CoverageSystem` gains a radius selector, so a second instance counts water sources. |
| Piped network (later ages) | `PowerSystem`'s network and allocation model, generalised. Network cells = roads ∪ pipe cells, 4-connected; no map-edge requirement. Sources (`WaterSupply` > 0) seed from the road or pipe cells sharing an edge with their footprint. Sources on one network pool supply. Draw = capacity × `WaterPerCapacity` (1.0, the same units as power), handed out in BFS order from the sources (the furthest cells go dry first). A grown cell is fed by a network cell beside it **or a pipe under it**. |
| Pipes ("both possible", decided 2026-10-03) | Roads always carry water, and pipes are optional. Pipes are a per-cell flag in `GridData` that can lie under empty, zoned, grown or built cells, but not under roads (a road placed on a pipe clears it, since the road carries water anyway). Pipes don't give road access. Pipe tool: drag to lay pipes; a drag that starts on a pipe removes them. Cost `PipeCost` $5 per cell, upkeep `PipeUpkeepPerDay` $0.02 per cell. The tool unlocks with Waterworks and is hidden before that. Pipes are drawn only while the Water view or the pipe tool is shown (they're underground). |
| Happiness | New `HappinessBreakdown.Water` term = `−WaterPenalty (0.05) × unwateredHousingShare`, ramped in with `SmallTownGracePopulation`, like Power. It counts in every age with a water rule, which is all of them in the shipped content. `ServiceStats.UnwateredHousingShare`. |
| Growth | New blockers `NoWater` (coverage ages: "no well nearby"; piped ages: "no water") and `WaterAtCapacity`. They are checked after power. Upgrades and redevelopment in piped ages reserve water headroom. Power and water must **both** have headroom before either is reserved (check both with `HasHeadroom`, then call `TryReserve` on both), so a blocked upgrade never leaks a reservation. |
| Ages advancing | Advancing Renaissance → Industrial switches the rule: wells stop counting, so upgrades stall until a tower is up (the same situation as power today). ~~Industrial's `RequiredTechs` = Electricity and Waterworks~~ — changed in 13b: a required tech must belong to an earlier age (`TechDatabase.Validate`), so Waterworks follows the Electricity precedent: an Industrial tech, researched first on arrival, and one of Industrial's `StartingTechs`. The "Welcome to the Industrial Age" toast adds the water hint. Already-grown levels never drop. |
| Content | **Well** (`well`): Utility, 1×1, $150, $1/day, `WaterRadius` 3, no tech (buildable from day one), `ObsoleteAge` industrial. **Fountain** (`fountain`): Service, 1×1, $1,200, $8/day, `WaterRadius` 5 + `CoverageRadius` 3 (it also counts as a park), needs a new Renaissance tech **Aqueducts** (`aqueducts`, 160 RP, prereq Masonry). **Water Tower** (`water_tower`): Utility, 1×1, $2,500, $25/day, `WaterSupply` 300, needs a new Industrial tech **Waterworks** (`waterworks`, 150 RP, no prereq). **Pumping Station** (`pumping_station`): Utility, 2×2, $8,000, $70/day, `WaterSupply` 1,200, needs Public Sanitation (existing; it has no unlock yet). All numbers are tunables (tuned in 13b: tower $1,500 / $15/day / 500, pump $6,000 / $50/day / 1,500, Waterworks 100 RP). |
| Obsolete buildings | New `BuildingDefinition.ObsoleteAge` (age Id string, empty = never): hidden from the toolbar in that age and later. Existing ones stay, and the SelectionPanel says "Obsolete — homes now need piped water". M14's "replace with X" hints build on this field. In piped ages the Fountain keeps its park effect, and its panel says its water no longer counts. |
| Saves | Pipes are persisted, so **`SaveData.CurrentVersion` 2 → 3**: `Pipes` (row-major bytes), plus a `SaveMigrations` step v2 → v3 (no pipes) with a test. Coverage, the network and watered cells are derived and never saved. Old Industrial / Modern saves load with no tower, so upgrades pause until the player builds one (the nudge toast says so). That is accepted, not migrated away. |
| UI | `InfoOverlay.View.Water` ("Water" VIEW button; available once any water building is unlocked, which is always with the shipped content because the Well needs no tech). Coverage ages: covered land blue, uncovered homes red. Piped ages: watered roads and pipes blue, grown cells blue / red, the pipe tilemap shown. HUD `Water` group beside `Power`: "Water demand / supply" (piped) or "Water n% of homes" (coverage), red while any grown cell is dry. SelectionPanel: source lines (radius or supply / city water), "Water ✓ / No water" on grown cells, "Carries water" on roads and pipes, the blocker texts. Tooltip line "Water". Toasts: the power set mirrored (tower online, water lost, shortage, a one-time "dig a well" / "build a water tower" nudge at `SmallTownGracePopulation`). The what-if preview covers water sources too. |

#### Architecture

**Pure, in the Grid / Simulation asmdefs:**

| New / changed | Notes |
|---|---|
| `GridData` (Grid asmdef) | `IsPipe / SetPipe` (false on road cells; `SetRoad(true)` clears the pipe), `CountPipes`, `ExportPipes`, `Import(..., pipes = null)`. `OnCellChanged` on change; `Resize` clears. |
| `UtilityNetwork` | `PowerSystem`'s body moved into an abstract base: `SupplyOf(ServiceSource)`, `CarriesAt(cell)` (power: road; water: road or pipe), `FeedsUnder(cell)` (water: a pipe under a grown cell), and a draw factor. It keeps the BFS order, the even split for multi-network sources, `HasHeadroom` / `TryReserve` and laziness. `PowerSystem` becomes a thin subclass with its public API unchanged (`IsPowered`, `IsEnergisedRoad`…), so `ServicesTests` is the regression proof. |
| `WaterSystem` | Façade owned by `SimulationSystem` (`Simulation.Water`): a `UtilityNetwork` for piped water plus a `CoverageSystem` over `WaterRadius`. Its mode comes from `Rules.Water`. API: `HasWater(cell)`, `IsCarrying(cell)`, `HasHeadroom` / `TryReserve` (always true in coverage mode), `Supply` / `Load` / `Demand` / `DryCells`, `CoverageAt(cell)`, `Mode`. Lazy; recomputes on `OnCellChanged`, on `SetSources` and when the age changes. |
| `CoverageSystem` | Optional `Func<ServiceSource, int>` radius selector (default `CoverageRadius`, so parks are unchanged). |
| `ServiceSource` | `+ WaterSupply`, `+ WaterRadius` (old constructor kept). |
| `AgeDefinition` / `AgeRules` | `WaterRule Water` (replaces the unused bool); `AgeRules(..., water = None)` keeps the old call sites; `Legacy` = `Piped`. `Init(..., water)`. |
| `GrowthSystem` | Water gate beside power in `CollectAtLevel`, `CollectRedevelopment` and `GetBlocker`, with the combined reservation; it reads `WaterSystem` (an optional constructor argument; null = no gate, so the old unit-test constructors keep working). |
| `GrowthBlocker` | `+ NoWater`, `+ WaterAtCapacity` (appended). |
| `ServiceStats` / `HappinessBreakdown` / `PopulationSystem` | `UnwateredHousingShare`, `Water` term (ramped). `Measure(..., countWater, water)`. |
| `EconomySystem` tick | Expense adds `CountPipes × PipeUpkeepPerDay`. |
| `BalanceConfig` | `WaterPerCapacity` 1.0, `WaterPenalty` 0.05, `PipeCost` 5, `PipeUpkeepPerDay` 0.02 — tagged "(M13)", mirrored in the asset. |
| `SaveData` / `SaveMigrations` / `SaveSystem` | v3 `Pipes`; the v2 → v3 step; `Capture` / `ApplyGrid`. |

**Runtime (`Assembly-CSharp`):** `BuildingDefinition.WaterSupply`, `WaterRadius`, `ObsoleteAge` → `GameManager` sources, `GameManager.WaterUnlocked` / `IsObsolete(def)`, `GameEvents.WaterChanged(supply, demand, dryCells)` (+ `ResetSubscribers`), `PlacementController` pipe mode (`SelectPipeTool`, drag lay / remove, hint, cost) + `InputReader` binding (`P`), `PipeTilemapView` (a `GridTilemapView` on a new `Grid/Pipes` Tilemap, thin auto-tiled runtime sprites, visible only in the Water view or with the pipe tool), `InfoOverlay.View.Water`, the toolbar `PIPE` and `VIEW` buttons, `HUDController` Water group, `SelectionPanel`, `HappinessTooltip`, `NotificationController`, and four building prefabs (placeholder primitives with shared materials, colliders on the root).

#### Steps (each one fits a session and is committed on its own)

- **13a Water in the sim (pure) — done (2026-10-03).** Notes from implementing: `PowerSystem`'s body moved unchanged into `UtilityNetwork` (`SupplyOf`, `Carries`, `DrawFor`); `PowerSystem` keeps its API as a thin subclass and `WaterNetwork` is the second one. The shipped ages still have `Water = None` (only the field was renamed in the generator), so the game, `EngagedCity` and `AgeBalanceTests` are untouched until 13b sets the rules with the water buildings; `IndustrialStart_RealContent_Baseline` is therefore unchanged and is re-recorded in 13b. Instead of a separate tower, `SeededCity`'s plant block also supplies water (`WaterSupply` 600, piped, and a well reach over the whole map so the coverage ages are watered too; a 1×1 water-only source at `(0,11)` without a plant; `waterSupply: 0` = dry) — no layout change, so the seeded numbers didn't move at all: day 60 = 212 pop / 0.616 happiness (0.70 with parks), no plant 172 pop; a dry seeded city stalls at 172 pop all level 1 with a −0.05 Water term. Five hand-built unit tests gained a water source (land value, two age tests, the v2 round trip, which now re-places the seeded source like the game re-places buildings). 168 EditMode tests green (19 new in `WaterTests`).
  Original plan: extract `UtilityNetwork` from `PowerSystem` first and commit nothing until `ServicesTests` and the seeded-city tests are unchanged. Then add `WaterSystem` (coverage + piped, without pipes yet: roads only), `WaterRule` on `AgeDefinition` / `AgeRules`, `ServiceSource` fields, the growth gate with the combined reservation, the blockers, the Water happiness term and the `BalanceConfig` fields. `SeededCity` seeds a tower; `TestAges` gets water rules. Tests: coverage radius and multiple sources; the piped network (feeds beside roads, pools, BFS order, the furthest cell goes dry first, headroom and reservation); the combined reservation doesn't leak power when water is short; blockers per age; the rule switching on advance; resize; the Water term (zero in a fully watered city). Run the whole suite and record what moved, including the re-recorded Industrial baseline.
- **13b Content and the engaged player — done (2026-10-03).** Notes from implementing: Waterworks can't be a required tech of the Industrial age (required techs must come from an earlier age), so like Electricity it is an Industrial starting tech that the engaged player researches first on arrival (techs gating a water source get the same priority as power and research buildings). `BuildingDefinition` gained `WaterSupply`, `WaterRadius` and `ObsoleteAge`; `GameManager.IsObsolete` / `CanBuild` / `ObsoleteAgeName`; the toolbar hides obsolete buildings (refreshes on `AgeChanged`) and placement refuses them (hint "Obsolete in the Industrial Age"). Building buttons now narrow from 104 to 80 px when more than five are unlocked (labels auto-size), because the Industrial age's seven buildings pushed the toolbar to 2,046 px; it is 1,878 px now — 13c's Water VIEW button will need room too. Tuning: with the planned tower ($2,500 / $25 / 300) the Medieval-start city spent 100+ days in the Industrial age (63 before M13) — towers and plants competed for cash and the player kept buying parks and roads instead of the tower it needed; the harness now **saves for utilities** (nothing else is bought while power or piped water supply is below demand) and builds the cheapest affordable water source that covers the shortfall, and the content moved to tower $1,500 / $15/day / 500 units, pump $6,000 / $50/day / 1,500 units, Waterworks 100 RP. Result: from Medieval 86 / 62 / 69 days (Modern on day 217, was 211), never in debt (min $2,153), happiness ≥ 0.64; wells don't slow the early ages at all (9 wells by Industrial); Renaissance / Industrial / Modern starts at day 120: 447 / 588 / 888 pop (Industrial and Modern exactly as in M12); the Industrial-start baseline didn't move (220 / 0.660; `SeededCity`'s plant block supplies water). Play-mode check: a Medieval city shows only the Well (it waters x ± 3), an Industrial city with Public Sanitation shows Fountain / Water Tower / Pumping Station and no Well, an obsolete well can't be selected, the four prefabs render (screenshot). 168 EditMode tests green.
  Original plan: Generator: the `WaterRule` per age, the Aqueducts and Waterworks techs, Industrial's required / starting techs. The four `BuildingDefinition`s and prefabs, plus `ObsoleteAge`. `SeededCity` / `EngagedCity.LoadBuildings` mirror the new asset fields (the M12 lesson). `EngagedCity` digs a well for any block blocked by `NoWater` in coverage ages (cheapest first, one a day), researches Waterworks before advancing into Industrial, builds a tower on the first piped day and adds towers / pumps when `WaterAtCapacity` shows up. Re-run `AgeBalanceTests` and tune (the first knobs: well radius and cost, `WaterPenalty`). `ContentTests` stays green. Runtime minimum so the game stays playable: `GameManager` sources and the toolbar hides obsolete buildings.
- **13c Water UI — done (2026-10-03).** Notes from implementing: the HUD is already ~1,920 px wide, so instead of a new group the water line sits under the power line in the Power group (`HUD/Power/WaterText`; the group shows when power or water is unlocked, each line on its own). Six VIEW buttons didn't fit either, so all of them are 88 px (was 104); the toolbar stays under 1,920 with seven buildings. `WaterSystem.Status` → `WaterStatus` (mode, piped supply / demand, dry / grown cells) is the event payload. The well nudge fires from 10 residents (wells gate the very first upgrade), the tower nudge at the usual `SmallTownGracePopulation`. Checked in Play mode: a Medieval street half in a well's reach (HUD "Water 58%" in red, reach tinted on the ground, homes blue / red, the panel's "No well nearby" and blocker text); an Industrial street split by a road gap (HUD "Power 176 / 600 · Water 176 / 500", the tower's road blue, "Carries water" on it, the "Water shortage" toast, the welcome toast's water hint). The what-if preview is exercised in 13e's play-through. 169 EditMode tests green (+ `WaterTests.Status_ReportsTheRuleSupplyAndDryShare`).
  Original plan: `GameEvents.WaterChanged`, `GameManager.WaterUnlocked`, the HUD Water group, the Water view (both modes, what-if preview), the SelectionPanel lines and blocker texts, the tooltip line and the toasts (including the Industrial welcome hint and the nudge). Play-mode screenshots of the view in a Medieval and an Industrial city.
- **13d Pipes and save v3 — done (2026-10-03).** Notes from implementing: `UtilityNetwork` now also feeds the carrying cell itself (self first, then `CellUtils.Neighbors4`'s order, so power's BFS is unchanged — roads draw nothing); `WaterNetwork` carries through roads and pipes. Pipes cost $5 a cell and $0.02/day (`PipeCost`, `PipeUpkeepPerDay`); no pipes exist in the age-less baseline, so no number moved. The pipe tool paints while the button is held (one "not enough money" toast per drag) and is bound to `P` (new `PipeTool` action); its button sits after Road and appears with Waterworks. Adding it pushed the toolbar to 2,002 px, so `ToolbarController` now scales the whole bar down to fit the canvas (0.95 at 1920 in an Industrial city with Public Sanitation) instead of squeezing buttons further. Carrying pipes are drawn pale with dark rims (a blue tint vanished on the Water view's blue ground), unfed ones grey. Checked in Play mode: a pumping station two rows behind the street, piped under a home to the road, feeds all 1,500 units and the home above the pipe; a pipe that stops short of the road waters only its neighbour; save → New City → load restores the 8 pipes and the same water numbers (the player's save was backed up and restored). 177 EditMode tests green (+8: pipes in `WaterTests`, `SaveSystemTests.SaveWithPipesMidShortage_ThenContinue_MatchesUninterruptedRun`, `SaveMigrationTests.V2Save_MigratesToV3_WithNoPipes`, the v1 fixture now also checks its empty pipe layer).
  Original plan: `GridData` pipes, the network carrying pipes and feeding cells with a pipe under them, the pipe upkeep, save v3 + migration + tests (v2 → v3, the v1 fixture through the chain, a round trip with pipes mid-shortage = uninterrupted run). Runtime: the pipe tool (button, `P`, drag lay / remove, hint, cost), `PipeTilemapView`, and pipes in the Water view. Tests: a pump away from roads linked by a pipe; two road networks joined by a pipe pool their supply; a pipe under a grown cell feeds it; a road placed on a pipe clears it; resize.
- **13e Balance, play-through, docs — done (2026-10-03).** Notes from implementing: `AgeBalanceTests` unchanged since 13b (from Medieval 86 / 62 / 69 days, Modern on day 217, never in debt, happiness ≥ 0.64; Renaissance / Industrial / Modern starts 447 / 588 / 888 pop at day 120; Industrial-start baseline 220 / 0.660). **Tick cost:** a network recompute is O(cells) and costs ~0.9 ms (power) and ~1.1 ms (water, with pipes) on a fully grown 96² map in EditMode; a day with any upgrade recomputes both twice (growth, then happiness), so the EditMode tick is 2.2 ms on a quiet day and 5.6 ms on an upgrade day (3.3 ms with the water rule off). `UtilityNetwork` now caches each cell's draw and carrying flag per recompute and `CapacityModel` copies the per-age scales into arrays (the `UnityEngine.Object` null check on the `AgeDatabase` ran per cell) — about −10%. Over the ≈1–2 ms target, so `perf-benchmark` ran in a development player, its city now fed with power and water from the west edge plus a pipe in every block: sim tick 3.52 ms (1.37 ms with power only), frames unchanged (idle 1.27 ms, 4× 1.30 ms, zoomed out 1.54 ms), edit-every-frame 3.8–4.5 ms (2.8–3.3 ms power only; ~2 ms before networks carried flow) — the per-frame `WaterChanged` recompute. Accepted for M13 (a 96² map at 4× spends ~1.4% of a second in ticks); making the networks incremental is noted as follow-up work (done right after M13: tick 1.56 ms, see §13 Water → Performance). UI-only play-through (virtual mouse / keyboard): New (Medieval, 32²) → road and zones → homes stuck at level 1, the HUD "Water 0%", the panel's "No well nearby … dig a well within reach" → Water view from its button → two wells from the toolbar → level 2 → (debug: jump to the Industrial age, research to it takes hundreds of days on 32²) → the welcome toast's tower hint, Well gone from the toolbar, Water Tower and Pipes shown, wells stop counting → plant and tower → 23 blocks rebuilt / grown in the Industrial style → a second tower off the road (no supply) → pipe tool from the toolbar (Water view) → drag a pipe to the road: supply 500 → 1,000, HUD "Water 268 / 1,000" → a drag from a pipe removes it, re-laid → `P` toggles the tool → Save, skip, Load: pipes and both towers back → `V` cycles Off → Power → Water. 30 checks; the one failure in the second run was driver state (the Water view was left chosen by the first run, so its button toggled it off) and passed in a clean re-check. The first run's pump checks failed only because the debug jump doesn't research Public Sanitation (a tower was used instead). The player's save was backed up and restored. 178 EditMode tests green.
  Original plan: Final `AgeBalanceTests` pass; measure the full 96² tick (power + water networks; it must stay ≈1–2 ms, and run `perf-benchmark` only if not or if the pipe tilemap shows up in frame time). UI-only play-through: New (Medieval, 32²) → zone and road → homes held at level 1, the panel says "no well nearby" → Water view → dig a well → level 2 → (debug) research to and advance into Industrial → wells drop out, the toast → build a tower → upgrades resume → a pumping station off the road, a pipe to the roads → the HUD supply rises → save, load, the pipes are still there. Docs: §13 (a new *Water (M13)* section; Services & power notes the shared `UtilityNetwork`; Save / load v3; UI), the §12 notes and status, §8, the `AGENTS.md` Project bullet, the `CLAUDE.md` header.

#### Risks / open questions

- **Medieval pacing is already near the limit** (86 days of 90). Wells now gate the only Medieval upgrade, so the engaged player must dig them promptly. If the age runs over, first make the well cheaper or wider before touching `MaxGrowthPerDay` (which is shared with the age-less game).
- **The Industrial stall on advancing** is intended, but `EngagedCity` must research Waterworks early or Renaissance → Industrial pacing slips (the advance now has one more required tech).
- **Old saves:** every v1 / v2 Industrial or Modern city loads without a tower, and its upgrades pause until the player builds one. Accepted (the nudge toast explains it); the alternative (placing a free tower during migration) would need a free spot beside a road.
- **Refactoring `PowerSystem`** risks a subtle order change in the BFS. Mitigation: extract first, with the full suite green and the seeded numbers identical, before any water code.
- **Tick cost:** a second network BFS after every grid change, which is almost every day. Power costs well under 0.4 ms on a full 96² map, so measure it in 13e.
- **Pipe UX:** underground pipes are invisible outside the Water view, which can confuse. The pipe tool forces the view on (like the Plant / Park tools force theirs), and the SelectionPanel mentions a pipe under a cell.

### M14 — Civic services (plan, 2026-10-03; revised after M13)

**Goal:** a growing city needs looking after. Four service lines (order, fire, health and
education) each get one building per age as the tech tree unlocks them. Without cover, crowded
homes suffer from crime, fear of fire and sickness. Crime also drags land value down, so it feeds
the M12 level-3 gate. Education buildings produce research, and the residents they reach produce
more. Older service buildings keep working, but once a better one of the same line can be built,
they leave the toolbar and their panel says "Outdated — replace with X". This builds on M13's
`ObsoleteAge` / `CanBuild`. The milestone also gives the many effect-less techs most of their
content.

**Done when:**
- A town past a few hundred people without a Watch House, Bucket Brigade or Apothecary has
  visibly lower happiness. The happiness tooltip names crime, fire risk and sickness. Placing the
  three buildings (they are cheap) removes those penalties within their reach.
- Crime lowers land value: a dense level-2 block with no order cover can be held by the level-3
  gate, and a Police Station nearby lets it upgrade.
- Each line's buildings unlock through techs in each age (content table below). The toolbar shows
  only the best buildable building of each line. A placed older building shows "Outdated —
  replace with X (strength, reach)".
- Education: a Monastery, Academy, University or Research Lab produces its flat RP, plus RP from
  the residents it reaches. The tech panel's research line shows both.
- A Services view (one sub-view per line: crime or fire risk where they apply, health and
  education cover) and SelectionPanel lines explain all of it.
- `AgeBalanceTests` still meet their targets (45–90 days per age, never in debt, happiness ≥ 0.55),
  with `EngagedCity` building civic services. All EditMode tests are green, and a UI-only
  virtual-input play-through passes.

#### Design decisions (defaults — change any before the step that uses them)

| Topic | Decision |
|---|---|
| Baseline | **Civic needs apply everywhere, the age-less sim included** (default, as in M12 / M13: one model, and the no-age sim still equals the Industrial rules). Every need ramps in with city population, from `CivicFreePopulation` 100 to `CivicFullPopulation` 700. The 24² seeded city (≤ ~350 pop) therefore moves only a little (estimate: −0.02 to −0.05 happiness at day 90–120). The legacy checks stay qualitative (retune only if one fails), and `IndustrialStart_RealContent_Baseline` (220 / 0.660) is re-recorded. Education changes nothing without ages, because research is 0 there. *Alternative: needs are zero without an `AgeDatabase`. Legacy numbers would stay untouched, but the no-age sim would drift from Industrial.* |
| Civic reach is its own field | Parks keep `CoverageRadius`, which the Fountain also uses as a park since M13, and the park count semantics (Services happiness, land value) are untouched. Civic buildings get new fields instead: `BuildingDefinition` / `ServiceSource` `+ CivicKind` (`ServiceKind` enum: `None, Order, Fire, Health, Education`, appended only, since it is serialized), `+ CivicRadius`, `+ CivicStrength` (0..1). These go in optional trailing constructor args, so every existing call site keeps its meaning. |
| Civic cover | A new `CivicCoverage` stores one `float[]` per kind = the **best strength in reach** (Chebyshev from the footprint, no stacking). Two watch houses side by side gain nothing, and a newer tier simply overrides. It is separate from `CoverageSystem` (which counts), recomputed when `Sources` is set, and reallocated in `Resize`. |
| Population ramp | `ramp = clamp01((pop − CivicFreePopulation) / (CivicFullPopulation − CivicFreePopulation))`, shared by the three penalties. Small towns (and almost all of the Medieval age, capacity ×0.5) feel nothing, and a city of 700+ feels the full effect. |
| Crime (order) | Per grown residential or commercial cell: `crime = min(1, capacity × CrimePerCapacity 0.04) × ramp × (1 − order)`. An Industrial level-1 home has capacity 4, so potential 0.16; level 3 (16) gives 0.64. Industry and empty land have none. Derived, never saved. |
| Crime effects | Happiness: a per-home `min(crime × CrimePenalty 0.15, CrimePenaltyCap 0.10)`, housing-weighted → the `HappinessBreakdown.Crime` term. Land value: `− crime × LandValuePerCrime 0.15` (a new `LandValueBreakdown.Crime` line), so an uncovered dense block falls toward the 0.40 level-3 gate. No new growth blocker: `LowLandValue` already explains it, and the panel names crime as a contributor. One-way: land value never feeds crime. |
| Fire risk | Per grown cell (all zones): `risk = FireRisk(built age) × (industrial ? FireRiskIndustrialFactor 1.5 : 1) × ramp × (1 − fire)`. `AgeDefinition.FireRisk` (generator column): Medieval 0.6 (timber), Renaissance 0.45, Industrial 0.35, Modern 0.2. Without ages, `BalanceConfig.FireRisk` 0.35. **In M14 it only costs happiness** (default): a per-home `min(risk × FirePenalty 0.10, FirePenaltyCap 0.06)` → `HappinessBreakdown.Fire`. M17 uses the same field to start fires. *Alternatives: a level-3 gate (one more gate beside road / power / water / land value), or display only.* |
| Health | Per home: `sickness = (1 − health) × ramp` → the `HappinessBreakdown.Health` term = housing-weighted `−sickness × HealthPenalty 0.08`. With full cover the term is 0; it never becomes a bonus. M17's plague reads the same cover. |
| Education | Research gains `ResearchPerEducatedResident` (0.01) × Σ over grown homes of `residents × education strength` (a fully covered city of 300 adds +3 RP/day), before the research multiplier. Buildings keep their flat `ResearchPerDay`. No happiness term. Without ages, research is 0, so the baseline is unaffected. The Monastery and Academy gain education reach, so research in an early-age city rises a little; that helps the tight Medieval pacing rather than hurting it. |
| Max uncovered cost | Crime 0.10 + fire 0.06 + health 0.08 = 0.24 happiness at full ramp, which is about the pollution cap. A city that builds nothing sinks below 0.55 and loses residents. A covered city loses nothing. |
| Outdated vs obsolete | M13's `ObsoleteAge` (hidden from that age on, "Obsolete" in the panel) stays for buildings whose purpose ends (wells). Civic tiers don't use it, because the successor's tech may not be researched yet when an age starts, and that would leave a line with nothing to build. A civic def is **outdated** instead: `GameManager.ReplacementFor(def)` = the latest-age def of the same `CivicKind` that `CanBuild`, if it is newer than `def`. An outdated def is hidden from the toolbar and refused by placement (like obsolete; `RestoreBuilding` / loads still place it). Placed copies keep their own strength and reach (no decay), and the panel says "Outdated — replace with X (strength s, reach r)". There is no automatic replacement; a one-click Replace button is a stretch goal in 14c (UI). |
| Toolbar | Best-per-line means 11 new buildings add at most 3 buttons in any one age (education replaces the Monastery / Academy pair). The toolbar already scales itself down to fit since 13d. Measure the scale at 1920 in an Industrial city with every line unlocked (13d: 0.95). *Measured in 14d: 0.87.* |
| Saves | Kinds, reach and strength come from the building assets. Crime, fire risk, sickness and cover are derived, so there is **no version bump** (stays v3). |
| UI | **One** new VIEW button, "Services" (`InfoOverlay.View` gains `Order, Fire, Health, Education`, appended after `Water`). Clicking it steps through the sub-views whose line has a buildable or placed building (the `PowerUnlocked` / `WaterUnlocked` pattern), and the label shows the sub-view. `V` cycles them like any view. Order and Fire shade crime / fire risk like the Pollution view; Health and Education shade cover strength, with uncovered homes striped once the ramp is above 0. Selecting a civic building to place switches to its sub-view and previews its reach (the 13c water-preview pattern). The park Coverage view is unchanged. |

#### Content (first pass; numbers are tunables)

| Line | Building (Id) | Age | Tech | Size | Cost | Upkeep/day | Reach | Strength | RP/day |
|---|---|---|---|---|---|---|---|---|---|
| Order | Watch House `watch_house` | Medieval | **Town Watch** `town_watch` (new) | 1×1 | $400 | $2 (tuned 14b, was $4) | 4 | 0.5 | — |
| Order | Constabulary `constabulary` | Renaissance | Civic Planning | 2×1 | $1,500 | $6 (tuned 14b, was $12) | 6 | 0.75 | — |
| Order | Police Station `police_station` | Industrial | Telegraph | 2×2 | $4,000 | $30 | 8 | 1.0 | — |
| Fire | Bucket Brigade `bucket_brigade` | Medieval | Town Watch | 1×1 | $300 | $1 (tuned 14b, was $3) | 3 | 0.5 | — |
| Fire | Fire Engine House `fire_engine_house` | Renaissance | Watermills | 2×1 | $1,200 | $5 (tuned 14b, was $10) | 5 | 0.75 | — |
| Fire | Fire Station `fire_station` | Industrial | Steam Power | 2×2 | $4,000 | $30 | 8 | 1.0 | — |
| Health | Apothecary `apothecary` | Medieval | **Herbalism** `herbalism` (new) | 1×1 | $500 | $2 (tuned 14b, was $5) | 4 | 0.5 | — |
| Health | Hospital `hospital` | Industrial | Public Sanitation (also the Pumping Station since M13) | 3×2 | $4,500 (tuned 14d, was $6,000) | $35 (tuned 14d, was $45) | 9 | 0.85 | — |
| Health | Medical Centre `medical_centre` | Modern | **Antibiotics** `antibiotics` (new) | 3×3 | $10,000 | $70 | 11 | 1.0 | — |
| Education | Monastery `monastery` (exists) | Medieval | Monasticism | 2×2 | $2,000 | $10 | 5 (new) | 0.4 (new) | 3 |
| Education | Academy `academy` (exists) | Renaissance | Academies | 2×2 | $5,000 | $25 | 6 (new) | 0.6 (new) | 5 |
| Education | University `university` | Industrial | **Universities** `universities` (new) | 3×3 | $7,500 / $40 (tuned 14b, was $9,000 / $50) | — | 8 | 0.85 | 8 |
| Education | Research Lab `research_lab` | Modern | Computing | 2×2 | $10,000 / $50 (tuned 14b, was $12,000 / $60) | — | 8 | 1.0 | 12 |

New techs (in `Tools/gen_age_content.py`; GUIDs are deterministic, so existing references survive):
- Town Watch (Medieval, 40 RP, after Charters): unlocks the Watch House and Bucket Brigade.
- Herbalism (Medieval, 35 RP, after Crop Rotation): unlocks the Apothecary.
- Universities (Industrial, 500 RP, after Telegraph + Academies): unlocks the University.
- Antibiotics (Modern, 900 RP, after Public Sanitation): unlocks the Medical Centre.

Existing techs that gain an unlock: Civic Planning, Watermills, Telegraph, Steam Power, Public
Sanitation and Computing (descriptions gain "Unlocks the X"; the tech panel already lists unlocks
from `RequiredTech`). No `TechsToAdvance` count changes. Check that the Medieval run's pacing
(86 days, near the 90 limit) doesn't slip: the new techs add cheap options, and `EngagedCity`'s
tech priority must not put them ahead of Waterworks, power or research techs.

Renaissance has no health tier (the Apothecary carries on), and Modern has no order or fire tier
(the Industrial ones are already full strength). Both are deliberate to keep the asset count down;
M18 can add more.

**Placeholder art (as built in 14b):** `Tools/gen_civic_content.py` writes the prefabs, materials and
building assets directly as YAML (no Editor in that session), re-runnable with deterministic GUIDs.
Planned: prefabs built from primitives through RunCommand like the M13 ones (prefab
contract: pivot at the ground centre, layer 9, collider on the root, collider-less `Decor`). Each
line has one shared roof material (order blue, fire red, health white with a green cross block,
education purple), and the walls reuse existing materials (`MonasteryStone`, `PumpBrick`, …).
That makes 4 new shared materials and no per-instance materials. See `scene-prefab-editing`.

#### Architecture

**Pure, in the Simulation asmdef:**

| New / changed | Notes |
|---|---|
| `ServiceKind` | `None, Order, Fire, Health, Education`. |
| `ServiceSource` | `+ CivicKind`, `+ CivicRadius`, `+ CivicStrength` (optional trailing args). |
| `CivicCoverage` (new) | Per-kind max-strength `float[]`s; `Recompute(sources)`, `GetStrength(kind, cell)`, `Resize`. |
| `CivicSystem` (new) | Per-cell `Crime`, `FireRisk`, `Sickness` from `GridData` (zones, levels, built ages), `CapacityModel`, `CivicCoverage`, the age data (fire risk per built age) and the population ramp. Computed on read (a capacity lookup and the cover strength; 14a found no cache was needed), so only `CivicCoverage` is stored, recomputed by `SetSources` and on `OnResized`. `Explain(cell)` → `CivicBreakdown {CrimePotential, Order, Crime, FireRisk, Fire, Sickness, Health, Education}` for the UI. Reallocates on `OnResized`. Owned by `SimulationSystem` (`Simulation.Civic`). |
| `ServiceStats` | `+ CrimePenalty`, `+ FirePenalty`, `+ HealthPenalty` (housing-weighted, capped per home), `+ EducatedResidents`. `Measure` reads `CivicSystem` (an optional argument; null = no civic terms, so old call sites keep their numbers). Static `CrimePenaltyAt` / `FirePenaltyAt` / `HealthPenaltyAt` for the panel. |
| `HappinessBreakdown` / `PopulationSystem` | `+ Crime`, `+ Fire`, `+ Health` (optional constructor args after `water`, default 0; in `Total`). They are already ramped, so not multiplied by the `SmallTownGracePopulation` ramp. |
| `LandValueSystem` | `− crime × LandValuePerCrime`; `LandValueBreakdown.Crime`. Reads `CivicSystem` (optional). |
| `SimulationSystem` | Owns `Civic`. `ResearchIncome()` adds `EducatedResidents × ResearchPerEducatedResident` before the multiplier, and `ResearchBreakdown()` returns {commercial, buildings, education} for the UI. |
| `AgeDefinition` / `AgeRules` | `+ FireRisk` (read per built age, like `PollutionScale`; `CapacityModel`-style array cache, since M13 found per-cell `UnityEngine.Object` null checks costly). |
| `BalanceConfig` | `CivicFreePopulation` 100, `CivicFullPopulation` 700, `CrimePerCapacity` 0.04, `CrimePenalty` 0.15, `CrimePenaltyCap` 0.10, `LandValuePerCrime` 0.15, `FireRisk` 0.35, `FireRiskIndustrialFactor` 1.5, `FirePenalty` 0.10, `FirePenaltyCap` 0.06, `HealthPenalty` 0.08, `ResearchPerEducatedResident` 0.01, all tagged "(M14)" and mirrored in the asset. |

**Runtime (`Assembly-CSharp`):** `BuildingDefinition.CivicKind` / `CivicRadius` /
`CivicStrength` → `GameManager` sources; `GameManager.ReplacementFor(def)`, `IsOutdated(def)`
(folded into `CanBuild`), `CivicUnlocked(kind)`; `ToolbarController` (hides outdated defs; one
Services VIEW button); `InfoOverlay` sub-views; `SelectionPanel`, `HappinessTooltip`, `TechPanel`
research line, `NotificationController`.

#### Steps (each one fits a session and is committed on its own)

- **14a Civic cover, crime and order (pure) — done (2026-10-03).** Notes from implementing: crime is
  computed on read (capacity × `CrimePerCapacity` × ramp × (1 − order)), so `CivicSystem` stores only
  `CivicCoverage` and needs no dirty flag; the ramp reads the population the sim had at the start of
  the tick (saved, so load → continue still equals an uninterrupted run). Planned numbers kept. What
  moved (no population, money or pacing changed anywhere; only happiness): seeded city 0.616 / 0.618 /
  0.586 → 0.610 / 0.600 / 0.559 at day 60 / 90 / 120 (Crime −0.006 / −0.018 / −0.027; with parks
  0.702 → 0.696 at day 60); Industrial-start baseline re-recorded 220 pop / 0.660 → **0.653** (Crime
  −0.007); `EngagedCity` (no civic buildings until 14b) Renaissance / Industrial / Modern starts keep
  447 / 588 / 888 pop at day 120 with Crime −0.04 / −0.07 / −0.10 (min happiness 0.708 / 0.722 /
  0.714); from Medieval still Modern on day 217, min happiness 0.644 → 0.589. Every `AgeBalanceTests`
  target holds. Verified with a Mono + NUnitLite stand-in harness (stub `UnityEngine`, YAML-backed
  `AssetDatabase`, which reproduces the M13 numbers exactly), because this session had no Unity
  Editor: 171 Simulation EditMode tests green (+11 in `CivicTests`); still to be run in the Editor.
  Original plan: `ServiceKind`, the `ServiceSource` fields,
  `CivicCoverage`, `CivicSystem` with crime only, the Crime happiness term, the land-value crime
  term, and the `BalanceConfig` fields for the ramp and crime. Tests (new `CivicTests.cs`):
  max-not-sum strength and reach; parks and the Fountain unaffected (all `ServicesTests`,
  `LandValueTests` and `WaterTests` untouched); crime scales with capacity, ramp and order;
  industry has no crime; zero below `CivicFreePopulation`; the land-value line; resize; laziness
  (recomputes after a level change or a source change). Run the whole suite and record which
  seeded-city numbers moved (old vs new), as 12a did.
- **14b Fire risk, health, education, with the content and the engaged player — done (2026-10-03).**
  Merged with 14c (decided 2026-10-03): with fire risk and sickness in but nothing to build against
  them, the Medieval run spent 105 days in the Industrial age (limit 90), so the buildings and the
  `EngagedCity` civic placement planned for 14c / 14e came in now to keep `AgeBalanceTests` green.
  Notes from implementing: research reads the education share measured in the tick's
  `MeasureServices` (`SimulationSystem` keeps it; `Restore` re-measures), so the HUD rate costs no map
  scan; `CivicSystem` caches fire risk per age index. Content: the 4 techs (36 in all) and descriptions
  in `gen_age_content.py`; the 11 buildings, 4 line materials (`CivicOrder` / `Fire` / `Health` /
  `Education`) and placeholder prefabs from the new `Tools/gen_civic_content.py` (written as YAML,
  re-runnable; also gives Monastery / Academy their education reach); runtime: `BuildingDefinition`
  civic fields → `GameManager` sources, `GameManager.ReplacementFor` / `IsOutdated` (in `CanBuild`, so
  the toolbar hides and placement refuses outdated tiers), the placement hint "Outdated — build the X"
  SelectionPanel lines (line, reach, strength, "Outdated — replace with X") and the happiness tooltip
  lines "Crime", "Fire risk", "Sickness (no health care)" (from the UI step, so the tooltip still adds up). `EngagedCity`: skips
  outdated tiers; for a line costing ≥ 0.01 happiness it places the best buildable building where it
  removes the most loss (≥ 0.002 of city happiness), in a block middle (≤ 2×2) or an already open
  service block, never while a plant / tower is wanted or when the day's surplus can't carry the
  upkeep, and never opening blocks itself; techs leading to a needed line (and their prerequisites)
  are researched first; research buildings past the first wait for a surplus covering their upkeep.
  What tuning found (each fixed before committing): 20 Apothecaries spammed for a line whose term
  never fell under the trigger (fixed by placing on removed loss); civic buildings taking service
  slots / opening blocks for big buildings cost up to 200 road cells ($200/day; fixed by middles only
  and no opening); Medieval / Renaissance tier upkeep halved (it dragged the Industrial age to 152
  days); University / Research Lab cheaper (the Modern start only just doubled its population).
  Result: from Medieval 84 / 61 / 73 days (Modern on day 218; M13 217), min happiness 0.565, never in
  debt; Renaissance / Industrial / Modern starts 447 / 584 / 763 pop at day 120 (M13 447 / 588 / 888),
  min happiness 0.70 / 0.68 / 0.72; seeded city happiness 0.589 / 0.556 / 0.504 at day 60 / 90 / 120
  (no civic buildings in the seeded city; population and money unchanged); Industrial-start baseline
  re-recorded 220 / 0.653 → **0.631** (fire −0.007, sickness −0.016). Left for 14d (balance): the later starts
  leave sickness uncovered (−0.06 / −0.08: hospitals are 3×2+ and only fit open service blocks, block
  middles hold parks) and nothing replaces outdated buildings. Verified in the sim harness (177
  Simulation tests green, +4 in `CivicTests`, +1 `ContentTests.CivicLines_…`; the harness now loads
  non-`.asset` references such as prefabs as placeholders); prefabs were checked structurally (every
  fileID / GUID resolves) but not seen: an Editor compile, Test Runner pass and Play-mode look are owed.
  Original plan: `AgeDefinition.FireRisk` (+ generator column),
  fire risk and sickness in `CivicSystem`, the Fire and Health terms, educated residents and
  `ResearchIncome`. Tests: fire risk by built age and the industrial factor; fire / health cover
  removes the penalty; education RP scales with residents × strength and goes through the
  multiplier; the no-age sim gains no research. Re-record `IndustrialStart_RealContent_Baseline`
  once the numbers settle.
- *Old 14c Content and placeholder art — merged into 14b (2026-10-03); the later steps moved up a letter
  (UI is now 14c, balance / play-through / docs 14d).* Original plan: the 4 techs and new descriptions in the generator, the 11
  building definitions and prefabs, the 4 materials, Monastery / Academy education reach, the
  `BuildingDatabase`, and `GameManager.ReplacementFor` with the toolbar / placement hiding (the
  minimum to keep the game playable, as 13b did). Mirror the assets in `EngagedCity.LoadBuildings`
  (it reads them) and in `SeededCity` if it places services (the M12 lesson). `ContentTests`:
  every civic def has a kind, a strength in (0, 1], a reach and a reachable tech; each line has
  exactly the buildings per age in the content table; strengths rise with age within a line; the
  tree is still valid and reachable. Play-mode screenshot of all 13 civic buildings placed in a row.
- **14c UI — done (2026-10-03).** Notes from implementing: `InfoOverlay.View` gains `Order`, `Fire`,
  `Health`, `Education` (after `Age` in the `V` cycle; available once a building of the line is
  unlocked, `GameManager.CivicUnlocked`). Ground = the line's cover strength in its colour (blue /
  orange / green / purple), striped red where a cell the line serves needs it but nothing reaches it;
  buildings pale → red by crime (full at 0.5) / fire risk (0.6) / sickness (1), schooled homes grey →
  purple, cells the line doesn't serve dark grey. A civic building's tool switches to its view and
  previews the newly covered cells (`InfoOverlay` keeps a preview `CivicCoverage`). The single
  Services VIEW button is **made at runtime** as a copy of the Age view button (no `Main.unity` edit:
  this session had no Editor); clicking steps Crime → Fire → Health → Schools → Off and the label
  follows. SelectionPanel: crime (homes and shops), fire risk, health care and schooling with each
  one's happiness cost on grown cells (or the small-town note below `CivicFreePopulation`); crime in
  the land-value breakdown; civic buildings say how many cells they reach. Toolbar tooltips describe
  civic buildings. Tech panel: a muted line splitting the RP (shops / buildings / schooled residents,
  × techs). Toasts: once per city and line when its term passes −2% and a building of it can be built
  ("Crime is rising — build a Watch House…"; re-seeded on load), and the research-complete toast names
  the placed buildings a new tier outdates. Not done: the Replace button (stretch). Verified by a
  Roslyn parse of every changed runtime file and an identifier check by hand; the 177 Simulation tests
  are unchanged and green in the harness. **Owed in the Editor:** a compile, the Play-mode screenshots of
  each sub-view, the toolbar scale at 1920 with the extra button, and whether the tech panel's status
  box fits its third line.
  Original plan: the Services VIEW button and sub-views, and the auto-switch plus reach preview while
  placing. SelectionPanel on grown cells: "Crime x% (order y%)", "Fire risk x% (fire cover y%)",
  "Health cover y%" with the happiness cost of each, and "Education y%" on homes. On civic
  buildings: line, strength, reach, homes covered, RP (education), and the "Outdated — replace
  with X" line. The land-value breakdown gains crime. Happiness tooltip: "Crime", "Fire risk",
  "Sickness (no health care)". Tech panel: research split (commercial / buildings / education).
  Toasts: once per city the first time each penalty reaches 0.02 while its line is buildable
  ("Crime is rising — build a Watch House"), and "X unlocked — replaces your Y" when a placed
  building becomes outdated. Play-mode screenshots of each sub-view on a seeded city; check the
  toolbar scale at 1920. Stretch goal: a Replace button (demolish and place the replacement on the
  same origin when its footprint fits and the player can afford it).
- **14d Balance, play-through, docs — done (2026-10-04).** First, the Editor checks 14b / 14c owed: the
  branch compiles, all EditMode tests pass in the Test Runner (194, then 195), the 13 civic buildings render
  with their line roofs, each Services sub-view looks as planned (Crime / Fire / Health / Schools), the
  toolbar scales to **0.87** at the 1920 reference in an Industrial city with every line buildable (13d: 0.95;
  "Fire Engine House" auto-sizes small but stays legible), and the tech panel's third line fits.
  Balance: the later starts left sickness uncovered because the Hospital and Medical Centre never got built
  (the probe showed why: early on these cities have $16k–45k but a daily deficit, so "the surplus carries
  the upkeep" refused them; once the surplus turned positive the cash had gone on blocks and parks).
  `EngagedCity` now handles the costliest line first; may build on its cash (cash after building ≥ 60
  days of the deficit it leaves) for a line costing ≥ 0.03; saves for that line's building (no new
  blocks, parks or cheaper civic buildings) when it can't yet afford it; lets buildings bigger than 2×2
  take a reserve block or open the next block (one block of roads, never `OpenServiceBlock`); and
  demolishes an outdated building once a newer tier covers its zoned cells at least as strongly (it
  never triggered in the 120-day runs: the tiers land in different places). Hospital $6,000 / $45 →
  **$4,500 / $35** (the Industrial start saved for 60 days and still couldn't reach $8,000 with the
  cushion). Result: from Medieval 84 / 61 / 73 days (unchanged; min happiness 0.57); Renaissance /
  Industrial / Modern starts 447 / 557 / 630 pop at day 120 (14b: 447 / 584 / 763) with happiness 0.71 /
  **0.72** / **0.83** (14b: 0.71 / 0.69 / 0.72) and health terms −0.024 / −0.019 / 0 (14b: −0.024 / −0.064 /
  −0.080): the later starts trade some growth for a happier city, which the targets allow (more than double
  between day 60 and 120, never in debt). A first attempt (cash rule for every line) put the Modern start
  in debt (−$734) by buying a Hospital early; the 0.03 floor fixed it. Industrial-start baseline unchanged
  (220 / 0.631). Not fixed: a Renaissance start then spends 107 days in the Industrial age without new-tier
  civic buildings (no test covers a later start's next age). Tick on a full 96² Modern city in EditMode
  (probe, all four civic lines covered): 1.83 → **1.57 ms** after two fast paths — `CivicSystem.HomeNeeds`
  (the capacity `ServiceStats.Measure` already has, the ramp read once, flat-index cover reads) and a
  crime-only `GetCrime` for land value; the civic share of `Measure` went 0.22 → 0.14 ms, cover recompute
  0.02 ms, so no player benchmark (the plan's 0.3 ms trigger). `PerfBenchmark` feeds sim-only civic cover
  from now on. UI-only play-through (virtual input, 49 checks, 0 failed): New Medieval 64² → roads, zones,
  12 wells → 240 days without services (234 pop; tooltip shows crime −1%, fire −1%, sickness −2%) → Town
  Watch (via Crop Rotation, Markets, Charters) and Herbalism by clicks → 2 Watch Houses, 2 Bucket Brigades,
  3 Apothecaries from the toolbar → the Services button steps Crime → Fire → Health → Off → needs per ramp
  fall (fire −0.060 → −0.054, health −0.080 → −0.059) → Apothecary panel; New Industrial 64² →
  Constabulary is the order button → 2 plants, 3 towers, a Constabulary away from the homes → after 150
  days a level-2 home held by crime (land value 0.396, crime −0.034; its panel now suggests "keep order")
  → Telegraph by click → the toolbar swaps in the Police Station and the Constabulary's panel says
  "Outdated — replace with the Police Station" → Police Station beside the home → land value 0.392 →
  0.430, level 3. Found by the play-through: the low-land-value hint only said "add parks or move industry
  away"; it now lists the fixes from the breakdown (`SelectionPanel.LandValueFixes`). In a city that builds
  nothing, crime is rarely what holds a home (−0.05 at most at level 2 against pollution's −0.2), so the
  driver picks a home whose land value without crime clears the gate.
  Original plan: `EngagedCity` learns to place the best buildable civic
  building of a line in the free middle of the block whose homes pay the most for that line (one
  placement a day, paid). It never does this while power or water is short (the 13b "save for
  utilities" rule wins), and it replaces outdated buildings when cash allows. Retune toward the
  `AgeBalanceTests` targets (now 86 / 62 / 69 days from Medieval; Renaissance / Industrial /
  Modern starts 447 / 588 / 888 pop at day 120). The first knobs are `CivicFullPopulation`, the
  penalties and the Medieval building costs. Measure the tick on a full 96² city: it is 1.10 ms
  in EditMode and 1.56 ms in the player since M13's incremental networks, so the civic recompute
  must stay well under 0.3 ms; run `perf-benchmark` if the tick grows by more than that. UI-only
  play-through: New (Medieval, 48²) → grow past ~300 pop without services → the tooltip shows
  crime / fire / sickness → research Town Watch and Herbalism → place the Watch House, Bucket
  Brigade and Apothecary from the toolbar → the Services view shows cover, and the penalties
  fall → (Industrial start) a dense block held at level 2 by crime → Police Station → level 3 →
  an old Constabulary's panel shows "Outdated — replace with Police Station". Docs: GamePlan §13
  (a new *Civic services (M14)* section, plus notes in Services & power, Land value and Ages),
  the §8 / §12 status, the `AGENTS.md` Project bullet and the `CLAUDE.md` header.

#### Risks / open questions

- **Medieval pacing (86 of 90 days):** the ramp keeps most Medieval towns below 300 pop, so the
  penalties should barely touch it. But the new Medieval techs compete for early RP. If the age
  runs over, first fix `EngagedCity`'s tech priority, then lower Town Watch / Herbalism costs, and
  only then touch the penalties.
- **Penalty stacking:** pollution, water, crime, fire and sickness all hit the same homes. Watch
  for cities stuck below 0.5 happiness, where 5% a day move out. The happiness tooltip has to make
  the cause obvious, or the player can't recover.
- **Cash competition:** civic upkeep (up to ~$200/day for a full set of Industrial tiers) lands
  just as power and water towers are needed. The engaged player saves for utilities first. If the
  Industrial age slips past 90 days, lower Industrial civic upkeep before touching water or power
  prices.
- **Monastery / Academy change role:** they gain education reach, so research in an existing
  early-age city rises slightly. That's acceptable; record the Medieval-run delta in 14b.
- **Tick cost:** `CivicSystem` is O(cells) per recompute, and civic cover is sources × reach².
  M13 spent effort bringing the tick down to 1.10 ms, so keep the recompute lazy and
  array-based (no per-cell `Vector2Int` allocations or `UnityEngine.Object` checks), and measure
  it in 14d.
