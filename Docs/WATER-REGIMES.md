# Water regimes

Design for data-driven channel placement, replacing the fixed per-rule constants of the channel network (rules V1-V8
in `WATER-REVIEW.md`). Owner decisions (2026-10-19):
- use regimes;
- tile mutators strongly shape regimes and make those maps very distinct;
- keep working on `feature/water`;
- add small- and medium-scale details and unique structures for gameplay and visual storytelling.

## 1. Why

The erosion model gets large-scale structure physically right. But every channel ran through one pipeline with the
same constants: width table, network density, wobble, migration, pool spacing, floor width. So every map had the
same river signature. The target maps combine large-scale structure with small- and medium-scale variation and
unique set pieces.

Three data tables replace the constants. The same code reads them; variety comes from the map's own data and from
random draws within data-defined ranges.

| Scale | Def | Chosen by | Sets |
|---|---|---|---|
| Map | `WaterRegimeDef` | The tile: mutators first, then climate, hilliness, river, coast | Network density, width scale, which channel regimes are favoured, mutator structures (delta, confluence...) |
| Reach | `ChannelRegimeDef` | Each stretch of channel: discharge, slope, confinement, bank strength, sediment, distance to base level | Shape and bed: wobble, meandering, splits, depth, valley floor, floodplain |
| Spot | `ChannelFeatureDef` | Places along channels where conditions hold, a number per map | Small and medium set pieces: rapids, falls and plunge pools, confluence pools, bars, rock-core islands, oxbow lakes, backwater marsh, spring pools |

All three use the ground rules' condition model: a field, `min` inclusive, `max` exclusive, all conditions must hold,
ordered lists, first match wins. Parameters are `FloatRange`s, drawn once per channel or feature from the map seed.

## 2. Map scale: water regimes

The regime is chosen once per map. Regimes tied to a mutator come first in the list, so a mutator always wins and
those maps look distinct. The other regimes follow, chosen by climate and relief.

| Regime | Selected by | Character |
|---|---|---|
| Delta | RiverDelta | The world river fans into 3-6 distributaries toward the downstream edge (or the sea or a lake), around teardrop islands in a wide shallow sheet (targets 2, 12) |
| Confluence | RiverConfluence | A second large river enters from the side and joins mid-map, with a confluence pool |
| River island | RiverIsland | The world river splits around one large island with a rock core and rejoins (target 7) |
| Headwater | Headwater | The river rises on the map: many short, steep sources (springs at their heads) gather into the trunk; no inflow at the upstream edge (target 11) |
| Marsh web | Wetland, Marshy | Channels split and rejoin into loops on flat ground. Shallow, with marsh between (target 3) |
| Lakeland | Lake, LakeWithIsland(s), Pond, Lakeshore, Oasis, CaveLakes | Channels drain into standing water as the base level; small deltas at the mouths (target 5) |
| Coastal | Coast, Bay, Cove, Fjord, Peninsula, Archipelago | Channels reach the sea with deltas and bars (targets 8, 9) |
| Basin | Basin | Inward drainage to a central lake |
| Arid washes | DryGround, or rainfall below about 400 mm | Channels are dry sandy or gravel washes, wide and braided, holding water only in pools. Sparse network |
| Mountain torrents | Mountainous or Impassable | Narrow, confined, steep channels with rapids and falls (target 9's gorge, target 1) |
| Hill creeks | Small or large hills | A dendritic tree of creeks in valley floors (target 11) |
| Plains | Flat (default) | Fewer, larger channels that meander, with oxbows and floodplains (targets 2, 4, 10) |

Each regime sets:
- network density (source share), and a width scale on hydraulic geometry;
- channel regime weights (favour, allow, forbid);
- feature count multipliers;
- floodplain wetness;
- whether channels hold water (washes do not);
- optional structure workers for its mutator (delta, confluence, island, headwater springs, marsh web).

Modifier mutators stack on top of the regime instead of replacing it:

| Modifier | Effect |
|---|---|
| Muddy | Wider mud floodplains |
| WetClimate | Denser network, wetter floodplains |
| Sandy, Dunes | Sand bars and sandy washes |
| HotSprings | Spring pools at channel heads |

## 3. Reach scale: channel regimes

Each channel is cut into reaches, about 5 widths long. Each reach has state fields:

| Field | Meaning |
|---|---|
| Discharge | log10 of drainage area times the climate's runoff (world inflow included) |
| Slope | Water surface drop per cell, smoothed over two widths |
| Confinement | Valley floor width over channel width |
| BankStrength | Rock strength along the banks |
| Sediment | Upstream erodibility times relief (more where soft rock wears off steep ground) |
| BaseLevel | Distance to the sea, a lake or the downstream edge, in widths |
| Order | Stream order |

The ordered table, first match wins:

| Channel regime | Typical conditions | Parameters |
|---|---|---|
| Delta distributary | BaseLevel small, Slope low | Fans into branches, shallow sheet, sand bars |
| Gorge | Confinement < 3, BankStrength high | Straight, constant width, deep, no floor |
| Torrent | Slope high | Small wobble; rapids and falls likely; boulder bed |
| Braided | Sediment high, Slope moderate, Confinement > 6 | Several shallow threads that split and rejoin around bars, wide gravel floor |
| Anabranching | Confinement opens out after narrows | Splits around large, stable islands; rejoins before the next narrows |
| Meandering | Slope low, Confinement > 6, BankStrength low | Migration: loops, oxbows, point bars, a wide floodplain |
| Wandering | Default | Gentle curves with a medium wobble, occasional bars |
| Marsh thread | Marsh web regime, Slope about 0 | Narrow, shallow, splits and rejoins |
| Wash | Arid washes regime | Dry braided bed, pools only at pinch points |

Parameters are ranges drawn per channel:
- wobble amplitude and wavelength, in widths;
- meander intensity (share of migration steps);
- split chance and island length;
- depth multiplier, pools and riffles;
- valley floor width and floodplain width.

The water regime's weights move reaches between neighbouring regimes, so the same reach state can meander on one
map and wander on another.

Widths come from hydraulic geometry: width = a × discharge^0.5, with `a` from the water regime. The world river keeps
vanilla's class order: its class sets its discharge.

## 4. Spot scale: channel features

Features are the small and medium set pieces, placed after the channels are planned. Each one is:
- a `ChannelFeatureDef`, with conditions on reach fields (plus position: bend apex, confluence, channel head,
  change of slope);
- a count per map (`FloatRange`, scaled by the water regime);
- spacing rules;
- a small worker class that edits the landform or cover nearby.

| Feature | Where | What it does |
|---|---|---|
| Rapids | Steep reach on strong rock | Shallow, rocky bed; scattered boulders (chunks) |
| Falls and plunge pool | Sharp step in the profile at a hard bed | A short drop with a deep round pool below |
| Confluence pool | Where two channels of similar size join | A widened deep pool, with a bar downstream |
| Point bar | Inside of a sharp bend | Sand or gravel bar, dry or shallow |
| Mid-channel bar | Wide, shallow reach | A sand or gravel island in the channel |
| Rock-core island | Anabranch on strong rock | The island keeps a knob of rock |
| Oxbow lake | Cut-off loop (already planned) | Still water or marsh with a ring of rich soil |
| Backwater marsh | Floodplain low behind a levee | Marsh pocket beside the channel |
| Spring pool | Channel head at a mountain foot (more with HotSprings) | A small round pool where the channel starts |
| Tributary fan | Where a steep tributary enters a flatter floor | A gravel fan pushing the main channel aside |

## 5. Variety check

A developer command (and test option) generates a seed set across biomes, hilliness and mutators. For each map it
reports: water regime, channel regime mix, channel count and widths, sinuosity, splits, features. Across maps with
different data the spread should be wide; across maps with the same regime, recognisably alike but not identical.

## 6. Build order (each step committed, tested on the seed set)

**Progress (2026-10-19):** steps 1, 2 and 4 are done. Steps 3, 5, 6 and 7 are next.

1. **Regime tables.**
   - `WaterRegimeDef` and `ChannelRegimeDef` in `1.7/Defs/WaterRegimes/`, using the shared `FieldCondition`.
   - Reach fields include Opening (floor width over its narrowest just upstream).
2. **Channel regimes drive shape and bed.** They set wobble, meandering, splits, depth, riffles and floor width.
3. **Mutator structures.** Each is a set of data knobs on the water regime, implemented once in the processes:

   | Knob | Used by |
   |---|---|
   | `deltaAtExit`, `deltaBranches`, `deltaSpreadWidths` | Delta |
   | `allRiverLinks` | Confluence; needs a tile with several river links |
   | `centralIsland` | River island |
   | `sourceLakeWidths` | Headwater |
   | `floodplainScale`, `climateOffset`, `marshland` | Marsh web; `marshland` is a ground rule field that drives a "wetland marsh" rule |
   | `replacesMutatorTerrain` | Any regime: the mutator's own water painting stands aside |

   Our river graph takes over every river variant. Wider support:
   - `WorldContext` now has river inflows and an exit, so headwaters and river mouths work;
   - the test harness swaps mutators that conflict with a forced one.

**World river geometry, as in vanilla (2026-10-19):**
- Rivers meet at a river centre, a fixed point per tile near the middle of the map (vanilla: 0.3-0.7).
- Each inflow enters along a valley from its side of the map, and the river leaves along one toward its exit. Inflows
  and exits sit on those rays, on the region's edge.
- A link to a water-covered tile is the mouth; every land link flows in, the widest being the main river.
- Deltas measure their distance to the coastline (vanilla's coast offset), not the map edge.
- Without a world river the map's own channels are capped at creek width.
- Flow vectors are about 2 long, as vanilla's are, so the water animates at vanilla speed.
- Maps without a world river get our channels in the river graph, so vanilla's flow debug view draws.

Still open: braided reaches that rejoin into loops between neighbouring channels (the marsh web has only short side
loops), rock cores on islands, and the Lakeland, Coastal and Basin regimes beyond density.

1. Defs and selection:
   - `WaterRegimeDef` and `ChannelRegimeDef` with the condition model;
   - map regime chosen and logged;
   - reach fields computed;
   - channel regimes classified and logged.

   No change to shapes yet.
2. Channel regimes drive the existing processes (wobble, migration, splits, bed, floor) from their ranges. The old
   constants become defaults in the Defs.
3. Network density and width from the water regime (hydraulic geometry).
4. Mutator structures, each a small worker: delta, confluence, river island, headwater, marsh web. On these tiles
   our network takes over from vanilla's river variants.
5. Arid washes (dry beds, pools at pinch points).
6. Channel features, one worker at a time, starting with confluence pools, rapids and point bars.
7. Variety check.
