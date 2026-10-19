# Living Landscapes – research and design summary
Handover from the planning chat (2026-10-14). Status: smoke test passed; milestone 2 not started.
Treat everything marked VERIFY as unconfirmed until checked against the decompiled 1.7 source.

## 1. Vision
Make RimWorld plant distribution emerge from biological and geological rules instead of uniform random scatter:
clustering, water dependence, canopy shade, slope aspect, soil, competition. Inspired by Star Citizen's
Genesis planet tech (CitizenCon 2954, "Brave New Worlds", Phase 1 "Planets").

## 2. Genesis – what to borrow (paraphrased from the talk transcript)
- Old approach (same weakness as vanilla RimWorld): hand-made biome "slices" scattered by temperature + humidity only → repetitive, sparse, two axes of variation.
- More data layers: geology, soil type, soil depth, nutrients, water availability, temperature, humidity.
- Every asset has a rule set. Per location the system scores **viability** (can it live?) and **vitality** (how well?), and candidates compete. Ferns end up under pines because their rules favour it, not because an artist put them there.
- **Dominance** knob per species on top of competitiveness.
- **Hierarchical spawning**: big assets first; their **shade is propagated down** to lower layers. Shade-tolerant plants can grow in sun but usually lose there → occasional "rogue" plants (keep some randomness).
- **Seed dispersal** clumping, tuned per species.
- **Slope aspect** (sun exposure) → different vegetation on opposite valley sides.
- **Rocks** placed from offline erosion maps → boulders below cliffs / at forest edges.
- Per-tree **age/vitality** → size and colour variation. Plants survive across a wide range but only thrive in ideal conditions (marginal sites = small/poor specimens, not nothing).
- **Same rules everywhere**: the distant/orbital colour asks the rules "what would grow here". → For us: map generation and regrowth must use the same scoring.

## 3. Other prior art
- Horizon Zero Dawn (GDC 2017, Guerrilla): world data maps → per-asset density graphs; placed objects leave "footprints" later layers read (trees suppress/attract undergrowth); density → points via dithering; big-to-small layering.
- Beneš et al. 2011 (I3D): neighbourhood-overlap competition with viability function (rare species protected); seeding in a ring around parents → clusters.
- Deussen et al. 1998 (competition simulation), Lane & Prusinkiewicz 2002 (multilevel placement with species association/repulsion).
- Conclusion: use the one-pass "evaluate rules + layers" family (Genesis/HZD), not multi-year simulation.

## 4. RimWorld scales
- Planet (seeded) → tiles with coordinates (biome, temperature, rainfall, elevation, hilliness, latitude, rivers, coast, tile mutators e.g. fjord, crater) → map with its own topography.
- Vanilla tiles are non-contiguous (mods can make terrain continuous). World-gen mods (e.g. Realistic Planets 2) already do the planet well.
- **Decision: read world data, never generate it.** Stack on top of any world-gen mod.
- Within one map, temperature/rainfall are ~constant; variation comes from local fields.
- World-level inputs: latitude/hemisphere (which slopes face the sun), tile mutators as habitat signals, rivers/coast, **neighbour tiles' biomes → edge ecotones** (fading band of neighbouring biome's species near map edges).

## 5. Map generation facts (from 1.7 defs/metadata)
GenStep order (CommonMapGenerator): ElevationFertility 10 · MutatorPostElevationFertility 20 · RocksFromGrid 200 · Terrain 210 · MutatorPostTerrain 220 · Roads 390 · Mutator structures 500/700 · Ruins 750 · Plants **900** · SteamGeysers 950 · RockChunks **970** (after plants) · Snow 1150 · Animals 1200.
Hook points present in 1.7: GenStep_Plants.Generate; WildPlantSpawner (CheckSpawnWildPlantAt, PlantChoiceWeight, GetCommonalityOfPlant, CalculatePlantsWhichCanGrowAt); PlantProperties wildClusterRadius / wildClusterWeight / wildOrder.
Also present: waterInfo.riverFlowMap, Lake* mutators (Lake, LakeWithIsland, DryLake, CaveLakes), Hilliness*, ElevationThreshold, GenStep_ScaleElevation, cave elevation.

## 6. Elevation design
- Need a continuous per-cell height field (scalar) + gradient (slope, aspect).
- **Primary:** capture vanilla's map-gen elevation grid (mountains = elevation above threshold) and save our own copy in a MapComponent. VERIFY storage and persistence.
- **Fallback** (old saves, maps lacking the grid): reconstruct from labels (deep water < shallow water < marsh/mud < soil < thin rock roof < overhead mountain) with signed distance transforms per class to avoid terraces, then smooth; scale relief by tile hilliness.
- Compute once at generation and freeze (players mine roofs away).
- Per cell, not chunks: ~62k cells is milliseconds; chunks would erase 1–3 cell riverbank zones.

## 7. Moisture design
- Vanilla already places water (noise, lake mutators, world rivers/coast). Never place water from our field. **Burn existing water into the elevation field** (stream burning) so lakes are minima by construction; enforce elevation decreasing along riverFlowMap.
- Inputs combined into a 0–1 moisture field:
  1. HAND – height above nearest drainage (water-table proxy).
  2. Topographic Wetness Index – ln(upslope contributing area / tan(slope)); finds wet hollows/valley floors.
  3. Distance to water, fresh vs salt.
  4. Terrain class: marsh/mud saturated, sand drains fast, gravel dry.
  5. Modifiers: tile rainfall; pole-facing slopes moister; canopy shade reduces evaporation (after tree pass).
- Priority-flood depression filling also finds dry enclosed hollows → damp-ground / seasonal-pond candidates (scope decision later).

## 8. Core plant algorithm
1. Environment fields in a MapComponent: elevation, slope/aspect, moisture, soilQuality, soilDepth/rockiness, sunExposure, edge influence per neighbour biome; canopyShade after trees.
2. `PlantHabitat` DefModExtension per species (XML): response curve (optimum + tolerance) per factor, dominance, stratum (canopy/shrub/ground), shadeTolerance, clusterScale/clusterStrength. Format to be agreed with the owner before implementation.
3. Fitness = product of factor responses (viability = fitness > 0, vitality = value).
4. Candidate pool = biome plants + neighbour-biome plants weighted by edge influence.
5. Strata passes canopy → shrub → ground, updating shade after each.
6. Per cell: score = fitness × dominance × clusterNoise(species) × commonality; soft-max choice (keep rogue plants). Density from fertility × best fitness.
7. Regrowth: patch WildPlantSpawner with the same scores + "parent within N cells" bonus.
8. Modded plants without rules: derive defaults from vanilla fields (fertilityMin, tree/bush/grass, cave plant, wildClusterRadius).

## 9. Mod integrations (optional, soft dependencies only)
- As Above, So Below 2 (Astryl): up to 7 levels as **bands of one real map** → compute fields per band. 1.6 only; author waiting for 1.7; Workshop page currently shows removed.
- Dubs Bad Hygiene: groundwater mechanics UNVERIFIED. If it has a water-table grid, read it as a moisture input (read-only first).
- Test with Realistic Planets (and similar world-gen mods) once they support 1.7.

## 10. Milestones
1 smoke test (done) · 2 environment fields (elevation, moisture) + debug overlays · 3 fitness + water/soil rules · 4 strata + shade · 5 clustering/seed dispersal · 6 slope aspect (latitude-aware) · 7 edge ecotones from neighbour tiles · 8 regrowth consistency · 9 erosion-aware rock chunks · 10 tuning, settings, vanilla defaults, world-gen mod compatibility.

## 11. Open questions to verify in decompiled 1.7 code (do this first)
1. Where GenStep_ElevationFertility stores elevation/fertility grids; do they persist after map gen; how hilliness + ElevationThreshold decide mountains.
2. waterInfo.riverFlowMap: type, contents, saved with map?
3. How lakes and Lake/DryLake/CaveLakes mutators place water.
4. When thick (overhead mountain) vs thin rock roofs are assigned.
5. World-tile data reachable during map gen: hilliness, latitude, rainfall, neighbour biomes, mutators.
6. If installed in the workshop folder: how DBH models groundwater; how AASB2 defines level bands.
Report findings before writing code.
