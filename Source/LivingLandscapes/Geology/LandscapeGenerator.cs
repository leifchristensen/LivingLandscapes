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

    /// <summary>How much more the upslope side of the region is uplifted than the downslope side.</summary>
    public float regionalUpliftBias = 0.5f;

    /// <summary>River discharge entering the map, as a multiple of the map's own drainage area.</summary>
    public float riverInflow = 8f;

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

    /// <summary>Channel patterns: meanders, cutoffs and islands (W4, W5).</summary>
    public RiverChannelSettings channels = new RiverChannelSettings();

    /// <summary>The river's bed: carved so it never rises along the course (W1).</summary>
    public RiverBedSettings riverBed = new RiverBedSettings();

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
    public static float[] Generate(Map map, GeologyGenerationExtension p, out WorldContext world, out List<ChannelCourse> channels,
        out RockStrata strata, out float[] rockStrength, out ThingDef[] rock)
    {
        world = WorldContext.For(map, p.tileSpacingMetres);
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
        float relief = p.ReliefFor(world.Hilliness);
        float reliefCells = relief / p.cellSize;
        float extent = Mathf.Max(gx, gz) * mapCellsPerModelCell * 0.75f;
        strata = new RockStrata(p.strata, Find.World.NaturalRockTypesIn(map.Tile).ToList(), new Vector2(sizeX * 0.5f, sizeZ * 0.5f), extent, reliefCells);
        var sites = new RockStrata.Site[gx * gz];
        for (int z = 0; z < gz; z++)
        {
            for (int x = 0; x < gx; x++)
                sites[z * gx + x] = strata.At((x - originX) * mapCellsPerModelCell, (z - originZ) * mapCellsPerModelCell);
        }

        bool[] outlet = Outlets(world, gx, gz, out int riverEntry);
        float[] inflow = new float[gx * gz];
        if (riverEntry >= 0)
            inflow[riverEntry] = p.riverInflow * gx * gz;

        float[] h = Erode(uplift, erodibility, outlet, inflow, gx, gz, p, strata, sites, reliefCells, out int[] receiver,
            out float[] area, out float modelTop);
        // The channels the model carved, from its drainage tree; the world river is the one from its entry.
        channels = RiverNetwork.Extract(receiver, area, gx, gz,
            c => new Vector2((c % gx - originX) * mapCellsPerModelCell, (c / gx - originZ) * mapCellsPerModelCell),
            mapCellsPerModelCell, sizeX, sizeZ, riverEntry, p.network);

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
        var ones = new float[sizeX * sizeZ];
        for (int i = 0; i < ones.Length; i++)
            ones[i] = 1f;
        window = GridMath.MaskedBlur(window, ones, sizeX, sizeZ, mapCellsPerModelCell * 0.75f);
        min = float.MaxValue;
        max = float.MinValue;
        foreach (float v in window)
        {
            min = Mathf.Min(min, v);
            max = Mathf.Max(max, v);
        }
        var micro = new Perlin(p.cellSize / p.microWavelength, 2.0, 0.5, 3, Rand.Int, QualityMode.Medium);
        var centre = new Vector2(sizeX * 0.5f, sizeZ * 0.5f);
        var result = new float[sizeX * sizeZ];
        rockStrength = new float[sizeX * sizeZ];
        rock = new ThingDef[sizeX * sizeZ];
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                // The bed exposed here, at the same height scale the erosion model used.
                RockStrata.Site site = strata.At(x, z);
                float bedHeight = window[z * sizeX + x] / Mathf.Max(modelTop, 1e-6f) * reliefCells;
                rockStrength[z * sizeX + x] = strata.Strength(site, bedHeight);
                rock[z * sizeX + x] = strata.RockAt(site, bedHeight);

                float rel = (window[z * sizeX + x] - min) / Mathf.Max(max - min, 1e-6f);
                float tilt = -Vector2.Dot(new Vector2(x, z) - centre, world.Downhill) * p.cellSize * world.RegionalGradient;
                float rough = (float)micro.GetValue(x, 0.0, z) * p.microRelief;
                result[z * sizeX + x] = world.Elevation + (rel - 0.5f) * relief + tilt + rough;
            }
        }
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
    /// Returns the river's entry cell (upstream edge) or -1.
    /// </summary>
    private static bool[] Outlets(WorldContext world, int gx, int gz, out int riverEntry)
    {
        var outlet = new bool[gx * gz];
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

        riverEntry = -1;
        if (world.HasRiver)
        {
            int exit = EdgeCellToward(world.RiverFlow, gx, gz);
            riverEntry = EdgeCellToward(-world.RiverFlow, gx, gz);
            outlet[exit] = true;
            outlet[riverEntry] = false;
        }
        return outlet;
    }

    private static int EdgeCellToward(Vector2 direction, int gx, int gz)
    {
        // Walk from the centre along the direction until leaving the grid.
        var pos = new Vector2((gx - 1) * 0.5f, (gz - 1) * 0.5f);
        Vector2 step = direction.normalized * 0.5f;
        int last = Mathf.RoundToInt(pos.y) * gx + Mathf.RoundToInt(pos.x);
        for (int i = 0; i < (gx + gz) * 4; i++)
        {
            pos += step;
            int x = Mathf.RoundToInt(pos.x), z = Mathf.RoundToInt(pos.y);
            if (x < 0 || z < 0 || x >= gx || z >= gz)
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
    private static float[] Erode(float[] uplift, float[] erodibility, bool[] outlet, float[] inflow, int gx, int gz,
        GeologyGenerationExtension p, RockStrata strata, RockStrata.Site[] sites, float reliefCells,
        out int[] receiverOut, out float[] areaOut, out float modelTop)
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
            h[i] = outlet[i] ? 0f : uplift[i] * 0.01f;

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

            // Uplift.
            for (int i = 0; i < n; i++)
            {
                if (!outlet[i])
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
                h[c] = (h[c] + f * h[r]) / (1f + f);
            }

            // Hillslope creep.
            Diffuse(h, outlet, gx, gz, p.hillslopeDiffusion, creep);
        }
        receiverOut = receiver;
        areaOut = area;
        modelTop = Mathf.Max(1e-6f, h.Max());
        return h;
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
