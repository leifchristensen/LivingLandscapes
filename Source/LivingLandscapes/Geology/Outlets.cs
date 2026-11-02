using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>
/// Where water may leave a grid, and at what level (P1 base level, #97, #101): one rule for the
/// erosion model and for the map's routings. Every cell has a base level B: NaN where water cannot leave, otherwise water
/// standing there leaves once its surface reaches B, so an outlet spills at max(ground, B).
/// <list type="bullet">
/// <item>The sea: sea level. The river's exit (and its mouth): always open.</item>
/// <item>The erosion model's region edge: max(Plane, Bed). Plane is the regional base level beyond it, the surface's own
/// regional trend continued outward from the most downhill edge (0 there). Bed applies inside a world river's valley: off-map
/// water there joins the river, so B is at least the highest bed of the course within the valley's half-width, and beside
/// an entry it rises on along the river's slope. An edge whose B stands above its ground is closed (it never ponds or is
/// raised); entries are closed.</item>
/// <item>The map's edge: the regional decision is the model's (ADR 0001, one-way handover). An edge cell is open, at its own
/// ground, where the model's water there flows on out of the map (<see cref="MapEdgesFromModel"/>), else closed.</item>
/// <item>Every connected land area keeps at least one outlet (<see cref="KeepOneOutlet"/>): a closed basin is P23's call
/// (#99), never an accident of the edges.</item>
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

    /// <summary>Closes the edge cells whose base level stands above their ground (water would have to pond to leave there).</summary>
    public static void CloseAboveGround(float[] baseLevel, float[] ground, int sizeX, int sizeZ, int keepOpen = -1)
    {
        for (int i = 0; i < baseLevel.Length; i++)
        {
            if (i != keepOpen && !float.IsNaN(baseLevel[i]) && baseLevel[i] > ground[i] && IsEdge(i, sizeX, sizeZ))
                baseLevel[i] = float.NaN;
        }
    }

    /// <summary>
    /// Every connected area of <paramref name="land"/> cells keeps an outlet: one with no outlet inside it or beside it opens
    /// its lowest edge cell at its ground. <paramref name="regions"/> and <paramref name="regionCount"/> are
    /// <see cref="GridMath.LabelRegions"/> of the land. Returns the number of cells opened.
    /// </summary>
    public static int KeepOneOutlet(float[] baseLevel, float[] ground, int sizeX, int sizeZ, int[] regions, int regionCount)
    {
        var drained = new bool[regionCount];
        var lowest = new int[regionCount];
        for (int r = 0; r < regionCount; r++)
            lowest[r] = -1;
        for (int i = 0; i < baseLevel.Length; i++)
        {
            int r = regions[i];
            if (r < 0)
                continue;
            if (!float.IsNaN(baseLevel[i]))
                drained[r] = true;
            int x = i % sizeX, z = i / sizeX;
            // Beside an outlet off the land (the sea).
            if ((x > 0 && regions[i - 1] < 0 && !float.IsNaN(baseLevel[i - 1])) || (x < sizeX - 1 && regions[i + 1] < 0 && !float.IsNaN(baseLevel[i + 1]))
                || (z > 0 && regions[i - sizeX] < 0 && !float.IsNaN(baseLevel[i - sizeX])) || (z < sizeZ - 1 && regions[i + sizeX] < 0 && !float.IsNaN(baseLevel[i + sizeX])))
                drained[r] = true;
            if (IsEdge(i, sizeX, sizeZ) && (lowest[r] < 0 || ground[i] < ground[lowest[r]]))
                lowest[r] = i;
        }
        int opened = 0;
        for (int r = 0; r < regionCount; r++)
        {
            if (drained[r] || lowest[r] < 0)
                continue;
            baseLevel[lowest[r]] = ground[lowest[r]];
            opened++;
        }
        return opened;
    }

    /// <summary>
    /// The map's edge from the erosion model's drainage (ADR 0001): a map edge cell is open where the model cell under it sends
    /// its water on outward, to a cell beyond the map's edge on that side; closed where the model's water there runs into the
    /// map (beside an entry, up the regional slope) or ends in an outlet. <paramref name="modelCell"/> gives the model cell
    /// under a map cell and <paramref name="toMap"/> a model cell's position in map cells.
    /// </summary>
    public static bool[] MapEdgesFromModel(int sizeX, int sizeZ, int[] receiver, System.Func<int, int, int> modelCell, System.Func<int, Vector2> toMap)
    {
        var open = new bool[sizeX * sizeZ];
        for (int i = 0; i < open.Length; i++)
        {
            if (!IsEdge(i, sizeX, sizeZ))
                continue;
            int x = i % sizeX, z = i / sizeX;
            int c = modelCell(x, z);
            int r = receiver[c];
            if (r < 0)
                continue;
            Vector2 at = toMap(c), to = toMap(r);
            var outward = new Vector2(x == 0 ? -1f : x == sizeX - 1 ? 1f : 0f, z == 0 ? -1f : z == sizeZ - 1 ? 1f : 0f);
            bool beyond = to.x < 0f || to.y < 0f || to.x > sizeX - 1 || to.y > sizeZ - 1;
            open[i] = beyond && Vector2.Dot(to - at, outward) > 0f;
        }
        return open;
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
