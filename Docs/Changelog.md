# Chronopolis changelog

## 1.3.0

A new look for the interface. Saves from 1.2.0 load unchanged; nothing in the simulation changed.

**Artwork**
- Icons on every toolbar tool, road, zone, building group, view and service building, and on the HUD readouts and speed buttons.
- Framed panels, windows, tooltips and notifications; buttons with normal, highlighted, pressed and disabled art; new meters, sliders and checkboxes.
- The top bar is a framed banner: the speed buttons, daily income and research bar hang below it, and the R / C / I / G demand bars sit under Settings.
- Window and panel titles use the Cinzel typeface.
- The frames and buttons take a tint for each age: bronze in the Medieval age, warm in the Renaissance, as drawn in the Industrial age and steel in the Modern age.

**Fixes**
- At a UI scale above 100% the top bar no longer runs its readouts into each other: it shrinks to fit the screen, like the toolbar.

## 1.2.0

The second update, from player feedback. Saves from 1.1.0 load unchanged.

**Building and demolishing**
- Roads, avenues, highways and pipes are laid in a **straight line**: press, drag along a row or column, release to build. The hint shows the total cost first; Esc or a right click drops the line.
- **Hold Demolish** and sweep across the map to clear everything under the cursor; if it passed placed buildings, it asks once before removing them.

**Browser version**
- Saves and settings now survive a refresh, a new upload and a closed tab (they were lost before).
- The Load panel can **Export** a save as a `.json` file and **Import** one back, to keep a copy outside the browser.

**A new look for low density**
- Low-density blocks have their own buildings in every age: **homes** are small houses that get nicer as they grow (a cottage, then a house in a fenced or hedged garden, then a larger house with a porch), **shops** are **markets** (open stalls, then a covered market) that become a single-storey shop, and **industry is farmland** (fields with a shed, a barn, then a barn with a rick, dovecote, windpump or silo).
- Farms pollute far less than factories (0.3x), and the selection panel calls them Farms. The Medieval town, which is all low density, now looks like cottages, market stalls and fields.

**Ages**
- Advancing to the next age takes more research: **2,000 RP** to the Renaissance, **12,000** to the Industrial Age and **40,000** to the Modern Age (were 250 / 2,400 / 7,500). Each age lasts longer, and the advance is a goal to work towards.

## 1.1.0

The first update after 1.0.0, built from player feedback. Saves from 1.0.0 load and are upgraded on the next save.

**Camera and toolbar**
- Turn the view in quarter turns with **Q** and **E** (rebindable in Settings > Controls); the pan keys follow the view.
- Buildings are grouped into flyouts on the toolbar; all the map views share one **Views** button (**V** still cycles them).
- The New City dialog shows Tutorial and Disasters as clear On / Off switches; the tutorial greys out Disasters.

**Roads**
- Avenues are two lanes wide with a median, laid as a pair (**R** flips the side).
- Highways can be one-way: drag the way traffic should flow, then Reverse or Make two-way from the selection panel. Traffic obeys the direction.

**Zoning**
- Paint Residential, Commercial and Industrial at **Low, Medium or High** density. A cell keeps the density you gave it.
- The Modern age has new High-density **Skyscrapers** and **Office blocks**, and Low and Medium Modern buildings look different from each other.
- A Density view shows what is zoned where.

**Goods**
- From the Industrial age factories make goods that shops and homes use. The city imports a shortfall and exports a surplus (both are Budget lines).
- A **G meter** in the HUD shows the supply; hover it for what it means. A Goods view shows who makes and who is short. Below the hold line, homes and shops stop at level 2.

**Progression and feedback**
- Later ages research more slowly; Avenue needs Macadam; the Industrial age needs Electricity and Waterworks.
- A repair icon marks a broken power plant or pump.

**Build your own**
- The **Build menu** (**F**, or Find on the toolbar) searches every building by name, group, age or effect; locked buildings show the tech they need.
- A guide (`Docs/CustomAssets.md`) and an Editor wizard add your own buildings, with a sample Market Hall.

## 1.0.0

First release: four ages (Medieval to Modern) with research, map sizes, pollution, land value, water, civic services, the budget, traffic, disasters and events, day and night, audio, a title screen, named saves, settings, rebindable controls and a tutorial. Windows installer and a WebGL build.
