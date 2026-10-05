# Chronopolis 1.1 — roadmap (M20–M26) (plan, 2026-10-05)

*Roadmap for the first update after 1.0.0. Each milestone has its own file in this folder (`Mxx.md`), an outline that is expanded into a full plan
when it starts (`milestone-workflow`); this file holds the order and the decisions that shape more
than one milestone. Where it fits: `../../GamePlan.md` §12 (stub), systems as built: §13.*

## Order and why

Feedback items marked **IMPORTANT** come first and all land in **M20**. They are all interface work (camera,
toolbar, New City dialog) and touch no sim numbers and no save format. They ship with the rest of 1.1 (no early
1.0.1, asked 2026-10-05).

The rest is ordered by size and dependency: small balance and feedback fixes first, then roads (save format),
then zoning density and the Modern skyline (save format, art), then Goods (a new sim system that changes the
baseline), and last custom assets, which builds on the M20 toolbar groups and the M23 art.

| # | Milestone | Feedback items | Sim / baseline | Save | Size |
|---|---|---|---|---|---|
| **M20** | **Camera & toolbar** (IMPORTANT) | Rotatable camera · building toolbar groups · visible tutorial / disasters toggles · condense View | none | none | M |
| M21 | Progression & feedback | Research costs scale too fast · paved before avenues · repair icon over buildings | research only (re-record `AgeBalanceTests` timings) | none | S |
| M22 | Road layout | Avenues 2 tiles wide · one-way highways | traffic graph becomes directed; age-less baseline unchanged | v8 (road direction) | L |
| M23 | Density & Modern skyline | Density controls for R / C / I · Modern L3 residential and commercial look too alike, not skyscrapers · new Modern Skyscrapers and Office blocks | capacity per density; default density = today | v9 (per-cell density) | L |
| M24 | Goods | Goods produced by industry, needed by commercial and residential | new system, identity when off (age-less baseline unchanged) | v10 (goods stock + RNG-free) | XL |
| M25 | Custom assets | Guidance on adding your own assets · searchable placement menu (e.g. custom residential) | none | none (ids already saved) | M |
| M26 | Release 1.1 | — | none | — | S |

## Milestone files

One file per milestone, like `../v1.0/` (M10–M19). Outlines become full plans when the milestone starts.

- [M20 — Camera & toolbar (IMPORTANT)](M20.md) — **done 2026-10-05**
- [M21 — Progression & feedback](M21.md) — **done 2026-10-05**
- [M22 — Road layout](M22.md) — planned (2026-10-05), save v8, in progress
- [M23 — Density & Modern skyline](M23.md) — outline, save v9
- [M24 — Goods](M24.md) — outline, save v10
- [M25 — Custom assets](M25.md) — outline
- [M26 — Release 1.1](M26.md) — outline

## Cross-milestone decisions (recommended defaults — change before the milestone that uses them)

| Topic | Default | Why / alternative |
|---|---|---|
| Ship M20 early | **Asked 2026-10-05: no.** Everything ships together as 1.1 after M26. | — |
| Camera rotation | **Asked 2026-10-05: 90° snaps** (4 views), animated over ~0.25 s; Q / E, rebindable. | Free rotation breaks the 2:1 iso look the art and the tile sprites are made for; snaps keep it while showing all four sides. |
| Skyscrapers / office blocks | **High density in the Modern age** = Skyscrapers (residential) and Office blocks (commercial), not new zone types. | A 5th / 6th `ZoneType` touches demand, growth, capacity, views, saves and the harnesses. Density does the same job and also answers "density controls". Only cells the player zones High can ever become them (see Density). |
| Density | **Asked 2026-10-05: the player zones Low, Medium or High**, painted with the zone tool (a density selector beside R / C / I). Density is a **hard ceiling chosen by the player and never changes by itself**: a Medium cell grows through its own levels 1–3 and never becomes High, whatever the land value, age or demand. Only repainting changes it (repainting lower redevelops a building that is above the new band). Each density has its own levels 1–3, capacity band and art (Low L3 = a large house, never an apartment). Migrated and harness zones are Medium with today's capacity numbers (identity), so the baselines hold. | Low: small capacity, +land value, −traffic. High: unlocked by age / tech (see M23), more capacity, needs water + power, more traffic and pollution. |
| Avenue width | **2 cells wide**, laid as a pair by the Avenue tool; each cell is a normal road cell of tier Avenue. | A true 2-cell road object would change `GridData`'s one-road-per-cell model everywhere. |
| One-way highways | **Highways only**, direction set by drag direction, one byte per road cell. Other tiers stay two-way. | Keeps the directed graph small and the age-less sim (no highways) unchanged. |
| Goods depth | **One goods resource**, made by industry, sold by commercial; homes need goods within reach of commercial. No trade routes or per-type goods in 1.1. | A full multi-resource chain is a 2.0-sized feature; one resource tests the idea. |
| Custom assets scope | **Asked 2026-10-05: in-Editor pipeline** (documented + an Editor wizard that makes the `BuildingDefinition` and checks the art contract) and an in-game searchable placement menu. **No** loading of models from disk in the shipped player. | Runtime mod loading (glTF import, sandboxing, saves that reference missing mods) is its own milestone; open question for after 1.1. |

## Risks across 1.1

- **Camera rotation** touches everything that assumes one view: picking, the pan axes, `ApplyBounds`, light tones,
  sorting, the showcase drift, building-face art (back faces were never seen). Isolated in M20 so later milestones
  build on it.
- **Save versions 8–10** in three milestones: each adds one migration step with a test; the v1 fixture must keep
  migrating.
- **Goods** is the only milestone that changes the real-content baseline; it goes after the cheaper milestones so
  a slip doesn't hold the rest of 1.1.
