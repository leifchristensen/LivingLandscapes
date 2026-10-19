# Surface rock plan

Plan for loose rock on the ground: pebbles, cobbles, boulders and vanilla rock chunks. Status: **proposal, not
started**. It follows the same principles as the rest of the geology: read the world, rock first, one rule table, no
exception passes (`GEOLOGY-RULES.md` section 1).

## 1. Goal
Loose rock should tell where it came from. A player who looks at a pile of stones should be able to read the process
that put it there:
- a scree slope below a cliff;
- a boulder trail down a gully;
- a gravel bar in a river bend;
- an erratic left by ice;
- a rubble fan after a landslide.

Vanilla scatters rock chunks near mountains at random. Scattered Stones (owner's reference) shows how small decorative
stones can carry this kind of visual story. We use our own defs and art (owner rule), with Scattered Stones as a style
reference and an optional compatibility target.

## 2. The science: where loose rock comes from

| Process | What it leaves | Where | Size and sorting | Rock type |
|---|---|---|---|---|
| **Rockfall / talus (scree)** | Cones and aprons of angular blocks below rock faces, at about 30-35 degrees (angle of repose) | Foot of bare faces and cliffs, where the slope eases | Fall sorting: largest blocks roll furthest, to the toe; fine scree near the face | The face above it |
| **Frost weathering, blockfields** | Angular blocks covering whole slopes or plateaus | Cold climates on strong, jointed rock; gentle ground | Block size set by joint spacing | Bedrock in place |
| **Spheroidal weathering (tors, corestones)** | Rounded boulders standing on or near their source, often stacked | Massive jointed rock (granite) on crests and knobs | Large, rounded | Bedrock in place |
| **Cryoturbation ("glacial churn"), patterned ground** | Stones pushed up and sorted into rings, nets and stripes (stripes on slopes) | Cold tiles with frost cycles, gentle ground, fine soil | Stones at the edges of cells | Local |
| **Glacial deposits** | Till: unsorted boulders in fine matrix. Moraines: ridges of rubble at the ice edge and along valley sides. Erratics: lone boulders of foreign rock | Near ice margins (ice neighbour), glacial valleys, scoured plains | Unsorted; erratics very large | **Foreign**: other rock types, carried in |
| **Debris flows, landslides (rapid erosion events)** | Lobes and tongues of mixed rubble with levees, fans at gully mouths, landslide scars with a rubble toe | Steep gullies and their mouths, weak rock on steep slopes, after heavy rain | Poorly sorted, boulders at the front and in the levees | Rock from the catchment |
| **Alluvial fans** | Fan of gravel spreading from a valley mouth onto a plain | Where a stream leaves steep ground (slope break on a drainage line) | Coarse at the apex, finer toward the toe | Catchment rock |
| **River deposits** | Gravel and cobble bars on the inside of bends, boulder lag in steep reaches, cobbles on the bed | Channels and their banks | Coarse where steep or fast, fine where slow; rounded | Upstream rock, mixed |
| **Coastal** | Shingle and boulder beaches at exposed headlands, sand in sheltered bays | Coast by wave exposure | Coarse where exposed | Local and drift |
| **Desert lag (desert pavement)** | A sheet of small stones left when wind removes the fines | Arid, gentle, bare ground | Small, packed, dark varnish | Local |
| **Impact** | Ejecta rays and blocks thrown downrange | Around an impact site (mutator) | Mixed, large near the rim | Local plus melt |

Two rules hold across all processes:
- **Size decreases with transport distance and energy loss.** Rock falls, rolls or is carried until the energy runs
  out, and rounding grows with distance.
- **Rock type is that of the source.** Transported stones show where they came from: upstream beds, the face above,
  or foreign rock carried by ice.

## 3. How it maps onto our model

### 3.1 Inputs we already have
- `LL_Debris`: rockfall routing from bare faces to where the slope eases. This is talus.
- `LL_Slope` and `LL_Aspect` (slope vector field), `LL_DrainageArea`, `LL_WaterFlow` and `LL_SurfaceWater`. These
  cover channels, bars, gully mouths and fans.
- `RockStrata`: rock type, strength and joint spacing, which set the source rock and block size.
- Climate and temperature from the tile, which drive frost processes and desert lag.
- Tile mutators and neighbour biomes (ice), which drive glacial deposits and impacts.

### 3.2 New pieces
1. **Clast supply per process (one layer per process family, each a normal `EnvironmentLayerDef`).**
   - `LL_Talus`: `LL_Debris` with fall sorting. Each fragment carries its run length; long runs give large blocks.
   - `LL_StreamClasts`: sediment carried along drainage lines. Stream power (area x slope) sets the largest size it
     can move; clasts settle where power drops, so boulders lie in steep reaches and gravel on bars and at fan apexes.
     Fans form where a channel crosses a slope break.
   - `LL_InSituClasts`: weathering in place. Corestones on strong, massive rock crests; blockfields where frost acts
     on jointed rock; desert lag on arid, bare, gentle ground.
   - `LL_GlacialClasts`: till and erratics from the ice edge influence (later, with the ice rules I1).

   Each layer stores a density (clasts per cell). Size and source rock are planned alongside it during generation.
2. **One clast rule table (XML), like the ground rules.** It maps process, size class, climate and ground to what is
   placed: a vanilla chunk (haulable stone), a decorative pebble cluster, cobbles, or a boulder (large, gives cover,
   costs movement). The same table decides the clast's look: angular or rounded, and its rock colour. All numbers
   live in XML.
3. **Placement by density, not by chance.**
   - Clasts are sampled from the density with a minimum spacing per size (Poisson disc), so a talus cone thins
     naturally toward its toe and a bar fills a bend.
   - Rock type comes from the source cell: the face for talus, upstream rock for streams, the bed for in-place
     clasts. Foreign rock comes only from ice.
4. **Our own defs and art.** Pebbles, cobbles and boulders in several shapes, angular and rounded. They are
   greyscale and tinted with the rock colour, like the cliffs. Art comes from `Source/ArtTools`, in the RimWorld style.
   Vanilla rock chunks remain the haulable resource.
5. **Rapid erosion events (later).** A debris flow or landslide is a planned event at generation, placed where the
   ground is steep, the rock weak and the catchment wet: a scar, a lobe of rubble with levees, and a fan at the mouth.
   This is the "story" piece. It is drawn from the same layers, with a low rate per map, so it is not a special case.

## 4. Gameplay rules
- **Resources stay balanced.** The total haulable stone per map stays close to vanilla's for the biome and hilliness.
  Our placement moves the chunks to where they belong; it does not add stone for free. This is checked on exports.
- **Paths and building.**
  - Boulders give cover and slow movement but are passable or minable.
  - Pebbles and cobbles are decorative only: no path cost, removed by building.
  - Nothing is placed in deep water; streams get bed stones only as decoration.
- **Settings:** density scale, and decorative stones on or off (performance and taste).

## 5. Checks on the exports
1. Talus lies within reach of a bare face, on gentler ground below it, and its clasts get larger toward the toe.
2. Stream clasts get smaller downstream along each channel, and boulders lie only where stream power is high.
3. Each clast's rock type matches its source: the face above, the rock upstream, or foreign rock under ice.
4. Haulable stone per map lies within ±25 % of vanilla's for the same tile.
5. No clasts lie in deep water, on cliffs or inside rock.

## 6. To verify before building
- Scattered Stones:
  - how its stones are defined (filth, plant-like, building or mote);
  - how it scatters them;
  - its licence;
  - whether to offer compatibility (use its defs when present) or only borrow the look.
- Vanilla `GenStep_RockChunks` (order 970):
  - where and how many chunks it places;
  - whether we replace its placement or feed it our density.
- RimWorld 1.7 performance of many small decorative things, compared with drawing them into a section layer as
  terrain decoration.

## 7. Build order
1. `LL_Talus` with fall sorting, replacing today's talus plan and vanilla's chunk placement. Uses vanilla chunks plus
   one boulder def.
2. Clast rule table and our own pebble, cobble and boulder art.
3. Stream clasts: bars, boulder lag, fans.
4. In-place clasts: corestones, blockfields, desert lag.
5. Debris flows and landslides as rare generation events.
6. Glacial clasts with the ice rules; impact ejecta with the impact landform.
