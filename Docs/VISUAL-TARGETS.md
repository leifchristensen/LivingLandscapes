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
