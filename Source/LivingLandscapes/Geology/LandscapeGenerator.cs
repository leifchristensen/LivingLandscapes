using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>Tuning for geological terrain, set on the height layer Def in XML. Lengths in metres.</summary>
public class GeologyGenerationExtension : DefModExtension
{
    /// <summary>Metres per map cell. A twin bed is 2 x 1 cells, so a cell is about 1 m.</summary>
    public float cellSize = 1f;

    /// <summary>Height difference between the lowest and highest ground on a map, by tile hilliness.</summary>
    public float reliefFlat = 4f;
    public float reliefSmallHills = 20f;
    public float reliefLargeHills = 70f;
    public float reliefMountainous = 180f;
    public float reliefImpassable = 300f;

    /// <summary>How the sea and the land by it are shaped where a coast mutator paints water.</summary>
    public CoastlineExtension coast = new CoastlineExtension();

    /// <summary>Distance between neighbouring world tile centres, used for the regional slope.</summary>
    public float tileSpacingMetres = 30000f;

    /// <summary>Typical spacing of ridges and valleys.</summary>
    public float landformWavelength = 300f;

    /// <summary>
    /// A second, shorter landform scale: spurs, knolls and side hollows on the hillsides between the main ridges and
    /// valleys. Its wavelength (metres) and its share of the uplift. Erosion then cuts gullies around them, so slopes
    /// are not single even planes and cliff lines bend and break with the ground.
    /// </summary>
    public float detailWavelength = 100f;
    public float detailUplift = 0.3f;

    /// <summary>Ruggedness of the uplift pattern (0 smooth swells, 1 sharp ridges); mountains use the upper value.</summary>
    public float ridgedness = 0.2f;
    public float ridgednessMountains = 0.6f;

    /// <summary>Grid spacing of the erosion model; the result is interpolated to map cells.</summary>
    public float erosionCellSize = 2f;

    /// <summary>
    /// The erosion model covers a region this many times the map's width, centred on it, with base level at the
    /// region's edges. The map is a window into that region, so valleys and ridges continue past its edges.
    /// </summary>
    public float regionScale = 2.5f;

    public int erosionIterations = 120;

    /// <summary>Stream-power erodibility (dimensionless model units): higher cuts more, narrower valleys.</summary>
    public float erodibility = 0.02f;

    /// <summary>Stream-power area exponent m (concavity m/n with n = 1); 0.4-0.6 in real rivers.</summary>
    public float areaExponent = 0.5f;

    /// <summary>Hillslope diffusion (model units): higher rounds ridges and widens valleys.</summary>
    public float hillslopeDiffusion = 0.12f;

    /// <summary>
    /// Fluvial deposition, G in the erosion-deposition law (Davy and Lague 2009; Yuan et al. 2019): each step a cell keeps
    /// G x (sediment it receives) / (its drainage area). 0 is the detachment-limited stream power law (no deposits); about 1
    /// balances them, so rivers drop their load where their slope eases and build plains and deltas.
    /// </summary>
    public float deposition = 1f;


    /// <summary>How much more the upslope side of the region is uplifted than the downslope side.</summary>
    public float regionalUpliftBias = 0.5f;

    /// <summary>River discharge entering the map, as a multiple of the map's own drainage area.</summary>
    public float riverInflow = 8f;

    /// <summary>
    /// Antecedent drainage: a world river is older than the relief around it and keeps cutting down as the land rises, so the
    /// erosion model starts with each world river already flowing from where it enters, through the meeting point the world gives,
    /// to its exit (a course falling to base level, antecedentDepth of the starting relief below the ground). Uplift, stream power
    /// and deposition then keep or change it like any other channel. Without it a river follows the region's own slope and can
    /// leave the map before the rivers of a confluence meet.
    /// </summary>
    public bool antecedentRivers = true;
    public float antecedentDepth = 0.5f;

    /// <summary>
    /// The valley a world river runs in. A map is a slice of a tile that contains the river, so the river's valley
    /// crosses it; this landform guides the river's channel through the map along the world flow direction.
    /// </summary>
    public LandformExtension riverValley = new LandformExtension
    {
        shape = LandformShape.Axis, size = 0.05f, edge = 0.1f, upliftInside = -0.4f, warp = 0.1f,
    };

    /// <summary>The channel network: where channels start and how wide each is (V1-V3).</summary>
    public RiverNetworkSettings network = new RiverNetworkSettings();

    /// <summary>Channels from the landform's own drainage: where they form, hydraulic geometry and noise.</summary>
    public TerrainChannelSettings terrainChannels = new TerrainChannelSettings();

    /// <summary>Tilted rock beds and joints: hard beds resist erosion, soft beds wear away (sizes in map cells).</summary>
    public RockStrataSettings strata = new RockStrataSettings();

    /// <summary>Small-scale roughness added after erosion (boulders, hummocks).</summary>
    public float microRelief = 0.3f;
    public float microWavelength = 6f;

    public float ReliefFor(Hilliness hilliness)
    {
        switch (hilliness)
        {
            case Hilliness.Flat: return reliefFlat;
            case Hilliness.SmallHills: return reliefSmallHills;
            case Hilliness.LargeHills: return reliefLargeHills;
            case Hilliness.Mountainous: return reliefMountainous;
            case Hilliness.Impassable: return reliefImpassable;
            default: return reliefSmallHills;
        }
    }
}

/// <summary>
/// "On" mode height: generates the terrain of the map's slice of the world from geological principles, in metres
/// above sea level. A region larger than the map is modelled and the map cut from its centre, because a map is a
/// small window into a world tile. Rock is uplifted (more on the upslope side of the region), rivers incise by the
/// stream-power law (implicit scheme of Braun and Willett 2013), and hillslopes creep (linear diffusion). Water
/// leaves the region through its edges facing downhill, the sea side and the river's exit; a world river enters as
/// extra discharge on its upstream edge, so it carves its own valley. The map window is then scaled to the relief
/// the tile's hilliness allows.
/// </summary>
public static class LandscapeGenerator
{
    private static readonly int[] DX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] DZ = { 0, 1, 1, 1, 0, -1, -1, -1 };
    private static readonly float[] Dist = { 1f, 1.4142135f, 1f, 1.4142135f, 1f, 1.4142135f, 1f, 1.4142135f };

    /// <param name="rockStrength">Strength (0..1) of the rock beneath each map cell, from <paramref name="strata"/>.</param>
    /// <param name="rock">Rock type beneath each map cell (null where the tile has no rock types).</param>
    /// <param name="channels">The channel network crossing the map, larger channels first (see <see cref="RiverNetwork"/>).</param>
    /// <param name="water">The map's water regime (Docs/WATER-REGIMES.md), or null if no regime matches.</param>
    /// <summary>Relief (metres) and plains feature of the map generated last, for the log.</summary>
    public static float Relief;
    public static PlainsFeatureDef PlainsFeature;

    public static float[] Generate(Map map, GeologyGenerationExtension p, out WorldContext world, out WaterRegimeDef water,
        out List<ChannelCourse> channels, out RockStrata strata, out float[] rockStrength, out ThingDef[] rock, out int[] rockStage)
    {
        world = WorldContext.For(map, p.tileSpacingMetres);
        water = WaterRegimes.Select(map, world);
        int sizeX = map.Size.x, sizeZ = map.Size.z;
        float metresPerModelCell = Mathf.Max(p.erosionCellSize, p.cellSize);
        float mapCellsPerModelCell = metresPerModelCell / p.cellSize;
        float regionScale = Mathf.Max(1f, p.regionScale);
        int gx = Mathf.Max(8, Mathf.CeilToInt(sizeX * regionScale / mapCellsPerModelCell) + 1);
        int gz = Mathf.Max(8, Mathf.CeilToInt(sizeZ * regionScale / mapCellsPerModelCell) + 1);
        // Model coordinates of map cell (0, 0): the map sits in the middle of the region.
        float originX = (gx - 1) * 0.5f - sizeX * 0.5f / mapCellsPerModelCell;
        float originZ = (gz - 1) * 0.5f - sizeZ * 0.5f / mapCellsPerModelCell;

        var landforms = new Landforms(map, world, world.HasRiver ? p.riverValley : null);
        float[] uplift = UpliftField(world, p, gx, gz, metresPerModelCell, out float[] erodibility,
            landforms, (x, z) => new Vector2(((x - originX) * mapCellsPerModelCell - sizeX * 0.5f) / sizeX,
                                             ((z - originZ) * mapCellsPerModelCell - sizeZ * 0.5f) / sizeX));
        // Rock beds, in map cells around the map's centre; heights in cells above the lowest ground.
        // A plains feature (butte, escarpment, bluffs) stands above or cuts below the tile's own relief.
        float relief = p.ReliefFor(world.Hilliness) + landforms.ExtraRelief;
        Relief = relief;
        PlainsFeature = landforms.PlainsFeature;
        float reliefCells = relief / p.cellSize;
        float extent = Mathf.Max(gx, gz) * mapCellsPerModelCell * 0.75f;
        strata = new RockStrata(p.strata, Find.World.NaturalRockTypesIn(map.Tile).ToList(), new Vector2(sizeX * 0.5f, sizeZ * 0.5f), extent, reliefCells,
            world.Hilliness);
        var sites = new RockStrata.Site[gx * gz];
        for (int z = 0; z < gz; z++)
        {
            for (int x = 0; x < gx; x++)
                sites[z * gx + x] = strata.At((x - originX) * mapCellsPerModelCell, (z - originZ) * mapCellsPerModelCell);
        }

        // The river centre in model cells: rivers enter and leave along rays from it, as vanilla lays them.
        var riverCentre = new Vector2(originX + (0.5f + world.RiverCentre.x) * sizeX / mapCellsPerModelCell,
                                      originZ + (0.5f * sizeZ + world.RiverCentre.y * sizeX) / mapCellsPerModelCell);
        // The sea, on the model's grid, is the base level rivers grade to, and its depth (metres, Coastline's sea floor profile)
        // the room their sediment fills.
        float[] seaDepth = null;
        if (Coastline.TryOffshore(map, out System.Func<float, float, float> offshoreAt))
        {
            var seaCell = new bool[gx * gz];
            for (int z = 0; z < gz; z++)
            {
                for (int x = 0; x < gx; x++)
                    seaCell[z * gx + x] = offshoreAt((x - originX) * mapCellsPerModelCell, (z - originZ) * mapCellsPerModelCell) > 0f;
            }
            float[] fromLand = GridMath.DistanceTo(i => !seaCell[i], gx, gz);
            seaDepth = new float[gx * gz];
            for (int i = 0; i < seaDepth.Length; i++)
                seaDepth[i] = seaCell[i] ? Mathf.Max(0.01f, p.coast.SeaDepth(fromLand[i] * mapCellsPerModelCell, world.Hilliness)) : 0f;
        }
        var mapWindow = new Rect(originX, originZ, sizeX / mapCellsPerModelCell, sizeZ / mapCellsPerModelCell);
        bool[] outlet = Outlets(world, water, gx, gz, riverCentre, out List<(int cell, float width)> entries, out int riverExit, seaDepth, mapWindow);
        // Where a river enters, the land beyond the edge slopes in (the river came from there): the region's edge near it does not
        // drain, or the river would leave again at the next edge cell. The map's channels keep the same guard (entryGuardCells).
        float guard = p.terrainChannels.entryGuardCells / mapCellsPerModelCell;
        int guarded = 0;
        foreach ((int cell, float _) in entries)
        {
            int ex = cell % gx, ez = cell / gx;
            for (int i = 0; i < outlet.Length; i++)
            {
                int x = i % gx, z = i / gx;
                if (!outlet[i] || (x != 0 && z != 0 && x != gx - 1 && z != gz - 1) || (seaDepth != null && seaDepth[i] > 0f) || i == riverExit)
                    continue;
                if ((x - ex) * (x - ex) + (z - ez) * (z - ez) <= guard * guard)
                {
                    outlet[i] = false;
                    guarded++;
                }
            }
        }
        float[] inflow = new float[gx * gz];
        float mainWidth = entries.Count > 0 ? Mathf.Max(entries[0].width, 1f) : 1f;
        foreach ((int cell, float width) in entries)
            inflow[cell] += p.riverInflow * gx * gz * width / mainWidth;

        float[] floor = p.antecedentRivers && riverExit >= 0 && entries.Count > 0
            ? AntecedentCourses(entries, riverCentre, riverExit, uplift, outlet, gx, gz, p.antecedentDepth) : null;
        float[] h = Erode(uplift, erodibility, outlet, inflow, seaDepth, relief, gx, gz, p, strata, sites, reliefCells, out int[] receiver,
            out float[] area, out float modelTop, floor);
        // The channels the model carved, from its drainage tree; the world river is the one reaching its exit.
        channels = RiverNetwork.Extract(receiver, area, gx, gz,
            c => new Vector2((c % gx - originX) * mapCellsPerModelCell, (c / gx - originZ) * mapCellsPerModelCell),
            mapCellsPerModelCell, sizeX, sizeZ, entries, riverExit, world.RiverExitWidth,
            water?.sourceShare.RandomInRange ?? p.network.sourceShare, p.network);
        if (entries.Count > 0)
            Log.Message($"[LivingLandscapes] World rivers enter the erosion model at {entries.Count} edge cell(s) ({guarded} edge cells beside them kept from draining); " +
                        $"{channels.Count} channels extracted, {channels.Count(c => c.VanillaWidth > 0f)} carrying world rivers.");

        // Cut the map window from the region and scale its range to the tile's relief, in metres above sea level.
        var window = new float[sizeX * sizeZ];
        float min = float.MaxValue, max = float.MinValue;
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                float v = Bilinear(h, gx, gz, originX + x / mapCellsPerModelCell, originZ + z / mapCellsPerModelCell);
                window[z * sizeX + x] = v;
                min = Mathf.Min(min, v);
                max = Mathf.Max(max, v);
            }
        }
        // Bilinear upscaling leaves the erosion grid's D8 pattern as a fine checkerboard in the slopes; smooth it out at
        // the model cell size (features that matter are several model cells wide).
        window = GridMath.Blur(window, sizeX, sizeZ, mapCellsPerModelCell * 0.75f);
        min = float.MaxValue;
        max = float.MinValue;
        foreach (float v in window)
        {
            min = Mathf.Min(min, v);
            max = Mathf.Max(max, v);
        }
        // The tile's elevation is the height of its typical ground. Without a plains feature that is the middle of the relief;
        // with one, the plain (most of the map) keeps the tile's elevation and the feature stands above it or cuts below it,
        // so a butte does not drop the plain below sea level.
        float level = 0.5f;
        if (landforms.ExtraRelief > 0f)
        {
            var sorted = (float[])window.Clone();
            System.Array.Sort(sorted);
            level = (sorted[sorted.Length / 2] - min) / Mathf.Max(max - min, 1e-6f);
        }
        var micro = new Perlin(p.cellSize / p.microWavelength, 2.0, 0.5, 3, Rand.Int, QualityMode.Medium);
        var centre = new Vector2(sizeX * 0.5f, sizeZ * 0.5f);
        var result = new float[sizeX * sizeZ];
        rockStrength = new float[sizeX * sizeZ];
        rock = new ThingDef[sizeX * sizeZ];
        rockStage = new int[sizeX * sizeZ];
        var resistant = new float[sizeX * sizeZ];
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                // The bed exposed here, at the same height scale the erosion model used.
                RockStrata.Site site = strata.At(x, z);
                float bedHeight = window[z * sizeX + x] / Mathf.Max(modelTop, 1e-6f) * reliefCells;
                rockStrength[z * sizeX + x] = strata.Strength(site, bedHeight);
                rock[z * sizeX + x] = strata.RockAt(site, bedHeight);
                rockStage[z * sizeX + x] = strata.HardBedsBelow(site, bedHeight);
                resistant[z * sizeX + x] = site.hard;

                float rel = (window[z * sizeX + x] - min) / Mathf.Max(max - min, 1e-6f);
                float tilt = -Vector2.Dot(new Vector2(x, z) - centre, world.Downhill) * p.cellSize * world.RegionalGradient;
                float rough = (float)micro.GetValue(x, 0.0, z) * p.microRelief;
                result[z * sizeX + x] = world.Elevation + (rel - level) * relief + tilt + rough;
            }
        }
        int bodies = resistant.Count(v => v >= 0.5f);
        if (bodies > 0)
            Log.Message($"[LivingLandscapes] Resistant rock bodies (plugs, dikes) cover {(float)bodies / resistant.Length:P1} of {map}.");
        int sea = Coastline.Apply(map, result, p.coast, resistant);
        if (sea > 0)
            Log.Message($"[LivingLandscapes] Coastline on {map}: {sea} sea cells set below the beach, land eased down to it.");
        return result;
    }

    /// <summary>
    /// Rock uplift rate per model cell: fractal noise at landform scale, sharper for mountains, stronger upslope,
    /// then adjusted by the tile's landforms (valleys, mountain fronts, plateaus...), which also set rock hardness.
    /// </summary>
    private static float[] UpliftField(WorldContext world, GeologyGenerationExtension p, int gx, int gz, float metresPerModelCell,
        out float[] erodibility, Landforms landforms, System.Func<int, int, Vector2> mapFraction)
    {
        erodibility = new float[gx * gz];
        double frequency = metresPerModelCell / p.landformWavelength;
        var swells = new Perlin(frequency, 2.0, 0.5, 4, Rand.Int, QualityMode.High);
        var ridges = new RidgedMultifractal(frequency, 2.0, 4, Rand.Int, QualityMode.High);
        var detail = new Perlin(metresPerModelCell / Mathf.Max(1f, p.detailWavelength), 2.0, 0.5, 3, Rand.Int, QualityMode.High);
        bool mountains = world.Hilliness >= Hilliness.Mountainous;
        float ridged = mountains ? p.ridgednessMountains : p.ridgedness;

        var centre = new Vector2(gx * 0.5f, gz * 0.5f);
        float half = Mathf.Max(gx, gz) * 0.5f;
        var uplift = new float[gx * gz];
        for (int z = 0; z < gz; z++)
        {
            for (int x = 0; x < gx; x++)
            {
                float smooth = 0.5f + 0.5f * (float)swells.GetValue(x, 0.0, z);
                float sharp = Mathf.Clamp01(0.5f * (float)ridges.GetValue(x, 0.0, z));
                float u = Mathf.Lerp(smooth, sharp, ridged) + p.detailUplift * (float)detail.GetValue(x, 0.0, z);
                float upslope = -Vector2.Dot(new Vector2(x, z) - centre, world.Downhill) / half;
                u *= Mathf.Max(0.1f, 1f + p.regionalUpliftBias * upslope);
                u = Mathf.Lerp(0.15f, 1f, Mathf.Clamp01(u));
                float k = 1f;
                if (landforms.Any)
                    landforms.Apply(mapFraction(x, z), ref u, ref k);
                uplift[z * gx + x] = Mathf.Max(0.02f, u);
                erodibility[z * gx + x] = k;
            }
        }
        return uplift;
    }

    /// <summary>
    /// Base-level cells: edges facing downhill (all edges on level ground), the sea side, and the river's exit.
    /// Returns the cells where world rivers enter (with vanilla's width): the main river, plus every other river link
    /// if the water regime asks for them (a confluence); none if the regime has the river rise on the map (headwater).
    /// </summary>
    private static bool[] Outlets(WorldContext world, WaterRegimeDef water, int gx, int gz, Vector2 riverCentre,
        out List<(int cell, float width)> entries, out int riverExit, float[] seaDepth = null, Rect? mapWindow = null)
    {
        var outlet = new bool[gx * gz];
        if (seaDepth != null)
        {
            for (int i = 0; i < outlet.Length; i++)
                outlet[i] = seaDepth[i] > 0f;
        }
        var centre = new Vector2((gx - 1) * 0.5f, (gz - 1) * 0.5f);
        bool level = world.Downhill == Vector2.zero;
        for (int z = 0; z < gz; z++)
        {
            for (int x = 0; x < gx; x++)
            {
                if (x != 0 && z != 0 && x != gx - 1 && z != gz - 1)
                    continue;
                Vector2 outward = (new Vector2(x, z) - centre).normalized;
                bool facesDownhill = level || Vector2.Dot(outward, world.Downhill) > -0.2f;
                bool facesSea = world.SeaDirection != Vector2.zero && Vector2.Dot(outward, world.SeaDirection) > 0.5f;
                outlet[z * gx + x] = facesDownhill || facesSea;
            }
        }

        entries = new List<(int, float)>();
        riverExit = -1;
        if (world.HasRiver)
        {
            riverExit = EdgeCellToward(world.RiverExit, gx, gz, riverCentre);
            // A river with its mouth at the sea leaves where its way out first meets the sea, not at the region's edge.
            if (seaDepth != null)
            {
                Vector2 pos = riverCentre, step = world.RiverExit.normalized * 0.5f;
                for (int i = 0; i < (gx + gz) * 4; i++)
                {
                    pos += step;
                    int x = Mathf.RoundToInt(pos.x), z = Mathf.RoundToInt(pos.y);
                    if (x < 0 || z < 0 || x >= gx || z >= gz)
                        break;
                    if (seaDepth[z * gx + x] > 0f)
                    {
                        riverExit = z * gx + x;
                        break;
                    }
                }
            }
            outlet[riverExit] = true;
            if (water == null || water.riverInflow)
            {
                for (int i = 0; i < world.RiverInflows.Count && (i == 0 || (water?.allRiverLinks ?? false)); i++)
                {
                    // The main river comes in at the region's edge and cuts its valley across it; the others meet it on this
                    // tile (where the world puts the confluence), so they come in where their side crosses the map's edge:
                    // beyond it each runs in its own valley on the neighbouring tile.
                    int entry = EdgeCellToward(world.RiverInflows[i].From, gx, gz, riverCentre, i > 0 ? mapWindow : null);
                    if (entry == riverExit)
                        continue;
                    outlet[entry] = false;
                    entries.Add((entry, world.RiverInflows[i].Width));
                }
            }
        }
        return outlet;
    }

    /// <summary>
    /// The starting floor of the world rivers' antecedent courses (<see cref="GeologyGenerationExtension.antecedentRivers"/>):
    /// straight from each entry to the meeting point, then on to the exit, falling evenly to base level and lying
    /// <paramref name="depth"/> of the starting relief below the ground along it; NaN elsewhere.
    /// </summary>
    private static float[] AntecedentCourses(List<(int cell, float width)> entries, Vector2 meeting, int exit, float[] uplift, bool[] outlet,
        int gx, int gz, float depth)
    {
        int n = gx * gz;
        var floor = new float[n];
        for (int i = 0; i < n; i++)
            floor[i] = float.NaN;
        Vector2 At(int c) => new Vector2(c % gx, c / gx);
        // Cells along a segment, in order.
        List<int> Line(Vector2 a, Vector2 b)
        {
            var cells = new List<int>();
            float length = (b - a).magnitude;
            for (float t = 0f; t <= length; t += 0.5f)
            {
                Vector2 v = Vector2.Lerp(a, b, length > 0f ? t / length : 1f);
                int c = Mathf.Clamp(Mathf.RoundToInt(v.y), 0, gz - 1) * gx + Mathf.Clamp(Mathf.RoundToInt(v.x), 0, gx - 1);
                if (cells.Count == 0 || cells[cells.Count - 1] != c)
                    cells.Add(c);
            }
            return cells;
        }
        // The starting ground is the uplift's first step (Erode); the courses lie below its lowest along them.
        foreach ((int entry, float _) in entries)
        {
            List<int> course = Line(At(entry), meeting);
            course.AddRange(Line(meeting, At(exit)));
            float low = float.MaxValue;
            foreach (int c in course)
                low = Mathf.Min(low, uplift[c] * 0.01f);
            float top = low * (1f - depth);
            for (int k = 0; k < course.Count; k++)
            {
                int c = course[k];
                if (outlet[c])
                    continue;
                float f = top * (1f - (float)k / course.Count);
                floor[c] = float.IsNaN(floor[c]) ? f : Mathf.Min(floor[c], f);
            }
        }
        return floor;
    }

    private static int EdgeCellToward(Vector2 direction, int gx, int gz, Vector2? from = null, Rect? window = null)
    {
        // Walk from the start (default: the centre) along the direction until leaving the grid.
        Vector2 pos = from ?? new Vector2((gx - 1) * 0.5f, (gz - 1) * 0.5f);
        Vector2 step = direction.normalized * 0.5f;
        int last = Mathf.RoundToInt(pos.y) * gx + Mathf.RoundToInt(pos.x);
        for (int i = 0; i < (gx + gz) * 4; i++)
        {
            pos += step;
            int x = Mathf.RoundToInt(pos.x), z = Mathf.RoundToInt(pos.y);
            if (x < 0 || z < 0 || x >= gx || z >= gz || (window.HasValue && !window.Value.Contains(pos)))
                break;
            last = z * gx + x;
        }
        return last;
    }

    /// <summary>Iterations between updates of the rock erodibility as erosion exposes deeper beds.</summary>
    private const int StrataUpdateInterval = 10;

    /// <summary>
    /// Uplift, stream-power incision and hillslope creep. Rock strength is read from the beds exposed at each cell's
    /// current height (scaled to the tile's relief), so cutting down through a hard bed into a soft one speeds up.
    /// </summary>
    private static float[] Erode(float[] uplift, float[] erodibility, bool[] outlet, float[] inflow, float[] seaDepth, float relief, int gx, int gz,
        GeologyGenerationExtension p, RockStrata strata, RockStrata.Site[] sites, float reliefCells,
        out int[] receiverOut, out float[] areaOut, out float modelTop, float[] initialFloor = null)
    {
        int n = gx * gz;
        var h = new float[n];
        var filled = new float[n];
        var receiver = new int[n];
        var receiverDist = new float[n];
        var area = new float[n];
        var stack = new int[n];
        var donorCount = new int[n];
        var donorStart = new int[n + 1];
        var donors = new int[n];

        var rockK = new float[n];
        var creep = new float[n];
        // Explicit diffusion is stable only below 0.25 per step.
        float maxCreepFactor = 0.24f / Mathf.Max(1e-6f, p.hillslopeDiffusion);

        for (int i = 0; i < n; i++)
        {
            h[i] = outlet[i] ? 0f : uplift[i] * 0.01f;
            // A world river's antecedent course (it starts below the ground around it).
            if (initialFloor != null && !outlet[i] && !float.IsNaN(initialFloor[i]))
                h[i] = Mathf.Min(h[i], initialFloor[i]);
        }
        // Sediment: the load each cell passes on.
        var eroded = new float[n];
        var load = new float[n];

        for (int iteration = 0; iteration < p.erosionIterations; iteration++)
        {
            if (iteration % StrataUpdateInterval == 0)
            {
                float top = Mathf.Max(1e-6f, h.Max());
                for (int i = 0; i < n; i++)
                {
                    float k = strata.Erodibility(sites[i], h[i] / top * reliefCells);
                    rockK[i] = erodibility[i] * k;
                    creep[i] = Mathf.Min(k, maxCreepFactor);
                }
            }

            // Uplift (not of the sea floor).
            for (int i = 0; i < n; i++)
            {
                if (!outlet[i] && (seaDepth == null || seaDepth[i] <= 0f))
                    h[i] += uplift[i];
            }

            // Route over the depression-filled surface so closed basins still drain (they fill with sediment).
            Array.Copy(h, filled, n);
            GridMath.FillDepressions(filled, gx, gz, i => outlet[i], 1e-4f, includeEdges: false);
            for (int c = 0; c < n; c++)
            {
                receiver[c] = -1;
                if (outlet[c])
                    continue;
                int cx = c % gx, cz = c / gx;
                float best = 0f;
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + DX[d], nz = cz + DZ[d];
                    if (nx < 0 || nz < 0 || nx >= gx || nz >= gz)
                        continue;
                    int nb = nz * gx + nx;
                    float slope = (filled[c] - filled[nb]) / Dist[d];
                    if (slope > best)
                    {
                        best = slope;
                        receiver[c] = nb;
                        receiverDist[c] = Dist[d];
                    }
                }
            }

            // Order cells from base level upstream (breadth-first over the receiver tree).
            Array.Clear(donorCount, 0, n);
            for (int c = 0; c < n; c++)
            {
                if (receiver[c] >= 0)
                    donorCount[receiver[c]]++;
            }
            donorStart[0] = 0;
            for (int c = 0; c < n; c++)
                donorStart[c + 1] = donorStart[c] + donorCount[c];
            Array.Clear(donorCount, 0, n);
            for (int c = 0; c < n; c++)
            {
                int r = receiver[c];
                if (r >= 0)
                    donors[donorStart[r] + donorCount[r]++] = c;
            }
            int count = 0;
            for (int c = 0; c < n; c++)
            {
                if (receiver[c] < 0)
                    stack[count++] = c;
            }
            for (int head = 0; head < count; head++)
            {
                int c = stack[head];
                for (int k = donorStart[c]; k < donorStart[c + 1]; k++)
                    stack[count++] = donors[k];
            }

            // Drainage area, accumulated from the top of each tree down.
            for (int i = 0; i < n; i++)
                area[i] = 1f + inflow[i];
            for (int k = count - 1; k >= 0; k--)
            {
                int c = stack[k];
                if (receiver[c] >= 0)
                    area[receiver[c]] += area[c];
            }

            // Stream-power incision, implicit in the receiver's new height (unconditionally stable).
            for (int k = 0; k < count; k++)
            {
                int c = stack[k];
                int r = receiver[c];
                if (r < 0)
                    continue;
                float f = p.erodibility * rockK[c] * Mathf.Pow(area[c], p.areaExponent) / receiverDist[c];
                float before = h[c];
                h[c] = (h[c] + f * h[r]) / (1f + f);
                eroded[c] = Mathf.Max(0f, before - h[c]);
            }
            if (p.deposition > 0f)
                Deposit(h, eroded, load, inflow, receiver, area, stack, count, outlet, p);

            // Hillslope creep.
            Diffuse(h, outlet, gx, gz, p.hillslopeDiffusion, creep);
        }
        receiverOut = receiver;
        areaOut = area;
        modelTop = Mathf.Max(1e-6f, h.Max());
        return h;
    }

    /// <summary>
    /// Deposition (Davy and Lague 2009): the load runs down the drainage tree from the divides; each land cell keeps
    /// G x load / drainage area of it (rivers drop their load where they gather little more water and slow) and passes the
    /// rest on. What reaches base level (the sea, the region's edges) leaves the model.
    /// </summary>
    private static void Deposit(float[] h, float[] eroded, float[] load, float[] inflow, int[] receiver, float[] area, int[] stack, int count,
        bool[] outlet, GeologyGenerationExtension p)
    {
        int n = h.Length;
        Array.Clear(load, 0, n);
        float yield = 0f;
        int land = 0;
        for (int i = 0; i < n; i++)
        {
            if (!outlet[i])
            {
                yield += eroded[i];
                land++;
            }
        }
        // Rivers entering the region bring the sediment of their catchment beyond it (sediment yield scales with drainage
        // area): their inflow, in model cells of drainage, eroding at the region's own mean rate.
        yield /= Mathf.Max(1, land);
        for (int k = count - 1; k >= 0; k--)
        {
            int c = stack[k];
            load[c] += eroded[c] + inflow[c] * yield;
            int r = receiver[c];
            if (r >= 0)
            {
                float keep = Mathf.Min(load[c], p.deposition * load[c] / Mathf.Max(area[c], 1f));
                h[c] += keep;
                load[r] += load[c] - keep;
            }
        }
    }

    /// <summary>Linear hillslope creep; weak rock (higher <paramref name="factor"/>) creeps faster.</summary>
    private static void Diffuse(float[] h, bool[] outlet, int gx, int gz, float k, float[] factor)
    {
        var next = (float[])h.Clone();
        for (int z = 1; z < gz - 1; z++)
        {
            for (int x = 1; x < gx - 1; x++)
            {
                int i = z * gx + x;
                if (outlet[i])
                    continue;
                float laplacian = h[i - 1] + h[i + 1] + h[i - gx] + h[i + gx] - 4f * h[i];
                next[i] = h[i] + k * factor[i] * laplacian;
            }
        }
        Array.Copy(next, h, h.Length);
    }

    private static float Bilinear(float[] g, int gx, int gz, float x, float z)
    {
        x = Mathf.Clamp(x, 0f, gx - 1);
        z = Mathf.Clamp(z, 0f, gz - 1);
        int x0 = (int)x, z0 = (int)z;
        int x1 = Mathf.Min(x0 + 1, gx - 1), z1 = Mathf.Min(z0 + 1, gz - 1);
        float fx = x - x0, fz = z - z0;
        return Mathf.Lerp(Mathf.Lerp(g[z0 * gx + x0], g[z0 * gx + x1], fx), Mathf.Lerp(g[z1 * gx + x0], g[z1 * gx + x1], fx), fz);
    }
}
