using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// Tuning of <see cref="Distributaries"/>, set with the sediment settings in XML. Modelled on vanilla's river delta
/// (<c>TileMutatorWorker_RiverDelta</c>): the river runs undivided to an apex, then splits again and again into branches that
/// fan toward the sea; branches whose ends come close merge.
/// </summary>
public class DistributarySettings
{
    public bool enabled = true;

    /// <summary>The apex lies this far from the sea along the river, as a share of the map's size (vanilla: 50-80% of the way across from the entry).</summary>
    public FloatRange apexDistance = new FloatRange(0.25f, 0.45f);

    /// <summary>Length (cells) of a branch segment before it splits again.</summary>
    public FloatRange segmentLength = new FloatRange(30f, 70f);

    /// <summary>Angle (degrees) each branch of a split turns away from the way to the sea.</summary>
    public FloatRange splitAngle = new FloatRange(15f, 30f);

    /// <summary>Chance a segment splits rather than running on as one channel.</summary>
    public float splitChance = 0.9f;

    /// <summary>The smaller branch of a split takes this share of the parent's width.</summary>
    public FloatRange widthShare = new FloatRange(0.33f, 0.67f);

    /// <summary>A branch ending this close (cells) to another branch's end joins it.</summary>
    public float mergeDistance = 25f;

    /// <summary>Branches narrower than this (cells) are not drawn.</summary>
    public float minWidth = 1.5f;

    /// <summary>Most splits along any one branch.</summary>
    public int maxDepth = 4;

    /// <summary>The trench of a branch lies this far (metres) below its water level.</summary>
    public float trenchDepth = 0.4f;
}

/// <summary>
/// Delta distributaries (issue #16; the physics version is issue #46): where a world river reaches the sea, branches are drawn
/// from an apex inland toward the sea as vanilla's delta draws them, and carved as trenches whose water level falls with the
/// distance to the sea, so routing divides the river between them and the water never climbs. Split points get the delta's
/// flow spreading so the water shares between the branches.
/// </summary>
public static class Distributaries
{
    private class Segment
    {
        public Vector2 start, end;
        public float width;
        public int depth;
    }

    /// <returns>Branch segments drawn.</returns>
    public static int Carve(float[] height, float[] filled, int[] receiver, float[] inflow, bool[] worldSource,
        float[] width, bool[] sea, float[] spread, float deltaSpread, int sizeX, int sizeZ, DistributarySettings p, out Vector2 apexAt,
        out List<(int at, int a, int b, float share)> splits)
    {
        apexAt = Vector2.zero;
        splits = new List<(int, int, int, float)>();
        if (!p.enabled || sea == null)
            return 0;
        int n = height.Length;
        // The main river: from the largest world entry, down the receivers, until it reaches the sea.
        int start = -1;
        for (int i = 0; i < n; i++)
        {
            if (worldSource[i] && (start < 0 || inflow[i] > inflow[start]))
                start = i;
        }
        if (start < 0)
            return 0;
        var path = new List<int>();
        for (int c = start, guard = 0; c >= 0 && guard < n; c = receiver[c], guard++)
        {
            path.Add(c);
            if (sea[c])
                break;
        }
        if (path.Count < 10 || !sea[path[path.Count - 1]])
            return 0;
        float[] toSea = GridMath.DistanceTo(i => sea[i], sizeX, sizeZ);
        float seaLevel = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            if (sea[i])
                seaLevel = Mathf.Max(seaLevel, filled[i]);
        }

        // The apex: where the path is the chosen distance from the sea (but not in its first fifth).
        float apexDistance = p.apexDistance.RandomInRange * Mathf.Max(sizeX, sizeZ);
        int apexIndex = path.Count - 1;
        for (int k = path.Count / 5; k < path.Count; k++)
        {
            if (toSea[path[k]] <= apexDistance)
            {
                apexIndex = k;
                break;
            }
        }
        int apex = path[apexIndex];
        if (toSea[apex] < 8f)
            return 0;
        var apexPoint = new Vector2(apex % sizeX + 0.5f, apex / sizeX + 0.5f);
        apexAt = apexPoint;
        float apexLevel = filled[apex];
        float apexToSea = toSea[apex];
        // The river's own direction at the apex, turned toward the sea.
        int back = path[Mathf.Max(0, apexIndex - 8)];
        Vector2 heading = (apexPoint - new Vector2(back % sizeX + 0.5f, back / sizeX + 0.5f)).normalized;
        Vector2 seaward = SeaDirection(toSea, apex, sizeX, sizeZ);
        if (heading == Vector2.zero)
            heading = seaward;
        float baseAngle = Mathf.Atan2((heading + seaward).y, (heading + seaward).x) * Mathf.Rad2Deg;

        var segments = new List<Segment>();
        var ends = new List<Vector2>();
        Split(new Segment { start = apexPoint, end = apexPoint, width = width[apex], depth = 0 }, baseAngle, sea, toSea, sizeX, sizeZ, p, segments, ends, splits);

        // Carve each segment: its water level falls with the distance to the sea, from the apex's level to the sea's.
        var trench = new float[n];
        for (int i = 0; i < n; i++)
            trench[i] = float.MaxValue;
        foreach (Segment s in segments)
        {
            float length = (s.end - s.start).magnitude;
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / 0.5f));
            float radius = s.width * 0.5f + 1f;
            float level = float.MaxValue;
            for (int k = 0; k <= steps; k++)
            {
                Vector2 v = Vector2.Lerp(s.start, s.end, k / (float)steps);
                int cx = Mathf.FloorToInt(v.x), cz = Mathf.FloorToInt(v.y);
                if (cx < 0 || cz < 0 || cx >= sizeX || cz >= sizeZ)
                    break;
                float share = Mathf.Clamp01(toSea[cz * sizeX + cx] / Mathf.Max(apexToSea, 1f));
                level = Mathf.Min(level, Mathf.Lerp(seaLevel, apexLevel, share));
                float bottom = level - p.trenchDepth;
                int reach = Mathf.CeilToInt(radius);
                for (int dz = -reach; dz <= reach; dz++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                            continue;
                        float d = new Vector2(x + 0.5f - v.x, z + 0.5f - v.y).magnitude;
                        if (d >= radius)
                            continue;
                        int i = z * sizeX + x;
                        trench[i] = Mathf.Min(trench[i], bottom + 0.15f * (d / radius) * (d / radius));
                    }
                }
            }
            // The water divides where a segment starts.
            int sx = Mathf.Clamp(Mathf.FloorToInt(s.start.x), 0, sizeX - 1), sz = Mathf.Clamp(Mathf.FloorToInt(s.start.y), 0, sizeZ - 1);
            for (int dz = -2; dz <= 2; dz++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = sx + dx, z = sz + dz;
                    if (x >= 0 && z >= 0 && x < sizeX && z < sizeZ)
                        spread[z * sizeX + x] = Mathf.Max(spread[z * sizeX + x], deltaSpread);
                }
            }
        }
        for (int i = 0; i < n; i++)
        {
            if (trench[i] < float.MaxValue && !sea[i])
            {
                height[i] = Mathf.Min(height[i], trench[i]);
                filled[i] = Mathf.Min(filled[i], trench[i]);
            }
        }
        return segments.Count;
    }

    /// <summary>One segment from the end of <paramref name="parent"/>, then its own splits, as vanilla's delta grows them.</summary>
    private static void Split(Segment parent, float baseAngle, bool[] sea, float[] toSea, int sizeX, int sizeZ, DistributarySettings p,
        List<Segment> segments, List<Vector2> ends, List<(int at, int a, int b, float share)> splits)
    {
        Vector2 at = parent.end;
        int ax = Mathf.FloorToInt(at.x), az = Mathf.FloorToInt(at.y);
        if (ax < 0 || az < 0 || ax >= sizeX || az >= sizeZ || sea[az * sizeX + ax] || parent.depth > p.maxDepth * 2)
            return;
        bool split = parent.depth < p.maxDepth && Rand.Chance(p.splitChance);
        if (!split)
        {
            Grow(at, baseAngle + p.splitAngle.RandomInRange - p.splitAngle.Average, parent.width, parent.depth + 1, baseAngle, sea, toSea, sizeX, sizeZ, p, segments, ends, splits);
            return;
        }
        float smaller = Mathf.Max(parent.width * p.widthShare.RandomInRange, p.minWidth);
        float larger = Mathf.Max(parent.width - smaller, p.minWidth) + 1f;
        float left = baseAngle - p.splitAngle.RandomInRange, right = baseAngle + p.splitAngle.RandomInRange;
        // Where the branches leave the split the water divides by their widths (routing alone splits only at right angles,
        // so the split is handed to it).
        int atCell = az * sizeX + ax;
        int a = FirstCell(at, left, sizeX, sizeZ, atCell), b = FirstCell(at, right, sizeX, sizeZ, atCell);
        if (a >= 0 && b >= 0 && a != b)
            splits.Add((atCell, a, b, larger / (smaller + larger)));
        Grow(at, left, smaller * 1.1f, parent.depth + 1, baseAngle, sea, toSea, sizeX, sizeZ, p, segments, ends, splits);
        Grow(at, right, larger * 1.1f, parent.depth + 1, baseAngle, sea, toSea, sizeX, sizeZ, p, segments, ends, splits);
    }

    /// <summary>The first neighbouring cell a branch leaving <paramref name="at"/> at this angle enters; -1 off the map.</summary>
    private static int FirstCell(Vector2 at, float angle, int sizeX, int sizeZ, int atCell)
    {
        float rad = angle * Mathf.Deg2Rad;
        for (float d = 1f; d <= 3f; d += 0.5f)
        {
            Vector2 v = at + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * d;
            int x = Mathf.FloorToInt(v.x), z = Mathf.FloorToInt(v.y);
            if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                return -1;
            int c = z * sizeX + x;
            if (c != atCell && Mathf.Abs(x - atCell % sizeX) <= 1 && Mathf.Abs(z - atCell / sizeX) <= 1)
                return c;
        }
        return -1;
    }

    private static void Grow(Vector2 from, float angle, float width, int depth, float baseAngle, bool[] sea, float[] toSea, int sizeX, int sizeZ,
        DistributarySettings p, List<Segment> segments, List<Vector2> ends, List<(int at, int a, int b, float share)> splits)
    {
        if (width < p.minWidth)
            return;
        float length = p.segmentLength.RandomInRange;
        float rad = angle * Mathf.Deg2Rad;
        var end = from + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * length;
        // Ends close to another branch's end join it (vanilla merges nodes within 25 cells): the branch stops there.
        foreach (Vector2 other in ends)
        {
            if ((end - other).magnitude < p.mergeDistance)
            {
                segments.Add(new Segment { start = from, end = other, width = width, depth = depth });
                return;
            }
        }
        ends.Add(end);
        var segment = new Segment { start = from, end = end, width = width, depth = depth };
        segments.Add(segment);
        // A segment that went no nearer the sea turns back toward it.
        int ex = Mathf.Clamp(Mathf.FloorToInt(end.x), 0, sizeX - 1), ez = Mathf.Clamp(Mathf.FloorToInt(end.y), 0, sizeZ - 1);
        int fx = Mathf.Clamp(Mathf.FloorToInt(from.x), 0, sizeX - 1), fz = Mathf.Clamp(Mathf.FloorToInt(from.y), 0, sizeZ - 1);
        float nextBase = toSea[ez * sizeX + ex] < toSea[fz * sizeX + fx] ? baseAngle
            : Mathf.Atan2(SeaDirection(toSea, ez * sizeX + ex, sizeX, sizeZ).y, SeaDirection(toSea, ez * sizeX + ex, sizeX, sizeZ).x) * Mathf.Rad2Deg;
        Split(segment, nextBase, sea, toSea, sizeX, sizeZ, p, segments, ends, splits);
    }

    /// <summary>The direction in which the distance to the sea falls fastest, from a few cells around.</summary>
    private static Vector2 SeaDirection(float[] toSea, int c, int sizeX, int sizeZ)
    {
        int x = c % sizeX, z = c / sizeX, r = 4;
        float Sample(int sx, int sz) => toSea[Mathf.Clamp(sz, 0, sizeZ - 1) * sizeX + Mathf.Clamp(sx, 0, sizeX - 1)];
        var gradient = new Vector2(Sample(x + r, z) - Sample(x - r, z), Sample(x, z + r) - Sample(x, z - r));
        return gradient.sqrMagnitude > 1e-6f ? -gradient.normalized : Vector2.down;
    }
}
