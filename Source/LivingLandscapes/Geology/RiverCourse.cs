using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// Finds the course of a world river across the map along the geological terrain: it enters where the valley floor
/// meets the upstream edge, then follows the steepest way down until it leaves the map.
/// </summary>
public static class RiverCourse
{
    private static readonly int[] DX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] DZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

    /// <summary>Fraction of the map width searched along the upstream edge for the valley floor.</summary>
    private const float EntrySearch = 0.25f;

    /// <summary>Douglas-Peucker tolerance (cells) when reducing the course to river segments.</summary>
    private const float SimplifyTolerance = 2f;

    /// <summary>Corner-cutting passes that round the course's bends before it is reduced to segments.</summary>
    private const int SmoothingPasses = 3;

    /// <summary>
    /// The river's width in cells, as vanilla would paint it: that of the river with the longest course through the
    /// tile. 0 if the tile has no river.
    /// </summary>
    public static float WidthOnMap(Tile tile)
    {
        if (!(tile is SurfaceTile surface) || surface.Rivers.NullOrEmpty())
            return 0f;
        return surface.Rivers.OrderBy(rl => -((SurfaceTile)Verse.Find.WorldGrid[rl.neighbor]).riverDist).First().river.widthOnMap;
    }

    /// <summary>
    /// The part of a planned course (map cells, upstream to downstream, may extend beyond the map) that crosses the
    /// map, simplified to river segments. Null if the plan is missing or doesn't cross the map.
    /// </summary>
    public static List<Vector2> FromPlan(List<Vector2> plan, int sizeX, int sizeZ)
    {
        if (plan == null || plan.Count < 2)
            return null;
        bool Inside(Vector2 v) => v.x >= 0f && v.y >= 0f && v.x <= sizeX && v.y <= sizeZ;
        int first = plan.FindIndex(Inside);
        int last = plan.FindLastIndex(Inside);
        if (first < 0 || last - first < 2)
            return null;
        // Keep one point either side so the river runs off the map edges.
        var crossing = plan.GetRange(Mathf.Max(first - 1, 0), Mathf.Min(last + 2, plan.Count) - Mathf.Max(first - 1, 0));
        return Simplify(Smooth(crossing, SmoothingPasses), SimplifyTolerance);
    }

    /// <summary>
    /// Returns the course as points from upstream to downstream (map cells), or null if no sensible course exists
    /// (the caller then falls back to vanilla's straight river).
    /// </summary>
    public static List<Vector2> Find(float[] height, int sizeX, int sizeZ, Vector2 flow)
    {
        var h = (float[])height.Clone();
        GridMath.BreachDepressions(h, sizeX, sizeZ, _ => false, 1e-4f, float.MaxValue);

        int entry = LowestEdgeCellToward(h, sizeX, sizeZ, -flow);
        var path = new List<Vector2>();
        int c = entry;
        var visited = new HashSet<int>();
        while (visited.Add(c))
        {
            int cx = c % sizeX, cz = c / sizeX;
            path.Add(new Vector2(cx + 0.5f, cz + 0.5f));
            bool onEdge = cx == 0 || cz == 0 || cx == sizeX - 1 || cz == sizeZ - 1;
            if (onEdge && path.Count > Mathf.Min(sizeX, sizeZ) / 4)
                break;

            int next = -1;
            float best = float.MaxValue;
            for (int d = 0; d < 8; d++)
            {
                int nx = cx + DX[d], nz = cz + DZ[d];
                if (nx < 0 || nz < 0 || nx >= sizeX || nz >= sizeZ)
                    continue;
                int n = nz * sizeX + nx;
                // Prefer going down; while still on the entry edge, step inward along the valley floor.
                float score = h[n] - (onEdge && path.Count < 3 ? Vector2.Dot(new Vector2(DX[d], DZ[d]), flow) * 1000f : 0f);
                if (score < best && !visited.Contains(n))
                {
                    best = score;
                    next = n;
                }
            }
            if (next < 0)
                return null;
            c = next;
        }

        // The river must leave on the far side, not curl back to where it came in.
        if (path.Count < 2 || Vector2.Dot(path[path.Count - 1] - path[0], flow) <= 0f)
            return null;
        return Simplify(path, SimplifyTolerance);
    }

    private static int LowestEdgeCellToward(float[] h, int sizeX, int sizeZ, Vector2 direction)
    {
        var centre = new Vector2(sizeX * 0.5f, sizeZ * 0.5f);
        float reach = Mathf.Max(sizeX, sizeZ);
        Vector2 target = centre + direction.normalized * reach;
        float window = EntrySearch * sizeX;
        int best = -1;
        float bestHeight = float.MaxValue;
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                if (x != 0 && z != 0 && x != sizeX - 1 && z != sizeZ - 1)
                    continue;
                var cell = new Vector2(x + 0.5f, z + 0.5f);
                // Edge cells facing the direction and within the search window of where its ray leaves the map.
                if (Vector2.Dot(cell - centre, direction) <= 0f)
                    continue;
                float offAxis = Mathf.Abs(Vector2.Dot(cell - centre, new Vector2(-direction.y, direction.x).normalized));
                if (offAxis > window && best >= 0)
                    continue;
                int i = z * sizeX + x;
                float score = h[i] + (offAxis > window ? 1e6f : 0f);
                if (score < bestHeight)
                {
                    bestHeight = score;
                    best = i;
                }
            }
        }
        return best >= 0 ? best : (sizeZ / 2) * sizeX + sizeX / 2;
    }

    /// <summary>Chaikin corner cutting: rounds bends while keeping both end points.</summary>
    private static List<Vector2> Smooth(List<Vector2> points, int passes)
    {
        for (int pass = 0; pass < passes && points.Count >= 3; pass++)
        {
            var next = new List<Vector2> { points[0] };
            for (int i = 0; i < points.Count - 1; i++)
            {
                next.Add(Vector2.Lerp(points[i], points[i + 1], 0.25f));
                next.Add(Vector2.Lerp(points[i], points[i + 1], 0.75f));
            }
            next.Add(points[points.Count - 1]);
            points = next;
        }
        return points;
    }

    private static List<Vector2> Simplify(List<Vector2> points, float tolerance)
    {
        if (points.Count < 3)
            return points;
        var keep = new bool[points.Count];
        keep[0] = keep[points.Count - 1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, points.Count - 1));
        while (stack.Count > 0)
        {
            (int a, int b) = stack.Pop();
            float maxDist = 0f;
            int index = -1;
            for (int i = a + 1; i < b; i++)
            {
                float d = DistanceToSegment(points[i], points[a], points[b]);
                if (d > maxDist)
                {
                    maxDist = d;
                    index = i;
                }
            }
            if (index >= 0 && maxDist > tolerance)
            {
                keep[index] = true;
                stack.Push((a, index));
                stack.Push((index, b));
            }
        }
        var result = new List<Vector2>();
        for (int i = 0; i < points.Count; i++)
        {
            if (keep[i])
                result.Add(points[i]);
        }
        return result;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return (p - (a + t * ab)).magnitude;
    }
}
