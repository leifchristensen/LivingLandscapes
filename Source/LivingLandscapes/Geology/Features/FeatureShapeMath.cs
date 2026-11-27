using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>The kinds of fuzzy region (FEATURE-REGIONS.md 2.2). Every kind has a core and the same smooth falloff round it.</summary>
public enum ShapeKind
{
    /// <summary>A corridor along a half-line from an origin, with a round end there (a river valley from where rivers meet to the map edge).</summary>
    Path,

    /// <summary>A round area with a flat core (basin, plateau, dome). Size is its radius.</summary>
    Area,

    /// <summary>A strip along a straight line (a valley floor), or with <see cref="BandSide.Inside"/> a front: one side of a line (mountain front, escarpment).</summary>
    Band,

    /// <summary>A peak with no flat core: weight 1 at the point, falling smoothly to 0 at size (a spring, an island knob).</summary>
    Point,

    /// <summary>A band round a circle of the shape's radius (a crater rim, three-sided cliffs). Size is the band's half-width.</summary>
    Ring,

    /// <summary>Many points within the shape's radius (cenotes, springs, icebergs); the region's weight is the strongest point's.</summary>
    Scatter,
}

/// <summary>Which side of its line a band covers.</summary>
public enum BandSide
{
    /// <summary>A strip on both sides of the line, size its half-width; the line runs along the orientation.</summary>
    Both,

    /// <summary>A front: weight 1 on the side the orientation points to, rising across a transition of half-width size.</summary>
    Inside,
}

/// <summary>One region's shape, resolved to a direction and origin on one map: what its weight is computed from.</summary>
public class RegionShape
{
    public ShapeKind kind;
    public BandSide side;

    /// <summary>Orientation (unit vector or zero) and origin, in map-width fractions from the map centre.</summary>
    public Vector2 direction;
    public Vector2 origin;

    public float offset;
    public float size;
    public float edge;
    public float warp;
    public float radius;

    /// <summary>A scatter's points, relative to the origin; null for other kinds.</summary>
    public List<Vector2> points;

    public float WeightAt(Vector2 q) => FeatureShapeMath.Weight(kind, side, direction, q, offset, size, edge, radius, points);
}

/// <summary>
/// Game-free maths of a region's weight: 0 outside, 1 in its core, smooth across its edge. Positions are fractions of the
/// map width from the region's origin, after the domain warp that keeps outlines irregular.
/// </summary>
public static class FeatureShapeMath
{
    /// <summary>
    /// A map position in map-width fractions from the map centre, the units regions are laid in. Both axes divide by the
    /// width, so shapes keep their proportions on a map that is not square.
    /// </summary>
    public static Vector2 MapFraction(float mx, float mz, int sizeX, int sizeZ) =>
        new Vector2((mx - sizeX * 0.5f) / sizeX, (mz - sizeZ * 0.5f) / sizeX);

    /// <summary>
    /// A region's weight at the map fraction <paramref name="p"/>, with the domain warp there passed in: each region moves
    /// the position by its own share of that warp before it reads its shape.
    /// </summary>
    public static float WarpedWeight(RegionShape region, Vector2 p, Vector2 warp) =>
        region.WeightAt(p + warp * region.warp - region.origin);

    /// <summary>
    /// The most weight a shape can reach anywhere. A shape with a falloff on both sides of its core peaks at the falloff's
    /// value at distance 0, which is below 1 when the edge is wider than the size; the other kinds reach 1.
    /// </summary>
    public static float PeakWeight(ShapeKind kind, BandSide side, float size, float edge)
    {
        bool falloff = kind == ShapeKind.Path || kind == ShapeKind.Area || kind == ShapeKind.Ring ||
            (kind == ShapeKind.Band && side == BandSide.Both);
        return falloff ? Falloff(0f, size, Mathf.Max(edge, 1e-4f)) : 1f;
    }

    /// <summary>A weight as a share of its shape's peak, 0..1: how strongly the region acts here compared with its strongest.</summary>
    public static float RelativeWeight(float weight, float peak) => peak > 0f ? Mathf.Clamp01(weight / peak) : 0f;

    /// <summary>1 within <paramref name="size"/> - <paramref name="edge"/> of the core, 0 beyond size + edge, smooth between.</summary>
    public static float Falloff(float distance, float size, float edge) =>
        1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(size - edge, size + edge, distance));

    /// <summary>A point's weight: 1 at the point, falling smoothly to 0 at <paramref name="reach"/>.</summary>
    public static float PointWeight(float distance, float reach) =>
        1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, Mathf.Max(reach, 1e-4f), distance));

    /// <summary>
    /// The weight of a region at <paramref name="q"/>, 0..1.
    /// </summary>
    /// <param name="direction">The region's orientation, a unit vector (zero for a region centred on its origin).</param>
    /// <param name="offset">Shift of the core along the orientation (sideways for a two-sided band).</param>
    /// <param name="size">Half-width, radius or reach, as each kind says.</param>
    /// <param name="edge">Half-width of the falloff.</param>
    /// <param name="radius">The ring's radius, or the radius a scatter's points lie within.</param>
    /// <param name="points">A scatter's points, relative to the origin.</param>
    public static float Weight(ShapeKind kind, BandSide side, Vector2 direction, Vector2 q, float offset, float size, float edge,
        float radius = 0f, IList<Vector2> points = null)
    {
        edge = Mathf.Max(edge, 1e-4f);
        switch (kind)
        {
            case ShapeKind.Band:
            {
                if (side == BandSide.Inside)
                {
                    float across = Vector2.Dot(q, direction) - offset;
                    return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-size - edge, size + edge, across));
                }
                var normal = new Vector2(-direction.y, direction.x);
                return Falloff(Mathf.Abs(Vector2.Dot(q, normal) - offset), size, edge);
            }
            case ShapeKind.Path:
            {
                float along = Vector2.Dot(q, direction);
                float distance = along < 0f ? q.magnitude : Mathf.Abs(Vector2.Dot(q, new Vector2(-direction.y, direction.x)));
                return Falloff(distance, size, edge);
            }
            case ShapeKind.Point:
                return PointWeight((q - direction * offset).magnitude, size);
            case ShapeKind.Ring:
                return Falloff(Mathf.Abs((q - direction * offset).magnitude - radius), size, edge);
            case ShapeKind.Scatter:
            {
                if (points == null)
                    return 0f;
                Vector2 c = q - direction * offset;
                float strongest = 0f;
                foreach (Vector2 p in points)
                    strongest = Mathf.Max(strongest, PointWeight((c - p).magnitude, size));
                return strongest;
            }
            default:
                return Falloff((q - direction * offset).magnitude, size, edge);
        }
    }
}
