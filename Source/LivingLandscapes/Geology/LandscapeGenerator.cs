using System;
using System.Collections.Generic;
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

    /// <summary>Ruggedness of the uplift pattern (0 smooth swells, 1 sharp ridges); mountains use the upper value.</summary>
    public float ridgedness = 0.2f;
    public float ridgednessMountains = 0.6f;

    /// <summary>Grid spacing of the erosion model; the result is interpolated to map cells.</summary>
    public float erosionCellSize = 3f;

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

    public static float[] Generate(Map map, GeologyGenerationExtension p, out WorldContext world)
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

        float[] uplift = UpliftField(world, p, gx, gz, metresPerModelCell);
        bool[] outlet = Outlets(world, gx, gz, out int riverEntry);
        float[] inflow = new float[gx * gz];
        if (riverEntry >= 0)
            inflow[riverEntry] = p.riverInflow * gx * gz;

        float[] h = Erode(uplift, outlet, inflow, gx, gz, p);

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
        float relief = p.ReliefFor(world.Hilliness);
        var micro = new Perlin(p.cellSize / p.microWavelength, 2.0, 0.5, 3, Rand.Int, QualityMode.Medium);
        var centre = new Vector2(sizeX * 0.5f, sizeZ * 0.5f);
        var result = new float[sizeX * sizeZ];
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                float rel = (window[z * sizeX + x] - min) / Mathf.Max(max - min, 1e-6f);
                float tilt = -Vector2.Dot(new Vector2(x, z) - centre, world.Downhill) * p.cellSize * world.RegionalGradient;
                float rough = (float)micro.GetValue(x, 0.0, z) * p.microRelief;
                result[z * sizeX + x] = world.Elevation + (rel - 0.5f) * relief + tilt + rough;
            }
        }
        return result;
    }

    /// <summary>Rock uplift rate per model cell: fractal noise at landform scale, sharper for mountains, stronger upslope.</summary>
    private static float[] UpliftField(WorldContext world, GeologyGenerationExtension p, int gx, int gz, float metresPerModelCell)
    {
        double frequency = metresPerModelCell / p.landformWavelength;
        var swells = new Perlin(frequency, 2.0, 0.5, 4, Rand.Int, QualityMode.High);
        var ridges = new RidgedMultifractal(frequency, 2.0, 4, Rand.Int, QualityMode.High);
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
                float u = Mathf.Lerp(smooth, sharp, ridged);
                float upslope = -Vector2.Dot(new Vector2(x, z) - centre, world.Downhill) / half;
                u *= Mathf.Max(0.1f, 1f + p.regionalUpliftBias * upslope);
                uplift[z * gx + x] = Mathf.Lerp(0.15f, 1f, Mathf.Clamp01(u));
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

    private static float[] Erode(float[] uplift, bool[] outlet, float[] inflow, int gx, int gz, GeologyGenerationExtension p)
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

        for (int i = 0; i < n; i++)
            h[i] = outlet[i] ? 0f : uplift[i] * 0.01f;

        for (int iteration = 0; iteration < p.erosionIterations; iteration++)
        {
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
                float f = p.erodibility * Mathf.Pow(area[c], p.areaExponent) / receiverDist[c];
                h[c] = (h[c] + f * h[r]) / (1f + f);
            }

            // Hillslope creep.
            Diffuse(h, outlet, gx, gz, p.hillslopeDiffusion);
        }
        return h;
    }

    private static void Diffuse(float[] h, bool[] outlet, int gx, int gz, float k)
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
                next[i] = h[i] + k * laplacian;
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
