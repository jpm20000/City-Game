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
| 19 | Release | Main menu, settings, save slots, controls, tutorial, player build | A standalone build plays start to finish — **done (2026-10-04)** |

**Status (2026-10-04):** M0–M16 implemented — the vertical slice is complete, M16 (§12) added a statistical homes→jobs commute (`TrafficSystem`), five tech-unlocked road tiers stored per road (save v5), a Traffic happiness term and land-value line, tiered placement with upgrade drags and a Traffic view, M15 (§12) added budget lines with funding, loans, ordinances and a Budget panel (save v4), M14 (§12) added civic services (order, fire, health and education lines; crime, fire risk and sickness as happiness terms, crime in land value, research from schooled residents, outdated tiers), M13 (§12) made every age need water (wells, then towers, pumps and pipes; save v3), M12 (§12) made pollution local and added land value (level 3 needs it) and the heritage bonus, M9 (§11) added power, park coverage and info views, M10 (§12) made the map size per city (default 64²) with a New City dialog, and M11 (§12) added four ages, research and a 30-tech tree, per-age growth rules and looks, redevelopment with Keep historical, save v2 with migration, and a starting-age picker. M7 shipped as UGUI + TextMeshPro
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

### M10–M14 — done (one file each)

The complete plans, design-decision tables, step notes and tuning histories live in [Docs/milestones/](milestones/): `M10.md`, `M11.md`, `M12.md`, `M13.md`, `M14.md`. Read the one you need, not all. What each delivered (the code as built is in §13):

- **M10 Scale — done (2026-10-02/03).** Variable map size (32/64/96 in the New City dialog; `GridData.Resize`
  in place with `OnResized` for all per-cell state), shared-material block rendering (23 ms → 1.1 ms frames on a full
  96² city), pooled blocks, v1 saves load as 24² cities. Risk carried forward: anything caching per-cell state must
  handle `GridData.OnResized`.
- **M11 Ages & technology — done (2026-10-03).** Four ages, 36 techs, research points, advancing an age (RP cost,
  techs of the age, population), selectable starting age, redevelopment and Keep historical, `CapacityModel`, save v2
  with the migration chain (`SaveMigrations`), the UI-only play-through template. Balance: `EngagedCity` and
  `AgeBalanceTests`; Industrial start = the accepted baseline.
- **M12 Land value & local pollution — done (2026-10-03).** Per-cell pollution by built age, land value with the
  level-3 gate (`GrowthBlocker.LowLandValue`), heritage bonus, Pollution and Land value views. The age-less baseline
  changed by decision (Industrial start 220 pop / 0.660 then).
- **M13 Water — done (2026-10-03).** Wells and fountains by coverage (Medieval / Renaissance), towers, pumps and
  player-drawn pipes on a `UtilityNetwork` shared with power (Industrial / Modern), water gate for upgrades, save v3
  (`Pipes`), Water view, HUD line, toasts, `ObsoleteAge`. `AgeBalanceTests` 86 / 62 / 69 days.
- **M14 Civic services — done (2026-10-04).** Order, fire, health, education lines with one building per age
  (`CivicCoverage`, `CivicSystem`), crime / fire risk / sickness as ramped happiness terms, crime in land value,
  research from schooled residents, outdated tiers leave the toolbar, Services views, `EngagedCity` civic rules. No save
  change; `AgeBalanceTests` 84 / 61 / 73 days; Industrial start 220 pop / 0.631.

### M15 — Budget depth — done (2026-10-04)

Full plan: [Docs/milestones/M15.md](milestones/M15.md). Per-service funding sliders (50–150%) for parks, power, water
and the four civic lines; loans by age; 12 tech-unlocked ordinances; the Taxes panel becomes a Budget panel with a
daily ledger; save v4. Defaults are identity (no baseline change). Steps 15a funding (pure) · 15b loans + save v4 ·
15c ordinances · 15d UI · 15e balance / play-through / docs. All five steps are done.

### M16 — Traffic — done (2026-10-04)

Full plan: [Docs/milestones/M16.md](milestones/M16.md). A statistical homes → jobs commute (one Dial pass over the
road graph per tick) loads every road cell; congestion costs happiness (a Traffic term from each home's commute) and
land value (roads beside a cell, so the level-3 gate). Five road tiers (dirt, cobble, paved, avenue, highway) stored
as a byte per road in `GridData` and unlocked by techs (new tech Macadam); highways give no frontage; upgrades by
dragging a better tier; Traffic view; save v5. Traffic runs in the age-less sim too (decision). Steps 16a road tiers
+ save v5 · 16b traffic (pure) · 16c UI · 16d balance / perf / play-through / docs — **all done (2026-10-04)**; the as-built notes are in §13 *Traffic (M16)*.

### M17 — Disasters & events — done (2026-10-04)

Full plan: [Docs/milestones/M17.md](milestones/M17.md). One seeded RNG (`SimRandom`, state saved) drives fires (from
M14 fire risk; spread block to block, roads as firebreaks, put out by fire cover; burnt grown blocks become rubble
and **placed buildings burn down too**, by decision), plague (Medieval / Renaissance, from sickness; kills residents,
Plague happiness term), plant / tower / pump breakdowns (by tech age and funding; repair for 20% of cost; Smart Grid
×0.25) and 13 random events with choices in a **pausing popup** (temporary effects folded into `TechModifiers`).
A Disasters & events switch in New City (on for new cities, **off for migrated saves**); the age-less sim never
runs them, so no baseline moves. `AgeBalanceTests` off and on for fixed seeds. Save v6. Steps 17a RNG + switch +
save v6 (17a done) · 17b fire (done) · 17c plague + breakdowns (done) · 17d events (done) · 17e UI (done) · 17f balance / perf / play-through / docs.

### M18 — Art & atmosphere — done (2026-10-04)

Full plan: [Docs/milestones/M18.md](milestones/M18.md). Presentation only: no sim, save or balance change. A
generated building kit (per age × zone × level, 2–3 variants, one shared palette material, facing the street) fills
the `AgeVisualSet` slots under the prefab contract, and your hand-made prefabs can replace any slot later. Textured
road sheets per tier and ground per age, a day/night cycle once per game month (derived from the date), generated
SFX and per-age ambience with empty per-age music slots, a Sound panel, and cosmetic vehicles from the traffic
load. Steps 18a art pipeline · 18b building kit · 18c roads and ground · 18d day/night · 18e audio · 18f vehicles ·
18g perf / play-through / docs — **all done (2026-10-04)**; the as-built notes are in §13 (Core / Buildings: *Art pipeline*, *Building kit*, *Road and ground art*, *Day/night*, *Audio*, *Vehicles*).

### M19 — Release — **done (2026-10-04)** (the roadmap is complete)

Full plan: [Docs/milestones/M19.md](milestones/M19.md). A main menu over a bundled showcase city, a pause menu with
one Esc router, named saves with thumbnails, quicksave and three rotating autosaves (save v7: city name and tutorial
progress; the legacy `city.json` is imported), one Settings screen (audio, display, UI scale, gameplay, rebindable
keys) that takes over the M18 Sound panel, a non-blocking Medieval tutorial (12 objectives, button highlights,
blocker help), and a Windows release (non-development player, zip, Inno Setup installer, `-smokeTest`). No sim
change. Steps 19a save slots + v7 · 19b pause menu / save browser / Esc · 19c main menu + showcase · 19d settings ·
19e controls · 19f tutorial · 19g Windows release · 19h play-through / docs — **all done (2026-10-04)**; the as-built
notes are in §13 (*Release (M19)*, and the M19 items under *Save / load* and *UI*). The game is Chronopolis by J-man
Studios, version 1.0.0. M0-M19 are complete; further work is polish and balance.

### Where M13–M19 plug in (integration notes)
Moved here from `AGENTS.md` (2026-10-03). Where each outline lands in the code that exists today; decide the details in each milestone's plan.

- **M12 Land value & local pollution — done** (see §13 Land value & pollution). Original outline: per-cell pollution (industrial cells emit in an age-scaled radius) replaces the city-wide `PollutionPenalty` term in `PopulationSystem`; per-cell land value from parks/services, pollution and the heritage bonus (`GridData.IsHistoric` raises value around kept cells). Build both like `CoverageSystem` (per-cell arrays, lazy recompute, `OnResized`). Level 3 gains a land-value gate → new `GrowthBlocker`. Pollution and Land value info views.
- **M13 Water — done** (see §13 Water; plan in `Docs/milestones/M13.md`). Original outline: `AgeDefinition.UpgradesNeedWater` already exists (unused) — add it to `AgeRules` and gate upgrades in `GrowthSystem` beside the power gate. Early ages: wells and fountains as coverage sources; Industrial and Modern: towers and pumps feeding pipes under roads, a copy of `PowerSystem`'s network and allocation model (`ServiceSource` + `BuildingDefinition` get a water supply). HUD group, view and toasts follow the power pattern.
- **M14 Civic services — done** (see §13 Civic services; plan in `Docs/milestones/M14.md`). Original outline: (M13 adds: `BuildingDefinition.ObsoleteAge` + `GameManager.IsObsolete` are the hook for "outdated, replace with X"; `UtilityNetwork` is the base for any further road-borne network; the toolbar now scales itself down when it overflows.) order, fire, health and education lines with per-age `BuildingDefinition`s unlocked by existing techs (e.g. fire station → Steam Power). Education buildings produce RP via `ResearchPerDay`. Health and crime become `HappinessBreakdown` terms (+ the happiness tooltip); per-cell crime and fire risk use the coverage pattern. Obsolete placed services get "outdated, replace with X" hints in `SelectionPanel`.
- **M15 Budget depth — done** (plan in `Docs/milestones/M15.md`). Original outline: per-service funding scales a service's radius and effect (`ServiceSource` / `CoverageSystem`); loans with interest live in `EconomySystem` (saved → version bump); ordinances are tech-unlocked toggles (a new `TechEffectType` if needed). `TaxPanel` grows into a budget panel in the `SidePanels` slot.
- **M16 Traffic — done:** statistical load per road cell from the homes↔jobs flow (no agents); congestion lowers road access quality and happiness. Road tiers (dirt → cobble → paved → avenue → highway) become a per-road byte in `GridData` (saved → version bump), are unlocked by tech (the `UnlockRoadTier` idea in §12) and drawn per tier by `RoadTilemapView`. Traffic view. Watch the 96² benchmark.
- **M17 Disasters & events** (plan in `Docs/milestones/M17.md`): fire spreads between cells without fire coverage, plague in the Medieval age without health coverage, plant breakdowns; random events with choices arrive as toasts or popups. Use a seeded RNG whose state is saved; an on/off switch goes in the New City dialog (and `SaveData`).
- **M18 Art & atmosphere** (plan in `Docs/milestones/M18.md`): hand-made per-age prefabs go into the `AgeVisualSet` slots under the prefab contract (pivot at the ground centre of a 1×1 cell, +Y up, 1 unit = 1 cell, layer 9, a collider on the root, shared materials only, one or two materials; GamePlan §12). Also per-age road tiles, day/night lighting, music and ambience. Re-run `PerfBenchmark`.
- **M19 Release** (plan in `Docs/milestones/M19.md`): main menu, settings (audio, keybinds, UI scale), multiple save slots with thumbnails (`SaveGameController` is single-slot `city.json` today), a first-age tutorial and a Windows player build (see `perf-benchmark` for building a player and reverting the settings churn it leaves behind).

### M15–M19 outline (detailed plans written when each milestone starts)

**M12–M14** are done; their plans are in `Docs/milestones/`.

**M15 — Budget depth** (plan in `Docs/milestones/M15.md`). Per-service funding sliders (funding scales radius/effect), loans with
interest and repayment, a few ordinances per age (unlocked by tech). Expands the TaxPanel into a
budget panel.

**M16 — Traffic — done** (plan in `Docs/milestones/M16.md`). Abstract load per road cell from the homes↔jobs flow (statistical, no
agents). Congestion reduces road access quality and happiness. Road tiers by age (dirt →
cobble → paved → avenue → highway), unlocked by tech, with capacity and cost. Traffic view.

**M17 — Disasters & events** (plan in `Docs/milestones/M17.md`). Fire spreads between cells without fire coverage (a big threat
in the timber ages), plague in the Medieval age without health coverage, plant breakdowns.
Random events with choices are delivered as toasts or popups. Can be toggled in New City.

**M18 — Art & atmosphere** (plan in `Docs/milestones/M18.md`). Your hand-made per-age assets go into the `AgeVisualSet` slots,
along with per-age road tiles, day/night lighting, and per-age music and ambience. Optional extra:
cosmetic carts/cars on busy roads (visual only).

**M19 — Release** (plan in `Docs/milestones/M19.md`). Main menu, settings (audio, keybinds, UI scale), multiple save slots with
thumbnails, a tutorial for the first age, and a Windows player build.

**Status (2026-10-03):** M10 done (variable map size, render fix, New City dialog). **M11 done** (steps 11a–11g,
all done-when checks met: Medieval start advances through all four ages with redevelopment and Keep
historical; Industrial start = today's game plus the accepted earlier-age bonuses; save v2 round
trips and v1 migrates; EditMode tests green plus the UI-only play-through). **M12 done** (steps 12a–12d:
local pollution by built age, land value with the level-3 gate, heritage bonus, Pollution and Value
views; the age-less baseline changed by decision, Industrial start re-recorded at 220 pop / 0.660;
EditMode tests green plus the UI-only play-through). **M13 done** (steps 13a–13e: water in every age — wells and fountains
by coverage in the Medieval and Renaissance ages, towers and pumps on the road network plus player-drawn pipes from the
Industrial age; the age-less sim needs piped water by decision; save v3 stores pipes; Water view, HUD line, panel, toasts;
`AgeBalanceTests` 86 / 62 / 69 days; EditMode tests green plus the UI-only play-through). **M14 done** (steps 14a–14d: order,
fire, health and education lines with one building per age, crime / fire risk / sickness as ramped happiness terms, crime in
land value, research from schooled residents, outdated tiers leave the toolbar; the age-less sim pays the civic needs by
decision, Industrial start re-recorded at 220 pop / 0.631; no save change; Services views, panel lines, toasts; `AgeBalanceTests`
84 / 61 / 73 days; 195 EditMode tests green plus the UI-only play-through). **M15 done** (steps 15a–15e: funding per budget line, 50-150%, scaling upkeep and, with diminishing returns, reach, supply and strength; loans by age; 12 tech-unlocked ordinances; a Budget panel with the day's ledger; save v4; the default settings change nothing, Industrial start still 220 pop / 0.631; `AgeBalanceTests` 84 / 61 / 73 days with and without the budget player; 229 EditMode tests green plus the UI-only play-through). Next: M16 (traffic) — planned in `Docs/milestones/M16.md` (2026-10-04); start with step 16a.

**M17 done** (steps 17a–17f: a seeded saved RNG, fire that spreads and burns blocks and placed buildings into rubble, plague, plant breakdowns with a repair price, 13 random events in a pausing popup, a New City switch, save v6; `AgeBalanceTests` hold with disasters on for three seeds (happiness floor 0.54 there), `Disasters.Step` 0.144 ms on a 630-pop 96² city and 1.44 ms on the fully grown stress city, no frame-time change in a player build, 336 EditMode tests green plus a UI-only play-through). Next: M18 (art & atmosphere) — planned in `Docs/milestones/M18.md` (2026-10-04); start with step 18a.

**M18 done** (steps 18a–18g, all presentation: no sim, save or balance change; a generated building kit of 108 prefabs on one shared `Kit.mat` filling every `AgeVisualSet` slot and facing the street, textured road tiles per tier and ground per age, a day/night cycle once per game month with lit windows, generated effects and ambience with empty music slots and a Sound panel, cosmetic vehicles from the traffic flow; benchmark with everything on 1.26-1.40 ms at default zoom and 1.65 ms zoomed out, 357 EditMode tests green, a UI-only play-through with 0 failed checks; owed: a human listen to the audio). Next: M19 (release): the Sound settings, `DayNightCycle.LockToDay` and the `PlayerPrefs` keys `CityGame.Audio.*` / `CityGame.Visual.LockDay` are what its settings menu should take over.

**M16 done** (steps 16a–16d: byte road tiers dirt / cobble / paved / avenue / highway with tech-unlocked content and Macadam, highways without frontage, save v5; `TrafficSystem` Dial's-algorithm flow with a Traffic happiness term and land-value line; tiered placement, upgrade drags, per-tier sprites, Traffic view and panel / tooltip / toast hooks; the engaged player lays best-tier roads and upgrades jams. `AgeBalanceTests` 84 / 61 / 57 days (budget 84 / 58 / 54), seeded baselines unchanged, traffic flow 0.29 ms on a 96² city in a player build, 260 EditMode tests green plus a UI-only play-through with 0 failed checks). Next: M17 (disasters & events) — planned in `Docs/milestones/M17.md` (2026-10-04); start with step 17a.

---

## 13. Systems reference (as built, M0–M19)

What each system is, where it lives and the numbers it was tuned to. Moved here from `AGENTS.md` (2026-10-03) so that file stays a short list of conventions; update the matching section in the same commit as any change (see the `milestone-workflow` skill). Where this section and the earlier sections of this document disagree, this section describes the code as built.

### Camera
- `Assets/_Game/Scripts/Camera/CameraSortAxis.cs` — applies `transparencySortMode` + `transparencySortAxis` in `Awake` (runtime-only, not serialized).
- `Assets/_Game/Scripts/Camera/IsoCameraController.cs` — pan (WASD/arrows; edge-scroll disabled via `m_EnableEdgePan`), mouse-position zoom (scroll wheel, 0.15 speed, 4 to `max(20, mapSide × 0.45)`), soft spring-back bounds (spring force 40, damping 0.88). Attached to Main Camera.

### Input
- `Assets/_Game/Scripts/Input/InputReader.cs` — wraps `CityBuilder.inputactions` (clones asset in `Awake`, enables `Gameplay` map). Exposes `Pan`, `Zoom`, `Pointer`, `ConfirmPressed`, `ConfirmHeld`, `CancelPressed`, `RotatePressed`, `DemolishPressed`, `RoadToolPressed`, `DebugTogglePressed`, `QuickSavePressed`, `QuickLoadPressed`, `CycleOverlayPressed`, `SpeedDelta`. Lives on a root `InputReader` GameObject in Main.unity.
- Bindings: `Confirm`=LMB, `Cancel`=RMB/Esc, `Rotate`=R, `Demolish`=Delete/Backspace, `RoadTool`=B, `PipeTool`=P (M13), `CycleOverlay`=V, `DebugToggle`=F1, `QuickSave`=F5, `QuickLoad`=F9, `SpeedDelta`=1–4 (keys 2/3/4 carry `Scale(factor=N)` processors so the action reads 1..4; without them every key reads 1).

### Grid
- `Assets/_Game/Scripts/Grid/CityBuilder.Grid.asmdef` — runtime asmdef for the grid core. Contains `GridSystem` (MonoBehaviour, references Unity Grid/Tilemap), `CellUtils`/`GridData`/`ZoneType` (pure C#).
- `Assets/_Game/Scripts/Grid/CellUtils.cs` — static math: `IsInBounds`, `Index`, `CellToWorld`/`WorldToCell` (origin-based), `Neighbors4`, `EffectiveSize` (odd rotation swaps dims — use it everywhere a rotated footprint's size is needed), `GetFootprint`.
- `Assets/_Game/Scripts/Grid/GridData.cs` — pure C# state (`zones`, `roads`, `occupancy` int ids, `0` = empty): `GetZone/SetZone`, `IsRoad/SetRoad`, **road tiers (M16)** `GetRoadTier/SetRoadTier` (`byte[] m_RoadTiers`, 0 = none, 1..`MaxRoadTier` 5; `SetRoad(cell, true)` lays `DefaultRoadTier` = 3 Paved and keeps an existing road's tier; a tier change raises `OnCellChanged`; `CountRoadsByTier`), `GetBuildingLevel/SetBuildingLevel` (grown zone level `0..3`, `0` = undeveloped; setting 0 also clears the built age and historic flag, so demolish/rezone need nothing else), `GetBuiltAge/SetBuiltAge` + `IsHistoric/SetHistoric` (M11; historic only sticks on grown cells), `CountRoads`, `CanPlace` (rejects roads, occupied **and grown** cells), `Occupy`/`Release`, `GetFootprint`, `OnCellChanged`. `Resize(w, h)` replaces the map with an empty one and raises only `OnResized` (no per-cell events) — anything caching per-cell state (`RoadNetwork`, `PowerSystem`, `CoverageSystem` via `SimulationSystem`, `GridTilemapView`, `GrowthVisuals`) reallocates there. `ExportRoads` / `Import` carry the tier bytes. `ExportZones/ExportRoads/ExportLevels/ExportBuiltAges/ExportHistoric` + `Import(zones, roads, levels, builtAges = null, historic = null)` (row-major `byte[]`, clears occupancy, raises `OnCellChanged` only for cells that changed) back save/load.
- `Assets/_Game/Scripts/Grid/ZoneType.cs` — `enum ZoneType { None, Residential, Commercial, Industrial }`.
- `Assets/_Game/Scripts/Grid/GridSystem.cs` — `MonoBehaviour` on the `Grid` GO; holds the `Ground` Tilemap ref (the scene's painted bounds are read once, lazily, as the tile anchor), origin `(0,0,0)`, `GridSize` = startup map size (scene: `(64,64)`). Exposes `CellToWorld`/`WorldToCell`, `LogicalToTileCell` and `PaintGround(size)` (refills the `Ground` tilemap; called by `GameManager`).
- Logical coordinate space is `(0..W-1)×(0..H-1)`; cell `(x,y)` center → world `(x+0.5, 0, y+0.5)`. Seeded-city tests still use 24×24 (`RunSeededCity`); `DebugSeedCity` scales its cross to the map.

### Placement
- `Assets/_Game/Scripts/Placement/PlacementController.cs` — mode machine `{ None, Road, Building, Demolish, Zone, Pipe }` (`Pipe`, M13: `SelectPipe` / `P` once `GameManager.PipesUnlocked`; paints while held; a drag that starts on a pipe removes pipes, no refund; `PipeCost` per cell, one insufficient-funds toast per drag; hint "Pipe $5 — drag to lay" / "Roads carry water already" / "Remove pipe"). Reads the shared `GridData` from `GameManager` in `Start` (**not** `Awake` — `GameManager.Awake` must run first). `RoadTool`/`Demolish` toggle modes, `Cancel` clears, `SelectBuilding(def)` + `R` rotates in `Building` mode, `Confirm` places/removes. Only writes `GridData` roads — the tilemap is painted by `RoadTilemapView`; validation reuses `GridData.CanPlace` (no separate validator class). Holds a `Dictionary<int, BuildingInstance>` (occupant id → instance) to resolve building demolition. Roads cost `BalanceConfig.RoadCost`, buildings `def.Cost` (via `GameManager.Economy`; unaffordable = red ghost, no place). Placed/demolished buildings are reported to `GameManager.RegisterBuilding/UnregisterBuilding`. Demolish on a grown cell resets its level to 0 (zone stays, so it regrows). `SelectZone(ZoneType)` paints zones while LMB is **held** (drag); `ZoneType.None` = unzone; rezoning/unzoning resets the cell's grown level; roads/occupied cells can't be zoned; placing a road or building clears zoning under it. `CursorHint`/`CursorHintValid` (set in `UpdateHint` while a tool is active: cost, or why the action is blocked — `FootprintProblem` mirrors `GridData.CanPlace`). The hint is the single source of validity: the ghost's green/red just mirrors `CursorHintValid`, so a new tool only needs a hint case; the ghost and hint hide while the pointer is over UI. Raises `GameEvents.MoneySpent(amount, worldPos)` after paying for a road/building. `DebugSeedCity()` lays a free cross-shaped road, a free `power_plant` at `(0, mid−3)` (same spot as the seeded-city tests) + R/C/I zones (debug shortcut). Save/load API: `PlacedBuildings`, `ClearAllBuildings()` (no refund; resets tool + selection), `RestoreBuilding(def, origin, rot)` (free; shares `CreateBuilding` with normal placement).
- `Assets/_Game/Scripts/Placement/GridTilemapView.cs` — abstract base for tilemaps mirroring `GridData` (holds the `m_GameManager`/`m_GridSystem`/`m_Tilemap` refs). Because any road edit can change things far away, `OnCellChanged` only marks it dirty and `LateUpdate` re-diffs every cell against a painted-tile cache, so only changed cells hit `SetTile`. Subclasses implement `CreateTiles()` and `TileFor(cell)`; override `OnDestroy` with `base.OnDestroy()` to free runtime tiles/textures. For state outside `GridData` call `MarkDirty()`; `OnRepainted()` runs after each repaint pass; `OnGridResized()` after a map resize (painted cache and tilemap already reset).
- `Assets/_Game/Scripts/Placement/ZoneOverlay.cs` (`GridTilemapView`) — on the `Grid/Zones` Tilemap (sibling of `Roads`, same material/sorting order 1); translucent runtime `Tile`s from the `ground_square` sprite per zone (`ZonePalette` colour × `m_Alpha`). Zoned cells **without road access** get a runtime-generated diagonal-stripe tile instead.
- `Assets/_Game/Scripts/Placement/InfoOverlay.cs` (`GridTilemapView`, M9) — on the `Grid/Info` Tilemap (sorting order 2, above Zones/Roads). Views `Off / Power / Coverage / Pollution / LandValue / Age / Water` (Pollution / LandValue: see Land value & pollution; Age: see Ages & research → UI; Water: see Water): `V` cycles `Chosen` in the order Off → Power → Water → Coverage → Pollution → LandValue → Age (`s_CycleOrder`) (skipping views that aren't `IsAvailable` yet), toolbar `VIEW` buttons toggle it; `Shown` = the Plant/Park tool's view while that tool is held (any def with `PowerSupply` / `CoverageRadius`), else `Chosen`. Power: energised roads yellow, grown cells green/red, undeveloped zones faint green/red (beside an energised road or not). Coverage: per-cell park count shading; homes recoloured by bonus, job buildings dark grey. Grown buildings are recoloured through `GrowthVisuals.SetColorOverride` (the ground tint is mostly hidden under them) and the `Zones` renderer (`m_ZoneRenderer`) is hidden while a view is shown. **What-if preview:** with the tool held and `PlacementController.HasBuildingPreview`, a private `PowerSystem`/`CoverageSystem` are fed `Simulation.Sources` + the ghost's source, so the view shows the result of placing it (newly covered cells blue); nothing in the real sim changes.
- `PlacementController` preview API: `HasBuildingPreview` / `PreviewOrigin` / `PreviewRotation` + `PreviewChanged` (Building mode, pointer on the map and not over UI, footprint fits; cost ignored).
- `Assets/_Game/Scripts/Placement/GhostRenderer.cs` — `SpriteRenderer` on `Ghost` layer 10, green/red tint; `Show(worldPos, size, valid)` scales it to a footprint. Must be rotated `-90° X` to match the `Grid`, else it renders standing up.
- `Roads` Tilemap (child of `Grid`, layer 9, URP sprite material, sorting order 1). `Placement/RoadTilemapView.cs` (`GridTilemapView`) on the `Roads` GO is the only thing that paints it: auto-tiles from the 4 neighbours using 16 **runtime-generated** 64 px sprites (asphalt, curbs on unconnected sides + inner-corner nubs, dashed centre line towards each connection; dash length divides 64 so dashes line up across tiles). A road meeting the map edge head-on connects off-map (the entry); one running along the edge doesn't. Roads not connected to the edge (`RoadNetwork.IsConnectedToEntry`) are tinted red. Sprite orientation: texture +x = logical +x, texture +y = logical **−y** (see `LogicalToTileCell`). `Art/road_tile.asset` is no longer used.

### Roads (M5)
- `Assets/_Game/Scripts/Simulation/CityBuilder.Simulation.asmdef` — runtime asmdef for pure simulation logic; references `CityBuilder.Grid`.
- `Assets/_Game/Scripts/Simulation/RoadNetwork.cs` — pure C# connectivity view over the shared `GridData` roads. Constructed with `GridData`; subscribes `OnCellChanged` to mark the connectivity cache dirty. `HasRoadAccess(cell)` = a 4-neighbor is a road connected (4-connected BFS) to the map edge (the "entry"); `IsConnectedToEntry(roadCell)`. Multi-source BFS from edge road cells recomputes lazily; diagonal adjacency does **not** count. **Frontage (M16a):** the optional `frontage(tier)` lookup (`SimulationSystem` installs it with `SetFrontage` when road-tier content exists) makes `HasRoadAccess` ignore neighbours whose tier has no frontage (highways); `IsConnectedToEntry` and the BFS count every tier, so a highway connects networks and the map edge but zoned land beside only a highway never grows (`NoRoadAccess`).
- **Road tiers (M16a)** — `RoadTierDefinition` (Simulation asmdef ScriptableObject in `Ages/`: `Tier`, `Id`, `RequiredTech`, `Cost`, `UpkeepPerDay`, `Capacity`, `TravelCost`, `Frontage`, `ObsoleteBy`), listed in `TechDatabase.RoadTiers` (validated: tiers 1..5 unique, tech in the tree, capacity > 0, travel cost ≥ 1). `RoadTiers` (`SimulationSystem.RoadTiers`): per-tier `Cost / UpkeepPerDay / Capacity / TravelCost / Frontage / DisplayName`, `IsUnlocked(tier)` (tech researched; none = always), `BestStreetTier` (the best unlocked tier ≤ Paved that no unlocked tier replaces: Dirt → Cobble → Paved; Avenue and Highway have their own tools), `UpgradeCost(from, to)`, `UpkeepPerDay(grid)`. **Legacy** (no content): every tier = today's Paved road from `BalanceConfig` (`RoadCost` 50, `RoadUpkeepPerDay` 1, `RoadCapacity` 160, `RoadTravelCost` 4), so the age-less sim and the seeded-city numbers are unchanged. Content (`Tools/gen_age_content.py` `ROAD_TIERS`, assets `Scriptables/Techs/RoadTiers/`): dirt $20 / 0.4 / cap 60 / travel 6 (no tech) · cobble $35 / 0.7 / 100 / 5 (Architecture) · paved $50 / 1 / 160 / 4 (**Macadam**, an Industrial starting tech) · avenue $150 / 3 / 400 / 3 (Electric Trams) · highway $400 / 6 / 1200 / 1, **no frontage** (Automobiles). `TechEffectType.TrafficMultiplier` (trips × value; Railways ×0.85, Electric Trams ×0.9, Automobiles ×1.2 + commercial demand ×1.1, Car-free Sundays ×0.9) is folded into `TechModifiers.TrafficMultiplier` but read only from 16b. The ledger's Roads line is Σ tier upkeep × the tech upkeep multiplier. 16a runtime: `PlacementController` lays `BestStreetTier` at its cost, the toolbar Road button and `SelectionPanel` read the tier (drawing, Avenue / Highway tools, upgrade drags: 16c). Tests: `RoadTierTests.cs`, `ContentTests.RoadTiers_Content`.

### Simulation (M6)
- All pure, in `CityBuilder.Simulation.asmdef`: `GameSpeed` enum, `DemandSnapshot` struct, `CityModifiers` (housing/jobs/upkeep from player-placed buildings — supplied by the runtime because `BuildingDefinition` is in `Assembly-CSharp`; spatial effects come from `ServiceSource`s, see Services & power), `BalanceConfig` (ScriptableObject, all §7 tunables; asset `Scriptables/Balance/BalanceConfig.asset`), `EconomySystem`, `PopulationSystem`, `DemandSystem`, `GrowthSystem`, `SimulationSystem`.
- `SimulationSystem.Tick()` order: recount capacity → **Demand → Growth → Population → Economy**. Grown buildings are **not GameObjects in the sim**: `GrowthSystem` raises `GridData` building levels (row-major scan, new cells before upgrades, eligible = zoned + road access + not road/occupied; upgrades past level 1 also need power **in ages whose `UpgradesNeedPower`** (Industrial and Modern, and the age-less sim), see Services & power, and water in every age with a water rule (M13, see Water); level caps per age, see Ages & research); capacity per level = `BalanceConfig.CapacityForLevel` (4/8/16), scaled per built age by `CapacityModel` (M11).
- **Balance deviates from `GamePlan.md` §7** (the doc's formulas deadlock at ~1–4 pop): constant residential base demand 0.40 (doc: 0.30 only at pop 0), move-in = `ceil(Vacant * 0.20)` not scaled by demand, C/I demand denominator floor 5 (doc: 20), `Employed = min(Workers, Jobs)` (doc: `min(Population, Jobs)`). Tuned fields are tagged "(tuned)" in `BalanceConfig`.
- **M8 balance layer** (fields tagged "(tuned, M8)"): happiness base 0.70 (doc 0.80); each zone's demand × `1 − 4·(its tax − 10%)` (`DemandSystem.TaxMultiplier`, so low taxes speed growth); C and I tax each cost `0.5 × (tax − 10%)` happiness like R; pollution was `0.5 × IndustrialJobs / (Housing + Jobs)` until M12 (now local, see Land value & pollution); unemployment + pollution penalties ramp in over the first 40 residents (`SmallTownGracePopulation` — new towns are always lopsided and would otherwise deadlock); below 0.5 happiness 5%/day of residents move out (`PopulationSystem.MovedOut`) while move-in continues, so unhappy cities sit at ~80% occupancy with halved R demand. Seeded city at day 60 (10% taxes, with its plant): 212 pop, +$134/day after the plant's $100, happiness 0.60 (0.68 with 2 well-placed parks); 20% C/I with no parks stalls at ~60 pop (pre-M12 numbers; M12a: 212 pop, 0.62 / 0.70 with parks, 20% C/I stalls at ~22). M9 numbers: see Services & power. `BalanceConfig.asset` must carry the same values as the code defaults (tests use `CreateInstance` defaults, the game uses the asset).
- `PopulationSystem.Happiness` is a `HappinessBreakdown` (signed Base/Unemployment/Taxes/Pollution/Services/Power/Homeless/Technology/Heritage/Water/Crime terms; `Total` = `AverageHappiness`), recomputed in `Step` and by `RefreshHappinessBreakdown` after a load. UI explanations should read it rather than re-deriving formulas; for previews of not-yet-applied taxes use `PopulationSystem.TaxHappinessPenalty(r, c, i)` (the same term `Step` uses).
- Tests: `Tests/EditMode/Simulation/SimulationTests.cs` (create `BalanceConfig` via `ScriptableObject.CreateInstance`).

### Core / Buildings (M4, M6)
- `Assets/_Game/Scripts/Core/GameManager.cs` — owns the **single shared `GridData`** (created in `Awake` from `GridSystem.GridSize`, 64×64 in the scene; resized in place by New City / Load, never replaced — `MapSize`; on `OnResized` it repaints the ground via `GridSystem.PaintGround` and raises `GameEvents.WorldResized`), the `RoadNetwork` (`Roads`), the `BuildingDatabase`, `BalanceConfig` (`Balance`; falls back to defaults with a warning if unassigned) and the `SimulationSystem` (`Simulation`, plus `Economy`/`Population`/`Demand` shortcuts). Subscribes `TimeManager.OnTick` → `Simulation.Tick()` → raises `GameEvents`. Tracks `CityModifiers` and the `ServiceSource` list (`Simulation.Sources`) from registered buildings, and raises `GameEvents.PowerChanged` from `LateUpdate`. `TimeManager` is exposed as `Clock` (not `Time`, to avoid shadowing `UnityEngine.Time`). `RaiseStateEvents()` pushes population/demand/happiness/cash flow to the UI (per tick and after a load). Root `GameManager` GameObject in Main.unity also carries `TimeManager`, `GrowthVisuals` and `SaveGameController`.
- `Assets/_Game/Scripts/Core/ZonePalette.cs` — the one place zone colours live (toolbar swatches, zone overlay, grown buildings); `ZonePalette.Get(zone)`.
- `Assets/_Game/Scripts/Core/TimeManager.cs` — accumulates `deltaTime × speed`, fires `OnTick` once per in-game day (max 4 ticks/frame), advances Day/Month/Year. Keys 1–4 = Paused/1×/2×/4×; `SetSpeed`, `SetDate` (load; drops the partial day).
- `Assets/_Game/Scripts/Core/GameEvents.cs` — static bus (`MoneyChanged`, `PopulationChanged`, `DateChanged`, `DemandChanged`, `HappinessChanged`, `CashFlowChanged`, `InsufficientFunds`, `SpeedChanged`, `CellChanged`, `CityLoaded`, `PowerChanged`, `WorldResized(size)`, `Notification(string)` → toast, `MoneySpent(amount, worldPos)` → floating text, `WaterChanged(WaterStatus)` (M13, raised with `PowerChanged`)); subscribers are cleared on `SubsystemRegistration` so disabled domain reload doesn't leak handlers. UI binds here rather than polling systems; a new event needs a line in `ResetSubscribers`.
- `Assets/_Game/Scripts/Buildings/GrowthVisuals.cs` — listens to `GridData.OnCellChanged`; per grown cell a body cube (keeps its collider for selection) + a roof piece (no collider): R/C get a darker cap (overhanging on level 1, inset rooftop box above), I gets a dark chimney. Footprint/height come from per-zone, per-level `Profile` tables (R house→apartment→tower, C shop→office→high-rise, I wide low sheds), × a deterministic per-cell height jitter (±15 %, hashed from the cell). Layer 9; colours are **shared materials, one per colour** (copies of `BuildingHouse.mat` with `_BaseColor` set, cached by colour) — never `MaterialPropertyBlock`s: they make renderers incompatible with the SRP Batcher / GPU Resident Drawer, which cost ~20 ms render thread per frame (110 ms zoomed out) on a full 96×96 map. Blocks are **pooled**: a demolished cell hides its body/roof (`renderer.enabled`/`collider.enabled = false`, not `SetActive`/`Destroy`) and new cells reuse them; a rezone reshapes the same roof block; `OnResized` destroys everything incl. the pool. A level increase plays a 0.35 s ease-out-back "pop" from 60 % scale (about the ground point); level drops/recolours apply instantly. Loading a save pops every building at once (intended). `SetColorOverride(Func<cell, Color?>)` / `RefreshColors()` let info views recolour bodies (roofs follow, chimneys stay).
- **Per-age looks (M11e).** `Scripts/Buildings/AgeVisualSet.cs` (SO, Assembly-CSharp, one per age, matched to the `AgeDatabase` by `AgeId` in `GrowthVisuals.Init(grid, ages)`; `GrowthVisuals.m_AgeVisuals` holds the 4 sets in `Scriptables/Ages/Visuals/`, generated by `Tools/gen_age_content.py`). Per zone × level 1..3 a `Style`: `Prefabs` (variants, one picked per cell by hash; empty = placeholder blocks), `Tint` (body = zone colour blended toward rgb by alpha), `Roof` (`Default` = today's cap / chimney, `Pitched` = a cube turned 45° about the ridge, ridge along X or Z per cell, `None`), `RoofColor` (alpha 0 = shaded body colour), `HeightScale`. The cell's **built age** picks the set (no age data or no set = plain). Industrial's set is plain, so an Industrial city looks exactly like before (ContentTests checks it). Medieval: timber tint, thatch / dark-wood gables, lower; Renaissance: sandstone, terracotta gables; Modern: concrete / glass tints, flat, taller. A built-age change on a grown cell (redevelopment) pops like an upgrade. Prefab instances are **pooled per prefab** (renderers/colliders disabled, never destroyed until `OnResized`); info views tint them by swapping every renderer to the shared tinted material and restore the prefab's own `sharedMaterials` afterwards. The per-cell hash keeps its low 16 bits for the height jitter, so pre-M11 skylines are unchanged. Prefab contract for the M18 art: see `AgeVisualSet` / GamePlan §12 and **M18a** below.
- `Assets/_Game/Scripts/Buildings/BuildingCategory.cs` — `enum { Zone, Road, Service, Utility, Decoration }`.
- `Assets/_Game/Scripts/Buildings/BuildingDefinition.cs` — `ScriptableObject` (Id, DisplayName, Category, Icon, Prefab, Size, Cost, UpkeepPerDay, HousingCapacity, JobsProvided, HappinessEffect, ZoneRestriction, **Height**, **CoverageRadius**, **PowerSupply**, **RequiredTech**, **ResearchPerDay**, **Pollution**, **PollutionRadius**, **WaterSupply**, **WaterRadius**, **ObsoleteAge**, **CivicKind**, **CivicRadius**, **CivicStrength**). `Height` (drives the 3D cube visual), the two M9 fields and the two M11 fields are additions beyond `GamePlan.md` §6 (`UnlockPopulation` was removed in M11). `HappinessEffect` is **not read by any code** (the Park asset's `HappinessEffect` does nothing; park happiness comes from `CoverageRadius` + `BalanceConfig.ServiceBonusEach`); `ZoneRestriction` only routes `JobsProvided` to industrial vs commercial jobs.
- `Assets/_Game/Scripts/Buildings/BuildingDatabase.cs` — `ScriptableObject` list + `GetById`.
- `Assets/_Game/Scripts/Buildings/BuildingInstance.cs` — `MonoBehaviour` on the building prefab. Mints a **static sequential `int`** occupant id (starts at 1; `0` = empty in `GridData`) — do not use `GetInstanceID()`. `Init(grid, def, origin, rotation)` calls `GridData.Occupy` then sets the transform (position = footprint center at `y = height/2`, local scale = **unrotated** `Size` × height, Y-rotation = `rotation*90°` — the rotation maps it onto the effective footprint); `Demolish()` calls `Release`. Optional `m_Decor` child is counter-scaled to world units and placed on the top surface, so decorations (park trees) can be authored at normal proportions.
- Placeholder building art is **3D primitives** (not sprites) with `Universal Render Pipeline/Simple Lit` materials (smoothness 0): `Art/BuildingHouse.mat`, `Art/BuildingPark.mat`, `Art/TreeCanopy.mat`, `Art/TreeTrunk.mat`. `Park.prefab` has a `Decor` child with four trees (colliders removed). M11 research buildings (both Service, 2×2, height 0.12, collider-less `Decor`): `Monastery` (`monastery`, $2,000, $10/day, 3 RP/day (tuned, M11g), needs `monasticism`; `MonasteryStone`/`MonasteryRoof.mat`: nave, bell tower, cloister) and `Academy` (`academy`, $5,000, $25/day, 5 RP/day, needs `academies`; `AcademyWall`/`AcademyDome.mat`: hall, portico, dome). Park needs `commons`, Power Plant `electricity`. Prefab `Prefabs/Buildings/House.prefab` (layer 9); def `Scriptables/Buildings/House.asset`; db `Scriptables/Buildings/BuildingDatabase.asset`. M13 water buildings (placeholder primitives, shared materials incl. new `WaterBlue` / `TowerTank` / `PumpBrick.mat`, `Decor` children without colliders): `Well` (`well`, Utility 1×1, $150, $1/day, `WaterRadius` 3, no tech, `ObsoleteAge` industrial), `Fountain` (`fountain`, Service 1×1, $1,200, $8/day, `WaterRadius` 5 + `CoverageRadius` 3, needs `aqueducts`), `WaterTower` (`water_tower`, Utility 1×1, $1,500, $15/day, `WaterSupply` 500, needs `waterworks`), `PumpingStation` (`pumping_station`, Utility 2×2, $6,000, $50/day, `WaterSupply` 1,500, needs `public_sanitation`). `BuildingDefinition.ObsoleteAge` (age Id): `GameManager.IsObsolete` / `CanBuild` (unlocked and not obsolete) / `ObsoleteAgeName`; the toolbar hides obsolete buildings and placement refuses them. `Park` (`park`): Service, 2×2, $500, $5/day upkeep, height 0.25, `Art/BuildingPark.mat`, `Prefabs/Buildings/Park.prefab` — `CoverageRadius` 4 (see Services & power). `PowerPlant` (`power_plant`): Utility, 3×3, $10,000, $100/day, `PowerSupply` 600, `Pollution` 6 within 5 cells (M12), height 0.6, `Prefabs/Buildings/PowerPlant.prefab` (base block `Art/BuildingPlant.mat`; collider-less `Decor`: turbine hall, two striped chimneys, transformer — `PlantHall`/`PlantChimney`/`PlantStripe`/`PlantTransformer.mat`); gets its toolbar button automatically. `House` stays in the database but has no toolbar button (R/C/I grow from zones, not manual placement).

- **Art pipeline (M18a).** The prefab contract is now checked: `Scripts/Editor/ArtContract.cs` (Assembly-CSharp-Editor; menu *CityBuilder > Validate Art* checks every prefab in every `AgeVisualSet`) requires layer 9, a collider on the root, at most two shared materials, the pivot at ground level (lowest point within 0.03 of y = 0), bounds inside the 1×1 cell (+0.04 margin) and a top height inside a per-level band (L1 0.15–1.2, L2 0.4–2.0, L3 0.6–3.4). A prefab's **front is local +Z**: `CellUtils.FacingRoad(grid, cell, hash)` (Grid asmdef) returns the quarter turn (0 = +y, 1 = +x, 2 = −y, 3 = −x) toward a road neighbour, scanning from a rotation picked by hash bits 22–23 so corner lots vary; with no road beside the cell it uses hash bits 20–21 (the pre-M18 rotation). `GrowthVisuals.ApplyTransform` rotates prefab instances by it, and `RefaceNeighbours` re-applies it to the 4 neighbours when a cell with no building changes (a road laid or removed). Placeholder blocks are symmetric and ignore it. **Ownership of the visual sets:** `Tools/gen_age_content.py` writes tints / roofs / heights and **keeps each style's `Prefabs` block** (`read_prefabs`: re-running it is a no-op and a prefab reference survives); Unity (the M18b kit generator, or you) owns the `Prefabs` slots. `CalendarMath` (Simulation asmdef, pure, never called by `Tick`): `DayPhase(day, dayFraction, daysPerCycle)` (0 = dawn, 0.25 noon, 0.5 dusk, 0.75 midnight), `SunHeight`, `NightFactor` for the M18d day/night cycle. **Markers on kit buildings (18g):** the kit's buildings are up to ~3 cells tall (the placeholders were 0.5-2.5), so `GrowthVisuals.VisualTop(cell)` (the top of the cell's shown prefab or block) lets `FireVisuals` put a flame / plague marker at 70% of the actual height instead of a fixed 0.65-1.25. Tests: `Tests/EditMode/Art/ArtContractTests.cs` (own asmdef `CityBuilder.Art.Tests`, since it needs `GameObject`; reaches `ArtContract` by name), `CellUtilsTests.FacingRoad_*`, `CalendarMathTests`.

- **Building kit (M18b).** The `AgeVisualSet` prefab slots are filled by a generated kit: `Scripts/Editor/BuildingKitGenerator.cs` (menu *CityBuilder > Generate Building Kit*, deterministic and in place, GUIDs kept) writes `Art/Kit/KitPalette.png` (16 x 16 swatches of 4 px, point filter, no mips) + `KitEmission.png` (black except window / glass swatches), the one shared **`Art/Kit/Kit.mat`** (URP Simple Lit, `_EmissionColor` black by day: M18d raises it at night), 108 flat-shaded meshes (`Art/Kit/Meshes/`, 2.0 MB, ~80 triangles each, every face samples one swatch centre so there is no bleeding) and 108 prefabs (`Prefabs/Kit/<Age>/<Age>_<R|C|I><level><a|b|c>.prefab`), each **one root with a MeshFilter, one MeshRenderer on `Kit.mat` and a BoxCollider fitted to the mesh, layer 9** (so a block is 1 renderer where the placeholders were 2, and the tinted-view swap is one material). `KitMeshBuilder` (quads, boxes, gable / hip / lean-to / saw-tooth roofs, cylinders, winding fixed against an outward hint) and `KitDesigns` (the palette and one `KitSpec` per age x zone x level; the three variants differ by wall / roof colour picks, plan size +-8%, and variant c turns the gable ridge) hold the look: Medieval timber-frame cottages, jettied houses, thatch / shingle, market stalls with striped awnings, a smithy; Renaissance rendered townhouses with terracotta hips, balconies, arcades, a guild exchange with a dome tower, a conical kiln; Industrial brick terraces and tenements, saw-tooth factory sheds, a mill with a tall stack, a steelworks with two stacks; Modern concrete / glass slabs with ribbon windows, setback towers with crowns, solar roofs, tanks. Zone legibility comes from the accents: commercial = striped awnings and teal signs, industrial = dark sheds and stacks, residential = warm roofs (the zone ground tint still shows). Window swatches are warm (candle) in Medieval / Renaissance and cool in Industrial / Modern. The generator assigns the prefabs into the four sets through `SerializedObject` and runs `ArtContract` over each (the Modern industrial hall's height was raised after that check). Re-running it overwrites the slots it owns; a hand-made prefab replaces a slot by editing the set, and survives `gen_age_content.py`. Tests: `ArtContractTests.EveryVisualSlot_HoldsThreeKitVariants_OnTheSharedKitMaterial` (36 slots x 3 variants, one renderer each, all on `Kit.mat`) and `EveryPrefabInEveryVisualSet_MeetsTheContract` (108 prefabs); `ContentTests.EveryAge_HasOneVisualSet_AndIndustrialFallbackIsPlain` (the placeholder fallback style stays plain). **Industrial no longer looks like the pre-ages game** (an intended change of a recorded fact; the sim numbers are untouched). The prefab front is local +Z (`CellUtils.FacingRoad`, with an in-bounds check: `GridData.IsRoad` does no bounds check and an off-map neighbour read the wrapped cell or threw at y = 0 until 18b fixed it; `FacingRoad_IgnoresOffMapNeighbours`).

- **Road and ground art (M18c).** `Tools/gen_tiles.py` (stdlib only, deterministic, GUIDs kept; Unity rewrites the `.meta`s on import, commit its version) writes 80 road PNGs (`Art/Tiles/Roads/road_t<tier>_m<mask>.png`: 5 tiers x the 16 connection masks, 64 x 64 px at 64 px / unit, texture row 0 = logical north exactly like the runtime sprites) and 16 ground PNGs (`Art/Tiles/Ground/ground_<age>_<0-3>.png`) plus `Scriptables/Art/TileArtSet.asset` (`TileArtSet`, Assembly-CSharp, `Road(tier, mask)` / `Ground(age, variant)`). Looks: dirt with wheel ruts and grass-tufted verges, cobbles with per-stone shades and light curb stones, paved asphalt with grain, cracks and worn dashes, avenue with a worn double line, highway black with white edge barriers and lane dashes; ground Medieval meadow with buttercups / daisies / clover, Renaissance tended grass with mown stripes, Industrial sooty grass with gravel, Modern fine lawn (ground detail stays 3 px off the border, stripes are continuous, so any variants tile seamlessly). Replace a PNG with hand-made art of the same size and keep its `.meta`. **Wiring:** `GameManager.m_TileArt` (the one scene reference; `GameManager.TileArt`). `RoadTilemapView.CreateTiles` takes each sprite from the set and draws the old runtime sprite for any missing one (so the set is optional); `GridSystem.SetGroundVariants(TileBase[])` (Grid asmdef) replaces the scene's single ground tile by variants picked per cell by a hash (`PaintGround` also repaints when the variants change), and `GameManager.ApplyGroundArt` calls it with the current age's four tiles at startup, on `GameEvents.AgeChanged` and on `CityLoaded` (New City / Load), so the ground follows the age and survives a resize (Tile objects cached per age, destroyed in `OnDestroy`). Roads look by tier, the ground by age (decision). Tests: `Tests/EditMode/Art/TileArtTests.cs` (80 + 16 sprites present, 64 px / 64 ppu; the scene references the set).

- **Day/night (M18d).** `Scripts/Core/DayNightCycle.cs` (runtime-created by `GameManager.Start`, `GameManager.DayNight`; cosmetic, never read by the sim, nothing saved). One cycle per game month: phase = `CalendarMath.DayPhase(day, TimeManager.DayFraction, DaysPerMonth)` + 0.07 (so day 1 starts in the morning), so it is calendar-derived (a load shows the right time), frozen while paused and 7.5 s per cycle at 4x. `TimeManager.DayFraction` (accumulator / `SecondsPerDay`) and `DaysPerMonth` are new. `CalendarMath.NightFactor(phase, bias)`: the cycle uses bias 0.25 (full daylight from sun height -0.05, full night below -0.5, about 42% of the cycle dark). What it drives: the directional light (elevation 12-52 deg by sun height, azimuth swinging 210-270 around the scene's 240 so the three face tones stay, a dim blue moon at 38 deg / 200 deg at night, colour white -> orange at the horizon -> blue, intensity x(0.6-1.0) -> 0.30), `RenderSettings.ambientIntensity` x1 -> x0.35, the colour of the Ground / Roads / Pipes / Zones tilemaps (sprite-unlit, so lights don't reach them: tint white -> warm at dusk -> (0.42, 0.50, 0.74) at night; the Hazards tilemap, flames and the info-overlay tilemap stay untinted) and the **window glow**: `Kit.mat` `_EmissionColor` = 0 .. 1.6 (grey) from night 0.35 to 0.9, which lights the emissive window swatches (warm in Medieval / Renaissance, cool in Industrial / Modern, baked into the palette). **Info views show in daylight** (`InfoOverlay.Shown != Off` forces the target to 0); the night factor moves toward its target at 3 per second, so views, Lock to day and load transitions fade in ~0.3 s. The placement ghost does not force day (decision: the ground tint floor keeps it readable). **Lock to day** = `DayNightCycle.LockToDay` (now `GameSettings.LockToDay`, PlayerPrefs `CityGame.Visual.LockDay`; the M19d Settings > Interface exposes it). Writes are throttled (sun only on a 0.0015-phase step, tilemap tint and glow only when they change). `Restore` (`OnDestroy` / `OnApplicationQuit`) puts the sun, ambient, tilemap colours and the material emission back, since runtime edits of the `Kit.mat` asset otherwise stick in the Editor (checked: Kit.mat has no diff after Play). Wiring: `GameManager.m_KitMaterial` (scene reference). DEBUG panel: **Time of day** cycles noon / dusk / midnight / calendar (`DayNightCycle.DebugPhase`). Tests: `CalendarMathTests` (incl. `DayBias_GivesTheDayMoreOfTheCycle...`).

- **Audio (M18e).** `Tools/gen_audio.py` (stdlib only, deterministic own PRNG, GUIDs kept; Unity rewrites the `.meta`s, commit its version) synthesizes **30 effect clips** for 21 `SfxId`s (`Audio/Sfx/<name>_<n>.wav`, 22.05 kHz mono, the busy ones with 2-3 variations) and **14 ambience loops** (`Audio/Ambience/`: per age base / city / industry + `night` + `fire`, 14 s with a 1 s crossfaded seam, 8.9 MB of WAV in all, imported as Vorbis) and writes `Scriptables/Audio/AudioCatalog.asset`. `AudioCatalog` (Assembly-CSharp, `Scripts/Audio/`): `SfxEntry {Id, Volume, Clips[]}`, `AmbienceSet {Base, City, Industry}` and `MusicSet {Tracks[]}` per age index, the shared `Night` and `Fire` loops; **music playlists ship empty** (add tracks in the Inspector and they play: one at a time, 15-45 s gaps, a 3 s fade-in, a 2 s fade-out when the age changes). A clip you replace or add stays in place: re-running the generator rewrites the catalog from its own table. `SfxId` is append-only (Click, Place, ZonePaint, RoadLay, RoadUpgrade, PipeLay, Demolish, Refused, NoMoney, Toast, LevelUp, ResearchDone, AgeAdvance, EventOpen, EventChoice, FireStart, PlagueBell, Breakdown, Repair, Save, Load). `AudioController` (created by `GameManager.Start` when `GameManager.m_AudioCatalog` is set, the one scene reference; `AudioController.Play(id, worldPos?)` is a static no-op without a controller, so EditMode tests and the sim harness stay silent): all sounds are 2D; effects use a pool of 12 voices, +-6% pitch, a 60 ms gap per effect (250 ms for LevelUp), pan by screen position (+-0.6), half volume off screen and 0.55-1.0 by zoom; `PlayLog` records the effects played (checks in Play mode cannot listen). **Ambience** = five layers of two crossfading sources (3 s on `AgeChanged` and `CityLoaded`): base = (0.55 + 0.45 zoom-in) x (1 - 0.55 x town) x (1 - 0.6 night); city = town x (0.5 + 0.5 zoom-in) x (1 - 0.4 night), town = population / 400; industry = industrial share of the grown cells in view x 2.5, scaled by their count / 12 and zoom; night = 0.8 x the day/night factor; fire = burning cells / 6 (smoothed, 1.5 s). The cells in view are sampled twice a second from the camera corners on the ground plane (a bounded `GridData` scan). Hooks (all runtime; the sim is untouched): `PlacementController` (Place, ZonePaint, RoadLay / RoadUpgrade, PipeLay, Demolish, Refused on a blocked building), `GrowthVisuals` (LevelUp for an upgrade, only while `AudioController.AllowGrowthSounds`, which `GameManager.HandleTick` opens around `Simulation.Tick`, so a load's blocks popping in stay silent), `ToolButton` (Click), `GameEvents` subscriptions inside the controller (InsufficientFunds -> NoMoney, Notification -> Toast, TechCompleted -> ResearchDone, AgeChanged -> AgeAdvance, CityLoaded -> Load), `DisasterController` (FireStart, PlagueBell, Breakdown), `EventPopup` (EventOpen, EventChoice), `SelectionPanel` (Repair), `SaveGameController.Save` (Save). **Settings:** `SoundSettings` (PlayerPrefs `CityGame.Audio.*`, not in the city save): master 0.8, music 0.5, ambience 0.6, effects 0.8, mute; `Effective(channel)` = master x channel (0 muted). `SoundPanel` (`Scripts/UI/`, built at runtime like the Budget panel, in the `SidePanels` slot, opened by a **Sound** button copied from Save, last in the Save / Load / New group, which still fits at 1920): four volume sliders (releasing one plays a click), **Mute** and **Always day** (the day/night lock) toggles. Tests: `Tests/EditMode/Art/AudioCatalogTests.cs` (21 effects, 4 ages x 3 loops, night / fire, the scene references the catalog).

- **Vehicles (M18f).** Cosmetic traffic, visual only. **Meshes:** `Scripts/Editor/KitVehicles.cs` builds 9 small vehicles from the kit's primitives (handcart, ox cart, carriage, horse tram, truck, three cars, bus, van; 0.2-0.35 cell long, on the ground, front +Z) with `BuildingKitGenerator.BuildVehicles` (part of *Generate Building Kit*): one mesh + one prefab each (`Prefabs/Kit/Vehicles/Vehicle_<name>.prefab`: a MeshRenderer on `Kit.mat`, **no collider**, default layer) and `Scriptables/Art/VehicleSet.asset` (`VehicleSet`: a prefab list per age: Medieval 2, Renaissance 3, Industrial 3, Modern 5). Two palette swatches were appended to the kit palette (`headlight`, `taillight`, emissive, so they glow with the night window light). **Runtime:** `VehicleView` (`Scripts/Placement/`, created by `GameManager.Start` when `m_VehicleSet` is set, the one scene reference; `GameManager.Vehicles`). Twice a second it lists the road cells in the camera view (the corners on the ground plane, +2 cells) with `TrafficSystem.Congestion` (load / capacity) > 0.02, sets the target count = `min(cap, round(Σ congestion x 1.5))` (cap 250, falling to 62 as the camera zooms out from 20 to 45), releases vehicles that left the view, lost their road or exceed the target by 25%, and spawns up to 12 on roads picked in proportion to their load. Each vehicle drives cell to cell at 3.2 / the tier's travel cost cells per second (x1, x2, x3 for the game speeds, **0 while paused**), in the right-hand lane (0.17 cell off the centre line, eased through turns), choosing the next road by load (+0.25, x2 straight on); a dead end or the map edge ends the trip. Drawn at 1.5x the prefab size. Hidden (all released) while an info view is on, cleared on `CityLoaded` / `WorldResized`; pooled per prefab (renderers disabled, never destroyed). Its random numbers come from a private LCG, never `SimRandom`; it never writes the sim. Tests: `Tests/EditMode/Art/VehicleSetTests.cs` (2-5 vehicles per age, one renderer on `Kit.mat`, no collider, under half a cell long, standing on the ground; the scene references the set). `PerfBenchmark` has a vehicles scenario (see the perf-benchmark baselines).

### Services & power (M9)
- Pure, in the Simulation asmdef. `BuildingDefinition.CoverageRadius` (cells, Chebyshev from the footprint) / `PowerSupply` (units); `GameManager` turns every placed building with either into a `ServiceSource {Origin, effective Size, CoverageRadius, PowerSupply}` and sets `SimulationSystem.Sources` on register/unregister (also during load, before `ApplySimulation`). `CityModifiers.ServiceCount` is gone.
- `CoverageSystem` — per-cell count of services in range; recomputed when `Sources` is set.
- `PowerSystem` (`Simulation.Power`; since M13 a thin subclass of `UtilityNetwork`, which holds the model below and is shared with piped water — `IsPowered` / `IsEnergisedRoad` / `UnpoweredCells` = `IsServed` / `IsCarrying` / `UnservedCells`; the base updates draws incrementally and rebuilds its topology only when roads / pipes / sources change, see Water → Performance) — a plant feeds the road cells sharing an edge with its footprint and every road 4-connected to them (**no** map-edge requirement); plants on one road network pool supply. Grown zone cells beside an energised road draw their capacity (`CapacityModel`, so scaled by built age); supply is handed out in multi-source BFS order from the (row-major sorted) seed roads, so it's independent of build order and the furthest cells go dark first. Lazy like `RoadNetwork`: any `GridData.OnCellChanged` or `SetSources` marks it dirty. `IsPowered`, `IsEnergisedRoad`, `Supply`/`Load`/`Demand`/`UnpoweredCells`, `HasHeadroom`/`TryReserve`.
- Growth: 0→1 needs only road access; upgrades need power **and** headroom for the extra draw, reserved during the scan (`GrowthBlocker.NoPower` / `PowerAtCapacity`), so a network is never overdrawn by upgrades.
- Happiness: `ServiceStats.Measure` (housing-weighted over grown R cells) → `Services` = average of per-home `min(coverage × ServiceBonusEach, ServiceBonusCap)`; `Power` = `−PowerPenalty × unpoweredHousingShare`, ramped in with `SmallTownGracePopulation`. `PopulationSystem.Step` takes a `ServiceStats`.
- Power/coverage are derived, never saved.
- UI feedback (M9d): `GameEvents.PowerChanged(supply, demand, unpoweredCells)` is raised by `GameManager.LateUpdate` at most once a frame after any grid/source change (so it's right while paused); `RaiseStateEvents` only marks it dirty, so after a load it arrives **after** `CityLoaded`. HUD `Power` group (`m_PowerText`: `Power —` idle, red `No power`, `Power demand / supply`, red while any grown cell is unpowered). `NotificationController` toasts once per state change: plant online, power lost, shortage (`supply > 0 && unpoweredCells > 0`), and a one-time "build a Power Plant" nudge when a powerless city reaches `SmallTownGracePopulation`; `SyncCityState` seeds these flags on load. `SelectionPanel`: plant supply / city power / unpowered count (or "not beside a road"), park reach, Powered / No power + parks-nearby bonus on grown cells, "Carries power" on roads, power availability on zoned land.
- **M9 balance** (fields tagged "(tuned, M9)"): `ServiceBonusEach` 0.10 per park reaching a home (doc 0.05 city-wide; a park now only helps its radius), cap 0.20; `PowerPenalty` 0.05 so a powerless town stays above 0.5 — the pressure to build a plant is being stuck at level 1. Seeded city (`SimulationTests.RunSeededCity`, plant 600 units at `(0,9)`, optional parks at `(5,14)`/`(14,16)`): no plant → 172 pop all L1, 0.54 happiness, never shrinks; plant → 212 @ d60, 332 @ d90, 600/600 units (second plant needed) by d120; + 2 parks → 0.68; 20% C/I → stalls at 61 without parks, grows to 200 @ d60 with them. The plant costs money below ~100 pop (−$25/day at 92 pop) and pays from ~150.
- Tests: `Tests/EditMode/Simulation/ServicesTests.cs` (power/coverage units) and the seeded-city balance tests in `SimulationTests.cs` (`RunSeededCity(grid, days, plantSupply, parks, jobTax)` → `SeededCity.Run`).

### Ages & research (M11)
- Pure, in the Simulation asmdef (`Scripts/Simulation/Ages/`). `AgeDefinition` (SO: `Id`, `StartYear`, growth rules `MaxLevel`/`CapacityScale`/`UpgradesNeedPower`, entry conditions `TechsToAdvance`/`RequiredTechs`/`PopulationToEnter`/`AdvanceCost`, `StartingTechs`/`StartingMoney`, `ZoneName(zone)`, `YearOnEntering(year)` = `max(year, StartYear)`) → `AgeRules` (`Legacy` = Industrial: level 3, ×1, power). `AgeDatabase` (ordered; index = age number everywhere, so never reorder; `Legacy` = index of Id `industrial`). `TechDefinition` (SO: `Id`, `Age` index, `Cost` RP, `Prerequisites`, `TechEffect[]` = `{Type, Target, Value}`; `DemandMultiplier` targets a zone name or empty = all). `TechDatabase.Validate(ages, errors)` and `AgeDatabase.Validate(errors)` catch broken data (duplicate Ids, cycles, later-age prerequisites, too few techs to advance, required/starting techs that can't be had).
- `TechSystem` (`SimulationSystem.Tech`, **null without databases** — `SimulationSystem(grid, roads, config, ages, techs)`, both or neither): `CurrentAge`, `Rules`, researched set, one `Active` `ResearchProject` + `Queue` (max `ResearchQueueMax`), `Progress` = an RP **pool** toward the active project (switching projects loses nothing, leftovers carry over; with nothing active RP bank up to `ResearchBankDays` × the day's income). Advancing is a `ResearchProject.Advance` (Id `advance:<ageId>`) that can be planned once `GetAdvanceStatus(population).Ready` (techs of the current age ≥ next age's `TechsToAdvance`, its `RequiredTechs`, population); completing it sets `CurrentAge` and raises `AgeAdvanced(index)` — the runtime moves the calendar with `YearOnEntering`. `TechCompleted(tech)`. `CanResearch` (prerequisites researched) vs `CanEnqueue` (prerequisites may be planned ahead); `Remove` drops queued dependents. `StartNew(age)` grants all earlier-age techs + `StartingTechs`; `Restore(age, ids, active, progress, queue)` drops unknown Ids and returns how many. `Modifiers` = `TechModifiers.Fold` over researched techs in database order (deterministic).
- **Research (11a).** Research step runs last in `Tick` (after Economy): `SimulationSystem.ResearchIncome()` = (`ResearchPerCommercialJob` × filled commercial jobs (`CommercialJobs × Employed / Jobs`) + `CityModifiers.ResearchPerDay`) × research multiplier. `GameManager` has `m_AgeDatabase` / `m_TechDatabase` slots (`Ages`/`Techs`, null unless both are set — both are assigned in `Main.unity`) and passes them to the sim, `GrowthVisuals` and save/load/new city.
- **Ages in the sim (11b).** `CapacityModel` (`SimulationSystem.Capacity`) is the only place grown-cell capacity is computed: `round(CapacityForLevel(level) × CapacityScale(builtAge))`, min 1 (scale 1 without ages); `PopulationSystem`, `PowerSystem` (draw), `ServiceStats`, `GrowthSystem` and `SelectionPanel` use it — never call `BalanceConfig.CapacityForLevel` for a cell directly. Their old constructors/signatures still work (optional `CapacityModel`, defaulting to unscaled). Growth stamps new cells with the current age; upgrades cap at `GrowthSystem.MaxLevelFor(cell)` (current age's `MaxLevel`, or the built age's for historic cells) and need power only when the current age's `UpgradesNeedPower`; then a **redevelop pass** rebuilds up to `RedevelopPerDay` outdated cells (`IsOutdated`: built in an older age, not historic) row-major into the current age at the same level (road access needed, not demand-gated, skips cells that upgraded this tick, reserves power headroom for the extra draw in powered ages; `Growth.Redeveloped` lists them). New blockers `AgeMaxLevel`, `Outdated`, `KeptHistoric`. M12 adds `LowLandValue` (see Land value & pollution), M13 `NoWater` / `WaterAtCapacity` (see Water). The Power happiness term is 0 in ages without the power gate (`ServiceStats.Measure(..., countPower)`). `TechModifiers` apply: per-zone demand ×, `HappinessBreakdown.Technology` (+bonus, shown in the happiness tooltip), all upkeep ×, research ×. `AgeDatabase` validation also rejects a falling `MaxLevel` (redevelopment keeps the level). Tests build ages/techs through `internal Init` methods (`AssemblyInfo.cs` → `InternalsVisibleTo("CityBuilder.Simulation.Tests")`). Tests: `Tests/EditMode/Simulation/TechSystemTests.cs`, `AgesSimulationTests.cs` (incl. Industrial start = no-age sim, exactly). The seeded test city lives in `SeededCity.cs` (`Seed`, `Build`/`Run` with optional ages + start age), shared by the simulation, save and age tests.
- **Save v2 (11c):** see Save / load. **Per-age looks (11e):** see Core / Buildings.
- **Content (11d).** `Scriptables/Ages/` (Medieval, Renaissance, Industrial, Modern + `AgeDatabase`) and `Scriptables/Techs/` (36 techs + `TechDatabase`; M13 added Aqueducts (Renaissance, 160 RP) and Waterworks (Industrial, 100 RP, an Industrial starting tech); M14 Town Watch (Medieval, 40), Herbalism (Medieval, 35), Universities (Industrial, 500) and Antibiotics (Modern, 900)), generated by `Tools/gen_age_content.py` from its tables (edit the tables there and re-run; asset GUIDs are deterministic uuid5s of the ids, so references survive regeneration). Unlocks are **`BuildingDefinition.RequiredTech`** (the `UnlockBuilding` tech effect exists but the content doesn't use it; a tech panel lists unlocks by scanning buildings). Both databases are assigned on the scene's `GameManager`, so **the game plays with ages**: the startup city is Industrial (year 1760, all Medieval/Renaissance techs + Electricity researched), New City picks the starting age (11f), v1 saves adopt Industrial. Because the earlier ages' tech modifiers stay researched, an Industrial city is slightly richer than the pre-ages game (seeded 24² city with plant, day 60: 220 pop / happiness 0.65 vs 212 / 0.60) — the no-age sim and the 82 legacy tests are unchanged. Runtime: `GameManager.IsUnlocked(def)` / `RequiredTechName(def)`; `PlacementController` refuses locked defs (select and place; hint "Locked — research X"; `RestoreBuilding`/loads ignore locks); the toolbar hides locked building buttons and re-checks on `GameEvents.TechCompleted` / `CityLoaded`; `CityModifiers.ResearchPerDay` sums placed buildings; `GameEvents.ResearchChanged` (per tick via `RaiseStateEvents`), `TechCompleted(id)`, `AgeChanged(index)`; advancing calls `TimeManager.SetYear(age.YearOnEntering(year))` (day, month and the partial day are kept). Tests: `ContentTests.cs` (both databases validate, every starting age has something to research, the whole tree is reachable from Medieval, building `RequiredTech`s exist).
- **UI (11f).**
  - **Research panel:** `Scripts/UI/TechPanel.cs` on `Prefabs/UI/TechPanel.prefab` (scene child of `UI` right after `TaxPanel`; `m_ToggleButton` = scene ref to `HUD/Budget/ResearchButton`, hidden without ages). Columns from the data: unresearched earlier-age techs (only if any), the current age, the next age; rows by `TechDatabase.DepthInAge` then database order. Tech entries are `ToolButton` instances (`SetBackground` per state: researched / active / queued #n / available / needs X / later age); hover fills the `Detail` line (description, prerequisites, unlocks = buildings whose `RequiredTech` is the tech). Click = `SetActive` (or `Enqueue` if a prerequisite is only planned), Shift (`Keyboard.current.shiftKey`) = `Enqueue`, click a planned tech = `Remove`; failures toast. Status line + progress `UIMeter`, queue, the advancement checklist (techs n/m, required techs, population, RP cost) and the Advance button (`SetActiveAdvance`; shift queues; click again cancels). Rebuilds columns only when the age or the leftover-earlier-tech count changes; refreshes on `ResearchChanged` / `TechCompleted` / `AgeChanged` / `CityLoaded` / `PopulationChanged` at most once a frame while open.
  - `Scripts/UI/SidePanels.cs`: the Taxes and Research panels share the slot under the HUD; opening one raises `SidePanels.Opened` and the other closes.
  - **HUD:** `Age` group after `Time` (age name in gold, `ResearchText` "Tech 34% · 1 RP/day" or a warning-coloured "No research", thin `ResearchMeter`); `Budget` now holds Taxes + Research buttons; widths/spacing tightened to fit 1920. The `Power` group (`m_PowerGroup`) is hidden until `GameManager.PowerUnlocked` (any def with `PowerSupply` unlocked; always true without ages).
  - **New City:** `AgeRow` (4 buttons named from the `AgeDatabase`, current age preselected) + `AgeHint` (year, money, what's researched, power or not, "small maps suit early ages") under the size row; Create → `SaveGameController.NewCity(size, startAge)`.
  - **SelectionPanel:** grown cells add "Built in the *age*", "Historic (kept)" or "Outdated — will be rebuilt in the *age* style", and a `KeepButton` (gold) toggling `GridData.SetHistoric`; Level shows `Growth.MaxLevelFor(cell)`; power lines only when the current age needs power. `PlacementController.SelectCell(cell)` is public (scripts / tests can select).
  - **Age view:** `InfoOverlay.View.Age` (third VIEW button `AgeView`, "Ages"): ground and buildings tinted by built age (orange oldest → blue newest), kept cells gold, outdated cells striped on the ground and darkened on the building. `InfoOverlay.IsAvailable(view)`: Power needs `PowerUnlocked`, Age needs ages; `V` skips unavailable views, the toolbar hides their buttons, and a chosen view that becomes unavailable (e.g. loading a Medieval city) falls back to Off. Stripe sprites come from `GridTilemapView.CreateStripeSprite` / `DestroyStripeSprite` (shared with `ZoneOverlay`).
  - **Toolbar:** the whole BUILDINGS section hides while nothing is unlocked.
  - **Toasts:** research complete (+ "X unlocked", + "pick the next project" when idle), "Ready to advance to the *age*" (once per age, when the checklist is met and the advance isn't planned), "Welcome to the *age*" (+ the power hint when entering a powered age before Electricity), first redevelopment per age (`GameEvents.Redeveloped(count)`, raised by `GameManager` after a tick that rebuilt cells). Power toasts are silent until power is unlocked. Toasts stay up ~30 characters a second (min 2.5 s).
- **Balance (11g).** Tuned with `Tests/EditMode/Simulation/EngagedCity.cs`, an "engaged player" harness over the shipped assets: grows a road grid block by block from the map edge (zoning only the 12 road-facing cells of each 4×4 block, opening land for a zone only once its blocks can't grow or upgrade), keeps research going (advances as soon as allowed; research-building and power techs first) and places Monasteries / Academies / parks / plants as they unlock and are needed, paying for everything and keeping a cash cushion. `AgeBalanceTests` assert, with `CreateInstance` defaults: from Medieval each age takes 45–90 days (86 / 62 / 63, Modern on day 211), never in debt, happiness ≥ 0.55; later starts at least double their population between day 60 and 120; and the **accepted Industrial-start baseline** with the real content (220 pop / 0.647 happiness / +0.05 Technology at day 60 on the seeded 24² city vs 212 / 0.60 without ages — the earlier ages' tech bonuses stay researched; the age-less sim and legacy tests are unchanged). `Report_FromMedievalWithAsset` (`[Explicit]`) is the tuning loop (see `balance-tuning`). Tuned values: `ResearchPerCommercialJob` 0.25 (tuned, M11g), Monastery 3 RP/day, Medieval tech costs 30–90, age entry population 120 / 350 / 650 and advance costs 250 / 1,200 / 3,000 RP (in `Tools/gen_age_content.py`). Growth pace (`MaxGrowthPerDay`) was left alone — it is shared with the age-less game — so the content fits it.

### Land value & pollution (M12)
- **Pollution (12a)**, pure (`Scripts/Simulation/PollutionSystem.cs`, `SimulationSystem.Pollution`): a per-cell float field. Grown **industrial** cells emit `IndustrialPollution` (0.25) × capacity × their built age's `AgeDefinition.PollutionScale` × `TechModifiers.PollutionMultiplier` points (an Industrial level-1 shed = 1, level 3 = 4); placed buildings emit `ServiceSource.Pollution` within `ServiceSource.PollutionRadius` (from `BuildingDefinition.Pollution` / `PollutionRadius`; the Power Plant 6 within 5). Points spread to every cell within Chebyshev distance `d ≤ r` of the footprint as `points × (1 − d / (r + 1))`; an industrial cell's `r` is its built age's `PollutionRadius` (Medieval 1, Renaissance 2, Industrial / Modern 3; 0 = `BalanceConfig.PollutionRadius` 3, which is also the no-age value). Scales: Medieval 0.3, Renaissance 0.5, Industrial 1, Modern 0.6; Renewables ×0.75 (`TechEffectType.PollutionMultiplier`). Lazy like `PowerSystem`: `OnCellChanged`, `SetSources` (via `SimulationSystem.Sources`) and a new `TechModifiers` object mark it dirty; reallocates on `OnResized`. `GetPollution(cell)`, `EmissionOf(cell)` / `EmissionOf(source)`, `RadiusOf(cell)`. Derived, never saved.
- **Pollution happiness:** `ServiceStats.PollutionPenalty` = housing-weighted average over grown homes of `ServiceStats.PollutionPenaltyAt(config, pollution)` = `min(pollution × PollutionPenaltyPerPoint (0.06, tuned M12), PollutionPenaltyCap 0.25)`; `PopulationSystem` uses it × the small-town ramp as the `Pollution` term (the city-wide `PollutionPenalty` field is gone). Tests seed the plant's pollution too (`SeededCity.PlantPollution`, EngagedCity reads the building assets). Industrial start with the real content re-recorded: 220 pop / 0.660 happiness at day 60 (was 0.647). Tests: `Tests/EditMode/Simulation/PollutionTests.cs`.
- **Land value (12b)**, pure (`Scripts/Simulation/LandValueSystem.cs`, `SimulationSystem.LandValue`): per cell 0..1 = `LandValueBase` 0.5 + services in range `min(coverage × LandValuePerService 0.10, LandValueServiceCap 0.20)` + heritage `min(kept historic grown cells within HeritageRadius 3 (the cell itself included) × HeritageLandValueEach 0.05, HeritageLandValueCap 0.20)` + `TechModifiers.LandValueBonus` (`TechEffectType.LandValueBonus`; Architecture +0.05) − `pollution × LandValuePerPollution` 0.04, clamped. Only the heritage counts are cached (lazy, `OnCellChanged` / `OnResized`); coverage, pollution and tech are read live. `GetLandValue`, `Explain(cell)` → `LandValueBreakdown {Base, Services, Heritage, Technology, Pollution, Total}` for the UI, `HeritageCount(cell)`, `AllowsLevel(cell, level)`.
- **Level-3 gate:** residential and commercial cells need `LandValueForLevel3` (0.40) to upgrade 2 → 3 (`GrowthSystem` skips them before reserving power; `GrowthBlocker.LowLandValue`, checked after road access and before power). Industry is exempt, level 3 never drops when the value falls, redevelopment (same level) isn't gated, and Medieval (max level 2) never meets it. `GrowthSystem`'s optional `landValue` constructor argument (null = no gate) keeps the old unit-test constructors working.
- **Heritage happiness:** `ServiceStats.HeritageBonus` = housing-weighted average of `ServiceStats.HeritageBonusAt(config, HeritageCount(home))` = `min(count × HeritageHappinessEach 0.02, HeritageHappinessCap 0.06)` → `HappinessBreakdown.Heritage` (not ramped). 0 without kept blocks, so the seeded-city numbers don't move. Tests: `Tests/EditMode/Simulation/LandValueTests.cs`.
- **UI (12c).** `InfoOverlay.View.Pollution` ("Pollution" VIEW button): ground haze bucketed by `pollution / m_PollutionFull` (6 points = full), polluters (`EmissionOf > 0`) dark, other buildings shaded clean grey → purple. `View.LandValue` ("Value"): ground and homes / shops coloured red → yellow → green **centred on `LandValueForLevel3`** (yellow at the threshold; land value buckets round down so a cell just below it never reads as passing), industry dark grey (not gated), homes / shops held by the gate (`GrowthSystem.IsHeldByLandValue`) striped and darkened. Both always available. `SelectionPanel`: grown cells and zoned land show "Pollutes x within r cells" (industry), "Pollution here x (−y% happiness)", "Land value v% (level 3 needs 40%)" + a grey breakdown line (base, parks, heritage, tech, pollution), and a Heritage line on kept blocks; placed polluters show their emission; `LowLandValue` blocker text. Happiness tooltip: "Pollution (near homes)", "Heritage (kept blocks)". `NotificationController`: one toast per city the first time any home / shop is held by land value (checked on `PopulationChanged` while not yet shown; `SyncCityState` re-seeds the flag).
- **Balance (12d).** `EngagedCity` puts a park in the free 2×2 middle of any block with a home or shop held by land value (one a day, paid); `AgeBalanceTests` targets hold without retuning (86 / 62 / 63 days from Medieval). Full 96² tick 0.76 ms in EditMode (pollution + heritage recompute 0.38 ms).

### Traffic (M16)
- `Scripts/Simulation/TrafficSystem.cs` (pure, `SimulationSystem.Traffic`; derived, never saved). `Update(occupancy, employment, trafficMultiplier)` runs **at the end of `Tick`** (after Step / economy / research) and at the end of `Restore`, so a restored city holds exactly the flow the uninterrupted one held (the plan put it before `MeasureServices`; Measure now reads the previous tick's flow, one day stale, which is invisible and keeps save → load → continue identical). One pass: a snapshot of the road tiers; **sinks** = road cells with frontage beside a grown commercial / industrial cell (distance 0) plus map-edge roads at `OutsideTripCost` 40; a multi-source **Dial's algorithm** (circular bucket queue, cost per cell = its tier's `TravelCost`, seeds row-major, neighbours in `Neighbors4` order +x −x +y −y, strict improvement keeps the first parent, so ties are deterministic) gives each road its distance and a parent step; each grown home (capacity × occupancy × employment × `TripsPerWorker` × `TechModifiers.TrafficMultiplier` trips, occupancy = Population / Housing capped at 1, employment = Employed / Workers) loads its frontage road with the lowest distance; loads accumulate down the tree in decreasing distance, then the **worst load / capacity on the way to the sink** flows back up. API: `Load / Capacity / Congestion(cell)` (road cells), `CommuteCongestion(home)`, `LocalCongestion(cell)` (worst of the 4 neighbouring roads), `HomeTrips`, `Trips`, `JammedRoads` (load > capacity), `WorstCongestion`, static `TrafficPenaltyAt` / `LandValueLossAt`. Reallocates on `OnResized`. No topology cache (the flow is one O(cells) pass: ~0.5 ms for a 96² city in the Editor's Mono, to be re-measured in a player build in 16d).
- **Happiness:** `ServiceStats.TrafficPenalty` = housing-weighted average over grown homes of `min(max(0, commute − CongestionFree 0.8) × TrafficPenalty 0.10, TrafficPenaltyCap 0.08)`, not ramped → `HappinessBreakdown.Traffic` (negative, in `Total`). **Land value:** `LandValueBreakdown.Traffic` = −`min(max(0, local − 0.8) × LandValuePerCongestion 0.10, LandValueCongestionCap 0.10)` from the roads beside the cell (so a jam can hold a block under `LandValueForLevel3`). `BalanceConfig` "Traffic (M16)": `TripsPerWorker`, `OutsideTripCost`, `CongestionFree`, `TrafficPenalty(Cap)`, `LandValuePerCongestion`, `LandValueCongestionCap` (+ `RoadCapacity` 160, `RoadTravelCost` 4 for the legacy Paved tier), mirrored in the asset.
- **Measured (16b probe, default tiers = Paved everywhere):** the seeded 24² city peaks at 0.77 of capacity (day 90, pop 332) and never jams, so every earlier baseline is unchanged; the engaged city from Medieval peaks at 0.92 around the Modern entry (pop ~770) with 0 jammed roads, so at Paved capacity jams only appear once Medieval / Renaissance cities lay Dirt (60) / Cobble (100) roads (16d) or in bigger cities.
- **Traffic UI (M16c; checked in Play mode and by the UI-only play-through).** `PlacementController`: `SelectRoad(tier = 0)` (0 = street tool = `BestStreetTier`; Avenue / Highway need their tech), `RoadToolTier`, `ActiveRoadTier`; the Road mode now **paints while the button is held**: laying on empty land pays `Cost(tier)`, dragging a better tier over a road upgrades it for `UpgradeCost`, the same or a better tier is skipped; one insufficient-funds toast per drag. Hints: "Paved Road $50", "Upgrade to Avenue $100", "Already Avenue", "Highway $400 — no access for blocks beside it". `B` always selects the street tool. `RoadTilemapView`: a 16-sprite set per tier generated at runtime (dirt brown with soft edges and no markings, cobble with stone joints, paved = the original look, avenue = darker asphalt with a double yellow line, highway = near-black with white edge lines and two white lane-dash lines); tiles connect to any tier. `ToolbarController`: the Road button's label / cost / tooltip follow the best street tier (refreshed in `RefreshUnlocked`, so on `TechCompleted` / `AgeChanged` / `CityLoaded`); `Road_avenue` and `Road_highway` buttons are runtime copies of it, hidden until their tech; a `TrafficView` VIEW button is a copy of the Value button. `InfoOverlay.View.Traffic` (appended to the enum; `V` cycles it after Value; the Avenue / Highway tools show it, the street tool doesn't; repainted on `DateChanged`): roads green → yellow (at `CongestionFree`) → red (at 150% load) from `TrafficSystem.Congestion`, homes recoloured by `CommuteCongestion`, other buildings grey. `SelectionPanel`: road = tier name, trips / capacity and %, "Jammed — upgrade it", the highway no-frontage note; home = a Commute line with its happiness cost; the land-value parts list a traffic term and `LandValueFixes` says "upgrade the jammed road beside it"; a `NoRoadAccess` cell beside a highway says "highways give no access — lay a street beside it". `HappinessTooltip`: "Traffic (commutes)". `NotificationController`: one "Roads are jamming" toast per city once the Traffic term passes −1%; research-complete names the unlocked road tier ("Avenue road").
- Tests: `Tests/EditMode/Simulation/TrafficTests.cs` (straight-street loads, trips formula, edge fallback, a job beats the edge, a faster avenue draws the flow, deterministic ties, highway carries but has no frontage, commute = worst on the path, penalty / land-value formulas, housing-weighted term, land value from neighbouring roads, resize, save → load → continue, tech multipliers).

### Disasters & events (M17)
- **17a (pure, `Scripts/Simulation/Hazards/`).** `SimRandom` = the sim's only randomness (xorshift64* seeded through SplitMix64, state one `ulong` saved as 16 hex digits, `NextFloat` / `NextInt` / `Chance` always draw once). `DisasterSystem` (`SimulationSystem.Disasters`): `Enabled` (the New City switch, never true without ages), `Random`, per-cell `Fires` / `Rubble` / `Plague` byte layers (reallocated empty on `OnResized`), `PlagueCooldown`, `Broken`, the pending event and its timers, `Export(SaveData)` / `Restore(SaveData)`; `Step()` runs in `Tick` after research and before the traffic flow and does nothing yet (17b-17d). `TechEffectType.HazardMultiplier` (Target `FireSpread` / `PlagueSpread` / `Breakdown`) folds into `TechModifiers.Hazard(HazardKind)`; `AgeDefinition.PlagueRisk` (generator column); `ServiceSource.Cost` / `TechAge`. Plan and decisions: `Docs/milestones/M17.md`. Tests: `DisasterTests.cs`.
- **Fire (17b, `Hazards/FireSystem.cs`).** Layers: `DisasterSystem.Fires` (0 = not burning, else days burnt, 1..`FireBurnDays`) and `Rubble` (days left). Per day, with a fixed draw order: rubble decays; the fires burning at dawn are snapshotted (a fire lit today starts acting tomorrow); **ignition** — `1 − exp(−FireIgnitionPerRisk × Σ CivicSystem.GrownFireRisk)` (one chance draw, then one pick draw in proportion to risk; nothing while the civic ramp is 0), at most one new fire a day; then each burning unit (a grown block, or a placed building's whole footprint, in row-major order): **extinguish** `min(0.95, FireExtinguishBase + FireExtinguishPerCover × fire cover)` × `DryExtinguishFactor` where the age has a water rule and the block has no water; **spread** to the four neighbours (grown or placed; roads are firebreaks, but a one-cell road is jumped at `FireJumpFactor`) with `FireSpreadChance × flammability × (1 − target's fire cover) × TechModifiers.Hazard(FireSpread)`, where flammability = built age's fire risk (× 1.5 industry) / `BalanceConfig.FireRisk`, placed buildings `PlacedFlammability` × the current age's; then it burns a day longer, or down at more than `FireBurnDays`. A burnt grown block drops to level 0 (the zone stays) with `RubbleDays` of rubble (`GrowthBlocker.Rubble`, `DisasterSystem.IsRubble`); a burnt placed building's cells are released from the grid with rubble, its occupant id is in `FireSystem.DestroyedBuildings`, `SimulationSystem` drops its source at once and raises `BuildingsDestroyed(ids)` (the runtime removes the object, no refund). Results of the last step: `Ignitions`, `Extinguished`, `LostBlocks`, `LostBuildings`. API for tests / DEBUG: `Ignite(cell)`, `IsBurning`, `FireDays`, `BurningCount`, `CanBurn`. Tests: `FireTests.cs`.
- **Plague and breakdowns (17c).** `PlagueSystem` (`DisasterSystem.Epidemic`; layer `Plague`: 0 healthy, 1..`PlagueDays` days ill, 255 immune): with no outbreak and no cooldown, in a town of `PlagueMinPopulation` (120)+ with `AgeDefinition.PlagueRisk` > 0 (Medieval 1, Renaissance 0.6, later 0), an outbreak starts with `PlagueOutbreakPerDay` (0.06) × risk × the homes' mean sickness (`CivicSystem.HomeSickness`); patient zero by capacity × sickness; each ill home infects healthy homes within `PlagueRadius` 2 with `PlagueSpread` 0.12 × their sickness × the PlagueSpread multiplier; ill for `PlagueDays` 10, then immune; ill homes lose `PlagueDeathRate` (1%) of their residents a day (remainder carried, `PopulationSystem.LoseResidents`); when nobody is ill the outbreak ends and `PlagueCooldownDays` 180 start; `HappinessBreakdown.Plague` = −min(ill share of housing × `PlaguePenalty` 0.5, cap 0.15). `BreakdownSystem` (`Breakdowns`): any placed source with a power or water supply; per day at most one breaks, lambda = Σ `BreakdownPerDay` (0.006) × (1 + 0.5 × ages its tech is behind) / funding² × the Breakdown multiplier, picked by weight in row-major order; a broken source feeds 0 for `BreakdownDays` 10 (`DisasterSystem.Broken`, `ApplySources`), repair = `SimulationSystem.Repair(origin)` for `RepairCostFraction` 0.2 of its cost. Tests: `PlagueBreakdownTests.cs`.
- **Events (17d).** `EventDefinition` (SO in `TechDatabase.Events`: Id, Title, Text, `MinAge`..`MaxAge`, `MinPopulation`, optional `RequiredTech`, `Weight`, 2-3 `EventChoice`s — label, description, `Cost` + `CostPerResident`, `Reward`, `ResearchPoints`, lasting `Effects` for `Days`; the last choice is free; validated by `TechDatabase.Validate`). `RandomEventSystem` (`DisasterSystem.Events`; state `PendingEvent` / `PendingDays` / `DaysToNextEvent` (0 = unscheduled) / `ActiveEvents` / `RecentEvents`): every `EventIntervalMin..Max` (45-90) days after the last answer one draw schedules, then a weighted draw offers an eligible event (age in range, population, tech researched, not offered in the last `EventRepeatDays` 360); `Choose(i)` charges, pays the reward and research points and adds the running effect (`TechSystem.SetEventEffects` -> `TechModifiers`, happiness as `EventHappiness` -> `HappinessBreakdown.Events`); unanswered for `EventAutoDays` (10) it takes the last choice; `OfferNow(id)` for tests / DEBUG; `Prune` / `RefreshEffects` on load. Content: 13 events and the `Quarantine` ordinance, `HazardMultiplier` effects on Steel Frames / Smart Grid / Building Code. Tests: `EventTests.cs`, `ContentTests`.
- **UI (17e).** All runtime-built. `DisasterController` (`Core/`) removes burnt placed buildings (`PlacementController.RemoveBurnt`, no refund) on `SimulationSystem.BuildingsDestroyed`, toasts fire / plague / breakdown / event results (keyed on `DisasterSystem.Steps`) and creates `HazardView` (a `Hazards` tilemap beside Roads: rubble, plague wash, fire glow), `FireVisuals` (pooled flame and smoke cubes on two shared materials) and `EventPopup` (pausing popup, choices with price and effect, speed restored). New City dialog toggle (default on); the startup city is on. `SelectionPanel`: On fire, Broken down + Repair, plague lines, Rubble panel + Clear rubble, `GrowthBlocker.Rubble` text; building / painting / demolishing on rubble clears it. `HappinessTooltip`: Plague, Events. DEBUG panel: Fire at selection, Start plague, Break a plant, Offer event.

### Water (M13)
- Pure, in the Simulation asmdef. `WaterRule` (`Scripts/Simulation/Ages/WaterRule.cs`) = `None / Coverage / Piped`, per age as `AgeDefinition.Water` → `AgeRules.Water` (replaced the unused `UpgradesNeedWater` bool; `AgeRules.Legacy` = `Piped`, so the age-less sim needs piped water like the Industrial age). Generated by `Tools/gen_age_content.py` (`m_Water`; all shipped ages `None` until 13b).
- `ServiceSource.WaterSupply` (units into the piped network) / `WaterRadius` (cells a well or fountain waters, Chebyshev from the footprint).
- `WaterSystem` (`SimulationSystem.Water`): mode read live from `Rules.Water` (so advancing switches it with no recompute). `Coverage` = a `CoverageSystem` over `WaterRadius` (`CoverageSystem` takes a radius selector; parks keep `CoverageRadius`); `Network` = `WaterNetwork : UtilityNetwork` (`SupplyOf` = `WaterSupply`, roads carry water, a grown cell draws `DrawFor(capacity)` = `max(1, round(capacity × WaterPerCapacity 1.0))`). `HasWater(cell)` (coverage: in reach; piped: fed; `None`: always), `HasHeadroom` / `TryReserve(cell, fromCapacity, toCapacity)` (piped: network headroom for the extra draw; otherwise = `HasWater`), `DryCells`. Reallocates on `OnResized`, keeping its sources. Derived, never saved.
- Growth: upgrades past level 1 and redevelopment need water in any age whose rule isn't `None`, checked after power (`GrowthBlocker.NoWater` / `WaterAtCapacity`). `GrowthSystem.TryReserveUtilities` checks power and water headroom first and then reserves both, so a cell short of water never holds power. `GrowthSystem`'s optional `water` argument (null = no gate) keeps the old constructors working.
- Happiness: `ServiceStats.UnwateredHousingShare` (homes without water, housing-weighted; 0 when the rule is `None`) → `HappinessBreakdown.Water` = `−WaterPenalty (0.05) × share`, ramped in with `SmallTownGracePopulation` like Power.
- `BalanceConfig` (M13): `WaterPerCapacity` 1, `WaterPenalty` 0.05.
- **Pipes (13d).** `GridData.IsPipe` / `SetPipe` (never on a road; `SetRoad(true)` clears the pipe) / `CountPipes` / `ExportPipes`, `Import(..., pipes)`; saved (v3). `WaterNetwork.Carries` = road or pipe, and `UtilityNetwork` feeds a carrying grown cell itself (a pipe under a home), so pipes link a tower or pump off the road network, join road networks (they pool supply) and reach blocks behind the street; pipes don't carry power. Upkeep `CountPipes × PipeUpkeepPerDay` (0.02) in the daily expense; `PipeCost` 5. `PipeTilemapView` (`Grid/Pipes` Tilemap, sorting order 3 above Info) auto-tiles runtime pipe sprites towards neighbouring pipes and roads, pale where `IsCarrying`, grey where not, and is only enabled in the Water view or with the pipe tool (which switches to the Water view). `SelectionPanel` mentions a pipe under a grown or zoned cell.
- Content (13b): Medieval / Renaissance `Coverage`, Industrial / Modern `Piped` (`Tools/gen_age_content.py`, last column of `AGES`); buildings and techs: see Core / Buildings and Ages & research. `EngagedCity` digs a well in the middle of any block with a dry zoned cell (up to two a day), builds towers / pumps like plants (the cheapest affordable one covering the shortfall + 75), saves for utilities while power or water is short, and puts a fountain in a free middle cell when a well took a block's park spot.
- **UI (13c).** `WaterSystem.Status` → `WaterStatus {Mode, Supply, Demand, DryCells, GrownCells, WateredShare}`; `GameManager` raises `GameEvents.WaterChanged(status)` beside `PowerChanged` (once a frame when dirty; also after an age change). `GameManager.WaterUnlocked` (no age data, an age with a water rule, or a buildable water building) gates the HUD line, the view and the toasts. HUD: see UI. `InfoOverlay.View.Water` ("Water" VIEW button, after Power in the `V` cycle): well ages tint the land in a well's reach (zoned land out of reach faint red), piped ages tint roads carrying water like the power grid; grown buildings blue (water) / red (dry); a well / fountain (well ages) or tower / pump tool switches to it and previews the newly watered cells in cyan (`m_PreviewWater`). `SelectionPanel`: wells / fountains ("Waters blocks within r cells", or "no longer counts" + "Obsolete in the Industrial Age"), towers / pumps (supply, city water, dry count, "Not beside a road"), "Carries water" on roads, "Water from a well" / "No well nearby" / "Water" / "No water" on grown cells, availability on zoned land, `NoWater` / `WaterAtCapacity` blocker texts. Happiness tooltip: "No water". `NotificationController`: tower online, water lost, shortage (piped), a one-time "dig a Well" nudge from `m_WellNudgePopulation` (10) residents with dry buildings in the well ages and a "build a Water Tower" nudge at `SmallTownGracePopulation` in the piped ages, and the Industrial welcome toast's "water now comes from towers" hint.
- **Performance (13e, reworked after M13).** `UtilityNetwork` keeps each cell's draw and carrying flag up to date in its `OnCellChanged` handler and splits the lazy recompute in two: the **topology** (networks, the sources' shares and the BFS feed order, recorded as a flat list of (cell, network) feed attempts) is rebuilt only when a carrying flag or the sources change; any other change (levels, zones, built ages — every growth day) only **replays the attempts** over int arrays, which serves exactly the cells a full BFS would (`WaterTests.IncrementalUpdates_MatchFreshNetworks` checks this against fresh networks after random edits). Flat indices and an int queue replace `Vector2Int` / `Queue`. Full 96² water city in EditMode: topology rebuild 0.15 ms (power) / 0.20 ms (water), a replay 0.02–0.05 ms, tick 1.10 ms (1.72 ms on an upgrade day; the rest is mostly the pollution recompute, 0.33 ms, and capacity recounts). Player benchmark: sim tick **1.56 ms** (was 3.52), edit-every-frame 2.0–2.4 ms (was 3.8–4.5), frames unchanged. `CapacityModel` keeps the per-age scales / max levels in arrays.
- Tests: `Tests/EditMode/Simulation/WaterTests.cs` (coverage reach, the piped network's BFS order / pooling / supply, `DrawFor`, resize, the age-less gate with headroom, power checked before water, the both-or-neither reservation, Medieval wells, advancing into a piped age, the Water term). `SeededCity`'s plant block also supplies water (`WaterSupply` 600 piped + a map-wide `WaterRadius`; without a plant a 1×1 source at `(0,11)`; `waterSupply: 0` = dry); `TestAges` uses Coverage for Medieval / Renaissance and Piped for Industrial / Modern.

### Civic services (M14)
- Pure, in the Simulation asmdef. `ServiceKind` (`None, Order, Fire, Health, Education`; stored by value in building assets — append only). `ServiceSource.CivicKind` / `CivicRadius` / `CivicStrength` (optional trailing constructor args); civic reach is separate from `CoverageRadius`, which stays the park reach (the Fountain is a park), so park coverage, the Services term and land value's services line are untouched.
- `CivicCoverage` (`SimulationSystem.Civic.Cover`): one `float[]` per kind = the **best** `CivicStrength` in reach (Chebyshev from the footprint; no stacking, a stronger building overrides). Recomputed when `Sources` is set, reallocated (keeping its sources) on `OnResized`.
- `CivicSystem` (`SimulationSystem.Civic`): `Ramp` = `RampAt(config, population)` = 0 up to `CivicFreePopulation` (100), 1 from `CivicFullPopulation` (700), linear between (the sim's current population). **Crime (14a)** on grown residential / commercial cells = `min(1, capacity × CrimePerCapacity 0.04)` × ramp × (1 − order cover); industry and empty land have none (an Industrial level-1 home 0.16, level 3 0.64 at full ramp). Computed on read; `GetCrime(cell)`, `Explain(cell)` → `CivicBreakdown {Ramp, CrimePotential, Order, Crime}`.
- Happiness: `ServiceStats.CrimePenalty` = housing-weighted average of `ServiceStats.CrimePenaltyAt(config, crime)` = `min(crime × CrimePenalty 0.15, CrimePenaltyCap 0.10)` → `HappinessBreakdown.Crime` (already ramped, so not multiplied by the `SmallTownGracePopulation` ramp). `Measure(..., civic)`; null = no crime term.
- Land value: `− crime × LandValuePerCrime` (0.15) → `LandValueBreakdown.Crime`; so a dense level-2 home or shop with some pollution can be held by the level-3 gate until order cover reaches it (`LandValueSystem`'s optional `civic` argument; null = no crime line).
- `BalanceConfig` (M14): `CivicFreePopulation` 100, `CivicFullPopulation` 700, `CrimePerCapacity` 0.04, `CrimePenalty` 0.15, `CrimePenaltyCap` 0.10, `LandValuePerCrime` 0.15. Derived, never saved (save stays v3).
- **Fire risk and sickness (14b).** Fire risk on every grown block = its built age's `AgeDefinition.FireRisk` (Medieval 0.6, Renaissance 0.45, Industrial 0.35, Modern 0.2; 0 or no ages = `BalanceConfig.FireRisk` 0.35; cached per age index) × `FireRiskIndustrialFactor` 1.5 for industry × ramp × (1 − fire cover). Sickness on grown homes = ramp × (1 − health cover). `GetFireRisk` / `GetSickness`; `CivicBreakdown` also carries `FireRiskBase`, `Fire`, `FireRisk`, `Health`, `Sickness`, `Education`. Happiness: `ServiceStats.FirePenalty` (per home `min(risk × FirePenalty 0.10, FirePenaltyCap 0.06)`) → `HappinessBreakdown.Fire`; `HealthPenalty` (per home `sickness × HealthPenalty 0.08`) → `HappinessBreakdown.Health`; both already ramped. M17 will start fires and plague from the same fields.
- **Education (14b).** `ServiceStats.EducatedShare` = housing-weighted education cover at homes; `SimulationSystem.ResearchBreakdown()` → `ResearchBreakdown {Commercial, Buildings, Education, Multiplier, Total}` with Education = population × that share (measured in the tick's `MeasureServices`, kept by the sim, re-measured by `Restore`) × `ResearchPerEducatedResident` 0.01; `ResearchIncome()` = its `Total`. 0 without ages.
- **Content (14b).** Lines and tiers (age = the `RequiredTech`'s age): order Watch House (Town Watch; 1×1, $400, $2/day, reach 4, 0.5) → Constabulary (Civic Planning; 2×1, $1,500, $6, 6, 0.75) → Police Station (Telegraph; 2×2, $4,000, $30, 8, 1.0); fire Bucket Brigade (Town Watch; 1×1, $300, $1, 3, 0.5) → Fire Engine House (Watermills; 2×1, $1,200, $5, 5, 0.75) → Fire Station (Steam Power; 2×2, $4,000, $30, 8, 1.0); health Apothecary (Herbalism; 1×1, $500, $2, 4, 0.5) → Hospital (Public Sanitation; 3×2, $4,500, $35, 9, 0.85; tuned 14d) → Medical Centre (Antibiotics; 3×3, $10,000, $70, 11, 1.0); education Monastery (reach 5, 0.4, 3 RP) → Academy (6, 0.6, 5 RP) → University (Universities; 3×3, $7,500, $40, 8, 0.85, 8 RP) → Research Lab (Computing; 2×2, $10,000, $50, 8, 1.0, 12 RP). All Service category. Generated by `Tools/gen_civic_content.py` (buildings, placeholder prefabs from primitives under the prefab contract, the shared line materials `Art/CivicOrder|Fire|Health|Education.mat`, the database entries and the Monastery / Academy reach; edit its tables and re-run).
- **Outdated tiers (14b).** `GameManager.ReplacementFor(def)` = the latest-age civic building of the same line that is unlocked and not obsolete, when newer than `def`; `IsOutdated(def)`; `CanBuild` excludes outdated defs, so the toolbar hides them (it refreshes on `TechCompleted`) and placement refuses them (hint "Outdated — build the X"). Placed copies keep working; `SelectionPanel` shows the line, reach and strength and "Outdated — replace with the X (strength, reach)". (M13's `ObsoleteAge` stays for wells.)
- **Balance (14a / 14b).** Without civic buildings (the seeded city has none) happiness at day 60 / 90 / 120 is 0.589 / 0.556 / 0.504 (pre-M14 0.616 / 0.618 / 0.586; crime −0.027, fire −0.017, sickness −0.038 at day 120); population and money unchanged. Industrial-start baseline 220 pop / 0.631. `EngagedCity` (see 14b's §12 note for its civic rules): from Medieval 84 / 61 / 73 days (Modern on day 218), min happiness 0.565; later starts 447 / 584 / 763 pop at day 120.
- **Balance (14d).** `EngagedCity`'s civic rules (header comment; §12 14d note): costliest line first, build on cash for a line costing ≥ 0.03 when 60 days of the deficit are covered, save for it otherwise, big buildings take reserve / next blocks, outdated buildings demolished once covered. Hospital tuned to $4,500 / $35. From Medieval 84 / 61 / 73 days (min happiness 0.57); Renaissance / Industrial / Modern starts 447 / 557 / 630 pop at day 120, happiness 0.71 / 0.72 / 0.83.
- **Performance (14d).** The tick reads civic needs on every grown home (`ServiceStats.Measure`) and crime on every land-value read, so both have fast paths: `CivicSystem.HomeNeeds(cell, capacity, ramp, out …)` (the capacity `Measure` already has, the ramp once per measure, unchecked flat-index `CivicCoverage.StrengthAt`) and a crime-only `GetCrime` that skips industry and empty land before the capacity lookup. Same numbers as `Explain` (`CivicTests.FastPaths_MatchExplain`). Full 96² Modern city in EditMode: tick 1.57 ms (1.83 before), civic share of `Measure` 0.14 ms, cover recompute 0.02 ms. `PerfBenchmark` adds sim-only civic cover of every line.
- **UI (14c).** `InfoOverlay.View.Order / Fire / Health / Education` (after `Age` in the `V` cycle; `IsAvailable` = `GameManager.CivicUnlocked(kind)`, a building of the line unlocked; `InfoOverlay.CivicViews`, `KindOf`, `ViewOf`, `IsCivic`): ground = cover strength in the line's colour (`m_OrderCover` / `m_FireCover` / `m_HealthCover` / `m_EducationCover`), striped `m_CivicUncovered` on served cells that need the line but get no cover; buildings `m_NeedLow` → `m_NeedHigh` by crime / fire risk / sickness (full at `m_CrimeFull` 0.5, `m_FireRiskFull` 0.6, `m_SicknessFull` 1), schooled homes grey → purple, unserved cells dark grey. A civic tool switches to its line's view and previews newly covered cells (preview `CivicCoverage`). `ToolbarController` creates the **Services** VIEW button at runtime as a copy of the Age view button (placed after it): click = next available civic view, then Off; label Services / Crime / Fire / Health / Schools; hidden while no line is unlocked. `SelectionPanel`: grown cells list crime (homes and shops), fire risk, health care, schooling and each one's happiness cost (a small-town note below `CivicFreePopulation`), the land-value parts include crime, civic buildings show line / reach / strength / cells reached / "Outdated — replace with X". `HappinessTooltip`: Crime, Fire risk, Sickness. The low-land-value hint lists fixes from the breakdown (`SelectionPanel.LandValueFixes`, 14d): add parks, keep order when crime costs land value, move industry away when pollution does. `TechPanel`: RP split (`SimulationSystem.ResearchBreakdown`). `NotificationController`: one toast per city and line when its term passes −2% while `GameManager.BestCivic(kind)` exists (flags re-seeded in `SyncCityState`), and the research-complete toast names placed buildings the new tier outdates (`GameManager.SourceBuildings`, `ReplacementFor`).
- Tests: `Tests/EditMode/Simulation/CivicTests.cs` (best-not-sum cover and reach, the ramp, crime by capacity / ramp / order, none for industry or small towns, the housing-weighted capped term, crime holding a polluted level-2 home until policed, civic buildings aren't parks, resize, the seeded city's small crime term; 14b: fire risk by built age / industry / cover, sickness at homes only, the fire and health penalties, education research through the multiplier and none without ages); `ContentTests.CivicLines_HaveOneBuildingPerPlannedAge_GettingStronger`; 14d: `CivicTests.FastPaths_MatchExplain`.

### Budget (M15)
- Pure, in the Simulation asmdef. `BudgetLine` (`Parks, Power, Water, Order, Fire, Health, Education`; saved by value from 15b: append only). `BudgetSystem` (`SimulationSystem.Budget`): funding per line, 50-150% in 10% steps (`BalanceConfig.FundingMin / Max / Step`), default 100% = identity. `SetFunding` clamps, snaps and raises `Changed`; `IsDefault`; `Restore` / `ExportFunding`. `EffectFactorAt` = f up to 100%, `1 + FundingOverSlope (0.5) x (f - 1)` above (150% = x1.25); `ReachFactorAt` = `1 + FundingReachSlope (0.5) x (effect - 1)` (50% = x0.75, 150% = x1.125).
- **Funded sources (15a).** `SimulationSystem.Sources` keeps the placed list; `ApplySources()` feeds Power, Water, Coverage, Pollution and Civic `Budget.FundAll(placed)` (the placed list itself while every line is 100%, so default numbers are bit-identical) and runs again on `Budget.Changed`. `BudgetSystem.Fund(source)` scales each effect by its own line: park reach (`CoverageRadius`) -> Parks, plant supply -> Power, tower supply and well / fountain reach (`WaterSupply`, `WaterRadius`) -> Water, civic reach and strength (strength capped at 1) -> the civic line; reach = `max(1, round(r x ReachFactor))`, supply = `max(1, round(s x EffectFactor))`; pollution is not funded. Park happiness and land value: `ServiceStats.Measure(..., parkFactor)` and `LandValueSystem`'s `parkFactor` multiply `ServiceBonusEach` / `LandValuePerService` by `EffectFactor(Parks)` (caps unchanged).
- **Upkeep and research.** `ServiceSource` gains optional trailing `UpkeepPerDay` and `ResearchPerDay` (the building's 100% values; `GameManager.RebuildSources` fills them). `CityModifiers.UpkeepPerDay` stays the 100% total; `SimulationSystem.Ledger()` adds `sum(upkeep x (funding - 1))` for sources on a line (`BudgetSystem.LineOf`: civic > power > water > parks, so the Fountain's upkeep is on Water while its cheer follows Parks) and returns a `BudgetBreakdown {IncomeResidential / Commercial / Industrial, UpkeepByLine[], OtherUpkeep, Roads, Pipes, TechUpkeepMultiplier, Expense}`; `Tick` charges exactly its `Income` / `Expense` (the plan called the method `Budget()`, renamed because `Budget` is the system). School RP: `ResearchBreakdown.Buildings` adds `BudgetSystem.ResearchDelta` = `sum(ResearchPerDay x (EffectFactor(Education) - 1))`.
- **Loans (15b).** `BudgetSystem.Borrow(amount)` opens a `Loan {Amount, DailyPayment, DaysLeft}`: flat `LoanInterest` (0.10, x the techs' loan multiplier from 15c) over `LoanTermDays` (360), so `DailyPayment = amount x 1.1 / 360`; at most `MaxLoans` (2). `SimulationSystem.LoanOffer()` = the age's `AgeDefinition.LoanAmount` (Medieval $10,000, Renaissance $15,000, Industrial $25,000, Modern $40,000; generator column) or `BalanceConfig.LoanAmount` ($25,000) without ages; `TakeLoan()` credits the principal at once (allowed in debt); `RepayLoan(i)` spends the remaining principal `amount x daysLeft / term` (the unpaid interest is waived) and fails without the money. Payments are `BudgetBreakdown.Loans` (added after the tech upkeep multiplier, so techs do not discount them) and `Tick` steps the loans after charging the day (the last payment is on the day `DaysLeft` is 1).
- **Ordinances (15c).** `OrdinanceDefinition` (Simulation asmdef ScriptableObject: Id, DisplayName, Description, `RequiredTech`, `CostPerDay`, `CostPerResident`, `Effects` = `TechEffect` list; `Age` = its tech's age, `DailyCost(population)`) listed in `TechDatabase.Ordinances` (`GetOrdinanceById`; `Validate` checks unique ids, a tech that is in the database, non-negative costs). `TechSystem`: `IsUnlocked` (tech researched), `IsEnacted`, `Enact` / `Repeal` (no fee; false when locked / already so), `EnactedOrdinances` (database order), `OrdinanceCostPerDay(population)`, `RestoreOrdinances(ids)` (load: unknown or locked ids dropped and counted); `StartNew` / `Restore` clear them and the folded `TechModifiers` include the enacted effects. New `TechEffectType`s (append only): `CivicNeedMultiplier` (Target `Order` / `Fire` / `Health`: crime / fire risk / sickness x Value; `TechModifiers.CivicNeed(kind)`) and `LoanInterestMultiplier` (Banking 0.5). An ordinance's `HappinessBonus` goes to `TechModifiers.OrdinanceHappiness` -> `HappinessBreakdown.Ordinances` (a separate term, so `Happiness.Technology` is untouched); the daily cost is `BudgetBreakdown.Ordinances` (not scaled by the upkeep multiplier). `CivicSystem` (new optional `tech` getter) multiplies crime, fire risk and sickness by the multipliers in `Explain`, `GetCrime` and `HomeNeeds`, re-reading them only when the `TechModifiers` object changes.
- **Ordinance content (15c)**, generated by `Tools/gen_age_content.py` (`ORDINANCES`; assets under `Scriptables/Techs/Ordinances/`; the unlocking tech's description says "Enables the X ordinance"): Medieval Feast Days (Charters, $0.03/res: happiness +3%, commercial demand x1.05), Curfew (Town Watch, $3: crime x0.7, happiness -1%, commercial demand x0.9), Herb Gardens (Herbalism, $0.02/res: sickness x0.85); Renaissance Fire Code (Architecture, $0.02/res: fire risk x0.7, industrial demand x0.95), Street Lighting (Civic Planning, $0.03/res: crime x0.8, happiness +1%), Public Lectures (Printing Press, $0.04/res: research x1.1); Industrial Smoke Abatement (Factories, $0.03/res: pollution x0.8, industrial demand x0.9), Building Code (Steel Frames, $0.02/res: fire risk x0.75, land value +2%), Workmen's Fares (Electric Trams, $0.03/res: residential demand x1.1), Free Clinics (Public Sanitation, $0.04/res: sickness x0.8); Modern Neighbourhood Watch (Mass Media, $2: crime x0.8, happiness +1%), Car-free Sundays (Automobiles, $0: pollution x0.9, happiness +2%, commercial demand x0.95). Banking also halves loan interest.
- **UI (15d).** `Scripts/UI/BudgetPanel.cs`, **built at runtime** (a `RuntimeInitializeOnLoadMethod` after the scene loads, so no `Main.unity` / prefab edit): a panel in the slot the Taxes and Research panels share (`SidePanels`), opened by a **Budget** HUD button (a copy of the Taxes button, last in the `HUD/Budget` group; the Taxes panel is unchanged). Top: today's ledger (`SimulationSystem.Ledger()`: income by zone, costs split into buildings / roads / pipes / loans / ordinances, the techs' upkeep multiplier, net) refreshed each tick and on every change. Tabs: **Services** (a 50-150% slider per `BudgetLine` whose buildings are unlocked, `GameManager.BudgetLineUnlocked`; each row shows its funded upkeep and the effect / reach factors, and Power warns when supply drops below demand), **Loans** (the offer with daily payment and interest, Take loan, active loans with Repay at the remaining principal) and **Ordinances** (unlocked ones with cost per day at today's population, effect text and Enact / Repeal; locked ones of the current or earlier ages muted with "needs X"). The tab content scrolls past 560 px. Every change goes through `GameManager.NotifyBudgetChanged()` (marks the power / water state dirty, raises `GameEvents.BudgetChanged`). Also: `HappinessTooltip` gains an Ordinances line; `SelectionPanel` shows "Funding x%: reach r (full b), strength s (full f)" on a civic building whose line is not at 100%; `InfoOverlay`'s placement preview funds the placed sources and the new building; toasts for a loan taken / repaid and the research-complete toast names the ordinances a tech enables; the debt banner mentions a loan while a slot is free.
- **Balance (15e).** `EngagedCity.UseBudget` (off by default, so every earlier run stays the regression baseline: from Medieval still 84 / 61 / 73 days, later starts 447 / 557 / 630 pop at day 120): the player enacts one ordinance a day that answers a happiness loss it has (a positive happiness effect, a need it pays for, pollution below -2%, or a research multiplier) while the surplus is at least 4x its cost and the cash cushion holds, repeals paid ones when the surplus turns negative, takes one loan after 10 days of saving for a must-build and repays it once the pressure is off and cash covers it; funding stays at 100% (the human's lever). With it on, from Medieval: 84 / 58 / 61 days (Modern on day 203, min happiness 0.60, one loan); Industrial start 584 pop at day 120 (557 without; one loan, cash $585 at the end, never in debt); Modern start 630 pop with four ordinances (+$52/day) and happiness 0.91 (0.83). `AgeBalanceTests` run both modes. Ordinance probe (seeded Modern-start city, 120 days, 375 pop, +$368/day): costs $0-15/day, so none is a real drain at that size; Feast Days (+3%) and Car-free Sundays (+2%) give the most happiness per dollar, Curfew is slightly negative below about 400 residents (crime term small, -1% flat) and pays off as the ramp grows, demand effects show nothing because growth is paced by `MaxGrowthPerDay`. Car-free Sundays was free in the probe, so it now costs $0.02 per resident. Tick on a full 96^2 Modern city in EditMode with every ordinance enacted and every line funded at 80%: 1.03 ms against 0.99 ms plain (funded sources add nothing per tick, `CivicSystem` caches the multipliers), so no player benchmark. **Known property:** overfunding an old civic tier is far cheaper per covered cell than the next tier (a Constabulary at 150% reaches 7 at strength 0.94 for $9/day against the Police Station's 8 at 1.0 for $30/day), so funding can stretch an old tier past the point the plan intended; left as is and recorded as an open design question for the player.
- Tests: `Tests/EditMode/Simulation/BudgetTests.cs` and `OrdinanceTests.cs` (enact needs the tech; effects fold and unfold; flat + per-resident cost in the ledger; crime / fire risk / sickness multipliers at a home; the separate happiness term; Banking halves loan interest; save keeps enacted ordinances and drops unknown ids; save -> continue = uninterrupted run; `ContentTests.Ordinances_AreSpreadOverTheAges_…`). BudgetTests: loan schedule, total, early repayment, limits, repay needs money, curves, clamp / snap, default feeds the placed sources, civic strength / reach and the cap, reach floor of 1, civic cover and power supply through the sim, brown-out at 50% power, well reach and tower supply, Fountain cheer vs upkeep line, school RP, park cheer, ledger per line and = what `Tick` charges, funding survives `GridData.Resize`).

### Release (M19)
M19 changed no simulation behaviour (every recorded number stands, 387 EditMode tests green). The player-facing pieces and where each is described:
- **Flow:** the game boots to the title (`GameFlow`, `MainMenu`: Continue, New city, Tutorial, Load, Settings, Quit) over the bundled showcase city; Esc opens the pause menu through `EscapeRouter`; New / Load / Main menu / Quit ask before unsaved progress is lost. See *UI (M7)*: game flow, menus and Esc; main menu and showcase.
- **Saves:** named saves with summaries and thumbnails, a quicksave per city, three rotating autosaves, save v7 (city name, tutorial progress). See *Save / load (M8)*.
- **Settings and controls:** one tabbed Settings window (Audio, Display, Interface, Gameplay, Controls) over `GameSettings` (`PlayerPrefs`, `CityGame.*`), rebindable keys in `KeyBindings`. See *UI (M7)*: Settings, Controls.
- **Tutorial:** twelve Medieval objectives, a card and button outlines, saved with the city. See *UI (M7)*: Tutorial.
- **Release:** *CityBuilder > Build Windows Release* (folder, zip, Inno Setup installer), `-smokeTest`, identity Chronopolis / J-man Studios / 1.0.0. See *UI (M7)*: Release.
- **UI-only play-throughs** (virtual mouse and keyboard, committed under `Scripts/Editor/Playthrough/`, started by a one-line RunCommand, they print `PASS` / `FAIL` / `DONE` lines): `FinishDriver` (29 checks, 0 failed: title -> Settings (mute, UI scale, autosave, a key rebound to N) -> Tutorial -> first objective with the rebound key -> pause menu -> Save as (file, summary and a 36 KB thumbnail on disk) -> Main menu -> Load from the title (the card is back at objective 2, the road and the key survived)), `ControlsDriver` (20), `TutorialDriver` (22). Run in the Editor with `SaveSlots.Root` in a scratchpad folder.
- **Owed to a human (recorded, not blocking):** a playtest of the installed build, a look at the layout in a 1280x720 window, the Display tab changing the real screen, the M18 audio listen, the tutorial's finish dialog and objectives 6-12 by UI, a real restart check of rebound keys, the pause-on-event switch with a real event, the real `wantsToQuit` path.

### WebGL (after M19)
A browser build of the same game, on `main` behind `#if UNITY_WEBGL` (desktop unchanged; 387 EditMode tests and the Windows `-smokeTest` re-run green after the merge). `CityBuilder > Build WebGL` (`Editor/Release/WebGLBuild.cs`) and the `Web - Desktop - Release` build profile (gzip, decompression fallback, 512 MB start memory). Differences: no Quit buttons, no Display tab (and `GameSettings.ApplyDisplay` is a no-op), and the title's showcase city is fetched with `UnityWebRequest` (`SaveGameController.LoadShowcaseWeb`, bracketed by `GameFlow.ShowcaseApplying` so the late apply keeps the title up). Checked by hand in a browser: the title, Settings tabs (a tab-highlight bug from skipping the Display tab was fixed with `m_TabIds`). **Not checked:** saves surviving a closed tab (IndexedDB), performance and memory with a large city, mobile browsers. The host serves its own `index.html` (scaled to the window); only `Build/` and `StreamingAssets/` are uploaded. Procedure and traps: the `webgl-build` skill.

### Save / load (M8)
- **Named saves (M19a):** `<persistentDataPath>/Saves/<name>.json` + `<name>.info.json` (a `SaveSummary`: city, age, population, money, date, size, UTC ticks, versions, kind, tutorial flag) + `<name>.png` (320x180 thumbnail). F5 quicksaves (`<city>_quick`), F9 loads the newest save of the current city (else the newest of all), the HUD Save writes the city's own Manual save (or a new `UniqueName`: the city name, else `City <date>`), HUD Load = F9. `Autosave()` rotates `autosave_1..3` (oldest first; the timer is the Gameplay setting, 19d). JSON via `JsonUtility`.
- **Save slots (19a):** `Simulation/Save/SaveSlots.cs` (static, pure `System.IO`; `Root` is set by `SaveGameController.Awake` to `persistentDataPath/Saves`, or `-savesDir <path>`; Play-mode checks set `SaveSlots.Root` after startup to keep the player's saves out): `SanitizeName` (drops invalid characters and **dots**, so `x.info` can't collide with the sidecar of `x`; reserved Windows names get `_`; max 48; never empty), `UniqueName`, `QuickName`, `NextAutosave`, `Write` (temp file + replace via `SaveSystem.TryWrite`; sidecar and thumbnail failures are non-fatal; no thumbnail removes a stale one), `List` (newest first; a missing or corrupt sidecar is rebuilt from the city, a city that can't be read is `Damaged`), `Delete`, `Rename`, `HasAny`, `ImportLegacy` (copies the old `city.json` to `Imported city` once, a `.legacy_imported` marker stops it coming back). `SaveGameController`: `CurrentName`, `CityName`, `Tutorial`, `Dirty` (set by cell / date / budget events, cleared by save, load and New City), `Save` / `SaveAs` / `QuickSave` / `Autosave` / `Load()` / `Load(name)` / `NewCity(size, age, disasters, cityName)`. `ThumbnailCapture` renders `Camera.main` through URP's `SingleCameraRequest` (no HUD in the picture; `Camera.Render` is the fallback). Tests: `SaveSlotsTests` (19), `SaveMigrationTests` v6 -> v7.
- Pure, in the Simulation asmdef (`Scripts/Simulation/Save/`): `SaveData` (public fields for `JsonUtility`; `Version` = `SaveData.CurrentVersion` = **7** (v7, M19: `CityName`, `Tutorial` (-1 = none); `V6ToV7`); a format change bumps it **and adds a step to `SaveMigrations`** plus a test — older files are migrated on read, newer ones rejected), `SaveMigrations.TryMigrate(data, ages, techs)` (v1 → v2 marks the city `Age = SaveData.NoAge` with empty research and zeroed per-cell arrays; then, when the game has age/tech databases, a `NoAge` city is **adopted as Industrial**: `TechSystem.StartingTechs(ages, techs, legacy)` researched (all earlier techs + Electricity), every grown cell built in Industrial, `Year += StartYear − 1` (year 1 → 1760); built ages above the city's age are clamped; an unknown age index is rejected), `BuildingRecord {Id, X, Y, Rotation}`, static `SaveSystem` (`Capture`, `CreateNew`, `ApplyGrid` (resizes the map to the save's `Width`/`Height`), `ApplySimulation`, `ToJson`/`TryFromJson(json, out data, out error, ages = null, techs = null)` with validation (sizes capped at `MaxMapSize` 256) and migration, `TryWrite` via temp file / `TryRead(path, …, ages, techs)`). `CreateNew(w, h, config, ages = null, techs = null, startAge = -1)`: with ages, the start age's year, `StartingMoney` and starting research (−1 = Industrial). `ApplySimulation` restores research first (`TechSystem.Restore`, or `StartNew(Industrial)` for data not migrated with these databases) and returns how many saved tech/project Ids were dropped as unknown/invalid (`SaveGameController` logs a warning; never a failure). Sim restore hooks: `SimulationSystem.Restore`, `EconomySystem.Restore`, `PopulationSystem.Restore` (recomputes capacity/employment/demand so the UI is right before the next tick).
- Saved: calendar + speed, money + last day's income/expense, R/C/I taxes, population, happiness, zones/roads/levels, placed buildings; v2: `Age` (index, `NoAge` = saved without ages), `Researched` (ids, database order), `ActiveResearch` / `ResearchQueue` (project ids, `advance:<ageId>` for advancing), `ResearchProgress` (the RP pool), `BuiltAges` / `Historic` (row-major bytes); v3 (M13): `Pipes` (row-major bytes; `SaveMigrations.V2ToV3` adds an empty layer); v4 (M15): `Funding` (a float per `BudgetLine`; null / short = 100%), `Loans` (`LoanRecord {Amount, DailyPayment, DaysLeft}`), `Ordinances` (enacted ids, empty until 15c) — `V3ToV4` sets full funding, no loans, no ordinances; `ApplySimulation` restores the budget before `sim.Restore`; v5 (M16): the `Roads` bytes hold the road **tier** (0 none, 1..5; v1–v4 stored 1 = a road) — `V4ToV5` maps every road to the best street tier the save has researched (`RoadTiers.BestStreet`; Paved for `NoAge`, which is adopted as Industrial with Macadam), and `TryFromJson` rejects bytes above 5; v6 (M17a): `Disasters` (the switch), `RandomState` (16 hex digits), `Fires` / `Rubble` / `Plague` byte layers, `PlagueCooldown` / `PlagueRemainder`, `Broken` (`BrokenRecord`), `PendingEvent` / `PendingDays` / `DaysToNextEvent`, `ActiveEvents` / `RecentEvents` (`EventRecord`) — `V5ToV6` turns the switch off and empties them; `DisasterSystem.Restore` falls back to empty layers / seed 1 for bad data. Not saved: occupant ids (re-minted on load), `TimeManager`'s partial-day accumulator.
- `Scripts/Save/SaveGameController.cs` (on `GameManager`) rebuilds in a fixed order: `ClearAllBuildings` → `ApplyGrid` → `RestoreBuilding` per record (unknown/blocked ones are skipped with a warning) → `ApplySimulation` (needs building modifiers in place) → `SetDate`/`SetSpeed` → `RaiseStateEvents` + `GameEvents.CityLoaded`. Files are validated (and migrated, with `GameManager.Ages`/`Techs`) before anything is torn down. `NewCity()` applies `SaveSystem.CreateNew` at the current size; `NewCity(size)` at another size.
- `Scripts/UI/NewCityDialog.cs` on `Prefabs/UI/NewCityDialog.prefab` (last child of `UI`; script on the container, child `Blocker` = dimmed full-screen raycast blocker + centred `Panel` is what's toggled): map size buttons 32/64/96 (current size preselected; selected = non-interactable, the HUD speed-button convention), Cancel / Esc close, Create → `SaveGameController.NewCity(size, startAge)`; the starting-age row is described under Ages & research → UI. It subscribes to `GameMenu.NewRequested` through its own `m_GameMenu` scene ref, so the HUD prefab instance carries no reference to it.
- `Scripts/UI/GameMenu.cs` on the HUD prefab root (buttons in `HUD/Game`), since M19b: the scene's Save / Load / New buttons became **Save · Menu · Settings** at runtime (Load relabelled Menu and opens the pause menu, New hidden: New city lives in the pause menu, Sound relabelled Settings and opens the Settings window). `GameMenu` raises `SaveRequested` / `MenuRequested` and `RequestNew()` raises `NewRequested` for the New City dialog. `TaxPanel` resyncs its sliders on `CityLoaded`.
- Tests: `Tests/EditMode/Simulation/SaveSystemTests.cs`, including save-at-day-30-then-continue = uninterrupted run; `SaveMigrationTests.cs` (the real 24² v1 save in `Fixtures/city_v1_24x24.json` migrates to Industrial and plays exactly like the pre-ages game; v2 round trip mid-research and mid-redevelopment = uninterrupted run; unknown tech ids dropped). Shared age fixture for tests: `TestAges.cs` (4 ages shaped like §12, 8 techs, `TestAges.Advance(sim)`).

### UI (M7)
- **Game flow, menus and Esc (M19b).** `Scripts/Core/GameFlow.cs` (runtime-created at `AfterSceneLoad`, `GameFlow.Instance`, no scene edit): the state is **Paused while any window is open** (`WindowOpened(closer)` / `WindowClosed(closer)`): the first open remembers the clock speed, sets `Paused` and sets `InputReader.Blocked`; the last close restores the speed (not after a load: `CityLoaded` closes every window and the loaded city's own speed stands), raises `GameEvents.FlowChanged`. `GameFlow.SpeedToSave` makes a save made from a menu record the running speed, not Paused. `InputReader.Blocked` makes every gameplay action read idle except Cancel. `Scripts/UI/EscapeRouter.cs`: one Esc press goes to the highest-priority handler that accepts it: event popup 110 (swallows it) > Confirm 100 > New City dialog 95 > save browser 90 > pause menu 80 > active tool / selection (`PlacementController`) 50 > side panels (Tax, Tech, Budget, Sound) 40; with no taker `GameFlow` opens the pause menu. Windows (all code-built with `UiKit.CreateWindow`: dimmed full-screen blocker + centred auto-height panel, moved last under the canvas when shown): `PauseMenu` (Resume, Save, Save as, Load, New city, Settings, Quit), `SaveBrowser` (Save / Load mode, name field, rows with thumbnail and the sidecar summary, Rename, Delete; reads only summaries), `ConfirmDialog` (`Ask(title, message, choices)`: the action runs, then the dialog closes; Esc cancels). **Unsaved changes:** `GameFlow.GuardDiscard(proceed)` asks Save / Don't save / Cancel when `SaveGameController.Dirty`; Load, New City, Quit use it; `Application.wantsToQuit` (window X, Alt+F4) asks too, except in the Editor (it also fires when leaving Play mode). `UiKit` holds the shared Row / Text / MakeButton / InputField / Window helpers (Budget, Sound and the event popup use it). The HUD Save saves the city's own file, or opens Save as when it has none. `SoundPanel.Bootstrap` now finds the HUD's Save button through `GameMenu` (the new windows have buttons of the same name).
- **Main menu and showcase (M19c).** `GameFlowState` gained `MainMenu`. At startup `GameFlow` enters it (HUD hidden, input blocked, camera in showcase mode) and, two frames later (after the scene's Starts), `SaveGameController.LoadShowcase()` replaces the startup city with `Assets/StreamingAssets/showcase.json` (a 64x64 Modern city, 3,377 pop, 57 buildings, 75 KB, disasters off; read like a save with the game's age / tech databases, `ShowcaseActive` = never dirty, never saved). The showcase runs at 1x behind the menu with day/night, vehicles and ambience; windows opened over it (New City, the browser, confirms) do not pause it. **Hiding the HUD** puts a `CanvasGroup` (alpha 0, not interactable, no raycasts) on every top-level canvas child except `PauseMenu`, `SaveBrowser`, `ConfirmDialog`, `MainMenu`, `NewCityDialog`, `Notifications` and `EventPopup`, and restores them after (components it added are removed): nothing is deactivated, so the HUD's event subscriptions keep running. `IsoCameraController.SetShowcase(bool)`: input ignored, the look-at point drifts on a slow loop around the map centre at zoom 11; leaving re-frames the map (`FrameMap`) and restores the zoom. `MainMenu` (code-built, a panel on the left, version label bottom left): Continue (the newest save, named under the button, disabled with none), New city, Tutorial, Load…, Settings, Quit. A load, Continue or New City raises `CityLoaded`, which closes every window and leaves the menu (`GameFlow.OnCityLoaded`); the pause menu's **Main menu** entry asks about unsaved changes, then `EnterMainMenu()`. **Skipping the menu:** `-skipMenu`, `-perfBenchmark`, `-smokeTest`, batch mode, and in the Editor *CityBuilder > Skip Main Menu In Play Mode* (`EditorPrefs` `CityGame.SkipMenu`); a Play-mode script that starts from the title can call `GameFlow.Instance.StartPlaying()`. **Regenerating the showcase:** `[Explicit] ShowcaseGenerator.Generate` (a Medieval-start `EngagedCity` run on the shipped content to the Modern age + 220 days; `EngagedCity.PlacedBuildings()` lists the harness's placements as save records), then commit `showcase.json` and its `.meta`; the load logs a warning if any building can't be restored, which means the content changed and it must be regenerated.
- **Settings (M19d).** `Scripts/Core/GameSettings.cs` (static, `PlayerPrefs` under `CityGame.*`, cached, every write raises `Changed`; it replaced `SoundSettings` and `DayNightCycle`'s own pref and **kept their keys and stored types** (`CityGame.Audio.Master / Music / Ambience / Sfx / Mute` as floats, `CityGame.Visual.LockDay` as an int), so M18 settings carry over; `SoundChannel` moved here). Settings: Audio (master, music, ambience, effects, mute), Display (window mode Full screen / Borderless / Windowed, resolution from `Screen.resolutions` >= 1280x720, VSync, frame cap 30 / 60 / 120 / 144 / 240 / unlimited while VSync is off; **applied only once the player has chosen something**, so the project's own screen settings stand until then, and never in `-perfBenchmark` / `-smokeTest` runs), Interface (UI scale 80-150%, Always day, camera pan speed 50-200%, edge scroll, tooltip delay), Gameplay (autosave every 0 / 1 / 3 / 6 game months, Disasters & events default for New City, pause on a random event, confirm before demolishing a placed building). `SettingsPanel` (code-built modal window, a counted window: pauses a running game, leaves the showcase running; tabs, steppers `<  value  >`, sliders, On / Off switches (`UiKit.Stepper` / `SwitchButton` / `MakeSlider`), **Reset this tab**; Esc closes it first over the pause menu). It opens from the HUD **Settings** button (a copy of the Save button made by `GameFlow.AddSettingsButton`), the pause menu and the title; the M18 Sound panel is gone. `UiScaling.Apply()` divides every `ScaleWithScreenSize` `CanvasScaler`'s reference resolution (1920 x 1080 remembered per canvas) by the scale. Consumers: `IsoCameraController` (pan speed, edge scroll; **it now ignores pan input while a menu blocks input**, which edge scroll would not have), `ToolbarController` (tooltip shown after the delay), `EventPopup` (without the pause switch the clock keeps running and the event takes its last option after `EventAutoDays`, as it always did when unanswered), `NewCityDialog` (the switch starts from the setting), `PlacementController.DemolishAt` (a `ConfirmDialog` before removing a placed building when the switch is on; roads, zones and grown blocks never ask), `GameFlow` (autosave: when the game month changes, every N months and only when the city changed, `SaveGameController.Autosave()` into the oldest of `autosave_1..3`; not on the title or for the showcase; the counter resets on a load). Windows that must stay visible over the title go in `GameFlow.s_KeepVisible` (`SettingsPanel` was missing at first: the HUD-hiding `CanvasGroup` made it invisible and unclickable on the title).
- **Controls (M19e).** `Scripts/Input/KeyBindings.cs` (static): the rebindable list (Pan up / down / left / right = the WASD part of the composite, Rotate, Demolish, Road tool, Pipe tool, Cycle info view, Quick save, Quick load; only the **first** binding of an action is rebound, so the arrows and Backspace stay as a second way in). `InputReader.Instance` / `.Asset` expose the cloned action asset; overrides are one JSON string in `PlayerPrefs` `CityGame.Input.Bindings` (not in the city save), loaded in `InputReader.Awake`. `Rebind` uses `PerformInteractiveRebinding` limited to the keyboard, **Esc cancels**; a key another rebindable action holds is **swapped** (the status line names it), a key a fixed binding uses (Esc, F1, 1-4 speed, arrows, Backspace) is refused. `SettingsPanel`'s Controls tab: one row per entry (key button, Reset), a status line, **Reset this tab** = all defaults; `SwallowEscape` keeps the cancelling Esc from also closing the window (the Esc handler swallows it during the rebind and the frame after). `KeyBindings.Fill` swaps the old hard-coded `[V]`, `[Del]`, `[B]`, `[P]`, `[R]` hints for the current keys where text is shown (tooltip, toast, selection panel line, cursor hint). The rebind needs the key held a moment (it waits for a better match), so a driver holds it about 0.3 s.
- **Tutorial (M19f).** Pure, in `Simulation/Tutorial/`: `TutorialContent` (12 Medieval objectives: road, homes, well, villagers 10, shops and crafts, Budget panel, research, Commons + Park, an info view, population 60, Monastery, the Renaissance; text and numbers tunable there), `TutorialObjective` (a `Measure` giving done / needed), `TutorialSnapshot.Measure` (road cells connected to the edge, residential cells with road access, commercial / industrial cells, population, age, research active, techs, placed building ids, and the two UI flags), `TutorialProgress` (an index: -1 none or skipped, 0..11 current, 12 finished; objectives are sequential and one already met completes at once; saved as `SaveData.Tutorial`). Runtime: `TutorialCard` (code-built, left of the screen, not a window so the game keeps running; it measures about 4 times a second, advances, writes the index with `SaveGameController.TutorialChanged`, toasts each step, outlines the needed control with `UiHighlight` (a pulsing frame found by button name: `Road`, `Residential`, `Build_well`, `BudgetButton`, `ResearchButton`, the view buttons; Research first and the building once its tech is in), shows the top `GrowthBlocker` in `SelectionPanel.BlockerText` wording on the two steps that wait on growth, ticks the last three finished steps, minimises, and **Skip** asks first). Start it from the title (Tutorial) or the New City dialog's Tutorial toggle: `SaveGameController.StartTutorial()` = Medieval, 48 x 48, disasters off, "Tutorial town". The UI flags (Budget panel opened, a view shown) reset at each step so an earlier click does not count. A load restores the index (`GameFlow.OnCityLoaded` calls `TutorialCard.CityLoaded`). Finishing shows a dialog offering to turn disasters on. Tests: `TutorialTests` (7), `TutorialPlaythroughTests` (the harness player on 48 x 48 reaches the Renaissance, the last objective, on day 84).
- **Release (M19g).** The game is **Chronopolis**, company **J-man Studios**, version **1.0.0** (`ProjectSettings`; this moved the data folder to `%USERPROFILE%/AppData/LocalLow/J-man Studios/Chronopolis/`, so `SaveGameController.ImportFromOldIdentity` copies the old `DefaultCompany/City Game/Saves` files over once, never overwriting, marker `.folder_imported`, via `SaveSlots.ImportFolder`; PlayerPrefs settings did not move and start from defaults). The title shows `Application.productName`. The icon and logo are the user's artwork: `Tools/release/Chronopolis.ico` (also the installer icon, `SetupIconFile`), its PNG entries extracted to `Art/Icon/icon_<size>.png` (16 to 256) for PlayerSettings, and `Resources/Logo.png` (1254 px, transparent) shown on the title screen in place of the title text (`MainMenu.AddLogo`; the text stays if the asset is missing). **`CityBuilder > Apply Release Settings`** (`Editor/Release/ReleaseBuild.cs`) sets identity, version, icons, full screen window at the native resolution, resizable, no run in background. **`CityBuilder > Build Windows Release`** builds `Build/Release/Chronopolis_<version>/`, a zip, and (Inno Setup 6 found) `Chronopolis_<version>_setup.exe` from `Tools/release/Installer.iss.template` (per-user install by default, saves and settings are never touched on uninstall), logging to `Build/Release/release_log.txt`; `Build/` is ignored. Measured 1.0.0: folder 132 MB, zip 50 MB, installer 37 MB, build 9-32 s. **`SmokeTest`** (`-smokeTest -savesDir <folder>`, any non-Editor player, exit code 0 / 1, report `smoke_test.txt` next to `Player.log`): showcase from StreamingAssets, a 32x32 Medieval city built through the grid and `RestoreBuilding`, 60 days, save / load compared (money, date, buildings, population, city name), the tutorial city and its index through a save, the debug panel disabled in a release player; 26 checks, about 10 s. A release player has no development tools: `DebugPanel` is switched off and `PerfBenchmark` only runs in development builds.
- `UI` Canvas (Screen Space Overlay, `CanvasScaler` 1920×1080, match 0.5, layer 5) + `EventSystem` with **`InputSystemUIInputModule`** (the legacy `StandaloneInputModule` throws under Input System only). TextMeshPro Essentials live in `Assets/TextMesh Pro/` (TMP's required location — not under `_Game`). `PlacementController` ignores map clicks when `EventSystem.current.IsPointerOverGameObject()` (the only UI-blocking check; there is no IMGUI left).
- `Assets/_Game/Scripts/UI/HUDController.cs` on `Prefabs/UI/HUD.prefab` (top bar, child of `UI`): date + speed buttons (active speed = non-interactable, so its disabled colour is the highlight), age + research (`HUD/Age`, M11), money + net/day, Taxes / Research buttons (`HUD/Budget`), pop/housing, employed/jobs, power demand/supply (`HUD/Power/PowerText`, hidden until power is unlocked) and water under it (`HUD/Power/WaterText`, M13: "Water d / s" piped, "Water n%" of grown buildings in the well ages, red while any are dry; hidden until `WaterUnlocked`), happiness meter, R/C/I demand meters. Subscribes to `GameEvents` in `OnEnable` and pulls current values once in `Start` (events only fire on change). `UIMeter.cs` = sprite-free bar (fill rect's anchorMax set to the 0..1 value).
- Events added for UI: `GameEvents.HappinessChanged`, `CashFlowChanged(income, expense)` (both raised per tick by `GameManager`), `InsufficientFunds(cost)` (raised by `PlacementController` when a road/building can't be paid for); `PlacementController.ModeChanged` (instance event on every tool change; read `CurrentMode`/`ZoneBrush`/`SelectedBuilding`).
- `Assets/_Game/Scripts/UI/ToolbarController.cs` on `Prefabs/UI/Toolbar.prefab` (bottom-centre, sections Transport / Zoning / Buildings / Tools / View; M13: Transport also holds `PipeButton`, hidden until `PipesUnlocked`; when the bar is wider than the canvas it scales itself down to fit, `m_ScreenMargin` 8). `VIEW` = `PowerView` + `WaterView` (M13) + `CoverageView` + `PollutionView` + `LandValueView` + `AgeView` `ToolButton`s (Power/Age hidden while not `InfoOverlay.IsAvailable`); their active state follows `InfoOverlay.ViewChanged`; `m_InfoOverlay` is a scene override on the Toolbar instance. Fixed tools (Road, R/C/I/Unzone, Demolish) are `ToolButton` prefab instances wired in the prefab; **Service/Utility `BuildingDefinition`s get a button generated at runtime** from `Prefabs/UI/ToolButton.prefab` (named `Build_<id>`). Clicking the active tool again clears it. Highlight follows `PlacementController.ModeChanged` (so hotkeys update it too); affordability follows `GameEvents.MoneyChanged` (`Button.interactable`). Shared tooltip panel floats above the bar.
- **Selection (no tool active):** `PlacementController` click → `SelectCell` (road, placed building, grown cell or zoned land; empty land/off-map clears). Exposes `HasSelection`/`SelectedCell`/`SelectionChanged`, `GetBuildingAt`, public `DemolishAt`/`Unzone`/`ClearSelection`. Any tool change or `Cancel` clears it; it auto-clears if the cell becomes empty. Highlight = `SelectionHighlight` (a second `GhostRenderer`, blue) raised onto the top of whatever stands there via a downward raycast on the Buildings layer — **building prefabs need a collider** (House/Park have `BoxCollider`; grown cubes get one from `CreatePrimitive`).
- `Assets/_Game/Scripts/UI/SelectionPanel.cs` on `Prefabs/UI/SelectionPanel.prefab` (top-right; script on the container, visuals on child `Panel` which it toggles). Describes building / road (edge-connected?) / grown cell (level, capacity) / zone, with the reason it isn't growing from `GrowthSystem.GetBlocker(cell, demand)` → `GrowthBlocker` (pure, tested). Action button = Demolish or Unzone. Refreshes immediately on `SelectionChanged`; `GameEvents.CellChanged`/`DateChanged` only mark it dirty and it refreshes at most once per frame in `LateUpdate` (a growth tick can change dozens of cells).
- `Assets/_Game/Scripts/UI/TaxPanel.cs` on `Prefabs/UI/TaxPanel.prefab` (top-left under the money readout; toggled by `HUD/Budget/TaxesButton`, which lives in `HUD.prefab`). R/C/I sliders in whole percent, 0–`m_MaxPercent` (20), writing `EconomySystem.TaxResidential/Commercial/Industrial` immediately; the hint shows the combined R/C/I tax happiness penalty (`PopulationSystem.TaxHappinessPenalty`) and yesterday's income/costs.
- `Assets/_Game/Scripts/UI/NotificationController.cs` on `Prefabs/UI/Notifications.prefab` (full-screen, non-raycasting root): fading toast above the toolbar (`ShowToast(text)`, neutral dark panel — colour individual messages with TMP rich text; unscaled time so it fades while paused). Toasts: `InsufficientFunds`, `Notification`, population milestones (`m_PopulationMilestones` 50…2000), once per drop below the low-happiness threshold, and the M9 power toasts; `CityLoaded` re-syncs both so loading doesn't spam. Red debt banner under the HUD while `MoneyChanged` < 0.
- Same prefab: `CursorHint.cs` (child `CursorHint` panel follows `InputReader.Pointer`, shows `PlacementController.CursorHint`, red when invalid) and `FloatingTextController.cs` (pooled copies of the hidden `FloatingText` TMP template, tracking a world point; driven by `GameEvents.MoneySpent`). Both convert screen → local with a null camera (overlay canvas), so their rects are anchored at the parent's centre. Scene-instance refs: `CursorHint.m_Placement`/`m_InputReader`.
- `Assets/_Game/Scripts/UI/HappinessTooltip.cs` on `HUD/Happiness` (transparent raycast `Image` as hover target; panel `HUD/Happiness/Tooltip` with `LayoutElement.ignoreLayout`): lists non-zero `HappinessBreakdown` terms. `HUDController` colours the happiness label red below the threshold.
- `Assets/_Game/Scripts/UI/ToolButton.cs` — background colour = active state, `Button.interactable` = affordable (also dims the label and turns the cost red); optional zone swatch strip and cost line; raises `Hovered`/`Unhovered` for the tooltip.
- `Assets/_Game/Scripts/Core/PerfBenchmark.cs` — development players only: run with `-perfBenchmark` to build a fully grown 96×96 city (11.5k blocks), time idle / 4× / per-frame rezone, demolish+regrow and road edits plus a zoomed-out view (frame, main thread, render thread and GPU medians via `FrameTimingManager`), write `persistentDataPath/perf_benchmark.txt` + screenshots and quit. Never saves. Since M11e it mixes built ages (each 4×4 block between roads gets one, cycling through every age) so every age style is drawn. How to run it and the baseline numbers (~1.1 ms frames at default zoom on the reference machine): `perf-benchmark` skill.
- `Assets/_Game/Scripts/UI/DebugPanel.cs` on `Prefabs/UI/DebugPanel.prefab` (top-left, hidden; **F1** toggles; disabled outside Editor/development builds via `Debug.isDebugBuild`): Seed test city (`PlacementController.DebugSeedCity`), +$10,000 (`Economy.Refund`), Skip 30 days (`TimeManager.DebugAdvanceDays`, which runs the normal `OnTick` + calendar path instantly). The old IMGUI `BuildToolbar` is gone.
