# Geology rules

How maps like the reference targets (`VISUAL-TARGETS.md`, 1-10) are made systematically. One model, driven by the
world tile's data, produces every target. There are no per-target generators: a desert delta and a mountain gorge
come from the same rules with different inputs. Each rule below names the targets it serves.

## 1. Principles
1. **Read the world, never invent it.** Every regime comes from tile data: hilliness, elevation, rainfall,
   temperature, biome, latitude, river size and direction, coast direction, tile mutators, neighbour biomes, rock types.
2. **Rock first, then water, then soil, then life.** Each stage only reads the stages before it.
3. **Hard rules are invariants**, checked on every test export (section 9). Tuning numbers live in XML.
4. **Start with realism, dial back to fun.** Gameplay limits (passability, buildable space) are rules too, with
   checks, not afterthoughts.
5. **Features at map scale, distinct areas.** Every reference map has several distinct areas (a gorge, a basin, a
   delta, a ridge with passes), so small parts of a map feel unique. Geological features are compressed so their
   effect is visible within one RimWorld map: we give up 1:1 scale on purpose. Feature sizes (bed thickness, meander
   wavelength, lake size, ridge spacing) are tuned in map cells, not real metres; see invariant 7.

## 2. Tile inputs and the regimes they select

| Input | What it controls |
|---|---|
| Hilliness | Uplift and relief; how much rock stands above the sediment fill |
| Rainfall / evaporation (temperature) | Water table height, which hollows hold lakes, width of the wet band, dunes |
| River size | Discharge, channel width, floodplain width |
| Coast direction | Base level, seabed, wave exposure, deltas |
| Neighbour biomes | Edge influence: ice sheets, later plant ecotones |
| Latitude | Wind belt, which slopes are shaded |
| Rock types (2-3 per tile) | Assigned to strata: strength, solubility, colour |
| Mutators | Landforms we model (valley, crater, cove, ...) as extra constraints |

The targets are just points in this input space:

| Target | Relief | Water | Rock | Special |
|---|---|---|---|---|
| 1 River valley | high | wet | strong bands across the valley | water comes and goes |
| 2 Desert delta | low | arid, big river | weak | coast, wind |
| 3 Marsh | very low | very wet | isolated strong knobs | sediment fill |
| 4 Mountain foot | high | moderate | tilted strata, soluble beds | caves, fans |
| 5 Lakes | moderate | wet, cold | jointed, glacially scoured | impact |
| 6 Entrenched meanders | moderate, incising | moderate | strong | apron |
| 7 Split river | basin between narrows | wet | strong at narrows | |
| 8 Glacier boundary | sloping to sea | meltwater | | ice neighbour, coast |
| 9 Cove | high, coastal | river | hard coastal band | cave river |
| 10 Broad valley | ranges far apart | arid, big river | | floodplain, structures |

## 3. Rock rules (all targets)
Simplified for now: every rock is treated as layered beds. Real rock forms in three ways and erodes differently;
revisit this later (roadmap): sedimentary (layered beds, dip, soluble limestone, caprock), igneous (intrusive
bodies such as granite domes and dikes, massive and jointed; extrusive lava flows and columnar basalt), metamorphic
(foliation, folds). The tile's rock types would then pick the formation style, not only strength and colour.

- **R1 Strata.** Rock strength is a 3D field of beds: a strike direction, a dip angle, bed thicknesses drawn from a
  distribution, and a smooth warp so bands are irregular (4). Flat-lying beds give caprock and terraces (1, 9).
- **R2 Rock types.** The tile's rock types are assigned to beds; each type carries strength and solubility (XML).
  Rock colour therefore shows the bands (4).
- **R3 Joints.** Two joint sets weaken rock along lines, so ridges break into blocks with a few gaps (5).
- **R4 Strength drives erosion.** Erodibility in the erosion model is the inverse of strength: hard beds stand as
  ridges, narrows, spires, headlands and stacks; soft beds become valleys, basins and coves (1, 3, 4, 7, 9).

## 4. Relief rules
- **F1 Uplift and erosion.** The existing stream-power and diffusion model, with erodibility from R4 and uplift from
  hilliness.
- **F2 Base level.** Sea, lakes and the river's exit fix the lowest level; land slopes toward them, so mountains get
  aprons that slope toward the river or sea (6, 8).
- **F3 Sediment fill.** On low-relief tiles, sediment fills low ground to a nearly flat level; only strong rock stands
  above it (3, 10).
- **F4 Glacial scour** (cold tiles): weak rock is removed along an ice-flow direction, leaving streamlined knobs and
  undrained hollows (5).
- **F5 Wind** (dry sand only): dune ridges at right angles to the wind; the wind comes from the latitude belt and is
  steered along large valleys (2, 10).
- **F6 Impact** (mutator or rare feature): crater bowl and rim, rubble thrown downrange, barren streak (5).

## 5. Water rules
- **W1 Downhill or flat.** Water flows downhill or level, never uphill. The river course is traced on the final heightmap; where it would
  climb, the channel is carved instead. Near a mountain the river therefore runs parallel to the face or away from
  it (6, all rivers).
- **W2 Sources.** World rivers enter at their tile edge; streams start where drainage area passes a threshold
  (mountain fronts, springs); meltwater enters along an ice margin (4, 8).
- **W3 Channel size.** Width grows with the square root of discharge; depth more slowly (hydraulic geometry).
  Split channels share the discharge (7, 10).
- **W4 Channel pattern**, chosen per reach from five inputs: slope, discharge, confinement (valley floor width over
  channel width), bank strength (R4) and sediment load:

  | Condition | Pattern | Targets |
  |---|---|---|
  | Inside rock, or confinement under ~3 | single channel, steady width, low curvature; a cave river under a roof | 1, 7, 9 |
  | Low slope, weak banks, room to move | meandering: migration, cutoffs (oxbows), scroll bars | 2, 10 |
  | As above but strong banks and downcutting | entrenched meanders: steep outer wall, gentle inner spur, terraces | 6 |
  | Confinement over ~6, high sediment or slope | braided or split around bars and islands, rejoining at the next narrows | 7, 8 |
  | Gradient near zero at a base level | delta: branching distributaries, mouth bars, islands | 2, 8 |
  | Near-flat ground with a high water table | anastomosing web with loops (multiple-direction flow) | 3 |
- **W5 Banks.** Outer banks of bends are cut steep, inner banks are gentle; where the flow presses on rock it leaves
  a steep face (6, 7). Rock strength limits how far a river can cut sideways (2).
- **W6 Lakes.** Closed depressions are kept, not breached. A depression fills to its spill level when rainfall over its
  catchment beats evaporation over its surface; otherwise it holds a smaller lake, a dry lake or a salt flat. Depth
  sets deep or shallow water (1, 3, 5, 6).
- **W7 Water table.** Height above the nearest drainage (HAND) sets the water table. Surface water appears where the
  water table reaches the ground or the alluvium is too thin and narrow to carry the flow, so rivers can sink into
  gravel and reappear at narrows (1).
- **W8 Springs and pools.** Where groundwater or a cave river reaches open ground, or a steep slope eases, place a
  spring pool or wet ground (1, 4, 9).
- **W9 Caves.** Cave passages follow soluble beds and the mountain's internal drainage and open at valley heads;
  rivers crossing rock run as cave rivers (4, 9).

## 6. Coast and ice rules
- **C1 Seabed.** Depth from distance to shore; shelf width from relief (2, 8, 9).
- **C2 Exposure.** Wave exposure from the open-sea direction: exposed headlands are rock, sheltered bays get beaches,
  spits grow along the drift direction (from the wind) (2, 8, 9).
- **C3 Coastal erosion.** The sea breaks through hard bands and hollows out soft rock behind them: coves, headlands,
  stacks (9). Bars form in a shallow depth band and follow depth contours (2, 9).
- **I1 Ice.** Ice cover from the edge influence of glacial neighbours, height and shade (aspect); its margin is a
  water source (W2) with high sediment (W4) (8).

## 7. Soil, moisture and surface rules
- **S1 Moisture** from HAND, wetness index (drainage area over slope), surface water and tile rainfall. On arid tiles
  it falls off within a few cells of water; on wet tiles it saturates whole plains (1, 2, 3).
- **S2 Floodplain.** Belt width from discharge; soil richness and moisture highest at the banks and falling to the
  belt edge, banded by scroll bars; a terrace step at the belt edge (10).
- **S3 Terrain classes** from moisture, soil depth, sediment type and slope, in this order: rock (no soil, or too
  steep), deep and shallow water, marsh, mud, rich soil, soil, gravel, sand (sediment type sand, or arid). Soft sand
  on dune crests (F5).
- **S4 Debris.** Talus and rock chunks where steep ground meets gentle ground (slope vector field), rubble at impacts
  (1, 5).
- **S5 Edge influence** layer (direction, distance, neighbour biome) for terrain now and plants later (8).

## 8. Gameplay and compatibility rules
- **G1 Passability.** Rock bands keep a few passes (2-5 routes); checked by pathfinding on the export (5). First
  version: open joints crossing a ridge become gaps; walkable areas are counted at generation.
- **G2 Structures.** Roads, bridges and mutator structures run after terrain; a road crossing our river must get a
  bridge or ford (10).
- **G3 Settings.** Intermittent rivers, cave rivers and anything that changes play (watermills, travel) can be
  switched off.

## 9. Invariants checked on every export
1. Height never rises along any river course: each step is downhill or level (W1).
2. Every lake lies in a closed depression of LL_Landform (W6).
3. Inside rock, rivers are single channels (W4).
4. Split channels rejoin or reach a base level; none ends in the open (W4).
5. Rock bands have at least one pass unless the tile is meant to be enclosed (G1).
6. Wet ground runs unbroken along every river course (S1).
7. The map has several distinct areas: at least 3 landform or terrain zones of meaningful size, none covering most
   of the map (principle 5). Thresholds tuned in XML.

## 10. Build order
Each step is a commit series with its own exports; later steps only add rules.

1. **Rock strata** (R1-R4) — first version done (beds, interbeds, joints, rock types, soil by strength); awaiting
   the owner's in-game review. Original plan:: `LL_RockStrength` layer and rock types per bed, feeding the erosion model. Overlay first,
   then terrain. Unlocks narrows, spires, ridges and coves.
2. **Downhill river tracing** (W1, W3) — done (feature/water): `RiverCarver` carves the world river's bed to the
   closest never-rising profile; streams are sized by drainage area (width ~ sqrt(area)). Invariant 1 holds on s7.
   The world river keeps vanilla's width (its discharge comes from the world).
3. **Water table, lakes and moisture** (W6, W7, S1, S3) — done (feature/water): `LL_SurfaceWater` fills closed
   hollows by water balance and runs streams on drainage lines; one ordered ground rule table (XML) places water,
   marsh, mud, rock, scree and soil; vanilla's noise no longer places water. Invariant 2 holds on s1, s4, s5, s7.
   Later: rivers sinking into gravel and reappearing at narrows (W7), salt flats and dry lakes in arid hollows.
4. **Channel patterns** (W4, W5): confinement and stream power per reach; single, split and braided first, then
   meanders with cutoffs, then entrenched meanders and terraces along rivers, then deltas.
5. **Coast** (C1-C3) and **sediment fill** (F3).
6. **Caves, springs, fans** (W8, W9). Verify 1.7 cave generation and water under roofs first.
7. **Wind and dunes** (F5), **ice and edge influence** (I1, S5), **glacial scour** (F4), **impacts** (F6).
8. **Debris** (S4), **floodplain banding** (S2), **gameplay checks** (G1-G3).

Further reference maps (the owner's source has many more) are added to `VISUAL-TARGETS.md` and mapped onto these
rules; a map that needs a new rule adds it here.
