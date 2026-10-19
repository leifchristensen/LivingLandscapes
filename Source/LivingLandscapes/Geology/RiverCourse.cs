using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// The course of a world river across the map: the channel the erosion model carved for it, cut to the map and
/// smoothed into river segments, and the width vanilla paints it with.
/// </summary>
public static class RiverCourse
{
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
