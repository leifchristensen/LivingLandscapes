# Project review (2026-10-16)

A snapshot of what Living Landscapes generates, why, and where it falls short of the reference maps. It ends with a
plan to replace special-case code with general rules. `ARCHITECTURE.md` stays the living handover; this file is a
point-in-time review.

Status in one paragraph: the mod generates a landscape from geological rules ("On" mode). Heights come from an erosion
model over tilted rock layers. That drives rock walls, soil, wet ground, streams and cliffs through vanilla's own
map-generation grids. Drainage, soil and cliffs are accepted by the owner. Water is the weak area: lakes, ponds and
marsh are still placed by vanilla's noise and only shuffled toward wet ground. Rivers are carved downhill but have
no channel pattern yet. Coasts, ice, wind and caves are not started. Plants, the mod's original goal, wait on all of this.

---

## 1. Data we generate

### 1.1 Environment layers (saved with the map, one overlay each)
Computed in dependency order by `MapComponent_Environment`. Elevation-stage layers run right after vanilla's
ElevationFertility step; Plants-stage layers run just before plants.

| Layer | Stage | How it is calculated | Used for |
|---|---|---|---|
| LL_Elevation | Elevation | Copy of vanilla's elevation grid (warped Perlin) | Off mode input; the grid we later overwrite |
| **LL_Landform** | Elevation | **On:** `LandscapeGenerator`: a region 2.5x the map, uplift by hilliness and tile mutators (`Landforms`), stream-power erosion plus hillslope creep on a 2 m grid, rock strength from `RockStrata`, cropped and smoothed, micro-relief added, river bed carved (`RiverCarver`). **Off:** `HeightInference` from vanilla terrain classes | Everything below; the "topology" overlay |
| LL_Height | Elevation | Landform stepped into equal height stages (`Terraces`) where steep; gentle ground unchanged | Walkable height; cliff placement; drainage, slope, aspect |
| LL_RockStrength | Elevation | Strength 0..1 of the rock bed at each cell's height (`RockStrata`) | Soil depth, erosion |
| LL_SoilDepth | Elevation | `SoilModel`: thins with slope, thickens where flow converges, deeper on weak rock | Rock walls, rock floor, gravel, fertility |
| LL_DrainageArea | Elevation | Multiple-flow-direction upslope area on LL_Height after depression filling (log10 cells) | Wetness, washes, streams |
| LL_Slope / LL_Aspect | Elevation | Gradient magnitude and downhill direction of LL_Height | Slope vector field: scree, cliff facing; later sun exposure |
| LL_Wetness | Elevation | Wetness index ln(area / slope) on **LL_Landform**, plus height above the drainage line (D8 descent), sharpened on dry tiles | Fertility rank, wet patches (mud, marsh, ponds), moisture |
| LL_WaterDistance | Plants | Chamfer distance to any water cell placed by then | Moisture |
| LL_Moisture | Plants | 1 - (1 - wetness)(1 - proximity to water) | Overlay; future plant rules |
| LL_Soil | Plants | Vanilla fertility / 1.2 | Overlay only (early prototype) |

### 1.2 Transient plans (used during generation, not saved)
| Plan | Made by | Used for |
|---|---|---|
| River course | Erosion model's trunk channel from the river entry (`PlannedRiverCourse`) | Carving (`RiverCarver`); river segments for vanilla's painter |
| Rock strata | `RockStrata` (beds, dip, strike, warp, soft interbeds, 2 joint sets) | Erodibility, rock type per cell (`StrataRockNoise`), passes |
| Cliff plan | `CliffPlan` from LL_Height stages: band cells, facing, 8-neighbour lower mask, steps | Spawning `LL_Cliff` / `LL_CliffSteps` |
| Talus | Cells just below cliff faces | Rock chunks after terrain is final |

### 1.3 What we write into vanilla
| Vanilla grid or thing | Written by | Rule |
|---|---|---|
| Elevation grid (a rock/floor classifier, not a height) | `VanillaTerrainWriter` | Rock and roof thickness by depth into bare rock. Rough floor where soil is under `rockFloorDepth`. Gravel within `screeReach` of rock on slopes, along dry washes, and in joint passes. Soil elsewhere |
| Rock type noise | `StrataRockNoise` | Vanilla's rock-type lookup reads our beds |
| Fertility grid | `GroundWriter` | Rank-mapped from wetness and soil depth, so each biome keeps its share of each soil type |
| Patch-maker noise (mud, marsh, ponds) | `GroundWriter` (reflection) | Rank-mapped from wetness |
| River graph | `Patch_TileMutatorWorker_River_*` | Our course as straight segments; vanilla paints the water |
| Moving water + river flow map | `StreamWriter` | Narrow still water on strong drainage lines becomes moving water |
| Cliffs, steps, talus | `Patch_GenStep_RocksFromGrid_Generate`, `Patch_GenStep_Plants` | From the cliff plan; cliffs in water removed afterwards |
| Scattered ruins | `Patch_MapGenerator_GenerateContentsIntoMap` | Skipped while the setting is off |

---

## 2. Geological principles

Status: **done** = in the model and accepted by the owner, **partial** = in the model but not to target, **planned** = rule
written in `GEOLOGY-RULES.md` only.

| Principle | Rule | Status | Expressed through (data source → layer → output) |
|---|---|---|---|
| Read the world, never invent it | P1 | done | Tile hilliness, elevation, rainfall, river, coast direction, mutators, rock types → `WorldContext`, `Landforms` |
| Rock first, then water, soil, life | P2 | done | Layer dependency order |
| Features at map scale, distinct areas | P5 | partial | Wavelengths and bed sizes in map cells. No distinct-areas check yet (invariant 7) |
| Tilted layered beds | R1 | done | XML strata settings → `RockStrata` |
| Rock types on beds | R2 | done | Tile rock types + `RockProperties` (strength, solubility) → `StrataRockNoise` |
| Joints break ridges | R3 | partial | Joint sets weaken strength in erosion. Passes are cut afterwards as a separate step (`OpenPasses`) |
| Strength drives erosion | R4 | done | Strength → erodibility per cell, refreshed every 10 erosion steps |
| Uplift and erosion | F1 | done | Stream power + creep on two landform scales |
| Base level | F2 | partial | Region edges, river exit, sea side are outlets. No lake base levels |
| Sediment fill on low relief | F3 | planned | Nothing yet: flat tiles have no rock at all (s4, s7) |
| Glacial scour, wind/dunes, impacts | F4-F6 | planned | – |
| Water downhill or level | W1 | done (one river map) | Course from the model, bed carved to a never-rising profile; invariant 1 on s7 |
| Sources, channel size | W2-W3 | partial | Streams from drainage area threshold. River width is vanilla's fixed width, not discharge |
| Channel pattern, banks | W4-W5 | planned | – |
| Lakes in closed hollows | W6 | **failing** | Ponds come from vanilla noise rank-mapped by wetness; invariant 2 fails on 4 of 4 seeds |
| Water table, surface water | W7 | partial | Height above drainage inside wetness. No surface-water layer |
| Springs, caves | W8-W9 | planned | – |
| Moisture | S1 | done | LL_Wetness (accepted by owner) |
| Floodplain | S2 | planned | – |
| Terrain classes in one order | S3 | partial | Spread over `VanillaTerrainWriter`, `GroundWriter`, `StreamWriter` |
| Debris at slope breaks | S4 | partial | Talus below cliffs. Gravel is a fixed-width ring around rock, not a slope-break rule |
| Edge influence, coast, ice | S5, C1-C3, I1 | planned | – |

Rock is deliberately simplified to layered beds. Formation types (sedimentary, igneous, metamorphic) are a later revisit.

---

## 3. Gameplay and visual-interest rules (owner decisions)

| # | Rule | Where it lives | Status |
|---|---|---|---|
| G-a | Every map has distinct areas; compress features to map scale | Wavelengths, bed sizes | No check yet |
| G-b | Ridges are not unbroken walls; formations show height change | Joint passes; walkable-area count in log | Works (s1: largest area 89%) |
| G-c | Sealed pockets are fine (minable) | – | Accepted |
| G-d | Cliffs climbable at high cost, never block sight | `Cliffs.xml` (path 400, fill 0.45) | Done; later: impassable + stairs |
| G-e | A cliff never suggests a way up that isn't there | Own defs, steps | Done |
| G-f | Cliffs face downhill only; ridges both sides; slopes as nested steps | `CliffPlan` lower mask | Done |
| G-g | Each face reads as one step up: thin, closely nested | Art (FACE 40 px), autotiles | Done, accepted |
| G-h | Closed cliff rings become rock (cliffs show direction) | `MakeRockKnobs` | Done |
| G-i | Own art in RimWorld style: soft, shaded, organic, rock-coloured | `generate_cliff_art.py` | Done, accepted |
| G-j | Moisture readable from soil, drainage from converging wet ground, later plants | GroundWriter rank maps | Soil done; plants pending |
| G-k | Flowing water is moving water; ponds and lakes stay still | `StreamWriter` | Done, by post-pass |
| G-l | Water never flows uphill | `RiverCarver`, invariant 1 | Done |
| G-m | Ruins off until terrain is polished | Setting | Done |
| G-n | Variety from topography, never exception passes | Principle | See section 5 |

---

## 4. In-game results vs the visual targets

Compared: the owner's reference maps (targets 1-10) against our latest exports (s1 mountainous, s4 boreal flat,
s5 large hills, s7 flat with wide river) and the owner's in-game screenshots of cliffs.

### 4.1 What already matches
- **Drainage networks** (s1 plain, s5): branching, converging streams that follow the land, with wet ground along
  them. Closest to target 4's streams leaving the mountain foot.
- **Cliffs** on hills (s5, in-game shots): nested steps that follow contours, rock-coloured, with shadows. Close to
  the stepped walls in targets 1 and 6.
- **Rock types in beds**: rock colour changes with the layers.
- **Rivers** no longer climb (s7).

### 4.2 Where we fail, and why

| # | Gap (what the target shows vs what we make) | Targets | Root cause in our model |
|---|---|---|---|
| 1 | **Mountains are striped rings, not solid masses.** Targets: dark mountain rock with a crisp, irregular edge, a thin gravel rim, knobs and fingers. Ours (s1, s5): wide concentric bands of rough floor, gravel and soil that follow the contours like bathtub rings | 1, 4, 5, 6, 7, 9 | Rock floor and gravel are pure threshold functions of soil depth and of distance to rock, both smooth functions of slope. Thresholds on a smooth field give even-width bands. Gravel uses a fixed `screeReach` halo instead of a deposition rule |
| 2 | **Flat maps have no rock at all.** Targets 3, 5, 6, 7 have isolated blocks, spires and knobs on flat or rolling ground. s4 and s7: 0% rock | 3, 5, 6, 7 | Low relief → small erosion signal → soil depth never reaches zero. Rule F3 (sediment fill, hard rock standing above it) not built |
| 3 | **Water is noise, not hydrology.** s4: hundreds of ragged ponds over the whole map. Targets: connected channel webs (3) or rounded lakes in hollows with shallow rims and deep cores (5) | 3, 5, 8 | Ponds and marsh still come from vanilla's patch-maker noise; we only re-rank it by wetness. No lake-filling model (W6), no surface-water layer (W7). Invariant 2 fails on every seed |
| 4 | **Rivers are smoothed straight runs of constant width.** Targets: meanders and oxbows (2, 6), splits around islands (7), narrows and basins (1), even width when confined (9) | 1, 2, 6, 7, 9, 10 | Course is the model's coarse trunk channel, smoothed and simplified. Width is vanilla's fixed value. No channel pattern (W4), no bank asymmetry (W5) |
| 5 | **No floodplain band.** Target 10: a band of rich soil and mud along the river, scroll-bar stripes, a step at its edge. s7: uniform soil to the bank | 1, 10 | Wetness comes from local drainage only. The river's own flood belt (S2) is not modelled; wetness does not see river discharge |
| 6 | **Mountain fronts are smooth curves.** Target 4: fingers and ridges reaching out at one angle (strike), valleys between | 4 | Beds are visible in colour, but the strength contrast is not strong enough to shape fingers. Also hidden by gap 1 |
| 7 | **No coasts, ice, dunes, caves, impacts** | 2, 5, 8, 9 | Planned rules (C1-C3, I1, F5, W9, F6) |
| 8 | **Terraces are equal height steps.** Targets show steps where hard beds crop out | 1, 6 | Accepted for now. Build step 2 (terraces from beds) |

Gaps 1-3 are the largest visual differences and cut across most targets. They come from how values are
**classified**, not from missing features. That makes them good candidates for systemic fixes (section 5).

---

## 5. Making it more systemic

### 5.1 Special cases and redundancy found

| Code | Type | Problem | Replace with |
|---|---|---|---|
| `GroundWriter` pond/marsh rank-mapping of vanilla noise | Workaround | Water placed by noise; breaks W6 | Surface-water layer (5.2 A) |
| `StreamWriter` (still water → moving water) | Post-pass fix | Corrects vanilla's water after the fact | Surface-water layer sets still or moving water from discharge |
| `Patch_GenStep_Plants`: remove cliffs in water | Post-pass fix | Cliffs are planned before water is known | Plan water first; cliff plan excludes water |
| `OpenPasses` (cut gaps through ridges along joints) | Exception pass | Gaps are cut after erosion, not grown by it | Let open joints erode in the model (higher erodibility along open joints); keep the walkable-area check as an invariant |
| Gravel halo `screeReach` | Fixed-width rule | Makes bathtub rings (gap 1) | Deposition: debris where steep ground meets gentle ground (S4), weighted by the slope break |
| Rock floor / gravel / soil thresholds on smooth soil depth | Threshold bands | Even-width bands (gap 1) | One ordered terrain classifier (5.2 B) that also uses rock strength, joints and convergence, so edges are irregular |
| Terrace stages computed twice (`HeightWorker` and `VanillaTerrainWriter`) | Duplication | Two copies of the same steps, can drift | Compute once; keep the stage index in the cliff plan or a layer |
| Wetness reads LL_Landform; drainage reads LL_Height | Inconsistency | Two surfaces for one water model | One surface (landform), with terraces only for walking and cliffs |
| `RiverCourse.Find` fallback | Legacy | On mode always has the planned course | Remove; Off mode keeps vanilla's river |
| `VanillaTerrainWriter.Write(aspect)` parameter | Dead code | Unused since crest cliffs were removed | Remove |
| LL_Soil (copy of vanilla fertility) | Legacy layer | Superseded by soil depth + wetness | Remove or redefine as soil class (S3) |
| LL_WaterDistance + LL_Moisture | Overlap | Moisture = wetness + distance to water, which double-counts once surface water exists | Moisture from wetness + surface water (S1) |
| `RiverCarver` after the model | Acceptable for now | The river is carved into the finished surface | Later: carve the river in the erosion model itself, with width from discharge |

### 5.2 Proposed systemic changes, in order
A. **One surface-water model (W6 + W7).** A layer `LL_SurfaceWater` computed at the Elevation stage from the
   landform:
   - closed hollows filled to their spill level when rainfall over the catchment beats evaporation (depth decides deep
     or shallow water);
   - water table from height above drainage (marsh and mud where it reaches the surface);
   - moving water where discharge is high enough.

   This replaces pond rank-mapping, `StreamWriter` and the cliffs-in-water fix. It fixes gap 3 and invariant 2, and
   gives targets 3 and 5 their base.
B. **One terrain classifier (S3).** A single ordered rule table in XML: rock, water, marsh, mud, rich soil, soil,
   gravel, sand. Inputs: soil depth, rock strength, surface water, wetness, slope break, climate. It replaces the
   scattered thresholds in `VanillaTerrainWriter` and `GroundWriter`, and the dry-wash and gravel special cases. Fixes
   gap 1 together with C.
C. **Rock exposure from rock, not from slope alone.** Bare rock where strong beds and steep ground coincide; open
   joints erode in the model (passes grow instead of being cut); debris collects at slope breaks. Fixes gaps 1 and 6
   and removes `OpenPasses`.
D. **Sediment fill on low relief (F3).** Fill low ground toward a flat level, so only strong rock stands above it.
   Fixes gap 2 and gives targets 3, 5 and 7 their knobs and spires.
E. **Channel from discharge (W3, then W4).** River width from discharge, then channel pattern per reach (single,
   meandering, split) from slope, confinement and bank strength. Fixes gaps 4 and 5 step by step.
F. **Clean-up** (small, do first): remove the dead parameter, the `Find` fallback and LL_Soil; compute terrace stages
   once; make wetness and drainage read the same surface.
G. **Invariants as the definition of done.** Add checks 3-7 (single channel in rock, split channels rejoin, passes,
   wet strip along rivers, distinct areas) to `DevTools/Invariants.cs`. Move the walkable-area count there.

Recommended order: F (cleanup), A (water), B + C (classifier and rock exposure, together), D, E. Water first, because
it is the current branch and the largest single gap.
