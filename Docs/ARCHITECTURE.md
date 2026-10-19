# Living Landscapes: architecture and plan

Developer handover for humans and AI assistants (Claude, local models). Keep it short and current: update it in the
same commit as any change to structure, decisions or plans. Machine-specific setup lives in the git-ignored CLAUDE.md.

## Goal
Geology drives everything: a realistic height field shapes terrain, water, soil and, later, plants (moisture, sun
exposure, competition, clustering; see the Genesis/Horizon-style rule design in the research summary).
Rule: **start with realism, dial back to fun.** Read world data (tile elevation, hilliness, rivers, mutators,
neighbours); never generate it. Stack on top of world-gen mods (Realistic Planets 2: optional, supported).

## Two height modes (setting "Geologically driven terrain", default on)
- **On**: a principles-driven heightmap (`Geology/LandscapeGenerator`) is generated first and drives vanilla terrain,
  rock, roofs, rivers and cliffs. Realism is perfected here first.
- **Off**: vanilla generation is untouched; height is inferred from the terrain it made (`Geology/HeightInference`).
  Calibrated later against On. For players running unsupported terrain mods.

## Generation pipeline (vanilla GenStep order, our hooks)
| Step | Hook | What we do |
|---|---|---|
| (all steps) | `Patch_MapGenerator_GenerateContentsIntoMap` | Skip scattered ruins while the setting is off (`GenStepFilterDef` LL_ScatteredRuins) |
| ElevationFertility (10) | `Patch_GenStep_ElevationFertility_Generate` postfix | Compute the Elevation stage layers (incl. surface water); classify ground by the ground rules into vanilla elevation (rock, floor, gravel) and planned cover (water, marsh, mud); fertility rank-mapped (`GroundWriter`); rock types follow strata; plan cliffs on dry ground |
| Terrain (210) | `Patch_MapGenUtility_GetNaturalTerrainAt` postfix | Planned cover replaces vanilla's choice; vanilla's noise water, marsh and mud elsewhere become dry ground |
| Mutators (20) | `Patch_TileMutatorWorker_GeneratePostElevationFertility` | Skip elevation rewrites of mutators we model as landforms |
| River graph | `Patch_TileMutatorWorker_River_*` | Rivers follow the planned channels (`RiverPlan`: main channel, side branches) as straight segments, no vanilla meander. Vanilla's river and bank painting (`RiverTerrainAt`, `RiverBankTerrainAt`) defers to planned cover when the river was carved |
| RocksFromGrid (200) | `Patch_GenStep_RocksFromGrid_Generate` | Spawn cliff bands, steps; plan talus |
| Plants (900) | `Patch_GenStep_Plants` | Write stream flow (`FlowWriter`); compute the Plants stage layers; remove cliffs under water vanilla painted later; spawn talus |
| After fog | `Patch_TileMutatorWorker_River_GeneratePostFog` | Vanilla rebuilt the flow map for its river: write our stream flow again |

## Environment layer framework (`Source/LivingLandscapes/Environment`)
- New environment data = new `EnvironmentLayerDef` (XML, `1.7/Defs/EnvironmentLayerDefs`) + `EnvironmentLayerWorker`
  (`Layers/`). Never a one-off field or overlay. Tuning goes in a `DefModExtension` on the def.
- `MapComponent_Environment` computes layers in dependency order per `GenerationStage` (Elevation, Plants), saves
  them quantised (`EnvironmentGrid`, per-map min/max). Transient plans (river course, cliffs, talus) live there too.

| Layer | Meaning |
|---|---|
| LL_Elevation | vanilla elevation grid as generated (classifier: >0.70 rock, roofs above) |
| LL_Landform | natural surface in metres (On: generator; Off: inference). Overlay label "topology" |
| LL_Height | walkable height: landform stepped into terraces where steep (`TerraceExtension`) |
| LL_RockStrength | strength 0..1 of the rock bed beneath each cell (On mode only) |
| LL_SoilDepth | soil thickness from slope, convergence and rock strength (`SoilModelExtension`) |
| LL_DrainageArea | multiple-flow-direction upslope area over the depression-filled landform, log10 cells |
| LL_Wetness | topographic wetness 0..1: wetness index + height above the drainage line a cell drains to, sharpened on dry tiles |
| LL_Debris | rockfall debris collected where the slope eases below bare rock faces (faces feeding each cell) |
| LL_SurfaceWater | water depth (m): lakes fill closed hollows to the water-balance level (rain on the catchment vs evaporation from tile temperature), streams where drainage area x climate passes `streamArea`, width ~ sqrt(area) |
| LL_WaterFlow | flow speed 0..1 in stream channels (0 in lakes); moving water in game |
| LL_Slope | rise over run of LL_Landform (hydrology and slope read the natural surface; terraces only shape walking and cliffs); drawn as a vector field using LL_Aspect |
| LL_Aspect | downhill direction, radians counter-clockwise from east, 0..2π |
| LL_WaterDistance, LL_Moisture | water proximity, moisture (wetness raised near water) |

**Slope vector field** = LL_Slope (magnitude) + LL_Aspect (direction); `GridMath.SlopeToward` gives the slope along
any heading. Intended consumers: runoff and erosion, scree/talus placement, cliff facing, sun exposure (latitude),
later pawn movement cost (vanilla path cost is per cell and non-directional).

## Geology (`Source/LivingLandscapes/Geology`)
- `GridMath`: masked blur/inpainting, chamfer distance, priority-flood fill, breach-fill, D8 and MFD flow, slope,
  aspect, region labelling.
- `LandscapeGenerator`: region model ~2.5x map size, stream-power erosion (Braun–Willett implicit) + hillslope
  diffusion, cropped and smoothed at the model cell size (removes the erosion grid's checkerboard). Two landform
  scales: main ridges/valleys (`landformWavelength` 300 m) and hillside detail (`detailWavelength` 100 m, spurs, knolls,
  hollows) on a 2 m erosion grid, so gullies cut the slopes and cliff lines bend and break with the ground.
- `RockStrata` (`<strata>` in XML): tilted beds of the tile's rock types (`RockProperties` patched onto vanilla rocks:
  strength, solubility), soft interbeds, warped, two partly open joint sets. Erodibility and creep per cell come from
  the bed exposed at its current height, so hard beds become ridges and soft beds strike valleys. `StrataRockNoise`
  replaces vanilla's rock-type noises so rock walls and floors show the beds. Tile mutators become `LandformExtension`s in XML (`1.7/Patches/TileMutatorLandforms.xml`).
- **Ground rules** (`GroundRulesDef` LL_GroundRules, `1.7/Defs/GroundRules/GroundRules.xml`): one ordered rule table
  classifies every cell (first match wins) into water, mountain, rough stone, marsh, mud, scree, washes, soil. Rules
  test any layer by defName plus derived fields (RockDepth, RockDistance, WaterDistance, Hollow, Climate; see
  `GroundFields`). Each class sets vanilla's elevation value and optional cover. Tuning terrain = editing this table.
- `GroundWriter`: vanilla's fertility grid and the biome's rich-soil patch makers are rank-mapped from wetness and soil
  depth, so each biome keeps its soil shares but rich soil sits where water collects. Water, marsh and mud never come
  from vanilla noise.
- Passes: ridges must not be unbroken walls in a 2D map. Open joints weaken the rock (`jointWeakness`), so erosion
  notches ridges along them and soil holds there; no separate pass-cutting step. The writer logs walkable areas.
- `LL_Debris` (rule S4): fragments from bare rock faces roll down the steepest way until the slope eases and collect
  there; scree lies where debris is high (fans and aprons below rock instead of a fixed-width ring).
- **Rivers** (W1, W3-W5): `RiverChannels` plans the world river's channels on the landform. Meander migration
  (Howard-Knutson) is scaled by vanilla's channel width, so creeks bend tightly and great rivers gently. Cutoffs
  leave oxbows. Banks erode by rock strength and stop at valley walls. Splits form around islands where carrying
  capacity (slope over floor width) falls, which means deposition. `RiverCarver` carves the plan:
  - The water surface follows the never-rising valley floor profile.
  - Under it, a cross-section: a deep core shifted to the outside of bends, a shallow rim and inner bar, and pools at
    bends with shallow riffles (fords) between them. The banks are steep outside and gentle inside.
  - Oxbows are plugged closed hollows.

  The plan's water level feeds `LL_SurfaceWater`, so river water comes from the same ground rules as lakes and
  streams. The river's upstream inflow (from its width, by the stream width rule read backwards) enters
  `LL_DrainageArea`. `LL_Wetness` lets the water table meet a river at its surface and widens the wet band with
  drainage area, giving the floodplain.
- `FlowWriter`: stream flow (LL_WaterFlow, smoothed downhill direction) into vanilla's river flow map, so streams
  animate and turn watermills; rewritten after vanilla's own river rebuilds the map after fog.
- `SoilModel` (soil depth: thinner, and bare at gentler slopes, on strong rock), `VanillaTerrainWriter` (runs the
  ground rules, passes, rock knobs, cliff plan).
- `Terraces` + `CliffPlan`: steep ground (above `gentleSlope`) is stepped into stages; cliffs only at stage edges where
  contours crowd together. Steps (ramps) every `stepSpacing` cells.

## Cliffs (`Building_Cliff`, `CliffDefs`, `1.7/Defs/ThingDefs/Cliffs.xml`)
A cliff shows which way a steep slope drops: a climbable step between heights, walkable ground on its high side.
Own defs and own art (owner, 2026-10-16); Decorative Cliffs is no longer used or required.
- `LL_Cliff` (PassThroughOnly, path cost 400, fill 0.45: cover, never blocks sight) and `LL_CliffSteps` (standable).
  One thing per band cell on the higher side; `CliffPlan.Lower` records which of the 8 neighbours are lower.
- Autotiled art: one texture per neighbour pattern (256, `Cliff_<mask>` / `Stairs_<mask>`), rounded organic outline
  that meets neighbours seamlessly, short rock face where the drop faces south, thin rim elsewhere, plateau side fading
  into the ground; greyscale, tinted with the local rock. Soft shadows (`Shadow_South/East/West`) on the lower cells.
  A face's apparent height must match the drop: thin faces, so stacked cliffs read as nested lines.
- Art: `Source/ArtTools/generate_cliff_art.py` (pure Python, `png_min.py`, ~1 min) writes
  `Textures/Things/Building/LivingLandscapes/Cliff/`. Style reference: owner-supplied asset pack (not in the repo).
  Grass overhang not done yet.
- Distribution: terrace bands where steep (12 stages across the relief). A cliff ring round a small summit has no
  direction, so summit stages up to `knobMaxCells` with mostly steep sides become rock knobs instead. Thin rock stays
  mountain rock (no crest cliffs). Talus chunks below faces.

## Overlays (`Source/LivingLandscapes/Overlay`)
One map toggle per layer with an `<overlay>` (lower right corner, `Patch_PlaySettings_DoPlaySettingsGlobalControls`);
any number can be on at once, stacked legends top right. Style is XML (`OverlayProperties`): colour ramp (absolute
or relative), bands, contours with labelled index lines, optional `icon`, and `directionLayer` to draw as arrows
(`VectorFieldRenderer`, baked once into a few meshes). GIS/USGS look; not saturation grading.

## Dev and testing
- Command line: `-quicktest -ll-export -ll-quit [-ll-quit-delay=s] [-ll-seed=text] [-ll-mutators=A+B] [-ll-hilliness=X]`.
  Exports every layer as PNG (cliffs marked; grey ramp for layers without an overlay), `Map.png` (rock in its own
  colour, rough floors, water, cliffs, ground types) plus summary.txt. Compare placed things against overlays.
- Test seeds: s1 mountainous + lake, s4 boreal lakes, s5 hills + water, s7 wide river.
- Conventions: one Harmony patch class per file `Patch_<Type>_<Method>`; prefix/postfix over transpilers; verify the
  1.7 signature in decompiled source first; never commit game code or assets; feature branches, owner merges.

## Working agreements
- Owner: systems architect, not a game dev. Ask about design and architecture; objective-oriented, systemic
  frameworks, human-readable modder-standard code.
- Usage limits: compress context often, keep this file current, commit small and often.

## Decisions log
- Overlays: GIS hypsometric bands + contours; one toggle per layer, several at once (replaced the nested menu).
- Cliffs are Things (option B), passable, directional, material-coloured; terraces rather than mountain blocks.
- Cliffs only where contour lines are very close; mountain rock edges currently excluded (open question below).
- Water flows downhill or level, never uphill (hard rule, checked on exports).
- Features are compressed to map scale so each map has distinct areas; 1:1 real scale is given up on purpose.
- Rock is simplified to layered beds for now; formation types revisited later.
- Variety comes from the topography generation; features are then placed by normal rules, never by exception passes.
- Soil type, wet ground and later plant density must read as indicators of moisture and drainage (owner, 2026-10-16).
  Vanilla ground is rank-mapped from our fields rather than replaced, to keep each biome's balance.
- Ridges show height change as identifiable rock formations broken by passes, not continuous walls (owner).
- Mountains for large rock masses; cliffs mark height changes (ridges, pass sides, ravines), oriented by the slope
  vector field. Water that flows (drainage-line streams) is moving water; ponds and lakes stay still.
- Moisture overlay is a heatmap (transparent dry to blue wet), no contours.
- Scattered ruins are off by default until terrain generation is polished (setting; tile-mutator structures stay).
- Erodibility raised 0.02 → 0.05 so erosion, not the uplift noise, shapes landforms (rock differences only show when
  erosion is strong enough). Rock walls start below 0.15 m soil.
- Slope stored as two scalar layers (magnitude, aspect) rather than a packed vector, to reuse the layer framework.

## Roadmap / open items
Latest review (data, principles, gaps against the targets, systemic clean-up plan): `Docs/PROJECT-REVIEW.md`.
Current focus: geological features that read well on the map, worked from the owner's reference maps in
`Docs/VISUAL-TARGETS.md` (1: river valley through mountains; 2: desert river, delta and dunes; 3: marsh with rock spires; 4: mountain foot with tilted strata and caves; 5: lakes, broken ridges, impact site; 6: entrenched meanders, mountain apron; 7: river splitting in an open basin; 8: boreal forest meets glacial plain; 9: cove with a cave river; 10: broad valley between ranges, bridge). The rules that produce them, and the build order, are in
`Docs/GEOLOGY-RULES.md`; it supersedes items 1, 2, 4 and 5 below where they overlap.

1. Cliffs: first release done (owner accepted placement, blending and rock colour, 2026-10-16). Later: terrace
   stages from hard beds instead of equal heights; impassable see-through cliffs with stairs that blend in and a
   connectivity check; grass overhang on the lip.
2. Use the vector field for runoff/erosion, scree and talus, sun exposure.
3. Height cues: stage tint, hillshading (cliff shadows and south faces are done).
4. Remaining landforms: Basin, Hollow, Chasm, Crevasse, coasts, river variants. Landform shapes are too rounded.
5. Water from the heightmap: done (lakes by water balance, streams, marsh and mud from the ground rules). Channel
   patterns first version (meanders, cutoffs, pools and riffles, splits by deposition, floodplain wetness); review of
   water against the targets in `Docs/WATER-REVIEW.md`. Next: sediment fill on low relief (F3), surface rock
   (`Docs/SURFACE-ROCK-PLAN.md`). Gameplay tuning of water comes later ("dial back to fun").
6. Off-mode calibration against On.
7. Plants: habitat rules per species in XML (`PlantHabitat`), strata, shade, clustering, regrowth consistency.
8. Toggle icons per layer (art).
9. Revisit rock formation: sedimentary, igneous and metamorphic rock form and erode differently (see
   `GEOLOGY-RULES.md` section 3). The simplified layered-beds model stands until then.
