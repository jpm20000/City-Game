# City Builder — Game Plan

> A human-readable design and development plan for an isometric 2.5D city-builder
> prototype built in Unity. This document is the shared reference for design
> intent, technical architecture, and the milestone roadmap.

---

## 1. Vision

Build a **playable vertical slice** of a city-builder: the player lays roads,
zones land, watches a statistical population grow, and manages money and
happiness over a ticking clock. The prototype prioritizes a clean, extensible
simulation core over art polish, so the mechanics can be tuned and layered on
later without rework.

**Design pillars**

| Pillar | Meaning |
|---|---|
| Readable at a glance | Isometric view, clear zoning colors, live demand meters. |
| Depth from simple rules | RCI demand + road access produce emergent growth. |
| Fast iteration | Data-driven buildings (ScriptableObjects) and a tunable balance file. |
| Keep it small | Placeholder art, PC only, abstract (not agent-based) simulation. |

---

## 2. Target & Scope

| Item | Decision |
|---|---|
| View | Isometric / 2.5D, orthographic camera |
| Grid | Square cells on a Unity isometric Tilemap |
| Simulation | Abstract / statistical (numbers per tick, no individual citizens) |
| Roads | Cell-based (placed like buildings; adjacency defines access) |
| UI | UGUI (Canvas / GameObjects) |
| Platform | PC (Windows / Mac), mouse + keyboard |
| Goal | Playable prototype / vertical slice |

**Core loop**

```
Place roads  ->  Zone land (R/C/I)  ->  Demand drives growth
      ^                                          |
      |                                          v
   Manage taxes & services  <--  Population, jobs & happiness change
```

---

## 3. Technical Foundation

| Property | Value |
|---|---|
| Unity | 6000.6.3f1 |
| Render pipeline | URP 17.6.0 |
| Color space | Linear |
| Input | New Input System **only** (`activeInputHandler: 1`) |
| Navigation | AI Navigation, Timeline, Visual Scripting, uGUI (installed) |
| Tests | Unity Test Framework (installed, none yet) |

> **Constraints:** no CLI build/lint is configured — the Unity Editor is the build
> system. Scripts compile into `Assembly-CSharp` / `Assembly-CSharp-Editor`
> (there are no asmdef files). Editor-only code must live under an `Editor/` folder.

---

## 4. Architecture

The game is split into four layers. Data drives logic; presentation only reacts.

```
+-------------------------------------------------------------+
|  PRESENTATION   HUD, Build Toolbar, Selection Panel,        |
|                 BuildingInstance sprites (Y-sorted), Ghost  |
+-------------------------------------------------------------+
|  LOGIC          Time, Economy, Population, Demand, Growth,  |
|                 RoadNetwork, Placement validation           |
+-------------------------------------------------------------+
|  DATA           GridData (zones, occupancy, roads),         |
|                 BuildingDefinition / Database, BalanceConfig|
+-------------------------------------------------------------+
|  WORLD          Unity Grid (Isometric Z as Y) + Tilemap,    |
|                 Iso Camera                                  |
+-------------------------------------------------------------+
```

**Key principles**

- `GridData` is the single source of truth for what occupies each cell; the
  Tilemap is purely visual.
- Systems communicate through a central `GameEvents` bus (UI never polls).
- Simulation runs on a fixed tick; the tick order is deterministic:
  **Demand → Growth → Population → Economy**.
- Buildings are GameObjects (not tiles) so each can hold logic and animation.

---

## 5. Project Structure

```
Assets/_Game/
  Scenes/            Main.unity
  Art/               Tiles, Sprites, Materials
  Prefabs/           Buildings/, UI/
  Scriptables/       Buildings/ (definitions), Balance/
  Scripts/
    Core/            GameManager, TimeManager, GameEvents, ServiceLocator
    Grid/            GridSystem, GridData, CellUtils
    Buildings/       BuildingDefinition, BuildingInstance, BuildingDatabase
    Placement/       PlacementController, PlacementValidator, GhostRenderer
    Simulation/      EconomySystem, PopulationSystem, DemandSystem,
                     GrowthSystem, RoadNetwork, SimulationManager
    Camera/          IsoCameraController
    UI/              HUDController, BuildToolbar, SelectionPanel
    Input/           InputReader
    Save/            SaveSystem, SaveData
Docs/                GamePlan.md (this file)
```

> Existing URP settings remain in `Assets/Settings/`. The default scene is
> `Assets/Scenes/SampleScene.unity`.

---

## 6. Systems & API Contracts

### Core

```csharp
class GameManager : MonoBehaviour {          // bootstrap; wires systems
    GridData Grid; TimeManager Time; EconomySystem Economy;
    PopulationSystem Population; DemandSystem Demand; RoadNetwork Roads;
}

class TimeManager : MonoBehaviour {
    float SecondsPerDay = 1f;  int DaysPerMonth = 30;  int MonthsPerYear = 12;
    int Day, Month, Year;
    GameSpeed Speed;                          // Paused, x1, x2, x4
    event Action OnTick;                      // once per in-game day
    void SetSpeed(GameSpeed s); void TogglePause();
}

static class GameEvents {                     // central event bus
    event Action<float> MoneyChanged;
    event Action<int,int> PopulationChanged;  // population, jobs
    event Action<int,int,int> DateChanged;    // day, month, year
    event Action<DemandSnapshot> DemandChanged;
    event Action<BuildingInstance> BuildingPlaced, BuildingRemoved;
    event Action<Vector2Int> CellChanged;
}
```

### Grid

```csharp
class GridSystem : MonoBehaviour {            // wraps Unity Grid + Tilemap
    Vector3 CellToWorld(Vector2Int c);
    Vector2Int WorldToCell(Vector3 w);
    Vector2 CellSize;
}

class GridData {                              // pure C# -> unit testable
    int Width, Height;
    bool InBounds(Vector2Int c);
    ZoneType GetZone(Vector2Int c);  void SetZone(Vector2Int c, ZoneType z);
    BuildingInstance GetBuilding(Vector2Int c);
    bool IsRoad(Vector2Int c);
    bool CanPlace(BuildingDefinition def, Vector2Int origin, int rotation,
                  out string reason);
    bool Occupy(BuildingInstance b);  void Release(BuildingInstance b);
    IEnumerable<Vector2Int> GetFootprint(Vector2Int origin, Vector2Int size,
                                         int rotation);
    event Action<Vector2Int> OnCellChanged;
}
```

### Buildings

```csharp
enum ZoneType { None, Residential, Commercial, Industrial }
enum BuildingCategory { Zone, Road, Service, Utility, Decoration }

class BuildingDefinition : ScriptableObject {
    string Id, DisplayName;  BuildingCategory Category;
    Sprite Icon;  GameObject Prefab;
    Vector2Int Size;  int Cost;  float UpkeepPerDay;
    int HousingCapacity, JobsProvided;  float HappinessEffect;
    ZoneType ZoneRestriction;  int UnlockPopulation;
}

class BuildingInstance : MonoBehaviour {
    BuildingDefinition Definition;  Vector2Int Origin;  int Rotation;
    int Level;                                // grown buildings upgrade
}
```

### Placement

```csharp
class PlacementController : MonoBehaviour {
    BuildingDefinition Selected;
    void Select(BuildingDefinition def); void Cancel(); void RotateCW();
    bool TryPlace(out string reason);
    bool TryDemolish(Vector2Int cell);
}

class GhostRenderer : MonoBehaviour {         // translucent preview, green/red tint
    void Show(BuildingDefinition def, Vector2Int o, int rot, bool valid);
    void Hide();
}
```

### Simulation

```csharp
class SimulationManager : MonoBehaviour {     // fixed tick order
    void Tick();   // Demand -> Growth -> Population -> Economy
}

class EconomySystem {
    float Money, IncomePerDay, ExpensePerDay;
    float TaxRes, TaxCom, TaxInd;             // 0..1
    bool CanAfford(float a); void Spend(float a); void Refund(float a);
    event Action<float> OnMoneyChanged;
}

class PopulationSystem {
    int Population, Employed, Unemployed, Homeless, Jobs, Housing;
    float AverageHappiness;                   // 0..1
}

class DemandSystem {
    float ResidentialDemand, CommercialDemand, IndustrialDemand;  // 0..1
}

class GrowthSystem {
    int MaxGrowthPerDay = 3;
    void RegisterZonedCell(Vector2Int c, ZoneType z);
}

class RoadNetwork {                           // reads roads from GridData
    RoadNetwork(GridData grid);
    bool HasRoadAccess(Vector2Int c);         // adjacent road, connected to entry
    bool IsConnectedToEntry(Vector2Int c);    // road cell on the entry-connected net
}
```

### Camera / Input / UI / Save

```csharp
class IsoCameraController : MonoBehaviour {
    float PanSpeed, ZoomSpeed, MinZoom, MaxZoom;  Bounds Bounds;
}

class InputReader {                           // backed by CityBuilder.inputactions
    Vector2 Pan;  float Zoom;  Vector2 Pointer;
    bool Confirm, Cancel, Rotate, Demolish;
    int SpeedDelta;
}

class HUDController : MonoBehaviour { }       // binds GameEvents -> labels/meters
class BuildToolbar : MonoBehaviour { void OnButton(BuildingDefinition d); }
class SelectionPanel : MonoBehaviour { void Show(BuildingInstance b); }

[Serializable] class SaveData {
    float Money, TaxRes, TaxCom, TaxInd;  int Day, Month, Year;
    List<BuildingRecord> Buildings;  byte[] Zones;   // { id, x, y, rot }
}
static class SaveSystem { void Save(string path, SaveData d); SaveData Load(string path); }
```

---

## 7. Simulation & Balance

All values live in a **`BalanceConfig` ScriptableObject** so they can be tuned
without recompiling.

### Time

| Setting | Value |
|---|---|
| 1 in-game day | 1.0 s at 1× |
| Month / Year | 30 days / 12 months (360-day year) |
| Tick | once per in-game day |
| Speeds | Paused, 1×, 2×, 4× |

### Starting values

| Money | Population | Taxes (R/C/I) | Happiness |
|---|---|---|---|
| $50,000 | 0 | 10% / 10% / 10% | 0.75 |

### Costs & upkeep

| Item | Build cost | Upkeep/day |
|---|---|---|
| Road tile | $50 | $1 |
| Zone designation | $0 | $0 |
| Residential (grown) | $0 | $0 |
| Park (service) | $500 | $5 |
| Power plant (utility) | $10,000 | $100 |

### Derived stats (per tick)

```
Workers      = floor(Population * 0.6)
Housing      = sum of residential housing capacity
Jobs         = sum of (commercial + industrial) jobs
VacantHomes  = max(0, Housing - Population)
UnfilledJobs = max(0, Jobs - Workers)
```

### Demand (0..1, shown as meters)

```
ResidentialDemand = clamp01( UnfilledJobs/max(Jobs,10)
                             - VacantHomes/max(Housing,1)
                             + (Population==0 ? 0.30 : 0) )

CommercialDemand  = clamp01( (Population*0.30 - CommercialJobs) / max(CommercialJobs,20) )
IndustrialDemand  = clamp01( (Population*0.40 - IndustrialJobs)  / max(IndustrialJobs,20) )
```

### Growth (per tick)

```
for each zone type with Demand > 0.15:
    budget = round(Demand * MaxGrowthPerDay)   // MaxGrowthPerDay = 3
    grow / upgrade up to `budget` eligible cells
        // eligible = zoned + road access + affordable
```

Building levels add capacity: **L1 = 4, L2 = 8, L3 = 16** residents (or jobs).

### Population (per tick)

```
Population = min(Population + ceil(VacantHomes * 0.10 * ResidentialDemand), Housing)
Employed   = min(Population, Jobs);  Unemployed = Population - Employed
```

### Happiness (0..1)

```
Happiness = clamp01( 0.80
                     - 0.60 * Unemployed/max(Workers,1)
                     - 0.50 * max(0, TaxRes - 0.10)
                     + ServiceBonus            // +0.05 each, cap +0.20
                     - 0.30 * Homeless/max(Population,1) )

Happiness < 0.5  ->  ResidentialDemand *= 0.5   // slow growth
```

### Income / expenses (per tick)

```
Income  = Employed * 10 * TaxRes              // $/worker/day
        + CommercialJobs * 12 * TaxCom
        + IndustrialJobs * 12 * TaxInd
Expense = sum(UpkeepPerDay for placed buildings) + Roads * 1
Money  += Income - Expense                    // negative => bankruptcy warning (soft)
```

> **M6 implementation note:** as written, these formulas deadlock at 1–4 population
> (residential demand hits 0 once any home is vacant, move-in is scaled by that demand,
> and C/I demand can't pass 0.15 below ~10 pop). The shipped `BalanceConfig` uses:
> residential base demand **0.40 always**; move-in **`ceil(VacantHomes * 0.20)`** (not
> demand-scaled); C/I demand denominator floor **5**; and **`Employed = min(Workers, Jobs)`,
> `Unemployed = Workers - Employed`**. Result: ~212 pop, +$234/day at day 60 on the debug
> seed city.

> **M8 balance note:** happiness base **0.70**; every zone's demand is scaled by
> `1 - 4*(tax - 0.10)`; C and I taxes cost happiness like residential
> (`0.5 * (tax - 0.10)` each); pollution `-0.5 * IndustrialJobs / (Housing + Jobs)`;
> unemployment and pollution ramp in over the first 40 residents; below 0.5 happiness,
> 5% of residents leave per day. High taxes are now a growth-vs-income trade-off, and
> parks are what keep a taxed or industrial city above 0.5.

> **M9 balance note:** a park adds **+0.10** happiness to each home within its radius
> (4 cells), capped at **+0.20** per home; the Services term is the housing-weighted average.
> Unpowered homes cost up to **−0.05** (scaled by the unpowered share, ramped like
> unemployment), and grown buildings can't pass level 1 without power. Seeded city: no plant
> → 172 pop, all level 1, happiness 0.54; one plant → 212 pop at day 60 and 332 at day 90,
> with its 600 units full by ~day 120; two well-placed parks → 0.68. With 20% C/I taxes the
> city stalls at ~60 without parks and grows normally with them.

> These are **starting numbers**, not final. The 60-day playtest should settle at
> roughly 200–400 population with positive cash flow at 10% tax.

---

## 8. Roadmap

Each milestone is independently verifiable before moving on.

| # | Milestone | Deliverable | Verify |
|---|---|---|---|
| 0 | Setup | Folders, sort axis, input map, iso Grid + Tilemap, layers | Scene renders an iso ground plane |
| 1 | Camera | `IsoCameraController` | Smooth pan/zoom over a test map |
| 2 | Grid data | `GridSystem`, `GridData`, `CellUtils` | World↔cell conversion and occupancy correct |
| 3 | Placement | Ghost + validator + place/demolish | Place/rotate/remove roads; blocking works |
| 4 | Buildings | `BuildingDefinition` + database + prefabs | Place a house and a road manually |
| 5 | Roads | `RoadNetwork` access + connectivity | Only road-adjacent cells report access |
| 6 | Sim core | Time / Economy / Population / Demand / Growth | Zoned cells auto-grow; money & pop change |
| 7 | UI | HUD + toolbar + selection panel | Full loop playable from UI alone |
| 8 | Polish | Placeholder art, feedback, balance, save/load | Vertical slice complete |
| 9 | Services & utilities | Power plant + road-carried power grid, park coverage radius, info overlays | Upgrades need power; park placement matters; see §11 |
| 10 | Scale | Map size per city (default 64²), New City dialog, perf pass | 64² seeded city runs at speed 4× without frame drops; v1 saves load; see §12 |
| 11 | Ages & technology | Save v2 with migration, ages 750 → today, research points, tech tree, age advancement, starting age, per-age growth rules, "Keep historical building" | Start in any age; research → advance; old buildings redevelop unless kept; see §12 |
| 12 | Land value & local pollution | Per-cell pollution and land value; value gates upgrades; heritage bonus | Industry next to homes hurts; parks/heritage raise value |
| 13 | Water | Wells → water towers → pipes under roads, per-age requirement | Industrial+ upgrades need water |
| 14 | Civic services | Order, fire, health, education per age; education produces research | Coverage drives crime/fire risk/health; schools speed research |
| 15 | Budget depth | Service funding sliders, loans, ordinances | Underfunding shrinks coverage; loans repayable |
| 16 | Traffic | Abstract road load, per-age road tiers, Traffic view | Congestion lowers access/happiness; better roads fix it |
| 17 | Disasters & events | Fire spread, plague, breakdowns, random events | Coverage prevents/limits disasters |
| 18 | Art & atmosphere | Hand-made per-age assets through the M11 visual sets, day/night, audio | Every age has its own skyline |
| 19 | Release | Main menu, settings, save slots, player build | A standalone build plays start to finish |

**Status (2026-10-03):** M0–M10 implemented — the vertical slice is complete, M9 (§11) added power, park coverage and info views, and M10 (§12) made the map size per city (default 64²) with a New City dialog. M7 shipped as UGUI + TextMeshPro
prefabs (`Prefabs/UI/`): HUD, build toolbar (`ToolbarController`, building buttons
generated from the database), selection panel, taxes panel, notifications (toast +
debt banner) and an F1 debug panel. The zone-painting tool landed early (after M6),
and `HUDController`/`SelectionPanel` names match §6 but the toolbar is
`ToolbarController` and the selection panel is driven by `PlacementController`
selection rather than `Show(BuildingInstance)`. M8 balance items (both
addressed in 8b): happiness sat at ~0.8 with no pressure, and commercial/industrial
taxes had no downside.

**M8 breakdown:** 8a save/load → 8b balance → 8c feedback & readability → 8d
placeholder art pass → 8e full UI-only play-through + docs. **8a done (2026-10-02):**
single-slot JSON save (F5/F9 + HUD Save/Load/New); `SaveSystem` is static and
`SaveData` matches §6 plus speed, population, happiness and last-day cash flow
(see `AGENTS.md` → Save / load). **8b done:** C/I taxes and pollution now cost happiness and
growth, unhappy cities shrink (see the §7 M8 balance note). **8c done:** happiness
breakdown tooltip on the HUD, striped zones without road access, cursor cost/blocker
hints, floating "-$" text, level-up pop, milestone and residents-leaving toasts. **8d done:**
lit placeholder blocks with per-zone/per-level silhouettes, roofs and chimneys; auto-tiled
roads (curbs, dashed lines, junctions, off-map entries, red when disconnected); park trees. **8e done:**
UI-only play-through (roads → zones → park → 60 days → taxes → save/new/load → demolish
→ select) driven with virtual input in Play mode; 61 EditMode tests green.

**Placeholder art:** flat colored isometric diamonds for ground and simple
colored blocks for buildings — no external art dependency for the prototype.

---

## 9. Testing

- **Edit-mode unit tests** (Unity Test Framework) for pure logic:
  `GridData`, `RoadNetwork`, `DemandSystem`.
- **Manual playtest checklist** per milestone; verify the Unity Console is
  error-free after each phase.
- **Balance validation:** a scripted 60-day playtest checked against the target
  described in §7.

---

## 10. Later / Not Now

Deliberately out of scope for the prototype, but designed for:

- Individual citizen agents (NavMesh) layered on top of the statistical model
  (cosmetic cars/pedestrians are a possible M18 extra).
- Freeform road drawing and zoning along curves (breaks the cell grid).
- Achievements, scenarios, map editor.
- Water, progression, disasters and day/night are now planned in §12 (M13, M11, M17, M18).

---

## 11. Milestone 9 — Services & Utilities (plan, 2026-10-02)

**Goal:** turn placed buildings from global stat sticks into *spatial* decisions. Today a park
anywhere gives +0.05 to the whole city and the §7 power plant does not exist. After M9, where
you put things matters: power flows along roads, and parks only help the homes near them.

**Done when:** a fresh city grows to level 1 without power but stalls there with "Needs power"
blockers; building a road-connected Power Plant lets it upgrade; a park only lifts happiness for
residents inside its radius; the player can see both through overlays; save/load restores the
same state; all EditMode tests pass.

### Design

**Power (utility)**
- `Power Plant`: Utility, 3×3, $10,000, $100/day (the §7 numbers), supply **600** units (tunable).
- Power travels **along roads**. The plant has to touch a road. Every road cell 4-connected to
  that road is energised (it doesn't have to connect to the map edge). A grown cell beside an
  energised road can be powered.
- Each grown cell consumes its capacity (L1 = 4, L2 = 8, L3 = 16). Supply is handed out in
  **BFS order from the plants**: cells closer to a plant along the roads get power first, and
  ties are broken row-major, so the result is deterministic. When demand exceeds supply, the
  cells furthest out lose power first.
- Effect: **an unpowered cell can grow to L1 but cannot upgrade** (new `GrowthBlocker.NoPower`).
  This keeps the opening exactly as it is now ($50k start, nothing to build first). It also
  makes the $10k plant the first big purchase.
- Happiness: new **Power** term = `-PowerPenalty × unpoweredHousing / Housing`. It ramps in
  with `SmallTownGracePopulation`, the same way unemployment does.
- Not saved: power is recomputed from the grid and the placed buildings, so **`SaveData`
  stays at version 1**.

**Coverage (services)**
- `BuildingDefinition` gains `CoverageRadius` (in cells, Chebyshev distance from the
  footprint) and `PowerSupply`. Park: radius **4**.
- Per residential cell: `bonus = min(coveringServices × ServiceBonusEach, ServiceBonusCap)`.
  The **Services** happiness term becomes the housing-weighted average of that bonus. So 4
  parks still reach +0.20, but only if every home sits inside their combined coverage.
- Global pollution stays as it is for M9. Local pollution is a candidate for M10.

### Architecture (Simulation asmdef stays pure)

| New / changed | Notes |
|---|---|
| `ServiceSource` struct | `{ Origin, EffectiveSize, CoverageRadius, PowerSupply }`. The runtime builds it from `BuildingInstance` (the asmdef can't see `BuildingDefinition`). |
| `SimulationSystem.Sources` | `IReadOnlyList<ServiceSource>`, set by `GameManager` on Register/Unregister. It replaces `CityModifiers.ServiceCount`. |
| `CoverageSystem` | Per-cell `byte` count of the services covering each cell. Recomputes only when the sources change (dirty flag). `GetCoverage(cell)`. |
| `PowerSystem` | `Recompute(grid, sources, config)`: multi-source BFS over roads plus allocation. Exposes `IsPowered(cell)`, `IsEnergisedRoad(cell)`, `Supply`, `Demand`. |
| `SimulationSystem.Tick` | Order unchanged. `PowerSystem` recomputes lazily on any grid/source change (like `RoadNetwork`); growth reserves upgrade draw during its scan. |
| `GrowthSystem` | Eligibility for level ≥ 1 requires `Power.IsPowered`. `GetBlocker` → `NoPower` (checked after `NoRoadAccess`, before `LowDemand`). |
| `HappinessBreakdown` | Adds a `Power` term. `Services` becomes coverage-weighted. `PopulationSystem.Step` takes the coverage and power views instead of `serviceCount`. |
| `BalanceConfig` | `PowerPenalty`, `PowerPerLevel` (or reuse capacity), park/plant numbers live on their defs. Tag the new fields "(M9)" and mirror them in the `.asset`. |

### Breakdown

- **9a Sim core — done (2026-10-02).** 77 EditMode tests green; seeded city day 60: 114 pop without a plant (all L1), 212 with one (352/600 units drawn).
  `ServiceSource`, `CoverageSystem`, `PowerSystem`, the new tick order, growth
  gate, `NoPower` blocker, Power and Services happiness terms. Tests: BFS through roads only; a
  plant that doesn't touch a road powers nothing; a disconnected road island stays dark;
  brownout drops the furthest cells first; deterministic tie-break; coverage radius around a
  rotated footprint; per-cell cap; growth stops at L1 without power; a save/load round trip
  gives the same powered set.
- **9b Content — done (2026-10-02).** Play-mode check: seeded city (with its free plant) reaches 212 pop / 13 L2 cells / 352 of 600 units at day 60, $147/day costs incl. the plant.
   `PowerPlant` def + prefab (grey 3×3 block with two chimney stacks, Simple Lit,
  `BoxCollider`, layer 9). Park gets `CoverageRadius = 4`. The plant appears on the toolbar
  automatically (it's a Utility). `DebugSeedCity` also places a free plant so the debug flow
  still reaches L3.
- **9c Overlays & feedback — done (2026-10-02).** Verified in Play mode with virtual input (V, plant/park tool hover previews, pointer-over-UI, tool clear); zone tints are hidden while a view is shown and grown buildings are recoloured. Toolbar buttons are `Power` / `Parks` under `VIEW`.
  - New `InfoOverlay` (`GridTilemapView`) with modes **Off / Power / Coverage**, a new input
    action `CycleOverlay` (key `V`) and toolbar "View" buttons.
  - Power mode: powered, unpowered and energised-road tints.
  - Coverage mode: shade by the per-cell bonus.
  - With the Park or Plant tool selected, switch to the matching overlay automatically and
    preview the new building's radius / energised roads under the ghost before you place it.
- **9d UI wiring — done (2026-10-02).** Verified in Play mode: HUD readout states, online / shortage / no-power nudge toasts (nudge at 40 pop), selection text for plant, powered / unpowered buildings and roads, no repeat toasts after save + load. Balance note for 9e: a city with no power sits exactly at 0.50 happiness (base 0.70 − pollution 0.10 − power 0.10) and keeps crossing the threshold.
  - HUD power readout `used / supply` (red when short), from a new `GameEvents.PowerChanged`.
  - `SelectionPanel`: Powered / Unpowered status, coverage count, and plant supply and load.
  - `HappinessTooltip`: a "Power" line.
  - Toasts: "Power shortage — N buildings dark" once per shortage, and "First power plant
    online".
- **9e Balance + play-through + docs — done (2026-10-02).** `ServiceBonusEach` 0.05 → 0.10 (radius parks
  were worth only +0.04 for two) and `PowerPenalty` 0.10 → 0.05 (a powerless town sat at exactly 0.50
  and kept tripping the move-out threshold); see the §7 M9 balance note. The no-plant plateau is
  172 (every zone at L1) rather than ~100: the pressure is being stuck at L1, not an exodus. 3 new
  seeded-city balance tests (80 EditMode tests green). UI-only virtual-input play-through: New → road +
  dragged zones → 45 days stalled at L1 (nudge toast, "needs power" blocker) → toolbar plant (online
  toast) → L2/L3 within 30 days → park (+0.09) → V views → Save / New / Load restores the same city.
  - Re-tune so the 60-day seeded run (with plant and 2 well-placed parks) lands in §7's
    200–400 pop with positive cash flow, and with no plant it plateaus around 100.
  - The plant's $100/day must be payable around 100–150 pop.
  - Update the balance assertions in `SimulationTests`.
  - Do a UI-only virtual-input play-through: roads → zones → stall at L1 → plant → upgrade →
    park coverage → save/load.
  - Update the `AGENTS.md` Systems section, the §7 numbers and the §8 status.

### Risks / open questions
- **Balance shift.** Gating L2/L3 roughly halves an unpowered city, so the existing day-60
  assertions (212 pop) will break on purpose in 9a. Update them in 9e, not piecemeal.
- **Per-tick cost.** BFS + allocation over 576 cells per tick is trivial. If the map grows
  later, recompute only when roads, levels or sources change.
- **Rotation.** Coverage must use `CellUtils.EffectiveSize`. Power adjacency uses the
  footprint's perimeter.
- **Scope guard.** Water, power lines (power off roads), local pollution and new service
  types (police, clinic) are deferred to M10+.

---

## 12. Milestones 10–19 — Ages roadmap (plan, 2026-10-02)

**Direction:** the city spans history, from an early-medieval settlement (~750) to today. The
player researches technologies, grows the population and advances through **ages**. Each age
changes what can be built, how dense the city gets and what upgrades need. A new city can start
in **any age**. Grown buildings redevelop into the current age's style, unless the player marks
them **Keep historical building**. Art is out of scope until M18 (the player authors the assets).
Until then, M11 sets up the slots the assets will plug into, and the placeholder blocks stay.

**Why ages come early (M11):** ages touch every building definition, the growth rules (the M9
power gate only makes sense from the Electric age), the visuals and the save format. Building the
foundation right after the map change means every later milestone just adds content per age,
instead of retrofitting eight milestones of buildings and balance numbers.

**Ground rules for M10–M19**
- The sim stays pure (`CityBuilder.Simulation`). Age and tech data are ScriptableObjects
  **inside the Simulation asmdef** (like `BalanceConfig`), and they refer to buildings by `Id`
  string, because `BuildingDefinition` lives in `Assembly-CSharp`.
- **"No age data" = today's rules.** A sim built without ages behaves like the Electric age with
  current balance, so the 80 existing tests keep passing unchanged. Age-specific tests are new.
- Save format: v2 in M11 adds a **migration chain** (`v1 → v2 → …`) instead of rejecting old
  files. Every later bump adds one migration step and a test.
- Each milestone ends with: EditMode tests green, a UI-only virtual-input play-through, and docs
  (`AGENTS.md` Systems + this file's status lines).

### The ages (tunable; display names and years are placeholders)

| # | Age | Starts | Max level | Capacity scale | Upgrades need | Typical unlocks |
|---|---|---|---|---|---|---|
| 1 | Early Medieval | 750 | 2 | ×0.5 | — | Huts, market, workshops, village green |
| 2 | High Medieval | 1100 | 2 | ×0.75 | — (well coverage from M13) | Stone houses, guild halls, monastery |
| 3 | Renaissance | 1450 | 3 | ×0.75 | — (well coverage from M13) | Townhouses, printing press, academy |
| 4 | Industrial | 1760 | 3 | ×1 | Water (from M13) | Factories, rail-era roads, water tower |
| 5 | Electric | 1880 | 3 | ×1 | Power + water | Power plant (the M9 rules), trams |
| 6 | Modern | 1945 | 3 | ×1.25 | Power + water | Apartments, offices, highways |
| 7 | Contemporary | 1990 | 3 | ×1.5 | Power + water | Towers, clean energy, tech parks |

Capacity per cell = `CapacityForLevel(level) × CapacityScale(builtAge)`. Zones stay R/C/I in code;
ages can give them display names (e.g. Industrial = "Crafts" in the medieval ages).

**Calendar:** the date starts at the starting age's year. Advancing to an age moves the year to
`max(current year, age start year)`, so history is compressed but the date always reads right
for the age.

### Research & advancement
- **Research points (RP)** accrue per tick from a base rate per employed commercial worker plus
  research buildings (`BuildingDefinition.ResearchPerDay`; monastery/scriptorium in M11; schools
  and universities in M14).
- **Technologies** (`TechDefinition`: Id, Age, RP cost, prerequisites, effects). There is one active
  research at a time, plus a queue; leftover RP carries over. Effects are a small typed list, read
  by the pure sim: `UnlockBuilding(id)`, `UnlockRoadTier`, and multiplier modifiers (demand,
  capacity, research, upkeep, happiness).
- **Advancing an age** is itself a special tech. It needs **(a)** its RP cost, **(b)** at least N
  techs of the current age, and **(c)** population ≥ the age's threshold. The tech panel shows all
  three as a checklist.
- **Starting age:** the New City dialog picks an age. Every tech of the earlier ages counts as
  researched, and the starting money scales with the age.

### Keep historical building
- After an advance, grown cells built in an older age are **outdated**. Growth can **redevelop**
  them (same level, current age's style and capacity). Redevelopment uses the growth budget after
  new cells and upgrades.
- **Keep historical** (a selection-panel toggle, per grown cell) freezes the cell's age, so its
  style and capacity stay as built and it is never redeveloped. It still upgrades within its own
  age's max level.
- The cost of keeping is lower density than a redeveloped block. The payoff is a **heritage**
  land-value and happiness bonus, which arrives in M12. M11 only stores and shows the flag.
- Placed buildings (services, utilities) never change by themselves. Obsolete ones show an
  "outdated, replace with X" hint (from M14).

---

### M10 — Scale

**Goal:** a map big enough for a city that lives through seven ages, with the size chosen per
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

### M11 — Ages & technology foundation

**Goal:** the full age/tech framework with enough content to play from age 1 to age 7 on
placeholder art.
**Done when:** a city started in Early Medieval researches and advances through every age;
outdated blocks redevelop unless kept; a city started in Electric plays exactly like today; save
v2 round-trips age, techs, research progress and per-cell age/historic flags.

- **11a Sim core.** In the Simulation asmdef:
  - `AgeDefinition` (index, name, start year, max level, capacity scale, upgrade requirements,
    population threshold, techs needed to advance, zone display names) and `AgeDatabase`.
  - `TechDefinition` + `TechDatabase` (validated: no cycles, prerequisites in the same or earlier
    age).
  - `TechSystem`: RP income, active research and queue, researched set, `CanAdvance` checklist,
    and effect aggregation into a `TechModifiers` struct.
  - `GridData` gains per-cell `BuiltAge` (byte) and `Historic` (flag), with export/import.
  - `GrowthSystem` reads the current age: level cap, power gate only where the age requires it,
    and redevelopment of outdated, non-historic cells (`GrowthBlocker.KeptHistoric` /
    `AgeMaxLevel`).
  - `SimulationSystem.Tick` adds the research step after Economy.

  Tests: tech graph validation, RP accrual and carry-over, advancement checklist, level cap per
  age, redevelopment order is deterministic, historic cells are never redeveloped, no-age config
  matches the current seeded-city numbers.
- **11b Content.** 7 `AgeDefinition` assets plus about 4–6 techs per age, using existing
  buildings where possible:
  - Power plant → Electric.
  - Park → available from age 1 (as "village green" via display name).
  - A new research building per early age: monastery (age 2), academy (age 3).
  - `BuildingDefinition` gains `RequiredTech` (Id string) and `ResearchPerDay`;
    `UnlockPopulation` is removed (superseded).
  - Toolbar buttons of locked buildings are hidden or locked with a "Requires *tech*" tooltip.
- **11c Visual slots.** `AgeVisualSet` asset per age: per zone, per level, an optional prefab;
  when empty, it falls back to today's procedural blocks, tinted/shaped per age so ages are
  distinguishable. `GrowthVisuals` picks the set by the cell's `BuiltAge`. This is the contract
  your hand-made assets plug into in M18 (pivot at the footprint's ground centre, 1 unit = 1
  cell, layer 9, needs a collider for selection).
- **11d UI.**
  - Tech panel: tree by age, active research and progress, queue, and the advancement checklist.
  - HUD: age name + RP/day.
  - New City dialog: starting age.
  - Selection panel: "Built in *age*", outdated hint, **Keep historical building** toggle.
  - Toasts: research complete, new age reached, first redevelopment.
  - Info view **Age**: tints cells by built age; historic cells are outlined.
- **11e Save v2 + migration, balance, play-through, docs.** v2 adds age, researched techs, active
  research + progress, queue, and per-cell `BuiltAge`/`Historic`. `SaveMigrations` holds one step
  per version; v1 → v2 = Electric age with all earlier techs and every cell built in Electric.
  Older files are migrated, and only newer ones are rejected. Balance target: about 45–90 in-game days per
  age at 1× for an engaged player (to be confirmed in play). Play-through: start medieval, reach
  age 3, keep a block historic, save/load.

**Risks / open questions**
- **Scope of content.** 7 ages × several techs is a lot of data. M11 ships thin tech trees, and
  later milestones fill them (each one adds its age-specific buildings).
- **Economy across ages.** One currency ($) and one balance table for now. Per-age income and
  upkeep multipliers go on `AgeDefinition` if the early ages feel too rich or poor.
- **Redevelopment churn.** Redeveloping a cell briefly drops its capacity. M11 keeps capacity
  during the rebuild (instant swap); construction time is a candidate for later.

### M12–M19 outline (detailed plans written when each milestone starts)

**M12 — Land value & local pollution.** Pollution becomes per-cell (industrial cells emit in a
radius scaled by age: workshops low, factories high, clean energy low), replacing the city-wide
term. Land value per cell comes from parks, services, water/coast later, pollution and the
**heritage bonus** (historic cells raise value around them). Level 3 needs a land-value
threshold. Adds Pollution and Land value info views. Reuses the `CoverageSystem` pattern.

**M13 — Water.** Wells and fountains (coverage radius, ages 1–3), water towers and pumps feeding
pipes under roads (ages 4+, a copy of the `PowerSystem` network/allocation model). Fills in the
"Upgrades need water" column above. Water info view, HUD readout, toasts.

**M14 — Civic services.** Four service lines, each with per-age buildings:
- order: watchman → police
- fire: bucket brigade → fire station
- health: apothecary → hospital
- education: monastery school → university, which produces RP

Per-cell crime and fire risk, health as a happiness term. Obsolete-service "replace with" hints.
Gives the tech trees most of their content.

**M15 — Budget depth.** Per-service funding sliders (funding scales radius/effect), loans with
interest and repayment, a few ordinances per age (unlocked by tech). Expands the TaxPanel into a
budget panel.

**M16 — Traffic.** Abstract load per road cell from the homes↔jobs flow (statistical, no
agents). Congestion reduces road access quality and happiness. Road tiers by age (dirt →
cobble → paved → avenue → highway), unlocked by tech, with capacity and cost. Traffic view.

**M17 — Disasters & events.** Fire spreads between cells without fire coverage (a big threat
in the timber ages), plague in the medieval ages without health coverage, plant breakdowns.
Random events with choices are delivered as toasts or popups. Can be toggled in New City.

**M18 — Art & atmosphere.** Your hand-made per-age assets go into the `AgeVisualSet` slots,
along with per-age road tiles, day/night lighting, and per-age music and ambience. Optional extra:
cosmetic carts/cars on busy roads (visual only).

**M19 — Release.** Main menu, settings (audio, keybinds, UI scale), multiple save slots with
thumbnails, a tutorial for the first age, and a Windows player build.

**Status (2026-10-03):** M10 done (variable map size, render fix, New City dialog). M11 is next — expand its
outline above into a full plan before implementing.
