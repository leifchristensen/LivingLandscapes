# Visual targets

The owner supplies reference maps (screenshots, not committed). Each target is written down as what can be seen,
the geological process behind it, and how our layers should produce it. Plans that follow go in the roadmap in
`ARCHITECTURE.md`. A target is met when the exported PNGs show the listed checks.

## 1. River valley through mountains

**What it shows** (map of roughly 250 x 250 cells, valley running diagonally across it, mountain rock on both sides)
- The valley floor changes width: wide basins (40-60 cells) alternate with narrows (5-10 cells) where the rock walls
  close in. The river runs through the gap at each narrows.
- The river is not continuous water. It shows as long pools and runs along the lowest line of the floor, a larger
  lake downstream, and in between only as a strip of marsh, mud and rich soil with denser, greener plants.
- The course bends where rock spurs push it aside. It is never a straight line.
- Wetness falls off from the river line: wet ground, then soil, then stony or gravel ground toward the walls.
- The walls are not one rock edge. Knobs of rock stand on the side slopes with passable benches and side gullies
  between them. On one side cliff bands form nested rings around high ground.
- Loose rock gathers at the foot of the walls.

**Geological processes**
| What is seen | Process | How we produce it |
|---|---|---|
| Basins and narrows | Differential erosion: soft rock widens into basins, hard rock bands make narrows | New rock strength (erodibility) field, banded across the valley, feeding the erosion model's K |
| Bending course | The river follows the lowest line of the floor, pushed by spurs | River course traced on the final heightmap (D8 thalweg), not straight model-cell segments |
| Water that comes and goes | Water flows under gravel where the floor is wide and deep; bedrock at a narrows forces it to the surface; it pools where the gradient flattens above a narrows | Water table from height above nearest drainage (HAND); surface water where the water table reaches the surface, or the alluvium cannot carry the flow (narrow and thin) |
| Wet strip, rich soil | Groundwater close to the surface along the valley axis | Moisture from HAND, wetness index and surface water; terrain writer turns moisture into marsh, mud, rich soil, soil, gravel |
| Rock knobs, benches, side gullies | Hard rock outcrops; tributaries cut into the walls | Strength field also leaves knobs; drainage area already cuts gullies; rock where soil depth reaches zero |
| Cliff rings | Hard caprock layers stepped by erosion | Existing terraces and cliff bands |
| Loose rock at the foot of walls | Rockfall stops where the slope breaks | Slope vector field: where steep ground meets gentle ground, place talus and rock chunks |

**New or changed pieces**
1. Layer `LL_RockStrength` (erodibility), used by `LandscapeGenerator` and later by rock type choice.
2. River course from the final heightmap thalweg, with width from discharge.
3. Layers for height above drainage (`LL_HeightAboveDrainage`) and surface water (`LL_SurfaceWater`). Lakes and
   pools come from filled depressions along the course.
4. `LL_Moisture` rebuilt from these, then used by the terrain writer for wet ground classes.
5. Talus and rock chunks at slope breaks.

**To verify first in the decompiled game:** how the river mutator paints river terrain and whether moving water
(watermills) needs a continuous river. An intermittent river is a gameplay change; keep a setting for it.

**Checks on the exports** (seed with a river and mountains, e.g. s7 plus `-ll-hilliness=Mountainous`)
- Valley floor width varies along the course by a factor of 4 or more.
- Surface water covers 30-70 % of the course, in 3 or more separate reaches.
- Wet ground (marsh, mud or rich soil) runs unbroken along the whole course.
- The course has no straight runs longer than about 20 cells.

## 2. Desert river and delta among dunes

**What it shows** (sand plain, rock massifs to the north and east, sea to the south)
- The river leaves the mountains in a narrow canyon at the top left, then winds across the sand in wide bends.
  One bend has been cut off: a curved, isolated stretch of water (an oxbow) lies beside the main channel.
- Downstream the river splits into several channels that rejoin and split again, leaving islands between them.
  At the coast it fans out into a delta: rounded sand islands and bars, shallow water between them, deep sea beyond.
- Sand bars and islands also line the rest of the shore. Water gets darker (deeper) with distance from the shore.
- Plants and dark wet ground make only a thin band along each channel. The rest is bare sand.
- The sand has light, wavy streaks, roughly parallel, all at the same angle: dunes shaped by one prevailing wind.
- Where the river runs beside the mountains it has cut into them: the rock edge follows the outside of the bends.
  The mountain front is broken into bays and isolated blocks, and one block by the coast encloses a shallow lagoon.

**Geological processes**
| What is seen | Process | How we produce it |
|---|---|---|
| Wide bends, oxbow | Meandering: on a low gradient with loose banks, the outer bank erodes and the inner bank builds a bar; bends grow until they cut off | Meander migration on the river's centre line (curvature-driven, a few hundred steps), cutoffs left as oxbow lakes |
| Splitting channels, islands | Channel pattern depends on gradient, discharge and sediment: steep with lots of sediment braids, gentle meanders | Choose the channel pattern per reach from slope and discharge (confined, meandering, braided, delta) |
| Delta, bars at the mouth | At sea level the river slows and drops its sediment; channels split around mouth bars | From where the gradient approaches zero near the coast, split the channel into a branching tree; build sand islands between branches |
| Shore bars, deeper water offshore | Waves rework sediment into bars parallel to the shore; the seabed slopes away | Seabed depth from distance to shore (shelf); bars in a shallow depth band |
| Thin wet band | Little rain: moisture comes only from the river and falls off within a few cells | Same moisture model as target 1, with the rainfall baseline from the tile making the band narrow |
| Dune streaks | Wind piles sand into ridges across its direction | Wind direction from latitude (trade winds, westerlies); low dune ridges as oriented ripples in the heightmap and light sand on crests, only on dry sand |
| River cut into the rock | Lateral erosion at the outside of bends | Meander migration may erode rock where rock strength is low; rock edge then follows the channel |

**New or changed pieces** (beyond target 1)
1. Channel pattern per reach from slope and discharge, reusing the river course from target 1.
2. Meander migration with cutoffs; delta branching at the coast.
3. Seabed depth and shore bars for coastal maps.
4. Wind field from latitude, then dune ridges on dry sand (also usable later for snow drifts and plant shelter).

**Checks on the exports** (a desert or arid tile with a river reaching the coast)
- Sinuosity (channel length over straight distance) of 1.5 or more on the plain.
- At least one cut-off bend left as standing water.
- 3 or more channels reach the sea through the delta, with islands between them.
- Wet ground and plants stay within about 6 cells of water on arid tiles.
- Dune streaks share one orientation, at right angles to the wind direction.

## 3. Marsh with rock spires

**What it shows** (flat, wet plain; no large relief)
- A dense web of narrow, shallow channels (2-5 cells) covers the whole map. They join and split again, forming
  closed loops rather than a tree, and widen into small pools where they meet. There is no clear downstream direction.
- Almost all land between the channels is marsh or wet ground with dense, low bog plants.
- Small blocks and spires of rock stand alone on the plain. They and a thin rim around them are the only dry ground.
- In the upper right a larger river runs through the rock, cutting a channel between the blocks.

**Geological processes**
| What is seen | Process | How we produce it |
|---|---|---|
| Web of looping channels | On an almost level surface with a high water table, flow has no strong direction; water spreads, splits and rejoins (anastomosing network) | Multiple-flow-direction routing on a low-relief surface with fine noise; channels where flow accumulation passes a threshold. Splitting flow produces the loops |
| Pools at junctions | Water stands in shallow hollows | Depression filling: filled depth above zero is standing water |
| Marsh everywhere | Water table at or just below the surface | Height above drainage near zero over most of the map gives saturated moisture |
| Isolated rock spires | Hard rock that resisted erosion while the plain around it was worn down and buried by sediment | Rock strength field: sediment fills the plain to a flat level and only hard knobs stand above it |
| River through the rock | A larger river cuts through weak zones between the blocks | Same river course and rock strength as targets 1 and 2 |

**New or changed pieces** (beyond targets 1 and 2)
1. Low-relief regime: the plain is filled with sediment to a nearly flat level (basin fill); rock stands above it
   only where it is strong.
2. Channel network from spreading (multiple-direction) flow, so loops can form, alongside the single river course.
3. Wet ground classes (marsh, mud) chosen from moisture, shared with targets 1 and 2.

**Checks on the exports** (a swamp or wet temperate tile, low hilliness)
- The channel network contains closed loops (more than one channel between some pairs of junctions).
- Channels are 2-6 cells wide and reach every part of the map, with no land more than about 25 cells from water.
- Marsh or mud covers at least half of the land.
- Dry ground lies mostly on or within a few cells of rock.
