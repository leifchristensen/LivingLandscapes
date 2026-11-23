using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

public static partial class TerrainChannels
{
    /// <summary>
    /// D3 measurement (DELTA-MODEL-PLAN.md, step D3): the shape of the land the rivers built into vanilla's water mask. Cells and share of
    /// the map; the largest lobe's depth into the sea against its width along the coast (aspect), its solidity (area over the convex hull of
    /// its cells), the count of separate protrusions (clumps of built cells farther than half the deepest reach from the original shore),
    /// the spread of its heights above the sea, the one-cell pockets and islands and the checkerboard blocks at its edge, the height of the
    /// built land at the mouth against the mouth depth, and the closure of the erosion model's sediment ledger. Read-only.
    /// </summary>
    private static string DeltaShape(bool[] built, bool[] originalWater, float[] height, float seaLevel, int sizeX, int sizeZ, int mouthCell,
        float mouthDepth, Vector2 seaDirection, ErosionLedger ledger)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string closure = ledger == null ? "n/a" : ledger.Closure.ToString("F6", inv);
        int n = sizeX * sizeZ, cells = 0;
        if (built != null)
        {
            for (int i = 0; i < n; i++)
                cells += built[i] ? 1 : 0;
        }
        if (cells == 0)
            return string.Format(inv, "built land 0 cells, ledger closure {0}", closure);

        int count = GridMath.LabelRegions(i => built[i], sizeX, sizeZ, out int[] labels, true);
        var sizes = new int[count];
        foreach (int l in labels)
        {
            if (l >= 0)
                sizes[l]++;
        }
        int biggest = 0;
        for (int k = 1; k < count; k++)
            biggest = sizes[k] > sizes[biggest] ? k : biggest;

        // Lobe depth and width: the largest lobe's extent along the sea direction and across it.
        Vector2 dir = seaDirection.sqrMagnitude > 0f ? seaDirection.normalized : Vector2.up, across = new Vector2(-dir.y, dir.x);
        float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
        var corners = new List<Vector2>();
        for (int i = 0; i < n; i++)
        {
            if (labels[i] != biggest)
                continue;
            var c = new Vector2(i % sizeX, i / sizeX);
            float a = Vector2.Dot(c, dir), b = Vector2.Dot(c, across);
            minA = Mathf.Min(minA, a);
            maxA = Mathf.Max(maxA, a);
            minB = Mathf.Min(minB, b);
            maxB = Mathf.Max(maxB, b);
            corners.Add(c);
            corners.Add(c + Vector2.right);
            corners.Add(c + Vector2.up);
            corners.Add(c + Vector2.one);
        }
        float depth = maxA - minA + 1f, width = maxB - minB + 1f;
        float solidity = sizes[biggest] / Mathf.Max(1f, HullArea(corners));

        // Protrusions: clumps of built cells that reach at least half as far from the original shore as the deepest cell.
        float[] fromLand = GridMath.DistanceTo(i => !originalWater[i], sizeX, sizeZ);
        float reach = 0f;
        for (int i = 0; i < n; i++)
        {
            if (built[i])
                reach = Mathf.Max(reach, fromLand[i]);
        }
        int protrusions = reach >= 4f ? GridMath.LabelRegions(i => built[i] && fromLand[i] >= 0.5f * reach, sizeX, sizeZ, out _, true) : 0;

        // Heights above the sea, pockets, islands, checkerboards.
        double sum = 0, sum2 = 0;
        float lo = float.MaxValue, hi = float.MinValue;
        int pockets = 0, islands = 0, checker = 0;
        for (int i = 0; i < n; i++)
        {
            int x = i % sizeX, z = i / sizeX;
            if (built[i])
            {
                float v = height[i] - seaLevel;
                sum += v;
                sum2 += v * v;
                lo = Mathf.Min(lo, v);
                hi = Mathf.Max(hi, v);
                if (x > 0 && z > 0 && x < sizeX - 1 && z < sizeZ - 1 && !built[i - 1] && !built[i + 1] && !built[i - sizeX] && !built[i + sizeX])
                    islands++;
            }
            else if (originalWater[i] && x > 0 && z > 0 && x < sizeX - 1 && z < sizeZ - 1 && built[i - 1] && built[i + 1] && built[i - sizeX] && built[i + sizeX])
                pockets++;
            if (x < sizeX - 1 && z < sizeZ - 1)
            {
                bool a = built[i], b = built[i + 1], c = built[i + sizeX], d = built[i + sizeX + 1];
                if ((a && d && !b && !c) || (b && c && !a && !d))
                    checker++;
            }
        }
        double mean = sum / cells, spread = System.Math.Sqrt(System.Math.Max(0.0, sum2 / cells - mean * mean));

        // Built land at the mouth: the lowest built cell within 4 cells of the mouth cell, against the mouth depth.
        string atMouth = "n/a";
        if (mouthCell >= 0)
        {
            float low = float.MaxValue;
            int mx = mouthCell % sizeX, mz = mouthCell / sizeX;
            for (int dz = -4; dz <= 4; dz++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = mx + dx, z = mz + dz;
                    if (x >= 0 && z >= 0 && x < sizeX && z < sizeZ && built[z * sizeX + x])
                        low = Mathf.Min(low, height[z * sizeX + x] - seaLevel);
                }
            }
            atMouth = low == float.MaxValue ? "no built land within 4 cells" : string.Format(inv, "lowest {0:0.00} m above the sea (mouth depth {1:0.00} m)", low, mouthDepth);
        }
        return string.Format(inv,
            "built land {0} cells ({1:P2} of the map) in {2} pieces, largest lobe {3} cells: depth {4:0.0} x width {5:0.0} cells (aspect {6:0.00}), solidity {7:0.00}, " +
            "{8} protrusions (reach {9:0.0} cells from the old shore), height above the sea mean {10:0.00} sd {11:0.00} range {12:0.00}..{13:0.00} m, " +
            "{14} one-cell pockets, {15} one-cell islands, {16} checkerboard blocks, at the mouth {17}, ledger closure {18}",
            cells, (float)cells / n, count, sizes[biggest], depth, width, depth / Mathf.Max(1f, width), solidity, protrusions, reach, mean, spread, lo, hi,
            pockets, islands, checker, atMouth, closure);
    }

    /// <summary>Area of the convex hull of the points (monotone chain).</summary>
    private static float HullArea(List<Vector2> points)
    {
        points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        var hull = new List<Vector2>();
        float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        foreach (int pass in new[] { 0, 1 })
        {
            int start = hull.Count;
            for (int k = 0; k < points.Count; k++)
            {
                Vector2 p = points[pass == 0 ? k : points.Count - 1 - k];
                while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
        }
        float area = 0f;
        for (int k = 0; k < hull.Count; k++)
        {
            Vector2 a = hull[k], b = hull[(k + 1) % hull.Count];
            area += a.x * b.y - b.x * a.y;
        }
        return Mathf.Abs(area) * 0.5f;
    }
}
