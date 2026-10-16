using System;
using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>
/// Raster operations on row-major per-cell grids (index = z * sizeX + x), shared by the geology layers.
/// Standard GIS/terrain-analysis algorithms; nothing here knows about RimWorld.
/// </summary>
public static class GridMath
{
    private static readonly int[] DX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] DZ = { 0, 1, 1, 1, 0, -1, -1, -1 };
    private static readonly float[] Dist = { 1f, 1.4142135f, 1f, 1.4142135f, 1f, 1.4142135f, 1f, 1.4142135f };

    /// <summary>
    /// Gaussian blur that ignores cells whose weight is 0 (normalised convolution), so masked cells are filled in
    /// from their surroundings instead of dragging the average towards their own values. Masked areas wider than
    /// the blur (e.g. a broad river) are filled by repeating with a doubled radius until land is within reach.
    /// </summary>
    public static float[] MaskedBlur(float[] values, float[] weights, int sizeX, int sizeZ, float sigma)
    {
        int n = values.Length;
        var weighted = new float[n];
        var ones = new float[n];
        for (int i = 0; i < n; i++)
        {
            weighted[i] = values[i] * weights[i];
            ones[i] = 1f;
        }

        var result = new float[n];
        var resolved = new bool[n];
        int remaining = n;
        float maxSigma = Mathf.Max(sizeX, sizeZ);
        for (float s = sigma; remaining > 0 && s <= maxSigma; s *= 2f)
        {
            float[] kernel = Kernel(s, out int radius);
            float[] num = Separable(weighted, sizeX, sizeZ, kernel, radius);
            float[] den = Separable(weights, sizeX, sizeZ, kernel, radius);
            float[] full = Separable(ones, sizeX, sizeZ, kernel, radius);
            for (int i = 0; i < n; i++)
            {
                // Need a meaningful share of the kernel on weighted cells, or the estimate is just noise.
                if (resolved[i] || den[i] < 0.05f * full[i])
                    continue;
                result[i] = num[i] / den[i];
                resolved[i] = true;
                remaining--;
            }
        }
        for (int i = 0; i < n; i++)
        {
            if (!resolved[i])
                result[i] = values[i];
        }
        return result;
    }

    private static float[] Kernel(float sigma, out int radius)
    {
        radius = Mathf.Max(1, Mathf.CeilToInt(sigma * 3f));
        var kernel = new float[radius * 2 + 1];
        for (int i = -radius; i <= radius; i++)
            kernel[i + radius] = Mathf.Exp(-(i * i) / (2f * sigma * sigma));
        return kernel;
    }

    private static float[] Separable(float[] input, int sizeX, int sizeZ, float[] kernel, int radius)
    {
        var temp = new float[input.Length];
        var output = new float[input.Length];
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                float sum = 0f;
                for (int k = -radius; k <= radius; k++)
                    sum += input[z * sizeX + Mathf.Clamp(x + k, 0, sizeX - 1)] * kernel[k + radius];
                temp[z * sizeX + x] = sum;
            }
        }
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                float sum = 0f;
                for (int k = -radius; k <= radius; k++)
                    sum += temp[Mathf.Clamp(z + k, 0, sizeZ - 1) * sizeX + x] * kernel[k + radius];
                output[z * sizeX + x] = sum;
            }
        }
        return output;
    }

    /// <summary>Two-pass chamfer distance (cells) to the nearest cell where <paramref name="isSource"/> is true.</summary>
    public static float[] DistanceTo(Func<int, bool> isSource, int sizeX, int sizeZ, float far = 9999f)
    {
        var dist = new float[sizeX * sizeZ];
        for (int i = 0; i < dist.Length; i++)
            dist[i] = isSource(i) ? 0f : far;

        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                int i = z * sizeX + x;
                float best = dist[i];
                if (x > 0) best = Mathf.Min(best, dist[i - 1] + 1f);
                if (z > 0)
                {
                    best = Mathf.Min(best, dist[i - sizeX] + 1f);
                    if (x > 0) best = Mathf.Min(best, dist[i - sizeX - 1] + 1.4142135f);
                    if (x < sizeX - 1) best = Mathf.Min(best, dist[i - sizeX + 1] + 1.4142135f);
                }
                dist[i] = best;
            }
        }
        for (int z = sizeZ - 1; z >= 0; z--)
        {
            for (int x = sizeX - 1; x >= 0; x--)
            {
                int i = z * sizeX + x;
                float best = dist[i];
                if (x < sizeX - 1) best = Mathf.Min(best, dist[i + 1] + 1f);
                if (z < sizeZ - 1)
                {
                    best = Mathf.Min(best, dist[i + sizeX] + 1f);
                    if (x < sizeX - 1) best = Mathf.Min(best, dist[i + sizeX + 1] + 1.4142135f);
                    if (x > 0) best = Mathf.Min(best, dist[i + sizeX - 1] + 1.4142135f);
                }
                dist[i] = Mathf.Min(best, far);
            }
        }
        return dist;
    }

    /// <summary>
    /// Priority-Flood with epsilon (Barnes et al. 2014): raises every depression to its spill level plus a tiny
    /// gradient, so each cell has a strictly downhill path to an outlet. Outlets are the map edge plus any cell
    /// where <paramref name="isOutlet"/> is true (e.g. the sea). With <paramref name="includeEdges"/> false only
    /// <paramref name="isOutlet"/> cells drain, so closed edges behave like walls.
    /// </summary>
    public static void FillDepressions(float[] h, int sizeX, int sizeZ, Func<int, bool> isOutlet, float epsilon, bool includeEdges = true)
    {
        Flood(h, sizeX, sizeZ, isOutlet, epsilon, carve: false, includeEdges);
    }

    /// <summary>
    /// Hybrid breach-and-fill (after Lindsay 2016): depressions are first filled up to <paramref name="maxBreachDepth"/>
    /// below their spill level, then the rest is removed by carving a channel through the lowest point of the rim
    /// (Priority-Flood carving, Soille 2004). Shallow pits drain through a notch instead of becoming flats; deep
    /// basins keep a filled floor and get an outlet gorge at most <paramref name="maxBreachDepth"/> deep.
    /// </summary>
    public static void BreachDepressions(float[] h, int sizeX, int sizeZ, Func<int, bool> isOutlet, float epsilon, float maxBreachDepth)
    {
        var filled = (float[])h.Clone();
        Flood(filled, sizeX, sizeZ, isOutlet, epsilon, carve: false, includeEdges: true);
        for (int i = 0; i < h.Length; i++)
            h[i] = Mathf.Max(h[i], filled[i] - maxBreachDepth);
        Flood(h, sizeX, sizeZ, isOutlet, epsilon, carve: true, includeEdges: true);
    }

    private static void Flood(float[] h, int sizeX, int sizeZ, Func<int, bool> isOutlet, float epsilon, bool carve, bool includeEdges)
    {
        var closed = new bool[h.Length];
        var parent = new int[h.Length];
        var open = new PriorityQueue<int, float>();
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                int i = z * sizeX + x;
                parent[i] = -1;
                bool edge = includeEdges && (x == 0 || z == 0 || x == sizeX - 1 || z == sizeZ - 1);
                if (edge || isOutlet(i))
                {
                    closed[i] = true;
                    open.Enqueue(i, h[i]);
                }
            }
        }

        while (open.TryDequeue(out int c, out _))
        {
            int cx = c % sizeX, cz = c / sizeX;
            for (int d = 0; d < 8; d++)
            {
                int nx = cx + DX[d], nz = cz + DZ[d];
                if (nx < 0 || nz < 0 || nx >= sizeX || nz >= sizeZ)
                    continue;
                int n = nz * sizeX + nx;
                if (closed[n])
                    continue;
                closed[n] = true;
                parent[n] = c;
                if (h[n] <= h[c])
                {
                    if (carve)
                    {
                        // Lower the path from here back towards the outlet so it descends from n.
                        float level = h[n];
                        for (int p = c; p != -1 && h[p] >= level; p = parent[p])
                        {
                            level -= epsilon;
                            h[p] = level;
                        }
                    }
                    else
                    {
                        h[n] = h[c] + epsilon * Dist[d];
                    }
                }
                open.Enqueue(n, h[n]);
            }
        }
    }

    /// <summary>
    /// Multiple-flow-direction accumulation (Freeman 1991): each cell passes its water to all lower neighbours in
    /// proportion to slope^<paramref name="exponent"/>. Spreads over hillslopes and converges in valleys.
    /// Returns the upslope contributing area of each cell, in cells (itself included).
    /// </summary>
    public static float[] FlowAccumulationMfd(float[] h, int sizeX, int sizeZ, float exponent = 1.1f)
    {
        int n = h.Length;
        var order = new int[n];
        for (int i = 0; i < n; i++)
            order[i] = i;
        Array.Sort(order, (a, b) => h[b].CompareTo(h[a]));

        var area = new float[n];
        for (int i = 0; i < n; i++)
            area[i] = 1f;
        var weights = new float[8];
        foreach (int c in order)
        {
            int cx = c % sizeX, cz = c / sizeX;
            float total = 0f;
            for (int d = 0; d < 8; d++)
            {
                weights[d] = 0f;
                int nx = cx + DX[d], nz = cz + DZ[d];
                if (nx < 0 || nz < 0 || nx >= sizeX || nz >= sizeZ)
                    continue;
                float drop = h[c] - h[nz * sizeX + nx];
                if (drop <= 0f)
                    continue;
                weights[d] = Mathf.Pow(drop / Dist[d], exponent);
                total += weights[d];
            }
            if (total <= 0f)
                continue;
            for (int d = 0; d < 8; d++)
            {
                if (weights[d] > 0f)
                    area[(cz + DZ[d]) * sizeX + cx + DX[d]] += area[c] * weights[d] / total;
            }
        }
        return area;
    }

    /// <summary>
    /// D8 flow routing: each cell drains to its steepest-descent neighbour. Returns the upslope contributing
    /// area of each cell, in cells (itself included). Cells with no lower neighbour keep their own water.
    /// </summary>
    public static float[] FlowAccumulation(float[] h, int sizeX, int sizeZ)
    {
        int n = h.Length;
        var receiver = new int[n];
        for (int c = 0; c < n; c++)
        {
            int cx = c % sizeX, cz = c / sizeX;
            int best = -1;
            float bestSlope = 0f;
            for (int d = 0; d < 8; d++)
            {
                int nx = cx + DX[d], nz = cz + DZ[d];
                if (nx < 0 || nz < 0 || nx >= sizeX || nz >= sizeZ)
                    continue;
                int nb = nz * sizeX + nx;
                float slope = (h[c] - h[nb]) / Dist[d];
                if (slope > bestSlope)
                {
                    bestSlope = slope;
                    best = nb;
                }
            }
            receiver[c] = best;
        }

        var order = new int[n];
        for (int i = 0; i < n; i++)
            order[i] = i;
        Array.Sort(order, (a, b) => h[b].CompareTo(h[a]));

        var area = new float[n];
        for (int i = 0; i < n; i++)
            area[i] = 1f;
        foreach (int c in order)
        {
            if (receiver[c] >= 0)
                area[receiver[c]] += area[c];
        }
        return area;
    }

    /// <summary>Gradient magnitude (height units per cell), central differences, one-sided at the edges.</summary>
    public static float[] Slope(float[] h, int sizeX, int sizeZ)
    {
        var slope = new float[h.Length];
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                Gradient(h, sizeX, sizeZ, x, z, out float dx, out float dz);
                slope[z * sizeX + x] = Mathf.Sqrt(dx * dx + dz * dz);
            }
        }
        return slope;
    }

    /// <summary>
    /// Aspect: the downhill direction of each cell in radians, counter-clockwise from east (0 = east, π/2 = north,
    /// π = west, 3π/2 = south), in 0..2π. Flat cells get 0. With <see cref="Slope"/> it forms the slope vector field.
    /// </summary>
    public static float[] Aspect(float[] h, int sizeX, int sizeZ)
    {
        var aspect = new float[h.Length];
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                Gradient(h, sizeX, sizeZ, x, z, out float dx, out float dz);
                float angle = Mathf.Atan2(-dz, -dx);
                aspect[z * sizeX + x] = angle < 0f ? angle + 2f * Mathf.PI : angle;
            }
        }
        return aspect;
    }

    /// <summary>
    /// How steeply the ground falls toward <paramref name="direction"/> (radians, as <see cref="Aspect"/>): the slope
    /// where it faces that way, 0 across it, negative where the ground rises that way.
    /// </summary>
    public static float SlopeToward(float slope, float aspect, float direction) => slope * Mathf.Cos(aspect - direction);

    /// <summary>Uphill gradient at one cell (height units per cell), central differences, one-sided at the edges.</summary>
    private static void Gradient(float[] h, int sizeX, int sizeZ, int x, int z, out float dx, out float dz)
    {
        int x0 = Mathf.Max(x - 1, 0), x1 = Mathf.Min(x + 1, sizeX - 1);
        int z0 = Mathf.Max(z - 1, 0), z1 = Mathf.Min(z + 1, sizeZ - 1);
        dx = (h[z * sizeX + x1] - h[z * sizeX + x0]) / (x1 - x0);
        dz = (h[z1 * sizeX + x] - h[z0 * sizeX + x]) / (z1 - z0);
    }

    /// <summary>Labels 4-connected regions where <paramref name="member"/> is true; -1 elsewhere. Returns the region count.</summary>
    public static int LabelRegions(Func<int, bool> member, int sizeX, int sizeZ, out int[] labels)
    {
        labels = new int[sizeX * sizeZ];
        for (int i = 0; i < labels.Length; i++)
            labels[i] = -1;
        int count = 0;
        var stack = new Stack<int>();
        for (int start = 0; start < labels.Length; start++)
        {
            if (labels[start] != -1 || !member(start))
                continue;
            labels[start] = count;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int c = stack.Pop();
                int cx = c % sizeX, cz = c / sizeX;
                for (int d = 0; d < 8; d += 2)
                {
                    int nx = cx + DX[d], nz = cz + DZ[d];
                    if (nx < 0 || nz < 0 || nx >= sizeX || nz >= sizeZ)
                        continue;
                    int nb = nz * sizeX + nx;
                    if (labels[nb] == -1 && member(nb))
                    {
                        labels[nb] = count;
                        stack.Push(nb);
                    }
                }
            }
            count++;
        }
        return count;
    }

    /// <summary>Calls <paramref name="visit"/> for every in-bounds 8-neighbour index of a cell.</summary>
    public static void ForEachNeighbour(int c, int sizeX, int sizeZ, Action<int> visit)
    {
        int cx = c % sizeX, cz = c / sizeX;
        for (int d = 0; d < 8; d++)
        {
            int nx = cx + DX[d], nz = cz + DZ[d];
            if (nx >= 0 && nz >= 0 && nx < sizeX && nz < sizeZ)
                visit(nz * sizeX + nx);
        }
    }
}
