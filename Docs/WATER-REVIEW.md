# Water in the reference maps, and how our rivers compare

Written 2026-10-19, before finishing channel patterns (W4, W5) on `feature/water`. Targets are numbered as in
`VISUAL-TARGETS.md`. Our side is the latest test exports: s7 flat (`r4`) and s7 with `LargeHills` (`r4h`), with the
uncommitted channel-pattern code.

## 1. What water does in the reference maps

### 1.1 Per map

| Target | Water features | Gameplay role | Story it tells |
|---|---|---|---|
| 1 Valley through mountains | A string of long pools and runs along the valley floor, a lake at each end, wet green ground in between | Water is a resource spread along the only buildable corridor. Dry stretches between the pools are natural crossings, so the river never cuts the colony in two | Water comes and goes: it surfaces at narrows and sinks into the gravel of the basins |
| 2 Desert delta | One winding channel, 8-10 cells wide; a cut-off crescent of water; braided channels and sand islands at the coast | The thin green band is the only farmland. Islands make defensible sites. The channel is narrow enough to ford | The river's history can be read: the oxbow and the scroll lines show where it ran before |
| 3 Marsh | A web of channels 2-5 cells wide, pools where they meet, marsh between them | Movement is slow everywhere. The dry rock islands are the only good building sites, so the map is a puzzle about where to settle | A drowned plain with only the hardest rock standing above it |
| 4 Mountain foot | Streams 4-6 cells wide start at the mountain foot, wind in loops across the plain and join | The streams cut the plain into parcels but stay shallow and crossable. They are watermill sites | Water leaves the mountain and finds its way downhill |
| 5 Lakes | Rounded lakes of every size, each with a shallow rim and a deep core | The lakes act as moats and natural walls; fishing; open ground between them | Ice once scoured the land |
| 6 Entrenched meanders | A channel 8-12 cells wide with a shallow rim, its bends cut into rock, and dry loops it abandoned | Each bend wraps around a peninsula with a narrow neck, which makes a ready-made defensive site with a chokepoint | The river cut down while the land rose |
| 7 Basin between narrows | One channel in each narrows. In the basin, 2-3 channels of 8-15 cells around large islands | The islands are big enough to build on and are reached through shallow water, so they make strong base sites. The narrows are chokepoints | Confinement versus freedom |
| 8 Glacial plain | A web of meltwater channels below the ice, joining into rivers 5-10 cells wide that fan out toward the sea | Many small crossable channels on the way from the ice to the coast | Melt drains from the mountains to the ocean |
| 9 Cove | A narrow river (about 6 cells), nearly straight, comes through the mountain into a bay | Fresh water reaches the coast. Deep water at the bay entrance closes it off | Water inside rock has nowhere to wander |
| 10 Broad river | About 30 cells wide with gentle bends, a deep centre, shallow edges and bars; a dark floodplain band; a bridge | A major barrier: the bridge and the bars are the only crossings, which makes them chokepoints. The floodplain is the farmland | An ancient road crossing a great river |

### 1.2 Principles common to the targets

1. **Width fits the map.** Most rivers are 5-15 cells wide. Only the one "great river" (target 10) is about 30, and
   it bends gently. Tight meanders appear only on narrow channels. Real rivers follow the same rule: meander
   wavelength is about 10-14 channel widths and bend radius about 2-3 widths. A 30-cell river therefore fits about
   one gentle wave on a 250-cell map, while a 10-cell river fits three to five tight bends.
2. **Depth has structure.** A deep core runs along the line of the channel. A shallow rim lines both banks. Deep
   water sits on the outside of bends and a shallow bar on the inside. Because shallow water is walkable, this
   structure decides **where the river can be crossed**: the rims, the bars, and the shallow runs between bends.
3. **A river is a fertile corridor.** A band of rich soil, mud or marsh and denser green plants follows every
   channel and stands out against drier land. The contrast is strongest on arid tiles (2, 10) but visible on every
   map (1, 4).
4. **Water divides the map into readable areas**, each a candidate base site with its own natural defences:
   - peninsulas inside bends, with a neck as the chokepoint (6);
   - islands between branches (2, 7);
   - parcels between streams (4, 8);
   - dry knobs in marsh (3);
   - narrows, fords and bridges as chokepoints (1, 7, 10).
5. **History is visible.** Oxbows, abandoned loops, scroll bars and terraces each say what the river used to do.
6. **Variety within one map, each in its own place.** A river, a few lakes and some streams sit in distinct areas.
   Never is everything wet.
7. **Banks are crisp.** A channel has a clear edge and steady local width; it is not a ragged blob. Pools and lakes
   are rounded.

## 2. Our rivers against the targets

### 2.1 Flat s7 (r4): large river, meandering

| Principle | Target | Ours | Gap |
|---|---|---|---|
| Width fits the map | 8-15 cells, or 30 with gentle bends | Channel width 30 with bends sized for a 14-cell river (`maxMeanderScale` 14). The water band is 50-80 cells wide; bend interiors fill with water and the whole meander reads as one lake | **Large.** Bend size and channel width do not match |
| Depth structure | Deep core, shallow rims, bars on the inside of bends | Deep and shallow patches are blobs with no relation to the channel line. Our carved bed is flat across the channel, so these patches come from vanilla's river painting, not from us | **Large.** We do not own the river's depth |
| Crossings | Rims, bars and shallow runs between bends | Crossings are wherever vanilla's blobs happen to be shallow | Follows from the depth gap |
| Fertile corridor | A band of rich soil or mud | Moisture is saturated across the whole map (rainfall 2331 mm), so there is no corridor contrast and no floodplain band | **Large.** Floodplain (S2) not built |
| Readable areas | Peninsulas, islands | Bend interiors are flooded, so there are no peninsulas, and there are 0 side channels (slope < 0.003 never splits) | Large |
| History | Oxbow water or wet green hollows | Oxbows show as **grey gravel crescents**. Two near the top are carved with straight-edged artefacts | Medium |
| Banks | Crisp | The landform has crisp, smooth banks, but the painted water does not follow them. Straight lines cross the bed where the course doubles back; these cause the invariant 1 failure (0.74 m rise) | Medium; a bug |
| Small streams | Clean winding streams 3-6 cells wide (4) | Ragged, broken lines and many tiny ponds over the whole plain | Medium; noise-like on very flat wet maps |

### 2.2 Hilly s7 (r4h): large river, confined

- **Good:**
  - one channel through the hills;
  - steep banks with cliff lines (close to target 6);
  - tributaries that join cleanly (close to target 4);
  - a rounded lake with a deep core and an outlet stream (close to target 5).
- **Gaps:**
  - The river is about 25-30 cells wide and fills the whole valley floor, with no bench or floodplain beside it.
  - Its depth shows the same vanilla blobs.
  - The course is almost straight with no bends at all, where target 6 shows that confined rivers keep their bends.

### 2.3 Root causes

1. **Width is vanilla's, at world scale.** `RiverCourse.WidthOnMap` takes vanilla's painted width. Rule 5 (features
   compressed to map scale) is not applied to rivers. Meander scale is capped at 14 while the channel stays 30, so
   the bends are too tight for the channel and the loops merge.
2. **Vanilla paints the river surface.** Its own width, wobble and depth noise lie on top of our carved channel.
   Our ground rules decide still water and streams but not the river's deep and shallow water.
3. **No floodplain.** Moisture does not see the river's discharge or belt, and on wet tiles it saturates everywhere.
4. **Cut-off loops** are classified as gravel (bare carved ground) instead of water, marsh or wet soil. They are
   carved where the river later crossed them again (the bed bug).
5. **Splits need slope ≥ 0.003**, so a flat map never splits. The targets split on gentle ground where the valley
   is wide (7) or near the sea (2, 8). Sediment supply and confinement matter more than slope there.

## 3. What to change, in order

1. **River width to map scale.** Map vanilla's river size classes to compressed widths (e.g. creek 4, river 8,
   large 12, huge 20), and tie bend size to the channel width (bend radius 2-3 widths, wavelength 10-14 widths).
   Wide rivers then bend gently and narrow ones meander tightly, as in the targets.
2. **Own the channel's depth.**
   - Carve a cross-section instead of a flat bed: a deep core along the line, shifted to the outside of bends, and
     a shallow rim and inner-bend bar.
   - Add **riffles**: shallow runs at the crossovers between bends, spaced about 5-7 widths apart (the pool-riffle
     sequence). They are real geomorphology and give the river its fords.
   - Set river cover from this depth through `GroundRules`, and stop vanilla's depth noise.
3. **Fix the bed bug.** Prevent cut-off loops and limbs that come close to each other from corrupting the main
   profile (invariant 1).
4. **Floodplain band (S2).**
   - A meander belt from discharge, with rich soil and mud falling off with distance from the channel.
   - Oxbows become still water or wet green hollows, by the lake model.
   - Moisture contrast scaled by climate, so wet tiles still show the corridor.
5. **Splits from confinement and sediment, not slope.** Islands in wide basins (7) and toward the coast (2, 8).
6. **Invariants for the gameplay reading:**
   - river width within the map-scale band;
   - at least one ford (walkable crossing) per map width of river;
   - a wet corridor along the whole course;
   - bend interiors stay land.

Steps 1-3 change what we have just built and should land before the channel-pattern commit. Steps 4-6 are
follow-ups on the same branch.

## 4. Owner decisions and what was built (2026-10-19)

1. **Width.** Vanilla already scales rivers by class (creek, river, large river) at world generation. We keep that
   width and scale bends from it; no own width table. A 30-cell river now bends gently (target 10). Tighter
   meanders come with narrower rivers.
2. **Depth from topology.** The carver cuts a cross-section into the landform:
   - deep core at the outside of bends;
   - rims and an inner bar;
   - pools at bends and shallow riffles between them, about 6 widths apart on straight reaches.

   The water level goes into `LL_SurfaceWater`, and the ground rules turn depth into deep or shallow moving water.
   Vanilla's river painting stands aside when the river was carved.
3. **Bed bug fixed.** Oxbows are plugged where they met the river and never carve into a live channel. Invariant 1
   checks the water surface, since pools in the bed are expected. It passes on s7 flat, small and large hills, and
   mountainous.
4. **Floodplain from existing layers.** No new layer:
   - the river's upstream inflow enters `LL_DrainageArea`;
   - `LL_Wetness` measures height above a river from its water surface;
   - the wet band widens with drainage area (`floodGrowthPerDecade`).

   On s7 (2,331 mm rain) the whole map is wet, so the band shows little. It needs a check on a drier river tile.
5. **Splits by deposition.** A river splits where carrying capacity (slope over floor width, smoothed) falls to half
   its best value upstream, the floor is at least 6 widths wide, and the banks are weak. s7 is uniform (flat) or
   confined (hills), so it does not split, which is correct. Not yet seen in game: no test seed has a river that
   enters an open basin.
6. **Gameplay checks deferred** to a later "dial back to fun" stage. Note for that stage: every vanilla river
   width now has fordable riffles (riffle depth under the deep-water threshold below about 48 cells wide).

## 5. River rules reverse-engineered from the owner's preferred maps (targets 11-13)

The owner judged the rivers from section 4 as looking increasingly wrong, even though the rules behind them are
physical. Targets 11-13 (custom maps) show the look wanted. These are the rules that would produce them. Targets 2,
4, 6 and 7 fit the same rules.

### 5.1 The rules

| # | Rule | Evidence |
|---|---|---|
| V1 | **Channels first, terrain around them.** The channel network is laid out first. The valley floor is ground within a set distance of a channel, wider for larger channels and at confluences. Rock rises beyond it. Rivers always sit in the middle of their valley because the valley is made from the river | 11: every channel is centred on an even-width floor; 12: the gorge, then the fan, follow the channel |
| V2 | **A network, not one river.** Several channels per map form a tree: sources in the highlands and at map edges, acute junctions pointing downstream, an exit at the lowest edge or a lake or sea | 11: about ten channels; 13: a creek reaching the coast |
| V3 | **Narrow, readable, nearly constant width.** 2-4 cells for creeks, about 6-10 for rivers, about 20 only for a trunk leaving a gorge. Width rises in steps at junctions, not continuously | 11, 12, 13 |
| V4 | **Shape: gentle curves plus a small wobble.** A smooth course with a 1-3 cell wobble over 10-20 cells. No meander loops on confined or sloping ground; tight winding only on flat plateaus or flats | 11, 13; vanilla paints rivers the same way (displaced segments with bend noise) |
| V5 | **Fan where confinement ends.** Where a channel leaves narrow ground (gorge to basin, valley to coast), the floor opens into a funnel and the channel branches repeatedly into narrower distributaries. Teardrop islands form, seeded on rock knobs or high points, and get smaller outward | 12; also targets 2, 7 and 8 |
| V6 | **A continuous deep thread.** A darker deep line runs without a break through every channel to the base level. Shallow water covers the margins, and in fans a wide shallow sheet | 11, 12, 13 |
| V7 | **Base levels hold water.** Channels end in a lake, the sea, a wide shallow flat or the map edge, never in the open | 11 (lake), 12 (sea), 13 (coastal flats) |
| V8 | **Thin wet banks.** A 1-2 cell dark wet band along each channel. The valley floor is sand or soil, and gravel lies at the foot of the rock | 11, 12 |

### 5.2 How this differs from our generator

- **Order is reversed.** We generate terrain first and fit the river into it afterwards (planning, carving, meander
  migration). The targets build the channel network first and let the valleys follow it (V1). That is why our rivers
  can look out of place even when they are physically correct.
- **Width.** We use vanilla's world width: 30 cells for s7's river, a whole valley floor. The targets' main channels
  are 2-10 cells, with about 20 only at a gorge.
- **One world river plus ragged streams.** Our streams come from a drainage-area threshold on the final surface. On
  flat ground they break into noise. The targets have a clean, designed tree (V2).
- **Meander migration** produces either nothing (wide river) or large loops. The targets want gentle curves with a
  small wobble (V4).
- **No fans.** Splits by deposition don't trigger on our test seeds. The targets fan at every release of
  confinement (V5).

### 5.3 How it could be built (proposal; owner to decide)

1. **Network plan** (new step before landform detail):
   - Take the coarse erosion model's drainage tree, which already exists for the world river.
   - Keep the channels above a drainage threshold, plus the world river's course and any streams entering from map
     edges in the uphill direction.
   - Rank each channel by Strahler order and set its width from that order. The world river's vanilla class sets the
     trunk's order.
   - Shape each course as a smoothed polyline with a small noise wobble (V3, V4).
2. **Valleys from the network.** The landform generator lowers ground toward each channel: floor half-width from
   the channel's order, walls beyond. Rock and terraces then follow the valleys. This fits the existing erosion model
   (channel incision) rather than replacing it.
3. **Fans** where a channel's valley-floor width jumps (confinement release) or reaches a base level. Branch into
   two or three distributaries per level and leave teardrop islands, kept where rock strength is high (V5).
4. **Depth** from the existing carver cross-section, without pools and riffles and with a continuous deep thread
   above a width threshold (V6).
5. **Retire** meander migration and splitting by deposition for now, or keep them only for flat lowlands
   (target 13's flats).

Owner decisions needed: channel widths compressed to targets 11-13 (instead of vanilla's world widths), and
channels first with valleys built around them (instead of terrain first).
