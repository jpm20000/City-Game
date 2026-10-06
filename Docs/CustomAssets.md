# Adding your own assets to Chronopolis

*Draft from M25a (2026-10-06). The **Add Custom Building…** wizard and the in-game **Build menu** arrive in
M25b / M25c; the manual route below works today and is what the wizard automates. Plan:
[`milestones/v1.1/M25.md`](milestones/v1.1/M25.md).*

Custom assets are added **in the Unity Editor** (the project, not the shipped game). The game does not load models
from disk at run time.

## Two kinds of asset

| Kind | What it is | Where it plugs in |
|---|---|---|
| **Placeable building** | Something the player places with a click: a service, a utility, a decoration, or a home / shop / factory you place yourself. | A `BuildingDefinition` asset + a prefab, listed in the `BuildingDatabase`. |
| **Growable variant** | A look for zoned blocks that grow by themselves (an age x zone x level x density slot). | A prefab added to a slot of an `AgeVisualSet`. |

A placeable that has `HousingCapacity` or `JobsProvided` adds that housing / jobs to the city while it stands (the
same path the shipped House uses), so "a custom residential building" is a placeable with housing, not a new zone.

## The art contract

The two kinds use **different** contracts, because the game positions them differently.

### Growable variants (one 1x1 cell)

- pivot at **ground level** (lowest point at y = 0, within 0.03) and at the **centre of the cell**;
- inside the cell (a 0.04 margin is allowed), facing local **+Z** (the game turns it toward the street);
- layer **9 (Buildings)**, a **collider on the root**;
- **shared materials only**, at most **two** per prefab (the shipped kit uses `Assets/_Game/Art/Kit/Kit.mat`, one URP
  Simple Lit palette material); never per-instance materials or `MaterialPropertyBlock`s, they break batching;
- body height by level (1: 0.15-1.2, 2: 0.4-2.0, 3: 0.6-3.4 cells).

`CityBuilder > Validate Art` checks every slot (`ArtContract.Validate`).

### Placeable buildings (a footprint of any size)

The game **scales the whole prefab** to `Size.x` x `Height` x `Size.y` and stands it at half its height
(`BuildingInstance.ApplyTransform`). So a placeable prefab is modelled as a **unit box**, not at final size:

- every mesh inside the unit box around the pivot: x, y and z each within -0.5..0.5 (0.02 margin); the pivot is the
  **centre** of the box, not the ground. A 3x2 hospital with `Height` 0.1 is still a 1x1x1 cube in the prefab;
- a **`BuildingInstance`** component on the root, **layer 9**, a **collider on the root**;
- an optional child assigned to `BuildingInstance.Decor` holds decorations (the Park's trees) authored in world
  units: the game counter-scales it and puts it on the top surface, and it is left out of the unit-box check;
- shared materials only, at most **six** per prefab (the shipped placeables use one to five small flat-colour
  materials). Nothing is asked of the facing: rotation is the player's (R).

Because the scale is non-uniform, a detailed model gets stretched by the footprint and height. Model placeables as
simple masses (as the shipped ones are) or keep `Size` and `Height` close to the proportions of your model.
`CustomBuildingValidator` runs these rules through `ArtContract.ValidatePlaceable`, and also checks: unique `Id`,
existing `RequiredTech`, non-negative cost / upkeep / housing / jobs, size at least 1x1, a prefab present. Both report
numbers, e.g. `bounds ... leave the unit box around the pivot`.

## Making a placeable building by hand

1. **Prefab:** build your model inside a unit box (see above) under a root object, save it as a prefab under
   `Assets/_Game/Prefabs/Custom/`; set layer 9, add a `BoxCollider` and a `BuildingInstance` on the root.
2. **Definition:** *Create > CityBuilder > Building Definition* in `Assets/_Game/Scriptables/Buildings/Custom/`.
   Set `Id` (lowercase, no spaces, unique, never changed later: saves refer to it), `Display Name`, `Category`
   (Service, Utility or Decoration), `Prefab`, `Size`, `Cost`, `Upkeep Per Day`, and the effects you want
   (`Housing Capacity`, `Jobs Provided`, `Happiness Effect`, `Coverage Radius`, the civic and power / water fields).
   `Required Tech` is the id of a tech that unlocks it; empty = available from the start. Leave `Toolbar Group` on
   Auto unless you want it elsewhere.
3. **Database:** add the definition to `Assets/_Game/Scriptables/Buildings/BuildingDatabase.asset`. The toolbar's
   group flyouts pick it up from there.
4. **Check:** run the EditMode test `CustomBuildingValidatorTests`, then enter Play mode, unlock the tech (or use
   the debug panel) and place it.

## Growable variants by hand

Add the prefab to the matching style table of the age's `AgeVisualSet` (`Assets/_Game/Scriptables/Ages/Visuals/`):
`m_Residential`, `m_Commercial`, `m_Industrial` for Medium, the `...Low` / `...High` tables for the other densities,
one entry per level 1-3, each with a list of variants (one is picked per cell). Run `CityBuilder > Validate Art`.

## Testing in Play mode

Enter Play mode on a new city, place or zone, and look at it from all four camera views (Q / E): a prefab that
looks right from one side may have an open back. Save, load, and check it is still there.

## Things not to do

- Do not change an `Id` after release: saved cities would lose the building.
- Do not edit generated content (`Tools/gen_age_content.py`, the building kit generator): put your assets under the
  `Custom/` folders, which the generators never touch.
- Do not add a material per prefab; reuse `Kit.mat` or share one material across your assets.
