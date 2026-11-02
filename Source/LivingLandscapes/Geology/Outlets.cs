using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>
/// Where water may leave a grid, and at what level (P1 base level, #97; tools/Docs/LAYER-UNIFICATION.md): one rule for the
/// erosion model and for the map's routings. Every cell has a base level B: NaN where water cannot leave, otherwise water
/// standing there leaves once its surface reaches B, so an outlet spills at max(ground, B) (never pinned upward).
/// <list type="bullet">
/// <item>The sea: sea level. The river's exit: its water surface (it always drains).</item>
/// <item>An edge cell: max(Plane, Bed). Plane is the regional base level beyond it, the surface's own regional trend
/// continued outward from the most downhill edge (base0 there). Bed applies inside a world river's valley: off-map water
/// there joins the river, so B is at least the highest bed of the course within the valley's half-width, and beside an
/// entry it rises on along the river's slope (the river beyond the entry is upstream of it).</item>
/// <item>A base level above the highest ground can never be reached: NaN (closed). A world river's entry: NaN.</item>
/// </list>
/// Static and array-level: the erosion model calls these every iteration (ADR 0001).
/// </summary>
public static class Outlets
{
    public static bool IsEdge(int i, int sizeX, int sizeZ)
    {
        int x = i % sizeX, z = i / sizeX;
        return x == 0 || z == 0 || x == sizeX - 1 || z == sizeZ - 1;
    }

    /// <summary>
    /// For each edge cell, how far it lies up the regional slope from the most downhill edge point, in cells:
    /// <c>d_max - d</c> with d its offset from the centre along the unit <paramref name="downhill"/>. 0 everywhere on level ground,
    /// NaN off the edge. Plane = base0 + offset x rise.
    /// </summary>
    public static float[] EdgeOffsets(int sizeX, int sizeZ, Vector2 downhill)
    {
        int n = sizeX * sizeZ;
        var offset = new float[n];
        downhill = downhill.normalized;
        var centre = new Vector2((sizeX - 1) * 0.5f, (sizeZ - 1) * 0.5f);
        float dMax = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            if (IsEdge(i, sizeX, sizeZ))
                dMax = Mathf.Max(dMax, Vector2.Dot(new Vector2(i % sizeX, i / sizeX) - centre, downhill));
        }
        for (int i = 0; i < n; i++)
            offset[i] = IsEdge(i, sizeX, sizeZ) ? dMax - Vector2.Dot(new Vector2(i % sizeX, i / sizeX) - centre, downhill) : float.NaN;
        return offset;
    }

    /// <summary>
    /// The surface's regional rise per cell up the slope: the least-squares slope of <paramref name="surface"/> against the
    /// offset along <paramref name="downhill"/>, over the cells <paramref name="counts"/> accepts; 0 if it falls uphill or
    /// the ground is level. Both passes take it from their own surface (the model's comes from its uplift trend, the map's
    /// also holds the tilt), so the plane beyond the edge continues the land the water is on.
    /// </summary>
    public static float RegionalRise(float[] surface, int sizeX, int sizeZ, Vector2 downhill, System.Func<int, bool> counts = null)
    {
        if (downhill == Vector2.zero)
            return 0f;
        Vector2 up = -downhill.normalized;
        double sd = 0, sh = 0, sdd = 0, sdh = 0;
        int count = 0;
        for (int i = 0; i < surface.Length; i++)
        {
            if (counts != null && !counts(i))
                continue;
            double d = Vector2.Dot(new Vector2(i % sizeX, i / sizeX), up);
            sd += d;
            sh += surface[i];
            sdd += d * d;
            sdh += d * surface[i];
            count++;
        }
        double var = count * sdd - sd * sd;
        if (count < 2 || var <= 0)
            return 0f;
        return Mathf.Max(0f, (float)((count * sdh - sd * sh) / var));
    }

    /// <summary>
    /// The edge cells of the side(s) facing most downhill (all edges on level ground): where base0 is measured on the map.
    /// </summary>
    public static bool OnDownhillSide(int i, int sizeX, int sizeZ, Vector2 downhill)
    {
        if (!IsEdge(i, sizeX, sizeZ))
            return false;
        if (downhill == Vector2.zero)
            return true;
        float best = Mathf.Max(Mathf.Max(-downhill.x, downhill.x), Mathf.Max(-downhill.y, downhill.y)) - 1e-4f;
        int x = i % sizeX, z = i / sizeX;
        return (x == 0 && -downhill.x >= best) || (x == sizeX - 1 && downhill.x >= best)
            || (z == 0 && -downhill.y >= best) || (z == sizeZ - 1 && downhill.y >= best);
    }

    /// <summary>
    /// A world river's valley along the edge: for each edge cell within <paramref name="halfWidth"/> cells of the course,
    /// the course cells within that half-width (null elsewhere), and its distance to the nearest entry within it
    /// (NaN if none).
    /// </summary>
    public static int[][] Valleys(int sizeX, int sizeZ, IList<int> course, IList<int> entries, float halfWidth, out float[] toEntry)
    {
        int n = sizeX * sizeZ;
        var valley = new int[n][];
        toEntry = new float[n];
        float reach = halfWidth * halfWidth;
        float Distance2(int a, int b)
        {
            float dx = a % sizeX - b % sizeX, dz = a / sizeX - b / sizeX;
            return dx * dx + dz * dz;
        }
        for (int i = 0; i < n; i++)
        {
            toEntry[i] = float.NaN;
            if (!IsEdge(i, sizeX, sizeZ))
                continue;
            List<int> near = null;
            foreach (int c in course)
            {
                if (Distance2(i, c) <= reach)
                    (near ??= new List<int>()).Add(c);
            }
            valley[i] = near?.ToArray();
            foreach (int e in entries)
            {
                float d2 = Distance2(i, e);
                if (d2 <= reach && !(Mathf.Sqrt(d2) >= toEntry[i]))
                    toEntry[i] = Mathf.Sqrt(d2);
            }
        }
        return valley;
    }

    /// <summary>
    /// Base levels of the edge cells: max(base0 + offset x rise, Bed), never below <paramref name="floor"/> (sea level where
    /// there is a sea), NaN above <paramref name="ceiling"/>. Bed is the highest <paramref name="level"/> of the valley's
    /// course cells, and beside an entry at least the entry's level plus <paramref name="riverSlope"/> x the distance to it.
    /// The exit keeps <paramref name="exitLevel"/> and entries are closed. Cells already given a base level (the sea) and cells
    /// off the edge are left as they are.
    /// </summary>
    public static void EdgeLevels(float[] baseLevel, float[] offset, float base0, float rise, int[][] valley, float[] toEntry,
        float[] level, float entryLevel, float riverSlope, float floor, float ceiling, int exit, float exitLevel, IList<int> entries)
    {
        for (int i = 0; i < baseLevel.Length; i++)
        {
            if (float.IsNaN(offset[i]) || !float.IsNaN(baseLevel[i]))
                continue;
            float b = Mathf.Max(floor, base0 + offset[i] * rise);
            if (valley?[i] != null)
            {
                foreach (int c in valley[i])
                    b = Mathf.Max(b, level[c]);
                if (!float.IsNaN(toEntry[i]))
                    b = Mathf.Max(b, entryLevel + riverSlope * toEntry[i]);
            }
            baseLevel[i] = b > ceiling ? float.NaN : b;
        }
        if (exit >= 0)
            baseLevel[exit] = exitLevel;
        foreach (int e in entries)
            baseLevel[e] = float.NaN;
    }

    /// <summary>Whether a cell is an outlet (water can leave it).</summary>
    public static bool IsOutlet(float[] baseLevel, int i) => !float.IsNaN(baseLevel[i]);

    /// <summary>
    /// Fills a surface to be routed toward its outlets: each outlet seeds the fill at its spill level max(ground, B), so
    /// water ponds up to B before it leaves (<see cref="GridMath.FillDepressions"/> from the outlets only).
    /// </summary>
    public static void Fill(float[] surface, int sizeX, int sizeZ, float[] baseLevel, float epsilon)
    {
        for (int i = 0; i < surface.Length; i++)
        {
            if (!float.IsNaN(baseLevel[i]))
                surface[i] = Mathf.Max(surface[i], baseLevel[i]);
        }
        GridMath.FillDepressions(surface, sizeX, sizeZ, i => !float.IsNaN(baseLevel[i]), epsilon, includeEdges: false);
    }

    /// <summary>Per side (west, east, south, north): edge cells that drain, and the mean of (B - ground) over them (diagnostic).</summary>
    public static string EdgeSummary(float[] baseLevel, float[] ground, int sizeX, int sizeZ)
    {
        var parts = new string[4];
        string[] names = { "W", "E", "S", "N" };
        for (int side = 0; side < 4; side++)
        {
            int open = 0, total = 0;
            double above = 0;
            int length = side < 2 ? sizeZ : sizeX;
            for (int k = 0; k < length; k++)
            {
                int i = side switch { 0 => k * sizeX, 1 => k * sizeX + sizeX - 1, 2 => k, _ => (sizeZ - 1) * sizeX + k };
                total++;
                if (float.IsNaN(baseLevel[i]))
                    continue;
                open++;
                above += baseLevel[i] - ground[i];
            }
            parts[side] = $"{names[side]} {open}/{total}" + (open > 0 ? $" (B-ground {above / open:F2})" : "");
        }
        return string.Join(", ", parts);
    }
}
