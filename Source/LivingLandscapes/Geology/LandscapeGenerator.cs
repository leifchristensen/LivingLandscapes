using System;
using System.Collections.Generic;
using System.Linq;
using LivingLandscapes.DevTools;
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

    /// <summary>
    /// Share of a neighbour's fall a map inherits when both tiles have the same hilliness (<see cref="RegionalSlopeMath"/>).
    /// 0: a plain among plains (hills among hills) takes no regional slope; its own valleys and rises shape it.
    /// </summary>
    public float uniformSlopeShare = 0f;

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

    /// <summary>
    /// Least drainage area a world river brings into the region, as a multiple of the region's own. A world river brings its
    /// catchment beyond the region (P2: incision grows with drainage area): vanilla's world flow through the tile it comes from,
    /// in tiles of this tile's rainfall, times the model cells in a tile (<see cref="tileSpacingMetres"/>); this is the floor
    /// where the world flow is unknown or small.
    /// </summary>
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
        world = WorldContext.For(map, p.tileSpacingMetres, p);
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
        RiverEnds(world, water, gx, gz, riverCentre, out List<(int cell, float width)> entries, out List<float> entryFlows,
            out int riverExit, seaDepth, mapWindow);
        // Cells at base level from the start: the sea and the river's exit.
        var fixedOutlet = new bool[gx * gz];
        for (int i = 0; i < fixedOutlet.Length; i++)
            fixedOutlet[i] = (seaDepth != null && seaDepth[i] > 0f) || i == riverExit;
        float[] inflow = new float[gx * gz];
        float cellsPerTile = Mathf.Pow(p.tileSpacingMetres / metresPerModelCell, 2f);
        float rainfall = Mathf.Max(1f, map.TileInfo.rainfall);
        for (int k = 0; k < entries.Count; k++)
            inflow[entries[k].cell] += Mathf.Max(p.riverInflow * gx * gz, entryFlows[k] / rainfall * cellsPerTile);

        float[] floor = p.antecedentRivers && riverExit >= 0 && entries.Count > 0
            ? AntecedentCourses(entries, riverCentre, riverExit, uplift, fixedOutlet, gx, gz, p.antecedentDepth) : null;
        System.Action<float[], float[]> baseLevels = ModelBaseLevels(world, seaDepth, floor, entries, riverExit, gx, gz,
            (p.riverValley.size + p.riverValley.edge) * sizeX / mapCellsPerModelCell);
        // Dev: -ll-export-raw also writes the model's own grid (DevTools/ErosionGridExport).
        ErosionGridExport.Begin();
        System.Action<int, float[], int[], float[], float[]> observe = null;
        if (ErosionGridExport.Enabled)
        {
            ErosionGridExport.Note($"model {gx}x{gz}, {mapCellsPerModelCell:F3} map cells per model cell, map window origin ({originX:F2}, {originZ:F2}), " +
                                   $"meeting ({riverCentre.x:F2}, {riverCentre.y:F2}), exit {riverExit % gx},{riverExit / gx}, " +
                                   $"entries {string.Join(" ", entries.Select(e => $"{e.cell % gx},{e.cell / gx}"))}, downhill ({world.Downhill.x:F2}, {world.Downhill.y:F2}), " +
                                   $"regional gradient {world.RegionalGradient:F5}, iterations {p.erosionIterations}, " +
                                   $"entry inflow {string.Join(" ", entries.Select(e => inflow[e.cell].ToString("G3")))} model cells (region {gx * gz})");
            ErosionGridExport.Write("Uplift", uplift, gx, gz);
            ErosionGridExport.Write("Erodibility", erodibility, gx, gz);
            ErosionGridExport.Write("Floor", floor, gx, gz);
            observe = (iteration, hNow, receiverNow, areaNow, rockKNow) =>
            {
                ErosionGridExport.Write($"H_{iteration}", hNow, gx, gz);
                ErosionGridExport.Write($"Receiver_{iteration}", receiverNow, gx, gz);
                ErosionGridExport.Write($"Area_{iteration}", areaNow, gx, gz);
                ErosionGridExport.Write($"RockK_{iteration}", rockKNow, gx, gz);
            };
        }
        float[] h = Erode(uplift, erodibility, baseLevels, inflow, seaDepth, relief, gx, gz, p, strata, sites, reliefCells, out int[] receiver,
            out float[] area, out float modelTop, out float[] baseLevel, floor, observe, WaterBalance.LakeShare(map, out _));
        ErosionGridExport.Write("BaseLevel", baseLevel, gx, gz);
        Log.Message($"[LivingLandscapes] Erosion model base levels (#97), edge cells draining per side: {Outlets.EdgeSummary(baseLevel, h, gx, gz)}.");
        // The map's edge drains where the model's water there flows on out of the map (#97, ADR 0001).
        Vector2 ToMap(int c) => new Vector2((c % gx - originX) * mapCellsPerModelCell, (c / gx - originZ) * mapCellsPerModelCell);
        MapEdgeOpen = Outlets.MapEdgesFromModel(sizeX, sizeZ, receiver,
            (x, z) => Mathf.Clamp(Mathf.RoundToInt(originZ + z / mapCellsPerModelCell), 0, gz - 1) * gx
                      + Mathf.Clamp(Mathf.RoundToInt(originX + x / mapCellsPerModelCell), 0, gx - 1),
            ToMap);
        Log.Message($"[LivingLandscapes] Map edge from the model (#97): open per side {OpenPerSide(MapEdgeOpen, sizeX, sizeZ)}; " +
                    $"model cells on the window's border sending water out {ModelOutPerSide(receiver, gx, gz, ToMap, sizeX, sizeZ)}.");
        // The channels the model carved, from its drainage tree; the world river is the one reaching its exit.
        channels = RiverNetwork.Extract(receiver, area, gx, gz,
            c => new Vector2((c % gx - originX) * mapCellsPerModelCell, (c / gx - originZ) * mapCellsPerModelCell),
            mapCellsPerModelCell, sizeX, sizeZ, entries, riverExit, world.RiverExitWidth,
            water?.sourceShare.RandomInRange ?? p.network.sourceShare, p.network);
        if (entries.Count > 0)
            Log.Message($"[LivingLandscapes] World rivers enter the erosion model at {entries.Count} edge cell(s); " +
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
        ErosionGridExport.Write("Window", window, sizeX, sizeZ);
        // Dev: -ll-tilt=<scale> scales the regional tilt (0 turns it off) to test what it does to the map.
        float tiltScale = GenCommandLine.TryGetCommandLineArg("ll-tilt", out string tiltArg)
            ? float.Parse(tiltArg, System.Globalization.CultureInfo.InvariantCulture) : 1f;
        var micro = new Perlin(p.cellSize / p.microWavelength, 2.0, 0.5, 3, Rand.Int, QualityMode.Medium);
        var centre = new Vector2(sizeX * 0.5f, sizeZ * 0.5f);
        var result = new float[sizeX * sizeZ];
        var smooth = new float[sizeX * sizeZ];
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
                float tilt = -Vector2.Dot(new Vector2(x, z) - centre, world.Downhill) * p.cellSize * world.RegionalGradient * tiltScale;
                float rough = (float)micro.GetValue(x, 0.0, z) * p.microRelief;
                smooth[z * sizeX + x] = world.Elevation + (rel - level) * relief + tilt;
                result[z * sizeX + x] = smooth[z * sizeX + x] + rough;
            }
        }
        int textureHollows = FillTextureHollows(result, smooth, sizeX, sizeZ);
        if (textureHollows > 0)
            Log.Message($"[LivingLandscapes] Micro-relief on {map}: {textureHollows} closed hollows it made filled to their rims.");
        ErosionGridExport.Write("Result", result, sizeX, sizeZ);
        int bodies = resistant.Count(v => v >= 0.5f);
        if (bodies > 0)
            Log.Message($"[LivingLandscapes] Resistant rock bodies (plugs, dikes) cover {(float)bodies / resistant.Length:P1} of {map}.");
        int sea = Coastline.Apply(map, result, p.coast, resistant);
        if (sea > 0)
            Log.Message($"[LivingLandscapes] Coastline on {map}: {sea} sea cells set below the beach, land eased down to it.");
        return result;
    }

    /// <summary>
    /// The micro-relief is surface texture, not a landform process, so it must not make closed hollows of its own (#81):
    /// soil creep wears bumps of its wavelength down within centuries, and real small hollows need a process that keeps
    /// making them (tree throw, wind scour, karst). Each hollow of <paramref name="textured"/> that lies wholly outside the
    /// depressions <paramref name="smooth"/> already had is raised to its spill level; the texture stays on slopes, and the
    /// basins the landform made (roughened floors included) are left to the water balance. Returns the hollows filled.
    /// </summary>
    private static int FillTextureHollows(float[] textured, float[] smooth, int sizeX, int sizeZ)
    {
        const float Tolerance = 1e-4f;
        var landform = (float[])smooth.Clone();
        GridMath.FillDepressions(landform, sizeX, sizeZ, _ => false, 0f);
        // Filled with the drainage's own gradient, so the floor slopes to its spill point as a graded deposit would, not a flat.
        var spill = (float[])textured.Clone();
        GridMath.FillDepressions(spill, sizeX, sizeZ, _ => false, Drainage.FillGradient);

        int count = GridMath.LabelRegions(i => spill[i] - textured[i] > Tolerance, sizeX, sizeZ, out int[] labels);
        var inLandformBasin = new bool[count];
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] >= 0 && landform[i] - smooth[i] > Tolerance)
                inLandformBasin[labels[i]] = true;
        }
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] >= 0 && !inLandformBasin[labels[i]])
                textured[i] = spill[i];
        }
        return inLandformBasin.Count(b => !b);
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
    /// Where the world river leaves the region (its exit: the edge cell toward the world's exit, or where its way out first
    /// meets the sea) and where world rivers enter (with vanilla's width): the main river, plus every other river link
    /// if the water regime asks for them (a confluence); none if the regime has the river rise on the map (headwater).
    /// </summary>
    private static void RiverEnds(WorldContext world, WaterRegimeDef water, int gx, int gz, Vector2 riverCentre,
        out List<(int cell, float width)> entries, out List<float> entryFlows, out int riverExit, float[] seaDepth = null, Rect? mapWindow = null)
    {
        entryFlows = new List<float>();
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
                    entries.Add((entry, world.RiverInflows[i].Width));
                    entryFlows.Add(world.RiverInflows[i].Flow);
                }
            }
        }
    }

    /// <summary>
    /// Map edge cells the model's water flows out through (#97, <see cref="Outlets.MapEdgesFromModel"/>), from the last
    /// <see cref="Generate"/>; null before. Map size, edge cells only.
    /// </summary>
    public static bool[] MapEdgeOpen { get; private set; }

    /// <summary>Diagnostic: open map edge cells per side (W, E, S, N).</summary>
    private static string OpenPerSide(bool[] open, int sizeX, int sizeZ)
    {
        int w = 0, e = 0, s = 0, n = 0;
        for (int k = 0; k < sizeZ; k++)
        {
            if (open[k * sizeX]) w++;
            if (open[k * sizeX + sizeX - 1]) e++;
        }
        for (int k = 0; k < sizeX; k++)
        {
            if (open[k]) s++;
            if (open[(sizeZ - 1) * sizeX + k]) n++;
        }
        return $"W {(float)w / sizeZ:P0}, E {(float)e / sizeZ:P0}, S {(float)s / sizeX:P0}, N {(float)n / sizeX:P0}";
    }

    /// <summary>
    /// Diagnostic for <see cref="Outlets.MapEdgesFromModel"/>: per side, the share of model cells along the map window's border
    /// whose receiver lies beyond that side. It should match the map's open share (a mismatch is a window or transform error).
    /// </summary>
    private static string ModelOutPerSide(int[] receiver, int gx, int gz, System.Func<int, Vector2> toMap, int sizeX, int sizeZ)
    {
        int[] count = new int[4], outward = new int[4];
        for (int c = 0; c < receiver.Length; c++)
        {
            Vector2 v = toMap(c);
            if (v.x < -0.5f || v.y < -0.5f || v.x > sizeX - 0.5f || v.y > sizeZ - 0.5f)
                continue;
            int r = receiver[c];
            Vector2 t = r >= 0 ? toMap(r) : v;
            // The border cells of each side: within half a model cell of the map's edge line.
            float half = 0.5f * (toMap(1).x - toMap(0).x);
            if (v.x <= half) { count[0]++; if (t.x < 0f && t.x < v.x) outward[0]++; }
            if (v.x >= sizeX - 1 - half) { count[1]++; if (t.x > sizeX - 1 && t.x > v.x) outward[1]++; }
            if (v.y <= half) { count[2]++; if (t.y < 0f && t.y < v.y) outward[2]++; }
            if (v.y >= sizeZ - 1 - half) { count[3]++; if (t.y > sizeZ - 1 && t.y > v.y) outward[3]++; }
        }
        string Share(int k) => count[k] > 0 ? ((float)outward[k] / count[k]).ToString("P0") : "-";
        return $"W {Share(0)}, E {Share(1)}, S {Share(2)}, N {Share(3)}";
    }

    /// <summary>
    /// The erosion model's base levels (P1, <see cref="Outlets"/>), recomputed from the surface each iteration: the sea and the
    /// river's exit at 0 (the model's base), edges at max(Plane, Bed) with Plane rising from 0 at the most downhill edge along
    /// the surface's own regional trend, and Bed from the world rivers' antecedent courses as they are cut down. Edges whose
    /// base level stands above the ground are closed, and every land area keeps an outlet.
    /// </summary>
    private static System.Action<float[], float[]> ModelBaseLevels(WorldContext world, float[] seaDepth, float[] floor,
        List<(int cell, float width)> entries, int riverExit, int gx, int gz, float valleyHalfWidth)
    {
        int n = gx * gz;
        float[] offset = Outlets.EdgeOffsets(gx, gz, world.Downhill);
        var entryCells = entries.Select(e => e.cell).ToList();
        var course = new List<int>();
        for (int i = 0; floor != null && i < n; i++)
        {
            if (!float.IsNaN(floor[i]))
                course.Add(i);
        }
        float[] toEntry = null;
        int[][] valley = course.Count > 0 ? Outlets.Valleys(gx, gz, course, entryCells, valleyHalfWidth, out toEntry) : null;
        bool Land(int i) => seaDepth == null || seaDepth[i] <= 0f;
        int regionCount = GridMath.LabelRegions(Land, gx, gz, out int[] regions);
        return (h, b) =>
        {
            for (int i = 0; i < n; i++)
                b[i] = Land(i) ? float.NaN : 0f;
            float rise = Outlets.RegionalRise(h, gx, gz, world.Downhill, Land);
            float entryLevel = entryCells.Count > 0 ? entryCells.Max(c => h[c]) : 0f;
            // The river's mean fall per cell from its entries to the exit (at 0).
            float riverSlope = course.Count > 0 ? Mathf.Max(0f, entryLevel) * Mathf.Max(1, entryCells.Count) / course.Count : 0f;
            Outlets.EdgeLevels(b, offset, 0f, rise, valley, toEntry, h, entryLevel, riverSlope, 0f, h.Max(), riverExit, 0f, entryCells);
            Outlets.CloseAboveGround(b, h, gx, gz, riverExit);
            Outlets.KeepOneOutlet(b, h, gx, gz, regions, regionCount);
        };
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
        // Erode adds a full step of uplift before it first routes the water, so the course must lie below the ground as it stands
        // then (start height plus one step), below its lowest bank along the course: the river is entrenched from the first routing
        // on, however unevenly the land rises across it. The floor is returned as a start height (less the first step's uplift).
        foreach ((int entry, float _) in entries)
        {
            List<int> course = Line(At(entry), meeting);
            course.AddRange(Line(meeting, At(exit)));
            float low = float.MaxValue;
            foreach (int c in course)
                low = Mathf.Min(low, uplift[c] * 1.01f);
            float top = low * (1f - depth);
            for (int k = 0; k < course.Count; k++)
            {
                int c = course[k];
                if (outlet[c])
                    continue;
                float f = top * (1f - (float)k / course.Count) - uplift[c];
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
    private static float[] Erode(float[] uplift, float[] erodibility, System.Action<float[], float[]> baseLevels, float[] inflow, float[] seaDepth,
        float relief, int gx, int gz, GeologyGenerationExtension p, RockStrata strata, RockStrata.Site[] sites, float reliefCells,
        out int[] receiverOut, out float[] areaOut, out float modelTop, out float[] baseLevelOut, float[] initialFloor = null,
        System.Action<int, float[], int[], float[], float[]> observe = null, float lakeShare = float.PositiveInfinity)
    {
        int n = gx * gz;
        // Base levels (P1, Outlets): water leaves a cell once its surface reaches them; NaN where it cannot leave.
        var baseLevel = new float[n];
        var outlet = new bool[n];
        var sink = new bool[n];
        int sinks = 0;
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
            h[i] = seaDepth != null && seaDepth[i] > 0f ? 0f : uplift[i] * 0.01f;
            // A world river's antecedent course (it starts below the ground around it).
            if (initialFloor != null && !float.IsNaN(initialFloor[i]))
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
                if (seaDepth == null || seaDepth[i] <= 0f)
                    h[i] += uplift[i];
            }
            // Outlets erode down to their base level; one standing below it is not raised (water ponds up to it instead).
            baseLevels(h, baseLevel);
            // Closed basins (P23, #99): a hollow whose lake evaporates all the runoff its catchment collects sends nothing over
            // its rim. Its lake is a sink at the balance level: still water, so it does not incise (P2), and sediment settles in it.
            Array.Clear(sink, 0, n);
            if (iteration > 0 && !float.IsInfinity(lakeShare))
                sinks = Outlets.CloseBasins(h, baseLevel, area, receiver, lakeShare, gx, gz, i => inflow[i] > 0f, sink);
            for (int i = 0; i < n; i++)
            {
                outlet[i] = !float.IsNaN(baseLevel[i]);
                if (outlet[i] && baseLevel[i] <= h[i])
                    h[i] = baseLevel[i];
            }

            // Route over the surface filled to the spill levels so closed basins still drain (they fill with sediment).
            Array.Copy(h, filled, n);
            Outlets.Fill(filled, gx, gz, baseLevel, 1e-4f);
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
            Array.Clear(eroded, 0, n);
            for (int k = 0; k < count; k++)
            {
                int c = stack[k];
                int r = receiver[c];
                // Still water (S <= 0, a hollow routed over its fill toward the rim) does not erode (P2); the implicit step would
                // otherwise raise the cell toward its receiver, filling hollows with ground from nowhere.
                if (r < 0 || h[c] <= h[r])
                    continue;
                float f = p.erodibility * rockK[c] * Mathf.Pow(area[c], p.areaExponent) / receiverDist[c];
                float before = h[c];
                h[c] = (h[c] + f * h[r]) / (1f + f);
                eroded[c] = Mathf.Max(0f, before - h[c]);
            }
            if (p.deposition > 0f)
                Deposit(h, eroded, load, inflow, receiver, area, stack, count, outlet, sink, baseLevel, p);

            // Hillslope creep.
            Diffuse(h, outlet, gx, gz, p.hillslopeDiffusion, creep);

            int quarter = Mathf.Max(1, p.erosionIterations / 4);
            if (observe != null && (iteration == 0 || (iteration + 1) % quarter == 0 || iteration == p.erosionIterations - 1))
                observe(iteration + 1, h, receiver, area, rockK);
        }
        if (!float.IsInfinity(lakeShare))
            Log.Message($"[LivingLandscapes] Erosion model closed basins (#99): {sinks} keep their water (lake area {lakeShare:G3} x catchment).");
        receiverOut = receiver;
        areaOut = area;
        baseLevelOut = baseLevel;
        modelTop = Mathf.Max(1e-6f, h.Max());
        return h;
    }

    /// <summary>
    /// Deposition (Davy and Lague 2009): the load runs down the drainage tree from the divides; each land cell keeps
    /// G x load / drainage area of it (rivers drop their load where they gather little more water and slow) and passes the
    /// rest on. What reaches base level (the sea, the region's edges) leaves the model; what reaches a closed basin's lake
    /// settles there, up to the lake's level.
    /// </summary>
    private static void Deposit(float[] h, float[] eroded, float[] load, float[] inflow, int[] receiver, float[] area, int[] stack, int count,
        bool[] outlet, bool[] sink, float[] baseLevel, GeologyGenerationExtension p)
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
            else if (sink[c])
                h[c] = Mathf.Min(h[c] + load[c], baseLevel[c]);
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
