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

> **Ages (M11g):** research = 0.25 RP per filled commercial job (+ research buildings: Monastery
> 3, Academy 5 RP/day) × tech multipliers; ages are entered at 120 / 350 / 650 residents for
> 250 / 1,200 / 3,000 RP after 5 / 4 / 5 techs of the current age. With engaged play (the
> `EngagedCity` harness) a Medieval start spends 86 / 62 / 63 days in Medieval / Renaissance /
> Industrial and reaches Modern on day 211. An Industrial start keeps every earlier tech's bonus
> (accepted): 220 pop / 0.65 happiness at day 60 vs 212 / 0.60 without ages.

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

**Status (2026-10-03):** M0–M11 implemented — the vertical slice is complete, M9 (§11) added power, park coverage and info views, M10 (§12) made the map size per city (default 64²) with a New City dialog, and M11 (§12) added four ages, research and a 30-tech tree, per-age growth rules and looks, redevelopment with Keep historical, save v2 with migration, and a starting-age picker. M7 shipped as UGUI + TextMeshPro
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
power gate only makes sense from the Industrial age), the visuals and the save format. Building the
foundation right after the map change means every later milestone just adds content per age,
instead of retrofitting eight milestones of buildings and balance numbers.

**Ground rules for M10–M19**
- The sim stays pure (`CityBuilder.Simulation`). Age and tech data are ScriptableObjects
  **inside the Simulation asmdef** (like `BalanceConfig`), and they refer to buildings by `Id`
  string, because `BuildingDefinition` lives in `Assembly-CSharp`.
- **"No age data" = today's rules.** A sim built without ages behaves like the Industrial age (with Electricity researched) with
  current balance, so the 80 existing tests keep passing unchanged. Age-specific tests are new.
- Save format: v2 in M11 adds a **migration chain** (`v1 → v2 → …`) instead of rejecting old
  files. Every later bump adds one migration step and a test.
- Each milestone ends with: EditMode tests green, a UI-only virtual-input play-through, and docs
  (`AGENTS.md` Systems + this file's status lines).

### The ages (tunable; display names and years are placeholders)

| # | Age | Starts | Max level | Capacity scale | Upgrades need | Typical unlocks |
|---|---|---|---|---|---|---|
| 1 | Medieval | 750 | 2 | ×0.5 | — (well coverage from M13) | Huts → stone houses, markets, workshops, village green, monastery |
| 2 | Renaissance | 1450 | 3 | ×0.75 | — (well coverage from M13) | Townhouses, printing press, academy |
| 3 | Industrial | 1760 | 3 | ×1 | Power + water (water from M13) | Power plant (the M9 rules), factories, railways, trams |
| 4 | Modern | 1945 | 3 | ×1.25 | Power + water | Apartments, offices, towers, highways, clean energy |

Capacity per cell = `CapacityForLevel(level) × CapacityScale(builtAge)`. Zones stay R/C/I in code;
ages can give them display names (e.g. Industrial = "Crafts" in the Medieval age).

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
  researched, plus the age's starting techs (Industrial starts with Electricity), and the
  starting money scales with the age.

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

### M12 — Land value & local pollution (plan, 2026-10-03)

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

- **12a Local pollution (pure + content).** `PollutionSystem`, the new `AgeDefinition`, `ServiceSource`,
  `BuildingDefinition` and `BalanceConfig` fields, the per-home Pollution term, `PollutionMultiplier`.
  Generator + Power Plant asset. Tests: falloff and radius, emission by level and built age, plant
  source, resize, laziness (recomputes after a level change), tech multiplier; per-home term (a home
  far from industry pays nothing); the rewritten formula tests. Re-run the seeded-city and age
  balance tests and record what moved.
- **12b Land value, the level-3 gate and heritage (pure + content).** `LandValueSystem`, the gate and
  `LowLandValue`, the Heritage happiness term, `LandValueBonus` + Architecture. Tests: contributors
  add up and clamp; polluted homes stop at level 2 and a park lets them through; industry ungated;
  historic cells raise neighbours' value and happiness; the blocker; Medieval unaffected by the gate.
- **12c UI.** Pollution and Land value info views (ground shading + building tints, through
  `GrowthVisuals.SetColorOverride`), two VIEW buttons, SelectionPanel lines, tooltip lines, the
  land-value toast. Play-mode screenshots of both views on a seeded city.
- **12d Balance, play-through, docs.** `EngagedCity` learns to place parks for homes blocked by land
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

### M12–M19 outline (detailed plans written when each milestone starts)

**M12 — Land value & local pollution** (full plan above). Pollution becomes per-cell (industrial cells emit in a
radius scaled by age: workshops low, factories high, clean energy low), replacing the city-wide
term. Land value per cell comes from parks, services, water/coast later, pollution and the
**heritage bonus** (historic cells raise value around them). Level 3 needs a land-value
threshold. Adds Pollution and Land value info views. Reuses the `CoverageSystem` pattern.

**M13 — Water.** Wells and fountains (coverage radius, Medieval and Renaissance), water towers and pumps
feeding pipes under roads (Industrial and Modern, a copy of the `PowerSystem` network/allocation model). Fills in the
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
in the timber ages), plague in the Medieval age without health coverage, plant breakdowns.
Random events with choices are delivered as toasts or popups. Can be toggled in New City.

**M18 — Art & atmosphere.** Your hand-made per-age assets go into the `AgeVisualSet` slots,
along with per-age road tiles, day/night lighting, and per-age music and ambience. Optional extra:
cosmetic carts/cars on busy roads (visual only).

**M19 — Release.** Main menu, settings (audio, keybinds, UI scale), multiple save slots with
thumbnails, a tutorial for the first age, and a Windows player build.

**Status (2026-10-03):** M10 done (variable map size, render fix, New City dialog). **M11 done** (steps 11a–11g,
all done-when checks met: Medieval start advances through all four ages with redevelopment and Keep
historical; Industrial start = today's game plus the accepted earlier-age bonuses; save v2 round
trips and v1 migrates; EditMode tests green plus the UI-only play-through). **M12 planned** (land value &
local pollution, steps 12a–12d above); next: 12a.
