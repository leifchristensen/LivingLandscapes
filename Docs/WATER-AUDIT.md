# Water audit: rules versus topology

Owner direction (2026-10-19):
- The rules-based approach is overfitting; vanilla's water looks more natural.
- Reset: find every exception and imposed rule, and keep what follows from topology and data.
- Keep regimes, but as gentle nudges, not a static rule set.

## 1. Why vanilla looks more natural

Vanilla's river is simple structure plus irregularity:
- one smooth line from the map edge through a centre point to the other edge;
- four independent noise fields: bends (Perlin, about 40 cells of amplitude), width (±15%), bank width, and a
  "shallowizer" that scatters deep and shallow patches.

Nothing in it is a perfect shape.

Ours does the opposite. The large structure is physical (erosion, drainage), but the channels are then drawn by
formulas: constant widths from tables, parabolic cross-sections, cosine pools, sine-shaped islands, a round lake,
flat valley floors with even walls. Formulas give clean, repeated shapes, and the eye reads them as artificial.
Every time a shape looked wrong we added another rule, so the rules kept growing.

**Lesson:** topology should set structure (where water goes and how much there is). The look should come from the
data's own irregularity (terrain, rock, flow) plus noise, not from shape formulas.

## 2. Inventory

Each entry is labelled:
- **T** (topology or data driven: keep);
- **N** (a nudge: keep, but make it soft);
- **R** (an imposed rule or shape: replace);
- **X** (an exception or patch over another rule: remove once its cause is gone).

### Large-scale structure

| Piece | Where | Type | Notes |
|---|---|---|---|
| Erosion model (uplift, stream power, creep, rock strata) | `LandscapeGenerator`, `RockStrata` | T | The strength of the project |
| Drainage tree and flow accumulation | `LandscapeGenerator.Erode`, `LL_DrainageArea` | T | |
| World river geometry: river centre, a valley ray toward each link, entry and exit | `WorldContext`, `Landforms` (Ray) | N | Maps world data the way vanilla does. A valley guides, erosion decides |
| River inflow as drainage area | `Outlets`, `RiverInflow` | T | |
| Water regime selection (mutators, climate, hilliness) | `WaterRegimes.Select` | N | Keep as the source of nudges |

### Channels

| Piece | Where | Type | Notes |
|---|---|---|---|
| Channel network from a drainage threshold, stream order | `RiverNetwork.Extract` | T (threshold N) | Fine as long as the threshold is one data value |
| Width tables: vanilla class curve, width by order, tributary share, creek cap without a world river | `RiverNetworkSettings` | R, X | Tables, not discharge. The caps patch over the tables |
| Course smoothing and simplification (Chaikin, Douglas-Peucker) | `RiverCourse.FromPlan` | R | Turns a terrain path into a drawn line |
| Wobble (noise on the line) | `RiverChannels.Wobble` | R | Noise on a drawn line, not on terrain |
| Meander migration with cutoffs (about 10 parameters) | `RiverChannels.Migrate`, `CutOff` | R | Physical model, but its output is a perfect curve; produced lassos and spaghetti |
| Tributary snapping to the parent | `JoinParent` | X | Needed only because both lines were redrawn |
| Reach classification by hard thresholds, first match wins | `ClassifyReaches`, `ChannelRegimeDef` | R | Sharp switches between parameter sets |
| Islands: sine-bowed side branch of fixed length | `Split` | R | Imposed shape |
| Delta: evenly spread branches on a power curve, run on to the edge | `Split` (delta) | R, X | Imposed shape plus a fix for it |
| River island: one sine-bowed branch at the centre | `Split` (central island) | R | |
| Headwater: course cut at the centre plus a round carved lake | `PlanNetwork`, `CarveSourceLake` | R | Perfect circle |

### Bed and valley

| Piece | Where | Type | Notes |
|---|---|---|---|
| Water surface that never rises (closest non-increasing profile) | `RiverCarver.Profile` | T | The physical constraint (W1). Keep |
| Cross-section: parabola with a rim depth, depth ~ √width, thalweg shift by curvature | `RiverCarver.Depth` | R | Same shape in every reach |
| Pools and riffles: cosine every 6 widths, or locked to bends | `RiverCarver.Pools` | R | |
| Bank asymmetry (outer and inner factors, smoothstep banks) | `CarveChannel` | R | |
| Valley floors: flat at bankfull height, width from rock, wall width, floor slope | `CarveChannel` | R, X | Even floors and walls; floor slope was added to fix drainage lines on the flat floors |
| Oxbow hollows with sediment plugs | `CarvePlan` | R, X | Exists because of migration; plugs fix overlaps |
| Side branches sharing the main's surface | `BranchSurface` | X | Fix for the imposed branch shape |

### Surface water, ground and flow

| Piece | Where | Type | Notes |
|---|---|---|---|
| Lakes by water balance in closed hollows | `SurfaceWaterModel.Lakes` | T | Keep |
| Planned river level into surface water | `SurfaceWaterModel.Compute` | T | Keep, whatever plans the level |
| Loose streams from a second drainage threshold with their own width formula | `SurfaceWaterModel.Streams` | R, X | A second channel system beside the network; its threshold was raised to hide it |
| Wetness: wetness index plus height above drainage; floodplain grows with drainage area | `WetnessWorker` | T | Keep |
| Regime floodplain scale, climate offset | `WaterRegimeDef` | N | Keep as nudges |
| Marshland field and the "wetland marsh" ground rule | `GroundRules.xml` | R | A regime switch inside the ground table |
| Deep and shallow thresholds (0.5 m still, 0.85 m moving) | `GroundRules.xml` | T | The depth is data; the threshold only maps it to vanilla's terrain classes |
| Flow from channel tangent or downhill, about 2 long | `FlowWriter` | T | Keep |
| Patches that make vanilla's river painting, mutator water and elevation rewrites stand aside | `Patches/` | T | Needed while we own the water |

### Summary

| | Count | Pieces |
|---|---|---|
| T | 15 | erosion, drainage, inflow, network threshold, water surface profile, lakes, wetness, flow, ground thresholds, patches... |
| N | 4 | world river geometry, regime selection, floodplain scale, climate offset |
| R and X | 18 | almost all channel geometry, the bed and valley shapes, splits, mutator structures, the second stream system, the marsh rule |

The regime tables are not the problem. The problem is what they switch: shape rules.

## 3. Reset plan

**Principle:** structure from topology; texture from data and noise; regimes nudge the parameters of physical
processes continuously, never switch shapes.

1. **Channels from the terrain, not drawn lines.**
   - Water runs where the fine landform's own flow accumulation (river inflow included) passes a threshold: one
     channel system replaces both the network lines and the loose streams.
   - Each channel follows the terrain's flow path. Its bends come from the terrain's multi-scale relief (the erosion
     model's valleys plus detail), as they do in nature.
   - Erosion-model valleys and the world river rays still decide where the big channels go.
2. **Width and depth from discharge, with noise.**
   - Width ~ a × Q^0.5 and depth ~ b × Q^0.3, where Q is drainage area times climate. Vanilla's class only scales `a`.
   - Low-frequency noise on width (about ±15%, as vanilla) and on depth (scattered shallow patches, as vanilla's
     shallowizer).
   - Deeper where local stream power (Q × slope) is high, so steep narrow reaches scour and wide flat ones shoal.
   - The bed is carved only as deep as the water needs, along the never-rising profile.
3. **Splits emerge.** Where flow spreads (low slope, wide floor), multiple-flow routing gives several cells above
   the threshold, so channels divide and rejoin by themselves. A regime nudges the spreading exponent: wetlands and
   deltas spread more, gorges less.
4. **Regimes as soft nudges.** Each `ChannelRegimeDef` becomes a soft membership: how well a reach's data matches,
   0..1, from smooth ramps instead of hard min/max. Parameters are the weighted blend of all regimes. Each
   `WaterRegimeDef` scales those weights and process parameters by modest factors (0.5-2), with nothing on or off.
   Parameters nudged:
   - flow-spreading exponent;
   - width and depth coefficients;
   - noise amplitudes;
   - erodibility and creep (wider or narrower valleys);
   - sediment fill;
   - floodplain wetness and climate offset.
5. **Mutator structures as terrain nudges, not drawn shapes.** The tile's water then follows from topology:

   | Mutator | Nudge |
   |---|---|
   | Delta | Lower relief and finer sediment toward the exit, wider flow spreading |
   | River island | A knob of resistant rock in the valley at the river centre |
   | Headwater | A depression at the river centre (negative uplift) for the lake model to fill |
   | Confluence | Every land link enters (data, already done) |
   | Wetland | Higher water table, wide flow spreading, flat sediment fill |

6. **Remove**:
   - the width tables and caps;
   - course smoothing, wobble, meander migration and cutoffs;
   - tributary snapping;
   - the island, delta and central-island shapes;
   - the round source lake;
   - the cross-section formula, cosine pools and bank factors;
   - flat valley floors;
   - oxbow plugs;
   - the second stream system;
   - the Marshland ground rule.

   Meander migration may return later as a nudge on the terrain (lateral erosion in the erosion model), not on a
   drawn line.
7. **Keep and reuse:**
   - the erosion model, rock strata, drainage, world river geometry;
   - the never-rising water surface;
   - lakes, wetness and flow;
   - the ground rule table, regime selection, the patches;
   - the invariants (downhill, lakes, base level, flow), which define done.
   - Add a variety check that the look varies between seeds without hard edges between reaches.

**Order:**
1. Steps 1 and 2 (one channel system with discharge widths and noise), then compare against vanilla and the targets.
2. Step 4 (soft regimes).
3. Steps 3 and 5 (spreading and mutator nudges).
4. Step 6 (removing the old code) as each replacement lands.
