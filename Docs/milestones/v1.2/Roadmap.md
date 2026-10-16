# Chronopolis 1.2 — roadmap (M27–M31) (plan, 2026-10-16)

*Roadmap for the second update. Each milestone has its own file in this folder (`Mxx.md`), a full plan written before
any code (`milestone-workflow`); this file holds the order and the decisions that shape more than one milestone. Where
it fits: `../../GamePlan.md` §12 (stub), systems as built: §13.*

## Feedback items (2026-10-16)
1. Road / Avenue / Highway tools: hold the button to drag a **straight** line (only along x or z), release to build.
2. The Demolish tool can be held down instead of clicking every cell.
3. Saves in the browser version disappear when the page is refreshed.
4. More research (RP) needed to advance to the next age.
5. Low-density look: homes are small houses that get nicer by level; shops are markets at L1 / L2 and single-storey
   shops at L3; industry is farmland at every level.

## Order and why

The two input items share `PlacementController` and the play-through drivers that drag on the map, so they are one
milestone and go first (pure UI, no sim or save change). The web-save bug is next: it loses players' cities and is
independent of everything else. Age pacing is a content-only balance change; it goes before the art so the art
milestone's screenshots and the release regression run on final numbers. The low-density art is the largest piece
(generator features and 4 ages × 9 slots × 3 variants) and touches no sim. Release last.

| # | Milestone | Feedback items | Sim / baseline | Save | Size |
|---|---|---|---|---|---|
| **M27** | [Drag tools](M27.md) — **done (2026-10-16)** | 1 straight road lines · 2 demolish drag | none | none | M |
| M28 | [Web saves](M28.md) | 3 browser saves survive a refresh | none (desktop path unchanged) | none (same files) | S–M |
| M29 | [Age pacing](M29.md) | 4 more RP to advance | research only: re-record `AgeBalanceTests` timings | none | S |
| M30 | [Low-density look](M30.md) | 5 small homes, markets / shops, farmland | Low industry (farms) pollutes less: density timelines re-recorded; age-less baseline unchanged (it zones Medium) | none | L |
| M31 | [Release 1.2](M31.md) | — | none | — | S |

## Cross-milestone decisions (answered 2026-10-16)
| Topic | Decision | Alternative (not taken) |
|---|---|---|
| Release vehicle | **All five ship together as 1.2.0 (M31)** (asked) | Ship M28 alone first as a 1.1.1 web hotfix |
| Web host | **https://jpm20000.com/** — the game page is `projects/city-game/play/` on the site (its own `index.html`, stable path) | — |
| Save format | No milestone bumps `SaveData.CurrentVersion` (none adds persisted state) | — |
| Age-less baseline | Untouched by every milestone (M29 only rescales content; M30 is art) | — |
| Drivers | Every driver that drags on the map is updated in M27 (straight segments), re-run in M31 (M27: all already dragged straight rows; none changed) | — |
