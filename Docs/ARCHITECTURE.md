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
| ElevationFertility (10) | `Patch_GenStep_ElevationFertility_Generate` postfix | Compute the Elevation stage layers; write vanilla elevation (rock, washes) and fertility and wet patches (`GroundWriter`); rock types follow strata; plan cliffs |
| Mutators (20) | `Patch_TileMutatorWorker_GeneratePostElevationFertility` | Skip elevation rewrites of mutators we model as landforms |
| River graph | `Patch_TileMutatorWorker_River_*` | Rivers follow the erosion model's trunk channel (straight segments, no vanilla meander) |
| RocksFromGrid (200) | `Patch_GenStep_RocksFromGrid_Generate` | Spawn cliff bands, steps; plan talus |
| Plants (900) | `Patch_GenStep_Plants` | Compute the Plants stage layers; remove cliffs in water; spawn talus |

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
| LL_DrainageArea | multiple-flow-direction upslope area over the depression-filled surface, log10 cells |
| LL_Wetness | topographic wetness 0..1: wetness index + height above the drainage line a cell drains to, sharpened on dry tiles |
| LL_Slope | rise over run of LL_Height; drawn as a vector field using LL_Aspect |
| LL_Aspect | downhill direction, radians counter-clockwise from east, 0..2π |
| LL_WaterDistance, LL_Moisture, LL_Soil | water proximity, moisture (wetness raised near water), soil class |

**Slope vector field** = LL_Slope (magnitude) + LL_Aspect (direction); `GridMath.SlopeToward` gives the slope along
any heading. Intended consumers: runoff and erosion, scree/talus placement, cliff facing, sun exposure (latitude),
later pawn movement cost (vanilla path cost is per cell and non-directional).

## Geology (`Source/LivingLandscapes/Geology`)
- `GridMath`: masked blur/inpainting, chamfer distance, priority-flood fill, breach-fill, D8 and MFD flow, slope,
  aspect, region labelling.
- `LandscapeGenerator`: region model ~2.5x map size, stream-power erosion (Braun–Willett implicit) + hillslope
  diffusion, cropped and smoothed at the model cell size (removes the erosion grid's checkerboard).
- `RockStrata` (`<strata>` in XML): tilted beds of the tile's rock types (`RockProperties` patched onto vanilla rocks:
  strength, solubility), soft interbeds, warped, two partly open joint sets. Erodibility and creep per cell come from
  the bed exposed at its current height, so hard beds become ridges and soft beds strike valleys. `StrataRockNoise`
  replaces vanilla's rock-type noises so rock walls and floors show the beds. Tile mutators become `LandformExtension`s in XML (`1.7/Patches/TileMutatorLandforms.xml`).
- `GroundWriter` (`GroundWriterExtension` on LL_Wetness): vanilla's fertility grid and the biome's wet patch makers
  (mud, marsh, ponds, rich soil) are rank-mapped from wetness and soil depth, so each biome keeps its ground shares
  but soil, rich soil, mud and ponds sit where water collects. Dry washes: gravel beds along drainage lines on dry tiles.
- Passes (`SoilModelExtension` pass*): ridges must not be unbroken walls in a 2D map. Where an open joint crosses a
  ridge (joint line within 45 degrees of the ridge's across direction, from the structure tensor of the rock mask),
  rock within `passMaxDepth` of the rock edge becomes rough stone. The writer logs the number of walkable areas.
- `SoilModel`, `VanillaTerrainWriter`: soil depth (thinner, and bare at gentler slopes, on strong rock) → vanilla elevation classes (rock, roof thickness, gravel, soil).
- `Terraces` + `CliffPlan`: steep ground (above `gentleSlope`) is stepped into stages; cliffs only at stage edges where
  contours crowd together. Steps (ramps) every `stepSpacing` cells.

## Cliffs (`Building_Cliff`, `1.7/Defs/ThingDefs/Cliffs.xml`)
Natural Things, not terrain: passable at very high path cost, directional bands (north/east/south/west art,
linked like walls), colour inherited from the local rock, talus stones at the foot. Art from Decorative Cliffs
(`Mlie.DecorativeCliffs`, hard dependency for now). Must stay compatible with As Above So Below 2 (optional, later):
don't duplicate its level system.

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
- Soil type, wet ground and later plant density must read as indicators of moisture and drainage (owner, 2026-10-16).
  Vanilla ground is rank-mapped from our fields rather than replaced, to keep each biome's balance.
- Ridges show height change as identifiable rock formations broken by passes, not continuous walls (owner).
- Moisture overlay is a heatmap (transparent dry to blue wet), no contours.
- Scattered ruins are off by default until terrain generation is polished (setting; tile-mutator structures stay).
- Erodibility raised 0.02 → 0.05 so erosion, not the uplift noise, shapes landforms (rock differences only show when
  erosion is strong enough). Rock walls start below 0.15 m soil.
- Slope stored as two scalar layers (magnitude, aspect) rather than a packed vector, to reuse the layer framework.

## Roadmap / open items
Current focus: geological features that read well on the map, worked from the owner's reference maps in
`Docs/VISUAL-TARGETS.md` (1: river valley through mountains; 2: desert river, delta and dunes; 3: marsh with rock spires; 4: mountain foot with tilted strata and caves; 5: lakes, broken ridges, impact site; 6: entrenched meanders, mountain apron; 7: river splitting in an open basin; 8: boreal forest meets glacial plain; 9: cove with a cave river; 10: broad valley between ranges, bridge). The rules that produce them, and the build order, are in
`Docs/GEOLOGY-RULES.md`; it supersedes items 1, 2, 4 and 5 below where they overlap.

1. Few cliffs: steep ground mostly becomes mountain rock, which is excluded. Decide whether mountain edges get cliffs;
   consider using the slope vector field for cliff placement and facing.
2. Use the vector field for runoff/erosion, scree and talus, sun exposure.
3. Height cues: cast shadows on lower stages, south-face emphasis, stage tint, hillshading.
4. Remaining landforms: Basin, Hollow, Chasm, Crevasse, coasts, river variants. Landform shapes are too rounded.
5. Water from the heightmap: lakes, ponds, marsh.
6. Off-mode calibration against On.
7. Plants: habitat rules per species in XML (`PlantHabitat`), strata, shade, clustering, regrowth consistency.
8. Toggle icons per layer (art), visual check of Decorative Cliffs art in game.
9. Revisit rock formation: sedimentary, igneous and metamorphic rock form and erode differently (see
   `GEOLOGY-RULES.md` section 3). The simplified layered-beds model stands until then.
