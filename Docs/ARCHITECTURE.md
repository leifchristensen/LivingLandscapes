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
| ElevationFertility (10) | `Patch_GenStep_ElevationFertility_Generate` postfix | Compute the Elevation stage layers; write vanilla elevation/fertility from our soil and rock model; plan cliffs |
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
| LL_SoilDepth | soil thickness from slope and convergence (`SoilModelExtension`) |
| LL_DrainageArea | multiple-flow-direction upslope area, log10 cells |
| LL_Slope | rise over run of LL_Height; drawn as a vector field using LL_Aspect |
| LL_Aspect | downhill direction, radians counter-clockwise from east, 0..2π |
| LL_WaterDistance, LL_Moisture, LL_Soil | water proximity, moisture index, soil class |

**Slope vector field** = LL_Slope (magnitude) + LL_Aspect (direction); `GridMath.SlopeToward` gives the slope along
any heading. Intended consumers: runoff and erosion, scree/talus placement, cliff facing, sun exposure (latitude),
later pawn movement cost (vanilla path cost is per cell and non-directional).

## Geology (`Source/LivingLandscapes/Geology`)
- `GridMath`: masked blur/inpainting, chamfer distance, priority-flood fill, breach-fill, D8 and MFD flow, slope,
  aspect, region labelling.
- `LandscapeGenerator`: region model ~2.5x map size, stream-power erosion (Braun–Willett implicit) + hillslope
  diffusion, cropped. Tile mutators become `LandformExtension`s in XML (`1.7/Patches/TileMutatorLandforms.xml`).
- `SoilModel`, `VanillaTerrainWriter`: soil depth → vanilla elevation classes (rock, roof thickness, gravel, soil).
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
  Exports every layer as PNG (cliffs marked) plus summary.txt. Compare placed things against overlays.
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
- Slope stored as two scalar layers (magnitude, aspect) rather than a packed vector, to reuse the layer framework.

## Roadmap / open items
Current focus: geological features that read well on the map, worked from the owner's reference maps in
`Docs/VISUAL-TARGETS.md` (1: river valley through mountains; 2: desert river, delta and dunes; 3: marsh with rock spires; 4: mountain foot with tilted strata and caves; 5: lakes, broken ridges, impact site; 6: entrenched meanders, mountain apron; 7: river splitting in an open basin). Collect the targets, then plan shared pieces.

1. Few cliffs: steep ground mostly becomes mountain rock, which is excluded. Decide whether mountain edges get cliffs;
   consider using the slope vector field for cliff placement and facing.
2. Use the vector field for runoff/erosion, scree and talus, sun exposure.
3. Height cues: cast shadows on lower stages, south-face emphasis, stage tint, hillshading.
4. Remaining landforms: Basin, Hollow, Chasm, Crevasse, coasts, river variants. Landform shapes are too rounded.
5. Water from the heightmap: lakes, ponds, marsh.
6. Off-mode calibration against On.
7. Plants: habitat rules per species in XML (`PlantHabitat`), strata, shade, clustering, regrowth consistency.
8. Toggle icons per layer (art), visual check of Decorative Cliffs art in game.
