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

**Status (2026-10-02):** M0–M8 implemented — the vertical slice is complete. M7 shipped as UGUI + TextMeshPro
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

- Water grid (M9 ships power + coverage; water would reuse the same machinery).
- Individual citizen agents (NavMesh) layered on top of the statistical model.
- Freeform road drawing and zoning along curves.
- Save/load is designed in but implemented late.
- Progression, disasters, day/night, achievements.

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
- **9d UI wiring.**
  - HUD power readout `used / supply` (red when short), from a new `GameEvents.PowerChanged`.
  - `SelectionPanel`: Powered / Unpowered status, coverage count, and plant supply and load.
  - `HappinessTooltip`: a "Power" line.
  - Toasts: "Power shortage — N buildings dark" once per shortage, and "First power plant
    online".
- **9e Balance + play-through + docs.**
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
