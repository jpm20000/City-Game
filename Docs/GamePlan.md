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

**Status (2026-10-03):** M0–M12 implemented — the vertical slice is complete, M12 (§12) made pollution local and added land value (level 3 needs it) and the heritage bonus, M9 (§11) added power, park coverage and info views, M10 (§12) made the map size per city (default 64²) with a New City dialog, and M11 (§12) added four ages, research and a 30-tech tree, per-age growth rules and looks, redevelopment with Keep historical, save v2 with migration, and a starting-age picker. M7 shipped as UGUI + TextMeshPro
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

### M13 — Water (plan, 2026-10-03)

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
- **13c Water UI.** `GameEvents.WaterChanged`, `GameManager.WaterUnlocked`, the HUD Water group, the Water view (both modes, what-if preview), the SelectionPanel lines and blocker texts, the tooltip line and the toasts (including the Industrial welcome hint and the nudge). Play-mode screenshots of the view in a Medieval and an Industrial city.
- **13d Pipes and save v3.** `GridData` pipes, the network carrying pipes and feeding cells with a pipe under them, the pipe upkeep, save v3 + migration + tests (v2 → v3, the v1 fixture through the chain, a round trip with pipes mid-shortage = uninterrupted run). Runtime: the pipe tool (button, `P`, drag lay / remove, hint, cost), `PipeTilemapView`, and pipes in the Water view. Tests: a pump away from roads linked by a pipe; two road networks joined by a pipe pool their supply; a pipe under a grown cell feeds it; a road placed on a pipe clears it; resize.
- **13e Balance, play-through, docs.** Final `AgeBalanceTests` pass; measure the full 96² tick (power + water networks; it must stay ≈1–2 ms, and run `perf-benchmark` only if not or if the pipe tilemap shows up in frame time). UI-only play-through: New (Medieval, 32²) → zone and road → homes held at level 1, the panel says "no well nearby" → Water view → dig a well → level 2 → (debug) research to and advance into Industrial → wells drop out, the toast → build a tower → upgrades resume → a pumping station off the road, a pipe to the roads → the HUD supply rises → save, load, the pipes are still there. Docs: §13 (a new *Water (M13)* section; Services & power notes the shared `UtilityNetwork`; Save / load v3; UI), the §12 notes and status, §8, the `AGENTS.md` Project bullet, the `CLAUDE.md` header.

#### Risks / open questions

- **Medieval pacing is already near the limit** (86 days of 90). Wells now gate the only Medieval upgrade, so the engaged player must dig them promptly. If the age runs over, first make the well cheaper or wider before touching `MaxGrowthPerDay` (which is shared with the age-less game).
- **The Industrial stall on advancing** is intended, but `EngagedCity` must research Waterworks early or Renaissance → Industrial pacing slips (the advance now has one more required tech).
- **Old saves:** every v1 / v2 Industrial or Modern city loads without a tower, and its upgrades pause until the player builds one. Accepted (the nudge toast explains it); the alternative (placing a free tower during migration) would need a free spot beside a road.
- **Refactoring `PowerSystem`** risks a subtle order change in the BFS. Mitigation: extract first, with the full suite green and the seeded numbers identical, before any water code.
- **Tick cost:** a second network BFS after every grid change, which is almost every day. Power costs well under 0.4 ms on a full 96² map, so measure it in 13e.
- **Pipe UX:** underground pipes are invisible outside the Water view, which can confuse. The pipe tool forces the view on (like the Plant / Park tools force theirs), and the SelectionPanel mentions a pipe under a cell.

### Where M13–M19 plug in (integration notes)
Moved here from `AGENTS.md` (2026-10-03). Where each outline lands in the code that exists today; decide the details in each milestone's plan.

- **M12 Land value & local pollution — done** (see §13 Land value & pollution). Original outline: per-cell pollution (industrial cells emit in an age-scaled radius) replaces the city-wide `PollutionPenalty` term in `PopulationSystem`; per-cell land value from parks/services, pollution and the heritage bonus (`GridData.IsHistoric` raises value around kept cells). Build both like `CoverageSystem` (per-cell arrays, lazy recompute, `OnResized`). Level 3 gains a land-value gate → new `GrowthBlocker`. Pollution and Land value info views.
- **M13 Water** (full plan above): `AgeDefinition.UpgradesNeedWater` already exists (unused) — add it to `AgeRules` and gate upgrades in `GrowthSystem` beside the power gate. Early ages: wells and fountains as coverage sources; Industrial and Modern: towers and pumps feeding pipes under roads, a copy of `PowerSystem`'s network and allocation model (`ServiceSource` + `BuildingDefinition` get a water supply). HUD group, view and toasts follow the power pattern.
- **M14 Civic services:** order, fire, health and education lines with per-age `BuildingDefinition`s unlocked by existing techs (e.g. fire station → Steam Power). Education buildings produce RP via `ResearchPerDay`. Health and crime become `HappinessBreakdown` terms (+ the happiness tooltip); per-cell crime and fire risk use the coverage pattern. Obsolete placed services get "outdated, replace with X" hints in `SelectionPanel`.
- **M15 Budget depth:** per-service funding scales a service's radius and effect (`ServiceSource` / `CoverageSystem`); loans with interest live in `EconomySystem` (saved → version bump); ordinances are tech-unlocked toggles (a new `TechEffectType` if needed). `TaxPanel` grows into a budget panel in the `SidePanels` slot.
- **M16 Traffic:** statistical load per road cell from the homes↔jobs flow (no agents); congestion lowers road access quality and happiness. Road tiers (dirt → cobble → paved → avenue → highway) become a per-road byte in `GridData` (saved → version bump), are unlocked by tech (the `UnlockRoadTier` idea in §12) and drawn per tier by `RoadTilemapView`. Traffic view. Watch the 96² benchmark.
- **M17 Disasters & events:** fire spreads between cells without fire coverage, plague in the Medieval age without health coverage, plant breakdowns; random events with choices arrive as toasts or popups. Use a seeded RNG whose state is saved; an on/off switch goes in the New City dialog (and `SaveData`).
- **M18 Art & atmosphere:** hand-made per-age prefabs go into the `AgeVisualSet` slots under the prefab contract (pivot at the ground centre of a 1×1 cell, +Y up, 1 unit = 1 cell, layer 9, a collider on the root, shared materials only, one or two materials; GamePlan §12). Also per-age road tiles, day/night lighting, music and ambience. Re-run `PerfBenchmark`.
- **M19 Release:** main menu, settings (audio, keybinds, UI scale), multiple save slots with thumbnails (`SaveGameController` is single-slot `city.json` today), a first-age tutorial and a Windows player build (see `perf-benchmark` for building a player and reverting the settings churn it leaves behind).

### M12–M19 outline (detailed plans written when each milestone starts)

**M12 — Land value & local pollution** (full plan above). Pollution becomes per-cell (industrial cells emit in a
radius scaled by age: workshops low, factories high, clean energy low), replacing the city-wide
term. Land value per cell comes from parks, services, water/coast later, pollution and the
**heritage bonus** (historic cells raise value around them). Level 3 needs a land-value
threshold. Adds Pollution and Land value info views. Reuses the `CoverageSystem` pattern.

**M13 — Water** (full plan above). Wells and fountains (coverage radius, Medieval and Renaissance), water towers and pumps
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
trips and v1 migrates; EditMode tests green plus the UI-only play-through). **M12 done** (steps 12a–12d:
local pollution by built age, land value with the level-3 gate, heritage bonus, Pollution and Value
views; the age-less baseline changed by decision, Industrial start re-recorded at 220 pop / 0.660;
EditMode tests green plus the UI-only play-through). Next: M13 (water) — plan written (2026-10-03), 13a next.

---

## 13. Systems reference (as built, M0–M12)

What each system is, where it lives and the numbers it was tuned to. Moved here from `AGENTS.md` (2026-10-03) so that file stays a short list of conventions; update the matching section in the same commit as any change (see the `milestone-workflow` skill). Where this section and the earlier sections of this document disagree, this section describes the code as built.

### Camera
- `Assets/_Game/Scripts/Camera/CameraSortAxis.cs` — applies `transparencySortMode` + `transparencySortAxis` in `Awake` (runtime-only, not serialized).
- `Assets/_Game/Scripts/Camera/IsoCameraController.cs` — pan (WASD/arrows; edge-scroll disabled via `m_EnableEdgePan`), mouse-position zoom (scroll wheel, 0.15 speed, 4 to `max(20, mapSide × 0.45)`), soft spring-back bounds (spring force 40, damping 0.88). Attached to Main Camera.

### Input
- `Assets/_Game/Scripts/Input/InputReader.cs` — wraps `CityBuilder.inputactions` (clones asset in `Awake`, enables `Gameplay` map). Exposes `Pan`, `Zoom`, `Pointer`, `ConfirmPressed`, `ConfirmHeld`, `CancelPressed`, `RotatePressed`, `DemolishPressed`, `RoadToolPressed`, `DebugTogglePressed`, `QuickSavePressed`, `QuickLoadPressed`, `CycleOverlayPressed`, `SpeedDelta`. Lives on a root `InputReader` GameObject in Main.unity.
- Bindings: `Confirm`=LMB, `Cancel`=RMB/Esc, `Rotate`=R, `Demolish`=Delete/Backspace, `RoadTool`=B, `CycleOverlay`=V, `DebugToggle`=F1, `QuickSave`=F5, `QuickLoad`=F9, `SpeedDelta`=1–4 (keys 2/3/4 carry `Scale(factor=N)` processors so the action reads 1..4; without them every key reads 1).

### Grid
- `Assets/_Game/Scripts/Grid/CityBuilder.Grid.asmdef` — runtime asmdef for the grid core. Contains `GridSystem` (MonoBehaviour, references Unity Grid/Tilemap), `CellUtils`/`GridData`/`ZoneType` (pure C#).
- `Assets/_Game/Scripts/Grid/CellUtils.cs` — static math: `IsInBounds`, `Index`, `CellToWorld`/`WorldToCell` (origin-based), `Neighbors4`, `EffectiveSize` (odd rotation swaps dims — use it everywhere a rotated footprint's size is needed), `GetFootprint`.
- `Assets/_Game/Scripts/Grid/GridData.cs` — pure C# state (`zones`, `roads`, `occupancy` int ids, `0` = empty): `GetZone/SetZone`, `IsRoad/SetRoad`, `GetBuildingLevel/SetBuildingLevel` (grown zone level `0..3`, `0` = undeveloped; setting 0 also clears the built age and historic flag, so demolish/rezone need nothing else), `GetBuiltAge/SetBuiltAge` + `IsHistoric/SetHistoric` (M11; historic only sticks on grown cells), `CountRoads`, `CanPlace` (rejects roads, occupied **and grown** cells), `Occupy`/`Release`, `GetFootprint`, `OnCellChanged`. `Resize(w, h)` replaces the map with an empty one and raises only `OnResized` (no per-cell events) — anything caching per-cell state (`RoadNetwork`, `PowerSystem`, `CoverageSystem` via `SimulationSystem`, `GridTilemapView`, `GrowthVisuals`) reallocates there. `ExportZones/ExportRoads/ExportLevels/ExportBuiltAges/ExportHistoric` + `Import(zones, roads, levels, builtAges = null, historic = null)` (row-major `byte[]`, clears occupancy, raises `OnCellChanged` only for cells that changed) back save/load.
- `Assets/_Game/Scripts/Grid/ZoneType.cs` — `enum ZoneType { None, Residential, Commercial, Industrial }`.
- `Assets/_Game/Scripts/Grid/GridSystem.cs` — `MonoBehaviour` on the `Grid` GO; holds the `Ground` Tilemap ref (the scene's painted bounds are read once, lazily, as the tile anchor), origin `(0,0,0)`, `GridSize` = startup map size (scene: `(64,64)`). Exposes `CellToWorld`/`WorldToCell`, `LogicalToTileCell` and `PaintGround(size)` (refills the `Ground` tilemap; called by `GameManager`).
- Logical coordinate space is `(0..W-1)×(0..H-1)`; cell `(x,y)` center → world `(x+0.5, 0, y+0.5)`. Seeded-city tests still use 24×24 (`RunSeededCity`); `DebugSeedCity` scales its cross to the map.

### Placement
- `Assets/_Game/Scripts/Placement/PlacementController.cs` — mode machine `{ None, Road, Building, Demolish, Zone }`. Reads the shared `GridData` from `GameManager` in `Start` (**not** `Awake` — `GameManager.Awake` must run first). `RoadTool`/`Demolish` toggle modes, `Cancel` clears, `SelectBuilding(def)` + `R` rotates in `Building` mode, `Confirm` places/removes. Only writes `GridData` roads — the tilemap is painted by `RoadTilemapView`; validation reuses `GridData.CanPlace` (no separate validator class). Holds a `Dictionary<int, BuildingInstance>` (occupant id → instance) to resolve building demolition. Roads cost `BalanceConfig.RoadCost`, buildings `def.Cost` (via `GameManager.Economy`; unaffordable = red ghost, no place). Placed/demolished buildings are reported to `GameManager.RegisterBuilding/UnregisterBuilding`. Demolish on a grown cell resets its level to 0 (zone stays, so it regrows). `SelectZone(ZoneType)` paints zones while LMB is **held** (drag); `ZoneType.None` = unzone; rezoning/unzoning resets the cell's grown level; roads/occupied cells can't be zoned; placing a road or building clears zoning under it. `CursorHint`/`CursorHintValid` (set in `UpdateHint` while a tool is active: cost, or why the action is blocked — `FootprintProblem` mirrors `GridData.CanPlace`). The hint is the single source of validity: the ghost's green/red just mirrors `CursorHintValid`, so a new tool only needs a hint case; the ghost and hint hide while the pointer is over UI. Raises `GameEvents.MoneySpent(amount, worldPos)` after paying for a road/building. `DebugSeedCity()` lays a free cross-shaped road, a free `power_plant` at `(0, mid−3)` (same spot as the seeded-city tests) + R/C/I zones (debug shortcut). Save/load API: `PlacedBuildings`, `ClearAllBuildings()` (no refund; resets tool + selection), `RestoreBuilding(def, origin, rot)` (free; shares `CreateBuilding` with normal placement).
- `Assets/_Game/Scripts/Placement/GridTilemapView.cs` — abstract base for tilemaps mirroring `GridData` (holds the `m_GameManager`/`m_GridSystem`/`m_Tilemap` refs). Because any road edit can change things far away, `OnCellChanged` only marks it dirty and `LateUpdate` re-diffs every cell against a painted-tile cache, so only changed cells hit `SetTile`. Subclasses implement `CreateTiles()` and `TileFor(cell)`; override `OnDestroy` with `base.OnDestroy()` to free runtime tiles/textures. For state outside `GridData` call `MarkDirty()`; `OnRepainted()` runs after each repaint pass; `OnGridResized()` after a map resize (painted cache and tilemap already reset).
- `Assets/_Game/Scripts/Placement/ZoneOverlay.cs` (`GridTilemapView`) — on the `Grid/Zones` Tilemap (sibling of `Roads`, same material/sorting order 1); translucent runtime `Tile`s from the `ground_square` sprite per zone (`ZonePalette` colour × `m_Alpha`). Zoned cells **without road access** get a runtime-generated diagonal-stripe tile instead.
- `Assets/_Game/Scripts/Placement/InfoOverlay.cs` (`GridTilemapView`, M9) — on the `Grid/Info` Tilemap (sorting order 2, above Zones/Roads). Views `Off / Power / Coverage / Pollution / LandValue / Age` (Pollution / LandValue: see Land value & pollution; Age: see Ages & research → UI): `V` cycles `Chosen` in that order (skipping views that aren't `IsAvailable` yet), toolbar `VIEW` buttons toggle it; `Shown` = the Plant/Park tool's view while that tool is held (any def with `PowerSupply` / `CoverageRadius`), else `Chosen`. Power: energised roads yellow, grown cells green/red, undeveloped zones faint green/red (beside an energised road or not). Coverage: per-cell park count shading; homes recoloured by bonus, job buildings dark grey. Grown buildings are recoloured through `GrowthVisuals.SetColorOverride` (the ground tint is mostly hidden under them) and the `Zones` renderer (`m_ZoneRenderer`) is hidden while a view is shown. **What-if preview:** with the tool held and `PlacementController.HasBuildingPreview`, a private `PowerSystem`/`CoverageSystem` are fed `Simulation.Sources` + the ghost's source, so the view shows the result of placing it (newly covered cells blue); nothing in the real sim changes.
- `PlacementController` preview API: `HasBuildingPreview` / `PreviewOrigin` / `PreviewRotation` + `PreviewChanged` (Building mode, pointer on the map and not over UI, footprint fits; cost ignored).
- `Assets/_Game/Scripts/Placement/GhostRenderer.cs` — `SpriteRenderer` on `Ghost` layer 10, green/red tint; `Show(worldPos, size, valid)` scales it to a footprint. Must be rotated `-90° X` to match the `Grid`, else it renders standing up.
- `Roads` Tilemap (child of `Grid`, layer 9, URP sprite material, sorting order 1). `Placement/RoadTilemapView.cs` (`GridTilemapView`) on the `Roads` GO is the only thing that paints it: auto-tiles from the 4 neighbours using 16 **runtime-generated** 64 px sprites (asphalt, curbs on unconnected sides + inner-corner nubs, dashed centre line towards each connection; dash length divides 64 so dashes line up across tiles). A road meeting the map edge head-on connects off-map (the entry); one running along the edge doesn't. Roads not connected to the edge (`RoadNetwork.IsConnectedToEntry`) are tinted red. Sprite orientation: texture +x = logical +x, texture +y = logical **−y** (see `LogicalToTileCell`). `Art/road_tile.asset` is no longer used.

### Roads (M5)
- `Assets/_Game/Scripts/Simulation/CityBuilder.Simulation.asmdef` — runtime asmdef for pure simulation logic; references `CityBuilder.Grid`.
- `Assets/_Game/Scripts/Simulation/RoadNetwork.cs` — pure C# connectivity view over the shared `GridData` roads. Constructed with `GridData`; subscribes `OnCellChanged` to mark the connectivity cache dirty. `HasRoadAccess(cell)` = a 4-neighbor is a road connected (4-connected BFS) to the map edge (the "entry"); `IsConnectedToEntry(roadCell)`. Multi-source BFS from edge road cells recomputes lazily; diagonal adjacency does **not** count.

### Simulation (M6)
- All pure, in `CityBuilder.Simulation.asmdef`: `GameSpeed` enum, `DemandSnapshot` struct, `CityModifiers` (housing/jobs/upkeep from player-placed buildings — supplied by the runtime because `BuildingDefinition` is in `Assembly-CSharp`; spatial effects come from `ServiceSource`s, see Services & power), `BalanceConfig` (ScriptableObject, all §7 tunables; asset `Scriptables/Balance/BalanceConfig.asset`), `EconomySystem`, `PopulationSystem`, `DemandSystem`, `GrowthSystem`, `SimulationSystem`.
- `SimulationSystem.Tick()` order: recount capacity → **Demand → Growth → Population → Economy**. Grown buildings are **not GameObjects in the sim**: `GrowthSystem` raises `GridData` building levels (row-major scan, new cells before upgrades, eligible = zoned + road access + not road/occupied; upgrades past level 1 also need power **in ages whose `UpgradesNeedPower`** (Industrial and Modern, and the age-less sim), see Services & power, and water in every age with a water rule (M13, see Water); level caps per age, see Ages & research); capacity per level = `BalanceConfig.CapacityForLevel` (4/8/16), scaled per built age by `CapacityModel` (M11).
- **Balance deviates from `GamePlan.md` §7** (the doc's formulas deadlock at ~1–4 pop): constant residential base demand 0.40 (doc: 0.30 only at pop 0), move-in = `ceil(Vacant * 0.20)` not scaled by demand, C/I demand denominator floor 5 (doc: 20), `Employed = min(Workers, Jobs)` (doc: `min(Population, Jobs)`). Tuned fields are tagged "(tuned)" in `BalanceConfig`.
- **M8 balance layer** (fields tagged "(tuned, M8)"): happiness base 0.70 (doc 0.80); each zone's demand × `1 − 4·(its tax − 10%)` (`DemandSystem.TaxMultiplier`, so low taxes speed growth); C and I tax each cost `0.5 × (tax − 10%)` happiness like R; pollution was `0.5 × IndustrialJobs / (Housing + Jobs)` until M12 (now local, see Land value & pollution); unemployment + pollution penalties ramp in over the first 40 residents (`SmallTownGracePopulation` — new towns are always lopsided and would otherwise deadlock); below 0.5 happiness 5%/day of residents move out (`PopulationSystem.MovedOut`) while move-in continues, so unhappy cities sit at ~80% occupancy with halved R demand. Seeded city at day 60 (10% taxes, with its plant): 212 pop, +$134/day after the plant's $100, happiness 0.60 (0.68 with 2 well-placed parks); 20% C/I with no parks stalls at ~60 pop (pre-M12 numbers; M12a: 212 pop, 0.62 / 0.70 with parks, 20% C/I stalls at ~22). M9 numbers: see Services & power. `BalanceConfig.asset` must carry the same values as the code defaults (tests use `CreateInstance` defaults, the game uses the asset).
- `PopulationSystem.Happiness` is a `HappinessBreakdown` (signed Base/Unemployment/Taxes/Pollution/Services/Power/Homeless/Technology/Heritage/Water terms; `Total` = `AverageHappiness`), recomputed in `Step` and by `RefreshHappinessBreakdown` after a load. UI explanations should read it rather than re-deriving formulas; for previews of not-yet-applied taxes use `PopulationSystem.TaxHappinessPenalty(r, c, i)` (the same term `Step` uses).
- Tests: `Tests/EditMode/Simulation/SimulationTests.cs` (create `BalanceConfig` via `ScriptableObject.CreateInstance`).

### Core / Buildings (M4, M6)
- `Assets/_Game/Scripts/Core/GameManager.cs` — owns the **single shared `GridData`** (created in `Awake` from `GridSystem.GridSize`, 64×64 in the scene; resized in place by New City / Load, never replaced — `MapSize`; on `OnResized` it repaints the ground via `GridSystem.PaintGround` and raises `GameEvents.WorldResized`), the `RoadNetwork` (`Roads`), the `BuildingDatabase`, `BalanceConfig` (`Balance`; falls back to defaults with a warning if unassigned) and the `SimulationSystem` (`Simulation`, plus `Economy`/`Population`/`Demand` shortcuts). Subscribes `TimeManager.OnTick` → `Simulation.Tick()` → raises `GameEvents`. Tracks `CityModifiers` and the `ServiceSource` list (`Simulation.Sources`) from registered buildings, and raises `GameEvents.PowerChanged` from `LateUpdate`. `TimeManager` is exposed as `Clock` (not `Time`, to avoid shadowing `UnityEngine.Time`). `RaiseStateEvents()` pushes population/demand/happiness/cash flow to the UI (per tick and after a load). Root `GameManager` GameObject in Main.unity also carries `TimeManager`, `GrowthVisuals` and `SaveGameController`.
- `Assets/_Game/Scripts/Core/ZonePalette.cs` — the one place zone colours live (toolbar swatches, zone overlay, grown buildings); `ZonePalette.Get(zone)`.
- `Assets/_Game/Scripts/Core/TimeManager.cs` — accumulates `deltaTime × speed`, fires `OnTick` once per in-game day (max 4 ticks/frame), advances Day/Month/Year. Keys 1–4 = Paused/1×/2×/4×; `SetSpeed`, `SetDate` (load; drops the partial day).
- `Assets/_Game/Scripts/Core/GameEvents.cs` — static bus (`MoneyChanged`, `PopulationChanged`, `DateChanged`, `DemandChanged`, `HappinessChanged`, `CashFlowChanged`, `InsufficientFunds`, `SpeedChanged`, `CellChanged`, `CityLoaded`, `PowerChanged`, `WorldResized(size)`, `Notification(string)` → toast, `MoneySpent(amount, worldPos)` → floating text); subscribers are cleared on `SubsystemRegistration` so disabled domain reload doesn't leak handlers. UI binds here rather than polling systems; a new event needs a line in `ResetSubscribers`.
- `Assets/_Game/Scripts/Buildings/GrowthVisuals.cs` — listens to `GridData.OnCellChanged`; per grown cell a body cube (keeps its collider for selection) + a roof piece (no collider): R/C get a darker cap (overhanging on level 1, inset rooftop box above), I gets a dark chimney. Footprint/height come from per-zone, per-level `Profile` tables (R house→apartment→tower, C shop→office→high-rise, I wide low sheds), × a deterministic per-cell height jitter (±15 %, hashed from the cell). Layer 9; colours are **shared materials, one per colour** (copies of `BuildingHouse.mat` with `_BaseColor` set, cached by colour) — never `MaterialPropertyBlock`s: they make renderers incompatible with the SRP Batcher / GPU Resident Drawer, which cost ~20 ms render thread per frame (110 ms zoomed out) on a full 96×96 map. Blocks are **pooled**: a demolished cell hides its body/roof (`renderer.enabled`/`collider.enabled = false`, not `SetActive`/`Destroy`) and new cells reuse them; a rezone reshapes the same roof block; `OnResized` destroys everything incl. the pool. A level increase plays a 0.35 s ease-out-back "pop" from 60 % scale (about the ground point); level drops/recolours apply instantly. Loading a save pops every building at once (intended). `SetColorOverride(Func<cell, Color?>)` / `RefreshColors()` let info views recolour bodies (roofs follow, chimneys stay).
- **Per-age looks (M11e).** `Scripts/Buildings/AgeVisualSet.cs` (SO, Assembly-CSharp, one per age, matched to the `AgeDatabase` by `AgeId` in `GrowthVisuals.Init(grid, ages)`; `GrowthVisuals.m_AgeVisuals` holds the 4 sets in `Scriptables/Ages/Visuals/`, generated by `Tools/gen_age_content.py`). Per zone × level 1..3 a `Style`: `Prefabs` (variants, one picked per cell by hash; empty = placeholder blocks), `Tint` (body = zone colour blended toward rgb by alpha), `Roof` (`Default` = today's cap / chimney, `Pitched` = a cube turned 45° about the ridge, ridge along X or Z per cell, `None`), `RoofColor` (alpha 0 = shaded body colour), `HeightScale`. The cell's **built age** picks the set (no age data or no set = plain). Industrial's set is plain, so an Industrial city looks exactly like before (ContentTests checks it). Medieval: timber tint, thatch / dark-wood gables, lower; Renaissance: sandstone, terracotta gables; Modern: concrete / glass tints, flat, taller. A built-age change on a grown cell (redevelopment) pops like an upgrade. Prefab instances are **pooled per prefab** (renderers/colliders disabled, never destroyed until `OnResized`); info views tint them by swapping every renderer to the shared tinted material and restore the prefab's own `sharedMaterials` afterwards. The per-cell hash keeps its low 16 bits for the height jitter, so pre-M11 skylines are unchanged. Prefab contract for the M18 art: see `AgeVisualSet` / GamePlan §12.
- `Assets/_Game/Scripts/Buildings/BuildingCategory.cs` — `enum { Zone, Road, Service, Utility, Decoration }`.
- `Assets/_Game/Scripts/Buildings/BuildingDefinition.cs` — `ScriptableObject` (Id, DisplayName, Category, Icon, Prefab, Size, Cost, UpkeepPerDay, HousingCapacity, JobsProvided, HappinessEffect, ZoneRestriction, **Height**, **CoverageRadius**, **PowerSupply**, **RequiredTech**, **ResearchPerDay**, **Pollution**, **PollutionRadius**). `Height` (drives the 3D cube visual), the two M9 fields and the two M11 fields are additions beyond `GamePlan.md` §6 (`UnlockPopulation` was removed in M11). `HappinessEffect` is **not read by any code** (the Park asset's `HappinessEffect` does nothing; park happiness comes from `CoverageRadius` + `BalanceConfig.ServiceBonusEach`); `ZoneRestriction` only routes `JobsProvided` to industrial vs commercial jobs.
- `Assets/_Game/Scripts/Buildings/BuildingDatabase.cs` — `ScriptableObject` list + `GetById`.
- `Assets/_Game/Scripts/Buildings/BuildingInstance.cs` — `MonoBehaviour` on the building prefab. Mints a **static sequential `int`** occupant id (starts at 1; `0` = empty in `GridData`) — do not use `GetInstanceID()`. `Init(grid, def, origin, rotation)` calls `GridData.Occupy` then sets the transform (position = footprint center at `y = height/2`, local scale = **unrotated** `Size` × height, Y-rotation = `rotation*90°` — the rotation maps it onto the effective footprint); `Demolish()` calls `Release`. Optional `m_Decor` child is counter-scaled to world units and placed on the top surface, so decorations (park trees) can be authored at normal proportions.
- Placeholder building art is **3D primitives** (not sprites) with `Universal Render Pipeline/Simple Lit` materials (smoothness 0): `Art/BuildingHouse.mat`, `Art/BuildingPark.mat`, `Art/TreeCanopy.mat`, `Art/TreeTrunk.mat`. `Park.prefab` has a `Decor` child with four trees (colliders removed). M11 research buildings (both Service, 2×2, height 0.12, collider-less `Decor`): `Monastery` (`monastery`, $2,000, $10/day, 3 RP/day (tuned, M11g), needs `monasticism`; `MonasteryStone`/`MonasteryRoof.mat`: nave, bell tower, cloister) and `Academy` (`academy`, $5,000, $25/day, 5 RP/day, needs `academies`; `AcademyWall`/`AcademyDome.mat`: hall, portico, dome). Park needs `commons`, Power Plant `electricity`. Prefab `Prefabs/Buildings/House.prefab` (layer 9); def `Scriptables/Buildings/House.asset`; db `Scriptables/Buildings/BuildingDatabase.asset`. M13 water buildings (placeholder primitives, shared materials incl. new `WaterBlue` / `TowerTank` / `PumpBrick.mat`, `Decor` children without colliders): `Well` (`well`, Utility 1×1, $150, $1/day, `WaterRadius` 3, no tech, `ObsoleteAge` industrial), `Fountain` (`fountain`, Service 1×1, $1,200, $8/day, `WaterRadius` 5 + `CoverageRadius` 3, needs `aqueducts`), `WaterTower` (`water_tower`, Utility 1×1, $1,500, $15/day, `WaterSupply` 500, needs `waterworks`), `PumpingStation` (`pumping_station`, Utility 2×2, $6,000, $50/day, `WaterSupply` 1,500, needs `public_sanitation`). `BuildingDefinition.ObsoleteAge` (age Id): `GameManager.IsObsolete` / `CanBuild` (unlocked and not obsolete) / `ObsoleteAgeName`; the toolbar hides obsolete buildings and placement refuses them. `Park` (`park`): Service, 2×2, $500, $5/day upkeep, height 0.25, `Art/BuildingPark.mat`, `Prefabs/Buildings/Park.prefab` — `CoverageRadius` 4 (see Services & power). `PowerPlant` (`power_plant`): Utility, 3×3, $10,000, $100/day, `PowerSupply` 600, `Pollution` 6 within 5 cells (M12), height 0.6, `Prefabs/Buildings/PowerPlant.prefab` (base block `Art/BuildingPlant.mat`; collider-less `Decor`: turbine hall, two striped chimneys, transformer — `PlantHall`/`PlantChimney`/`PlantStripe`/`PlantTransformer.mat`); gets its toolbar button automatically. `House` stays in the database but has no toolbar button (R/C/I grow from zones, not manual placement).

### Services & power (M9)
- Pure, in the Simulation asmdef. `BuildingDefinition.CoverageRadius` (cells, Chebyshev from the footprint) / `PowerSupply` (units); `GameManager` turns every placed building with either into a `ServiceSource {Origin, effective Size, CoverageRadius, PowerSupply}` and sets `SimulationSystem.Sources` on register/unregister (also during load, before `ApplySimulation`). `CityModifiers.ServiceCount` is gone.
- `CoverageSystem` — per-cell count of services in range; recomputed when `Sources` is set.
- `PowerSystem` (`Simulation.Power`; since M13 a thin subclass of `UtilityNetwork`, which holds the model below and is shared with piped water — `IsPowered` / `IsEnergisedRoad` / `UnpoweredCells` = `IsServed` / `IsCarrying` / `UnservedCells`) — a plant feeds the road cells sharing an edge with its footprint and every road 4-connected to them (**no** map-edge requirement); plants on one road network pool supply. Grown zone cells beside an energised road draw their capacity (`CapacityModel`, so scaled by built age); supply is handed out in multi-source BFS order from the (row-major sorted) seed roads, so it's independent of build order and the furthest cells go dark first. Lazy like `RoadNetwork`: any `GridData.OnCellChanged` or `SetSources` marks it dirty. `IsPowered`, `IsEnergisedRoad`, `Supply`/`Load`/`Demand`/`UnpoweredCells`, `HasHeadroom`/`TryReserve`.
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
- **Content (11d).** `Scriptables/Ages/` (Medieval, Renaissance, Industrial, Modern + `AgeDatabase`) and `Scriptables/Techs/` (32 techs + `TechDatabase`; M13 added Aqueducts (Renaissance, 160 RP) and Waterworks (Industrial, 100 RP, an Industrial starting tech)), generated by `Tools/gen_age_content.py` from its tables (edit the tables there and re-run; asset GUIDs are deterministic uuid5s of the ids, so references survive regeneration). Unlocks are **`BuildingDefinition.RequiredTech`** (the `UnlockBuilding` tech effect exists but the content doesn't use it; a tech panel lists unlocks by scanning buildings). Both databases are assigned on the scene's `GameManager`, so **the game plays with ages**: the startup city is Industrial (year 1760, all Medieval/Renaissance techs + Electricity researched), New City picks the starting age (11f), v1 saves adopt Industrial. Because the earlier ages' tech modifiers stay researched, an Industrial city is slightly richer than the pre-ages game (seeded 24² city with plant, day 60: 220 pop / happiness 0.65 vs 212 / 0.60) — the no-age sim and the 82 legacy tests are unchanged. Runtime: `GameManager.IsUnlocked(def)` / `RequiredTechName(def)`; `PlacementController` refuses locked defs (select and place; hint "Locked — research X"; `RestoreBuilding`/loads ignore locks); the toolbar hides locked building buttons and re-checks on `GameEvents.TechCompleted` / `CityLoaded`; `CityModifiers.ResearchPerDay` sums placed buildings; `GameEvents.ResearchChanged` (per tick via `RaiseStateEvents`), `TechCompleted(id)`, `AgeChanged(index)`; advancing calls `TimeManager.SetYear(age.YearOnEntering(year))` (day, month and the partial day are kept). Tests: `ContentTests.cs` (both databases validate, every starting age has something to research, the whole tree is reachable from Medieval, building `RequiredTech`s exist).
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

### Water (M13)
- Pure, in the Simulation asmdef. `WaterRule` (`Scripts/Simulation/Ages/WaterRule.cs`) = `None / Coverage / Piped`, per age as `AgeDefinition.Water` → `AgeRules.Water` (replaced the unused `UpgradesNeedWater` bool; `AgeRules.Legacy` = `Piped`, so the age-less sim needs piped water like the Industrial age). Generated by `Tools/gen_age_content.py` (`m_Water`; all shipped ages `None` until 13b).
- `ServiceSource.WaterSupply` (units into the piped network) / `WaterRadius` (cells a well or fountain waters, Chebyshev from the footprint).
- `WaterSystem` (`SimulationSystem.Water`): mode read live from `Rules.Water` (so advancing switches it with no recompute). `Coverage` = a `CoverageSystem` over `WaterRadius` (`CoverageSystem` takes a radius selector; parks keep `CoverageRadius`); `Network` = `WaterNetwork : UtilityNetwork` (`SupplyOf` = `WaterSupply`, roads carry water, a grown cell draws `DrawFor(capacity)` = `max(1, round(capacity × WaterPerCapacity 1.0))`). `HasWater(cell)` (coverage: in reach; piped: fed; `None`: always), `HasHeadroom` / `TryReserve(cell, fromCapacity, toCapacity)` (piped: network headroom for the extra draw; otherwise = `HasWater`), `DryCells`. Reallocates on `OnResized`, keeping its sources. Derived, never saved.
- Growth: upgrades past level 1 and redevelopment need water in any age whose rule isn't `None`, checked after power (`GrowthBlocker.NoWater` / `WaterAtCapacity`). `GrowthSystem.TryReserveUtilities` checks power and water headroom first and then reserves both, so a cell short of water never holds power. `GrowthSystem`'s optional `water` argument (null = no gate) keeps the old constructors working.
- Happiness: `ServiceStats.UnwateredHousingShare` (homes without water, housing-weighted; 0 when the rule is `None`) → `HappinessBreakdown.Water` = `−WaterPenalty (0.05) × share`, ramped in with `SmallTownGracePopulation` like Power.
- `BalanceConfig` (M13): `WaterPerCapacity` 1, `WaterPenalty` 0.05.
- Content (13b): Medieval / Renaissance `Coverage`, Industrial / Modern `Piped` (`Tools/gen_age_content.py`, last column of `AGES`); buildings and techs: see Core / Buildings and Ages & research. `EngagedCity` digs a well in the middle of any block with a dry zoned cell (up to two a day), builds towers / pumps like plants (the cheapest affordable one covering the shortfall + 75), saves for utilities while power or water is short, and puts a fountain in a free middle cell when a well took a block's park spot.
- Tests: `Tests/EditMode/Simulation/WaterTests.cs` (coverage reach, the piped network's BFS order / pooling / supply, `DrawFor`, resize, the age-less gate with headroom, power checked before water, the both-or-neither reservation, Medieval wells, advancing into a piped age, the Water term). `SeededCity`'s plant block also supplies water (`WaterSupply` 600 piped + a map-wide `WaterRadius`; without a plant a 1×1 source at `(0,11)`; `waterSupply: 0` = dry); `TestAges` uses Coverage for Medieval / Renaissance and Piped for Industrial / Modern.

### Save / load (M8)
- Single slot: `Application.persistentDataPath/city.json` (JSON via `JsonUtility`). F5 / F9 or the HUD **Save / Load / New** buttons.
- Pure, in the Simulation asmdef (`Scripts/Simulation/Save/`): `SaveData` (public fields for `JsonUtility`; `Version` = `SaveData.CurrentVersion` = **2**; a format change bumps it **and adds a step to `SaveMigrations`** plus a test — older files are migrated on read, newer ones rejected), `SaveMigrations.TryMigrate(data, ages, techs)` (v1 → v2 marks the city `Age = SaveData.NoAge` with empty research and zeroed per-cell arrays; then, when the game has age/tech databases, a `NoAge` city is **adopted as Industrial**: `TechSystem.StartingTechs(ages, techs, legacy)` researched (all earlier techs + Electricity), every grown cell built in Industrial, `Year += StartYear − 1` (year 1 → 1760); built ages above the city's age are clamped; an unknown age index is rejected), `BuildingRecord {Id, X, Y, Rotation}`, static `SaveSystem` (`Capture`, `CreateNew`, `ApplyGrid` (resizes the map to the save's `Width`/`Height`), `ApplySimulation`, `ToJson`/`TryFromJson(json, out data, out error, ages = null, techs = null)` with validation (sizes capped at `MaxMapSize` 256) and migration, `TryWrite` via temp file / `TryRead(path, …, ages, techs)`). `CreateNew(w, h, config, ages = null, techs = null, startAge = -1)`: with ages, the start age's year, `StartingMoney` and starting research (−1 = Industrial). `ApplySimulation` restores research first (`TechSystem.Restore`, or `StartNew(Industrial)` for data not migrated with these databases) and returns how many saved tech/project Ids were dropped as unknown/invalid (`SaveGameController` logs a warning; never a failure). Sim restore hooks: `SimulationSystem.Restore`, `EconomySystem.Restore`, `PopulationSystem.Restore` (recomputes capacity/employment/demand so the UI is right before the next tick).
- Saved: calendar + speed, money + last day's income/expense, R/C/I taxes, population, happiness, zones/roads/levels, placed buildings; v2: `Age` (index, `NoAge` = saved without ages), `Researched` (ids, database order), `ActiveResearch` / `ResearchQueue` (project ids, `advance:<ageId>` for advancing), `ResearchProgress` (the RP pool), `BuiltAges` / `Historic` (row-major bytes). Not saved: occupant ids (re-minted on load), `TimeManager`'s partial-day accumulator.
- `Scripts/Save/SaveGameController.cs` (on `GameManager`) rebuilds in a fixed order: `ClearAllBuildings` → `ApplyGrid` → `RestoreBuilding` per record (unknown/blocked ones are skipped with a warning) → `ApplySimulation` (needs building modifiers in place) → `SetDate`/`SetSpeed` → `RaiseStateEvents` + `GameEvents.CityLoaded`. Files are validated (and migrated, with `GameManager.Ages`/`Techs`) before anything is torn down. `NewCity()` applies `SaveSystem.CreateNew` at the current size; `NewCity(size)` at another size.
- `Scripts/UI/NewCityDialog.cs` on `Prefabs/UI/NewCityDialog.prefab` (last child of `UI`; script on the container, child `Blocker` = dimmed full-screen raycast blocker + centred `Panel` is what's toggled): map size buttons 32/64/96 (current size preselected; selected = non-interactable, the HUD speed-button convention), Cancel / Esc close, Create → `SaveGameController.NewCity(size, startAge)`; the starting-age row is described under Ages & research → UI. It subscribes to `GameMenu.NewRequested` through its own `m_GameMenu` scene ref, so the HUD prefab instance carries no reference to it.
- `Scripts/UI/GameMenu.cs` on the HUD prefab root (buttons in `HUD/Game`): Load is disabled until a save exists; New raises `GameMenu.NewRequested`, which opens the New City dialog. `TaxPanel` resyncs its sliders on `CityLoaded`.
- Tests: `Tests/EditMode/Simulation/SaveSystemTests.cs`, including save-at-day-30-then-continue = uninterrupted run; `SaveMigrationTests.cs` (the real 24² v1 save in `Fixtures/city_v1_24x24.json` migrates to Industrial and plays exactly like the pre-ages game; v2 round trip mid-research and mid-redevelopment = uninterrupted run; unknown tech ids dropped). Shared age fixture for tests: `TestAges.cs` (4 ages shaped like §12, 8 techs, `TestAges.Advance(sim)`).

### UI (M7)
- `UI` Canvas (Screen Space Overlay, `CanvasScaler` 1920×1080, match 0.5, layer 5) + `EventSystem` with **`InputSystemUIInputModule`** (the legacy `StandaloneInputModule` throws under Input System only). TextMeshPro Essentials live in `Assets/TextMesh Pro/` (TMP's required location — not under `_Game`). `PlacementController` ignores map clicks when `EventSystem.current.IsPointerOverGameObject()` (the only UI-blocking check; there is no IMGUI left).
- `Assets/_Game/Scripts/UI/HUDController.cs` on `Prefabs/UI/HUD.prefab` (top bar, child of `UI`): date + speed buttons (active speed = non-interactable, so its disabled colour is the highlight), age + research (`HUD/Age`, M11), money + net/day, Taxes / Research buttons (`HUD/Budget`), pop/housing, employed/jobs, power demand/supply (`HUD/Power/PowerText`, hidden until power is unlocked), happiness meter, R/C/I demand meters. Subscribes to `GameEvents` in `OnEnable` and pulls current values once in `Start` (events only fire on change). `UIMeter.cs` = sprite-free bar (fill rect's anchorMax set to the 0..1 value).
- Events added for UI: `GameEvents.HappinessChanged`, `CashFlowChanged(income, expense)` (both raised per tick by `GameManager`), `InsufficientFunds(cost)` (raised by `PlacementController` when a road/building can't be paid for); `PlacementController.ModeChanged` (instance event on every tool change; read `CurrentMode`/`ZoneBrush`/`SelectedBuilding`).
- `Assets/_Game/Scripts/UI/ToolbarController.cs` on `Prefabs/UI/Toolbar.prefab` (bottom-centre, sections Transport / Zoning / Buildings / Tools / View). `VIEW` = `PowerView` + `CoverageView` + `PollutionView` + `LandValueView` + `AgeView` `ToolButton`s (Power/Age hidden while not `InfoOverlay.IsAvailable`); their active state follows `InfoOverlay.ViewChanged`; `m_InfoOverlay` is a scene override on the Toolbar instance. Fixed tools (Road, R/C/I/Unzone, Demolish) are `ToolButton` prefab instances wired in the prefab; **Service/Utility `BuildingDefinition`s get a button generated at runtime** from `Prefabs/UI/ToolButton.prefab` (named `Build_<id>`). Clicking the active tool again clears it. Highlight follows `PlacementController.ModeChanged` (so hotkeys update it too); affordability follows `GameEvents.MoneyChanged` (`Button.interactable`). Shared tooltip panel floats above the bar.
- **Selection (no tool active):** `PlacementController` click → `SelectCell` (road, placed building, grown cell or zoned land; empty land/off-map clears). Exposes `HasSelection`/`SelectedCell`/`SelectionChanged`, `GetBuildingAt`, public `DemolishAt`/`Unzone`/`ClearSelection`. Any tool change or `Cancel` clears it; it auto-clears if the cell becomes empty. Highlight = `SelectionHighlight` (a second `GhostRenderer`, blue) raised onto the top of whatever stands there via a downward raycast on the Buildings layer — **building prefabs need a collider** (House/Park have `BoxCollider`; grown cubes get one from `CreatePrimitive`).
- `Assets/_Game/Scripts/UI/SelectionPanel.cs` on `Prefabs/UI/SelectionPanel.prefab` (top-right; script on the container, visuals on child `Panel` which it toggles). Describes building / road (edge-connected?) / grown cell (level, capacity) / zone, with the reason it isn't growing from `GrowthSystem.GetBlocker(cell, demand)` → `GrowthBlocker` (pure, tested). Action button = Demolish or Unzone. Refreshes immediately on `SelectionChanged`; `GameEvents.CellChanged`/`DateChanged` only mark it dirty and it refreshes at most once per frame in `LateUpdate` (a growth tick can change dozens of cells).
- `Assets/_Game/Scripts/UI/TaxPanel.cs` on `Prefabs/UI/TaxPanel.prefab` (top-left under the money readout; toggled by `HUD/Budget/TaxesButton`, which lives in `HUD.prefab`). R/C/I sliders in whole percent, 0–`m_MaxPercent` (20), writing `EconomySystem.TaxResidential/Commercial/Industrial` immediately; the hint shows the combined R/C/I tax happiness penalty (`PopulationSystem.TaxHappinessPenalty`) and yesterday's income/costs.
- `Assets/_Game/Scripts/UI/NotificationController.cs` on `Prefabs/UI/Notifications.prefab` (full-screen, non-raycasting root): fading toast above the toolbar (`ShowToast(text)`, neutral dark panel — colour individual messages with TMP rich text; unscaled time so it fades while paused). Toasts: `InsufficientFunds`, `Notification`, population milestones (`m_PopulationMilestones` 50…2000), once per drop below the low-happiness threshold, and the M9 power toasts; `CityLoaded` re-syncs both so loading doesn't spam. Red debt banner under the HUD while `MoneyChanged` < 0.
- Same prefab: `CursorHint.cs` (child `CursorHint` panel follows `InputReader.Pointer`, shows `PlacementController.CursorHint`, red when invalid) and `FloatingTextController.cs` (pooled copies of the hidden `FloatingText` TMP template, tracking a world point; driven by `GameEvents.MoneySpent`). Both convert screen → local with a null camera (overlay canvas), so their rects are anchored at the parent's centre. Scene-instance refs: `CursorHint.m_Placement`/`m_InputReader`.
- `Assets/_Game/Scripts/UI/HappinessTooltip.cs` on `HUD/Happiness` (transparent raycast `Image` as hover target; panel `HUD/Happiness/Tooltip` with `LayoutElement.ignoreLayout`): lists non-zero `HappinessBreakdown` terms. `HUDController` colours the happiness label red below the threshold.
- `Assets/_Game/Scripts/UI/ToolButton.cs` — background colour = active state, `Button.interactable` = affordable (also dims the label and turns the cost red); optional zone swatch strip and cost line; raises `Hovered`/`Unhovered` for the tooltip.
- `Assets/_Game/Scripts/Core/PerfBenchmark.cs` — development players only: run with `-perfBenchmark` to build a fully grown 96×96 city (11.5k blocks), time idle / 4× / per-frame rezone, demolish+regrow and road edits plus a zoomed-out view (frame, main thread, render thread and GPU medians via `FrameTimingManager`), write `persistentDataPath/perf_benchmark.txt` + screenshots and quit. Never saves. Since M11e it mixes built ages (each 4×4 block between roads gets one, cycling through every age) so every age style is drawn. How to run it and the baseline numbers (~1.1 ms frames at default zoom on the reference machine): `perf-benchmark` skill.
- `Assets/_Game/Scripts/UI/DebugPanel.cs` on `Prefabs/UI/DebugPanel.prefab` (top-left, hidden; **F1** toggles; disabled outside Editor/development builds via `Debug.isDebugBuild`): Seed test city (`PlacementController.DebugSeedCity`), +$10,000 (`Economy.Refund`), Skip 30 days (`TimeManager.DebugAdvanceDays`, which runs the normal `OnTick` + calendar path instantly). The old IMGUI `BuildToolbar` is gone.
