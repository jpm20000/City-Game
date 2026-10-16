# UI art: sizing prompts for ChatGPT

Sizing and layout wording to paste into ChatGPT image prompts, one block per kind of UI art. Each block covers
canvas, grid, margins, proportions and background only. Put your own style prompt (palette, outline, lighting, age
theme) in front of it, the same style text for every sheet.

## Why these numbers

- The canvas is laid out at **1920 x 1080** (`CanvasScaler` Scale With Screen Size, match 0.5). Every size below in
  *design px* is a size at that resolution.
- The largest it ever draws: a 4K screen (x2) at the top UI scale 150% (`GameSettings.UiScales` 0.8 to 1.5) = **x3**.
  So art ships at **3x design size** and stays sharp everywhere.
- ChatGPT doesn't hit exact pixels and only makes a few canvas sizes (1024 x 1024, 1536 x 1024, 1024 x 1536). So the
  prompts describe **proportions** (grid cells, margins as a fraction of the cell), and the processing script
  crops and resizes to the ship size.
- **Small-detail rule:** at the smallest setting (1080p, 80%) a 32 px icon is about 26 screen pixels. No detail
  narrower than **1/16 of the icon width**, outlines about **1/20 of the icon width**.

## Rules for every prompt

```
Transparent background (alpha), PNG. Flat front view, no perspective, no camera tilt.
No text, letters or numbers anywhere. No drop shadow or glow that spreads outside the shape.
Nothing touches or crosses the edge of the image or of its grid cell.
```

If a transparent background doesn't come out, use this background line instead (the script keys it out):
`Solid flat pure green background #00FF00, no gradient, and no green anywhere in the artwork.`

## Summary

| Kind | Canvas | Layout | Design px | Ship px (3x) |
|---|---|---|---|---|
| HUD icons | 1024 x 1024 | 4 x 4 grid | 24 x 24 | 72 x 72 |
| Tool, group and view icons | 1024 x 1024 | 4 x 4 grid | 32 x 32 | 96 x 96 |
| Building icons | 1024 x 1024 | 4 x 4 grid | 32 (toolbar) / 48 (panels) | 144 x 144 |
| Panel and window frames | 1024 x 1024 | 1 frame | 9-slice, border 8 | 192 x 192, border 24 |
| Tooltip and toast frames | 1024 x 1024 | 1 frame | 9-slice, border 5 | 144 x 144, border 14 |
| HUD top bar | 1536 x 1024 | 1 strip | 3-slice, 1920 x 76 | about 1030 x 228, ends 170 |
| Buttons (4 states) | 1024 x 1024 | 2 x 2 grid | 9-slice, border 8 | 96 x 96, border 24 |
| Meters (track and fill) | 1536 x 1024 | 2 bars stacked | 3-slice, height 10 | about 324 x 30, ends 15 |
| Toggle, tick, slider handle, small marks | 1024 x 1024 | 2 x 2 grid | 20 x 20 | 60 x 60 |
| Main-menu background | 1536 x 1024 | full frame | 1920 x 1080 | 1536 x 864 (upscale optional) |
| Logo / title emblem | 1536 x 1024 | 1 emblem | 380 wide | 1140 wide |
| Event popup illustrations (optional) | 1536 x 1024 | full frame | 360 x 203 | 1080 x 608 |
| Cursor (optional) | 1024 x 1024 | 2 x 2 grid | 32 x 32 (Windows cursor) | 32 and 64 |

## The blocks

### 1. HUD icons (money, population, jobs, power, water, happiness...)

They sit beside the HUD readouts (rows 22 to 28 design px tall). About 20 are needed: money, net income up and
down, population, housing, jobs, power, water, happiness (happy, neutral, unhappy), research, age, date, pause,
play x1 / x2 / x3, taxes, budget, save, load, menu, settings. That's two sheets.

```
A sprite sheet of 16 icons in a 4 x 4 grid of equal square cells, square 1024 x 1024 image (each cell 256 x 256).
Each icon is centred in its cell and fits inside the middle 80% of the cell, leaving an empty margin of at least
10% of the cell on every side. All 16 icons share the same visual size, line weight and lighting.
Simple bold silhouettes readable at 24 pixels: no detail thinner than 1/16 of the icon width, outlines about 1/20
of the icon width. No grid lines or cell borders drawn.
Row 1, left to right: [icon], [icon], [icon], [icon]. Row 2: ... Row 3: ... Row 4: ...
```

### 2. Tool, group and view icons (toolbar)

Tool buttons are 104 x 64 design px with the label under the icon, so the icon area is about 32 x 32.
- **Tools:** road per tier (dirt, cobble, paved, avenue, highway), pipe, residential, commercial, industrial,
  unzone, low / high density, demolish.
- **Groups:** Utilities, Services, Health, Education, Parks.
- **Views:** Off, Power, Water, Parks, Pollution, Value, Traffic, Ages, Crime, Fire, Health, Schools.

That's about 30 icons, two sheets.

```
A sprite sheet of 16 icons in a 4 x 4 grid of equal square cells, square 1024 x 1024 image (each cell 256 x 256).
Each icon is centred in its cell and fits inside the middle 80% of the cell, leaving an empty margin of at least
10% of the cell on every side. All icons share the same visual size, line weight and lighting.
Bold, simple silhouettes readable at 32 pixels: no detail thinner than 1/16 of the icon width, outlines about 1/20
of the icon width. Each icon is a single object or symbol, no background plate or circle behind it.
No grid lines or cell borders drawn.
Row 1, left to right: [icon], [icon], [icon], [icon]. Row 2: ... Row 3: ... Row 4: ...
```

### 3. Building icons (one per service / utility building)

About 19 buildings (Well, Fountain, Water Tower, Pumping Station, Power Plant, Park, Watch House, Constabulary,
Police Station, Bucket Brigade, Fire Engine House, Fire Station, Apothecary, Hospital, Medical Centre, Monastery,
Academy, University, Research Lab). Shown at 32 in the toolbar flyouts and up to 48 in the selection panel, so they
ship at 144.

```
A sprite sheet of 16 building icons in a 4 x 4 grid of equal square cells, square 1024 x 1024 image (each cell
256 x 256). Each building is a small isometric-style object seen from the same 3/4 angle, standing on the same
invisible ground line about 15% above the bottom of its cell, centred left to right, and fitting inside the middle
80% of the cell with at least 10% empty margin on every side. All buildings drawn at the same scale and footprint
so they read as one set; no ground tile, no base plate, no scenery around them.
Readable at 32 pixels: one strong silhouette per building, no detail thinner than 1/16 of the icon width.
No grid lines or cell borders drawn.
Row 1, left to right: [building], [building], [building], [building]. Row 2: ... Row 3: ... Row 4: ...
```

### 4. Panel and window frames (side panels, dialogs, menus)

9-sliced: the corners stay fixed, the edges stretch along their length and the centre fills the panel. The border
is 1/8 of the image, so after resizing to 192 px it's 24 px (8 design px).

```
A single rectangular UI panel frame filling the whole square 1024 x 1024 image edge to edge (the outer edge of
the frame is the edge of the image, nothing outside it).
The border is exactly the same thickness on all four sides: 1/8 of the image width (about 128 pixels).
All decoration (corner ornaments, rivets, bevels) stays inside the four corner squares of 128 x 128 pixels.
The four edges between the corners are plain and uniform along their length (the same pattern at every point),
so they can be stretched.
The centre inside the border is one flat, even fill with no texture, gradient, vignette or pattern
[or: the centre is fully transparent].
Symmetric left to right and top to bottom.
```

### 5. Tooltip and toast frames

Same as 4 with a thinner, plainer border: 1/10 of the image, which becomes 14 px at 144 (about 5 design px).

```
A single small UI tooltip frame filling the whole square 1024 x 1024 image edge to edge.
The border is exactly the same thickness on all four sides: 1/10 of the image width (about 100 pixels), with
slightly rounded corners whose rounding stays inside the four 100 x 100 pixel corner squares.
Minimal decoration: a thin outline and an inner bevel only. The edges between the corners are plain and uniform
along their length. The centre is one flat, even fill with no texture or gradient.
Symmetric left to right and top to bottom.
```

### 6. HUD top bar

The HUD bar runs the whole screen width and is 76 design px tall. It's 3-sliced: the left and right ends stay fixed,
the middle stretches sideways.

```
A single horizontal UI bar, landscape 1536 x 1024 image. The bar spans the full width of the image, edge to edge,
and is centred vertically with a height of exactly 1/3 of the image height (about 340 pixels); the space above
and below it is empty.
The left and right ends (each about 1/6 of the image width) may carry end-cap decoration; everything between the
two ends is plain and uniform along its length, so it can be stretched sideways.
The face of the bar is one flat, even fill with no texture or gradient. The top and bottom edges are straight and
the same along the whole length.
```

### 7. Buttons (normal, hover, pressed, disabled)

Buttons are 9-sliced too; the four states must line up exactly. **Easier option:** ask for the normal state only,
in neutral light grey, and let Unity tint it for the other states (and for the coloured zone buttons). Then use
only the first line below and a single shape filling the middle of the image.

```
Four UI button states in a 2 x 2 grid of equal square cells, square 1024 x 1024 image (each cell 512 x 512).
Each cell holds the same rounded-rectangle button with an identical shape, size and position: the button fills
the middle 88% of its cell (about 450 x 450 pixels), centred, with the same empty margin on every side.
Corner rounding stays inside the outer 1/4 of the button on each side. The border and bevel are the same thickness
on all four sides, about 1/4 of the button width, and the edges between the corners are plain and uniform.
The centre of each button is a flat, even fill. No label, icon or text.
Top left: normal. Top right: hover (slightly brighter). Bottom left: pressed (slightly darker, bevel inverted).
Bottom right: disabled (flat, desaturated, low contrast).
Only colour and lighting differ between the four; the outline shape is pixel-identical.
```

### 8. Meters (track and fill)

Used for happiness, R / C / I demand, research and the tech progress bars (8 to 12 design px tall). The fill must be
greyscale (near white) because the code colours it (green to red, zone colours).

```
Two horizontal progress-bar pieces stacked one above the other, landscape 1536 x 1024 image.
Each bar spans 90% of the image width, centred left to right, with a height of 1/8 of the image height (about 128
pixels), in pill shape with fully rounded ends.
Top half of the image: the empty track (a recessed, darker groove with a thin rim).
Bottom half: the fill bar, exactly the same shape and size as the track, in plain light grey close to white with
only a soft top highlight, no colour, so it can be tinted.
Between the rounded ends both bars are uniform along their length so they can be stretched.
```

### 9. Toggle box, tick, slider handle, small marks

```
Four small UI controls in a 2 x 2 grid of equal square cells, square 1024 x 1024 image (each cell 512 x 512).
Each control is centred in its cell and fits inside the middle 75% of the cell.
Top left: an empty square checkbox with a thin border and a flat centre. Top right: a bold tick mark sized to sit
inside that checkbox. Bottom left: a round slider handle. Bottom right: a small round notification dot.
Bold shapes readable at 20 pixels, no fine detail.
```

### 10. Main-menu background

Shown full screen, so the shipped image is a 16:9 crop. The menu buttons sit in the middle and the logo near the top,
so keep those areas quiet. 1536 px is enough for 1080p and 1440p; for 4K, upscale (any 2x upscaler) to 3072 x 1728.

```
Landscape 1536 x 1024 image, a wide establishing view [your scene].
Composition for a 16:9 crop: the top and bottom 1/16 of the image hold nothing important (they will be cut off).
The central 40% of the width is calm, low-detail and mid-to-dark in value so menu buttons and a title can sit
over it; interesting detail goes to the left and right thirds. No text, no logo, no UI.
```

### 11. Logo / title emblem

The main menu draws `Resources/Logo` 380 design px wide. ChatGPT is unreliable with lettering: generate the emblem
only, and set the word "Chronopolis" in the game font (or letter it by hand) on top of or under it.

```
A single title emblem centred on a landscape 1536 x 1024 image with a transparent background.
The emblem spans about 85% of the image width and stays inside the image with an even empty margin of at least 5%
on every side. Leave a clear horizontal band across the middle third of the emblem, plain and uncluttered, where a
title word will be placed later. No letters or text.
```

### 12. Event popup illustrations (optional)

One picture per event type (fire, plague, breakdown, festival...) at the top of the event popup.

```
Landscape 1536 x 1024 image, a single illustrated scene [event], composed for a 16:9 crop: the top and bottom
1/16 of the image hold nothing important. The main subject sits in the central 60% of the width and is readable
at a small size (about 360 pixels wide): a strong silhouette and clear contrast, little fine detail.
No text, no border, no frame.
```

### 13. Cursor (optional)

Windows draws hardware cursors at 32 px (64 at 200% display scaling). The hotspot (the tip) must be at the top-left.

```
Four mouse cursors in a 2 x 2 grid of equal square cells, square 1024 x 1024 image (each cell 512 x 512).
In each cell the cursor's tip sits exactly in the top-left corner of the cell, 4% in from the top and left edges,
and the cursor fills at most the top-left 60% of the cell. Bold shapes with a dark outline about 1/12 of the cursor
size so they read at 32 pixels.
Top left: pointer arrow. Top right: build / place. Bottom left: demolish. Bottom right: not allowed.
```

## After generating (what the processing script does)

- Drop the PNGs into the untracked `Data/ui_raw/<kind>/`, named after the sheet, for example
  `tools_1.png` with a matching `tools_1.txt` listing the cell names row by row (`-` skips a cell; a first line
  `grid 3 3` overrides the grid for a sheet laid out differently). Kinds: `hud`, `tools`, `buildings`, `controls`,
  `cursors`, `buttons`, `frames`, `hudbar`, `meters`, `backgrounds`, `events`, `logo`. Building icons are named by
  the building's `Id` (`water_tower`). Then run `python Tools/gen_ui_art.py` (needs `pip install pillow`).
- A new frame, button, HUD bar or meter needs its border measured once (source px where the corner ornaments end) in
  the script's `SLICED` table; the shipped sizes differ a little from the table above where the art isn't the asked
  shape (M32b: frames 251 x 244 border 37-40, buttons 117 x 96, HUD bar 1596 x 228 ends 254, meter 235 x 30).
- The script slices grids into cells, keys out the green background if used, trims and recentres on the same
  margin, and resizes to the ship size (Lanczos), then writes `Assets/_Game/Art/UI/<kind>/<name>.png`.
- Frames, buttons, bars and meters are resized as a whole; the 9-slice / 3-slice borders are the ship-px borders in
  the summary table.
- Unity import: Sprite (2D and UI), **Pixels Per Unit 300** (so a 3x sprite and its 9-slice borders draw at design
  size on a 100 PPU canvas), mipmaps on for icons (they are shrunk up to 3.75x at the smallest UI scale), off for
  frames, all in one UI sprite atlas.

## Tips for consistent sheets

- Keep the same style prompt and these blocks word for word; change only the row lists.
- For the second and later sheets of a kind, attach the first finished sheet and add: "Match the style, size,
  line weight and lighting of the attached sheet exactly."
- Check every sheet before saving: icons inside their cells, even margins, no stray text, frame borders even on all
  four sides. Regenerate the misses rather than fixing them by hand.
