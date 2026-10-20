using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// Helpers for a world river's course: the width vanilla paints it with and reducing a polyline to river segments.
/// </summary>
public static class RiverCourse
{
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

    /// <summary>Douglas-Peucker: the polyline reduced to the points that keep it within <paramref name="tolerance"/> cells.</summary>
    public static List<Vector2> Simplify(List<Vector2> points, float tolerance)
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
