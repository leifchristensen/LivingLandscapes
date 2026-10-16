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

## 4. Mountain foot with tilted strata and caves

**What it shows** (mountain mass to the north-east, lowland to the south-west)
- The mountain front is not a smooth edge. Long rock ridges and fingers reach out of it onto the plain, most of them
  pointing the same way at a similar angle, but not exactly parallel or evenly spaced.
- Narrow valleys cut back into the mountain between the ridges. Streams start at their heads, at the foot of the
  mountain, and wind down across the plain, joining as they go. Their direction shows which way the land slopes.
- A cave winds into the mountain from the head of one valley as a long, branching dark passage.
- Isolated rock outcrops and stepped benches lie just in front of the mountain; green, wetter patches dot the plain.

**Geological processes**
| What is seen | Process | How we produce it |
|---|---|---|
| Ridges at one angle, irregular | Sedimentary layers, tilted by tectonics, alternate hard and soft; erosion leaves the hard layers as ridges along the strike (hogbacks) and hollows out the soft ones | Rock strength as tilted strata: bands of hard and soft rock along a strike direction, with dip, varying bed thickness and warping so they are not uniform |
| Valleys cut back into the mountain, streams from their heads | Water from the mountain collects in the soft layers and leaves at the foot (springs) | Drainage area already counts the mountain; streams appear where it passes a threshold, so they start at the front |
| Streams wind across the plain | Runoff follows the slope down from the mountain | The slope vector field (LL_Aspect) gives the direction; the river tracing from target 1 gives the course |
| Spreading, shifting channels at the foot | Streams slow where the slope eases and drop sediment in fans (alluvial fans) | At a sharp drop in slope, build a fan of sediment and split the channel |
| Cave along the strike, opening at a valley head | Water dissolves soluble layers along bedding planes and drainage lines; caves connect to springs | Guide cave tunnels along soluble strata and the mountain's internal drainage lines, with mouths at valley heads |
| Wet patches | Springs and seeps at the foot of the slope | Moisture model: water table rises where slope eases below steep ground |

**New or changed pieces** (beyond targets 1-3)
1. Rock strength becomes stratigraphy: strike, dip, bed thicknesses and warping. The tile's rock types (two or three
   per tile in vanilla) are assigned to the beds, so rock colour shows the bands too.
2. Alluvial fans where streams leave the mountain.
3. Caves guided by soluble strata and drainage. Verify first how vanilla's cave generation works in 1.7 and where it
   can be steered.

**Checks on the exports** (a mountainous tile with caves)
- Rock ridges at the mountain front share an orientation within about 20 degrees, with uneven spacing.
- Streams start at or near the mountain foot and run down LL_Aspect.
- Rock types alternate in bands that follow the ridge direction.
- Cave passages run mostly along the strike and open at the head of a valley.

## 5. Lakes among broken rock ridges, with an impact site

**What it shows** (rolling ground, many lakes, rock in clusters)
- Lakes of every size. Small ponds sit in hollows between rock knobs, even high up; the largest lakes lie at the
  lowest ground, here along the map edges. Each lake has a shallow rim and a deeper core.
- The rock is not a solid ridge but a band of rounded blocks and knobs, elongated and lined up in one direction,
  with narrow gaps between them. Only a few gaps go all the way through, so crossing the band means finding a pass
  or mining.
- Wet green ground and rich soil gather around the lakes; the open ground in the middle is drier.
- Lower right: an impact site. Rubble is strewn around it and a barren, light streak runs away from it in one
  direction.

**Geological processes**
| What is seen | Process | How we produce it |
|---|---|---|
| Lakes in hollows at every height, largest at the lowest ground | Water collects in closed depressions until it spills or evaporation balances inflow | Keep depressions instead of breaching them all: fill each to its spill level when rainfall over its catchment exceeds evaporation, lower (or dry) otherwise. Bigger catchments in low ground give bigger lakes |
| Shallow rim, deep core | Depth grows toward the centre of the depression | Water depth from fill depth: deep water beyond a depth threshold |
| Aligned knobs and hollows, many lakes | Glacial scouring (as on the Canadian Shield): ice plucks and grinds weak rock along its flow direction, leaving streamlined knobs and undrained hollows | Cold-climate regime: ice-flow direction (from the world, later), scour that removes weak rock and stretches shapes along that direction |
| Broken ridge with few passes | Rock breaks along joints (fracture sets); erosion opens the joints into gaps | Weaken rock strength along two joint sets so ridges split into blocks; check how many passable routes cross a ridge |
| Rubble and a barren streak | Impact: crater, ejecta thrown downrange, ground scorched along the path | Impact landform: crater bowl and rim, rubble (rock chunks) downrange, barren terrain along the trajectory |

**New or changed pieces** (beyond targets 1-4)
1. Lake water balance: which depressions hold water, and to what level, from rainfall, catchment and evaporation.
   This replaces breaching every depression, and also gives dry lakes and salt flats in arid climates.
2. Joint sets in the rock strength model, plus a passability check (number of routes across a ridge).
3. Glacial scour regime for cold tiles.
4. Impact sites as a landform with direction. Check which 1.7 tile mutators or features already describe impacts.

**Checks on the exports** (a boreal or temperate tile with lakes, e.g. s4)
- Every lake lies in a closed depression of LL_Landform; the largest lakes lie in the lowest quarter of the map.
- Rock bands have 2-5 passable routes across them.
- Rock knobs are elongated, mostly along one direction.
- An impact site has rubble on one side and a barren streak in the same direction.
