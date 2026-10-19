using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>Channel pattern settings (part of <see cref="GeologyGenerationExtension"/>). Lengths in map cells.</summary>
public class RiverChannelSettings
{
    /// <summary>Spacing of the centre line's points while the channel is shaped.</summary>
    public float spacing = 2f;

    /// <summary>
    /// Meander migration steps (Howard-Knutson). Each step moves the bank outward at bends, weighted by the curvature
    /// upstream, so bends grow and travel downstream.
    /// </summary>
    public int meanderSteps = 500;
    public float migrationRate = 4f;
    public float localWeight = -1f;
    public float upstreamWeight = 2.5f;

    /// <summary>
    /// Upstream curvature fades over this many channel widths. Bends grow only when this is short against their
    /// length (about a tenth of the wavelength), so keep it under one.
    /// </summary>
    public float decayWidths = 0.6f;

    /// <summary>
    /// Bends travel downstream as they grow. The course is extended this many map widths upstream of the map, so bends
    /// that formed upstream keep arriving instead of the river straightening where it enters.
    /// </summary>
    public float upstreamMaps = 2f;

    /// <summary>Lateral wobble (cells) that seeds bends on a straight reach.</summary>
    public float seedWobble = 3f;
    public float seedWavelengthWidths = 10f;

    /// <summary>A bend whose neck closes to this many channel widths is cut off; the loop is left as an oxbow.</summary>
    public float cutoffWidths = 1.7f;

    /// <summary>
    /// Ground this much higher (metres) than the valley floor stops the channel moving sideways: the river wanders
    /// only over its floodplain, and valley walls confine it.
    /// </summary>
    public float wallHeight = 1.5f;

    /// <summary>Bank erodibility is (1 - rock strength) raised to this power: strong rock barely moves.</summary>
    public float bankStrengthExponent = 1f;

    /// <summary>
    /// Splitting (anabranching): the river drops sediment as bars and divides around them where it can no longer carry
    /// its load. Its carrying capacity goes with slope over valley floor width (ground within <see cref="floodDepth"/>
    /// metres of the channel): where the floor is wider than <see cref="splitConfinement"/> channel widths and the
    /// capacity has fallen to <see cref="depositionRatio"/> of its best over the last <see cref="upstreamWidths"/>
    /// channel widths upstream (the valley opens out, or the gradient eases toward the sea), the river splits.
    /// </summary>
    public float splitConfinement = 6f;
    public float depositionRatio = 0.5f;
    public float upstreamWidths = 10f;
    public float floodDepth = 1.5f;

    /// <summary>Slopes below this count as this (no reach is truly level), so noise on flat ground does not split rivers.</summary>
    public float minSlope = 0.0005f;

    /// <summary>Island length in channel widths (at least <see cref="minIslandLength"/> cells), and the width each branch keeps.</summary>
    public float islandWidths = 3f;
    public float minIslandLength = 24f;
    public float branchWidthShare = 0.6f;
}

/// <summary>A river's channels across the map: the main channel, side branches around islands, and cut-off loops.</summary>
public class RiverPlan
{
    /// <summary>Centre line of the main channel, upstream to downstream (map cells; ends may lie beyond the map).</summary>
    public List<Vector2> Main = new List<Vector2>();

    /// <summary>Channel width at each main point (narrower where a side branch takes part of the flow).</summary>
    public List<float> MainWidth = new List<float>();

    public List<SideBranch> Sides = new List<SideBranch>();

    /// <summary>Abandoned loops (oxbows) left by cutoffs; the lake model decides whether they hold water.</summary>
    public List<List<Vector2>> Oxbows = new List<List<Vector2>>();

    public float NominalWidth;

    /// <summary>Water surface height in each cell of a live channel (NaN elsewhere), set when the channels are carved.</summary>
    public float[] WaterLevel;

    public class SideBranch
    {
        public List<Vector2> Points = new List<Vector2>();
        public float Width;

        /// <summary>Indices on <see cref="Main"/> where the branch leaves and rejoins.</summary>
        public int From, To;
    }
}

/// <summary>
/// Channel patterns (rules W4, W5). One process shapes every river: meander migration, where bends erode their outer
/// bank. Bank erodibility comes from the rock and the valley walls, so a river in rock or in a narrow valley stays a
/// single, nearly straight channel, and one on an open plain of weak ground winds and cuts off loops. Bends scale with
/// the channel width vanilla gives the river (creek, river, large river), so great rivers bend gently and creeks
/// tightly. Where the river can no longer carry its sediment it divides around islands.
/// </summary>
public static class RiverChannels
{
    public static RiverPlan Plan(float[] height, float[] rockStrength, int sizeX, int sizeZ, List<Vector2> course, float width, RiverChannelSettings p)
    {
        var plan = new RiverPlan { NominalWidth = width };
        if (course == null || course.Count < 2)
            return plan;
        float scale = width;
        bool Inside(Vector2 v) => v.x >= 0f && v.y >= 0f && v.x < sizeX && v.y < sizeZ;
        float BankErodibility(Vector2 v)
        {
            float strength = rockStrength == null ? 0.5f : rockStrength[Index(v, sizeX, sizeZ)];
            return Mathf.Pow(Mathf.Clamp01(1f - strength), p.bankStrengthExponent);
        }

        // Beyond the map the ground is unknown: it erodes like the course's average inside the map.
        float sum = 0f;
        int count = 0;
        foreach (Vector2 v in course)
        {
            if (!Inside(v))
                continue;
            sum += BankErodibility(v);
            count++;
        }
        float outside = count > 0 ? sum / count : 0.25f;
        float Erodibility(Vector2 v) => Inside(v) ? BankErodibility(v) : outside;
        float[] floor = ValleyFloor(height, sizeX, sizeZ, course);
        float AboveFloor(Vector2 v) => height[Index(v, sizeX, sizeZ)] - floor[Index(v, sizeX, sizeZ)];

        var extended = new List<Vector2>(course);
        Vector2 back = (course[0] - course[1]).normalized;
        extended.Insert(0, course[0] + back * p.upstreamMaps * Mathf.Max(sizeX, sizeZ));
        List<Vector2> points = Resample(extended, p.spacing);

        Seed(points, scale, p, Erodibility);
        for (int step = 0; step < p.meanderSteps; step++)
        {
            Migrate(points, scale, p, Erodibility, AboveFloor, Inside);
            points = Resample(points, p.spacing);
            if (step % 4 == 0)
                CutOff(points, Mathf.Max(width, scale) * p.cutoffWidths, plan.Oxbows);
        }

        // Keep the part that crosses the map, with one point beyond each edge.
        int first = points.FindIndex(v => Inside(v)), last = points.FindLastIndex(v => Inside(v));
        if (first < 0)
            return plan;
        first = Mathf.Max(0, first - 1);
        last = Mathf.Min(points.Count - 1, last + 1);
        points = points.GetRange(first, last - first + 1);
        plan.Oxbows.RemoveAll(loop => !loop.Exists(v => Inside(v)));

        plan.Main = points;
        foreach (Vector2 _ in points)
            plan.MainWidth.Add(width);
        Split(plan, height, sizeX, sizeZ, width, p, Erodibility);
        return plan;
    }

    /// <summary>A gentle wobble along the course, scaled by bank erodibility, from which bends can grow.</summary>
    private static void Seed(List<Vector2> points, float scale, RiverChannelSettings p, System.Func<Vector2, float> erodibility)
    {
        var noise = new Perlin(1.0 / (p.seedWavelengthWidths * scale), 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);
        float along = 0f;
        var moved = new Vector2[points.Count];
        for (int i = 1; i < points.Count - 1; i++)
        {
            along += (points[i] - points[i - 1]).magnitude;
            Vector2 t = (points[i + 1] - points[i - 1]).normalized;
            moved[i] = new Vector2(-t.y, t.x) * (float)noise.GetValue(along, 0.0, 0.0) * p.seedWobble * 2f * erodibility(points[i]);
        }
        for (int i = 1; i < points.Count - 1; i++)
            points[i] += moved[i];
    }

    /// <summary>One migration step: each point moves toward the outside of its bend, by local and upstream curvature.</summary>
    private static void Migrate(List<Vector2> points, float scale, RiverChannelSettings p,
        System.Func<Vector2, float> erodibility, System.Func<Vector2, float> aboveFloor, System.Func<Vector2, bool> inside)
    {
        int n = points.Count;
        if (n < 5)
            return;
        var curvature = new float[n];
        for (int i = 1; i < n - 1; i++)
        {
            Vector2 a = points[i] - points[i - 1], b = points[i + 1] - points[i];
            float turn = Mathf.Atan2(a.x * b.y - a.y * b.x, Vector2.Dot(a, b));
            curvature[i] = turn / Mathf.Max(0.5f * (a.magnitude + b.magnitude), 1e-3f);
        }
        for (int pass = 0; pass < 2; pass++)
        {
            var smooth = (float[])curvature.Clone();
            for (int i = 1; i < n - 1; i++)
                smooth[i] = 0.25f * curvature[i - 1] + 0.5f * curvature[i] + 0.25f * curvature[i + 1];
            curvature = smooth;
        }

        float decay = p.decayWidths * scale;
        int reach = Mathf.CeilToInt(4f * decay / p.spacing);
        var move = new Vector2[n];
        for (int i = 2; i < n - 2; i++)
        {
            float upstream = 0f, weights = 0f;
            for (int k = 1; k <= reach && i - k >= 0; k++)
            {
                float w = Mathf.Exp(-k * p.spacing / decay);
                upstream += curvature[i - k] * w;
                weights += w;
            }
            float m = p.localWeight * curvature[i] + p.upstreamWeight * (weights > 0f ? upstream / weights : 0f);
            Vector2 t = (points[i + 1] - points[i - 1]).normalized;
            var normal = new Vector2(-t.y, t.x);
            // Positive curvature turns left (centre on the normal's side): the outer bank lies opposite the normal.
            float distance = Mathf.Clamp(p.migrationRate * m * scale, -0.5f, 0.5f) * erodibility(points[i]);
            Vector2 target = points[i] - normal * distance;
            // Valley walls: the channel moves over its floodplain, not up ground well above the valley floor.
            if (inside(target))
                distance *= Mathf.Clamp01(1f - aboveFloor(target) / p.wallHeight);
            move[i] = -normal * distance;
        }
        for (int i = 2; i < n - 2; i++)
            points[i] += move[i];
    }

    /// <summary>Where a bend's neck closes, the river takes the short way; the loop is kept as an oxbow.</summary>
    private static void CutOff(List<Vector2> points, float neck, List<List<Vector2>> oxbows)
    {
        int minApart = Mathf.CeilToInt(4f * neck / 2f) + 2;
        for (int i = 0; i < points.Count; i++)
        {
            for (int j = points.Count - 1; j >= i + minApart; j--)
            {
                if ((points[i] - points[j]).sqrMagnitude >= neck * neck)
                    continue;
                oxbows.Add(points.GetRange(i + 1, j - i - 1));
                points.RemoveRange(i + 1, j - i - 1);
                break;
            }
        }
    }

    /// <summary>
    /// Islands: where the river deposits its load on a wide floor of weak ground, the main channel narrows and a side
    /// branch takes part of the flow, bowing out toward the side with more room and rejoining downstream.
    /// </summary>
    private static void Split(RiverPlan plan, float[] height, int sizeX, int sizeZ, float width, RiverChannelSettings p,
        System.Func<Vector2, float> erodibility)
    {
        List<Vector2> pts = plan.Main;
        int n = pts.Count;
        var left = new float[n];
        var right = new float[n];
        var slope = new float[n];
        // The ground along the channel and the slope, both over two channel widths either way, so the terrain's small
        // bumps don't count.
        int window = Mathf.Max(2, Mathf.CeilToInt(Mathf.Max(10f, 2f * width) / p.spacing));
        var ground = new float[n];
        for (int i = 0; i < n; i++)
            ground[i] = height[Index(pts[i], sizeX, sizeZ)];
        ground = MovingAverage(ground, window);
        float maxScan = 4f * width + 20f;
        for (int i = 1; i < n - 1; i++)
        {
            Vector2 t = (pts[i + 1] - pts[i - 1]).normalized;
            var normal = new Vector2(-t.y, t.x);
            float floor = height[Index(pts[i], sizeX, sizeZ)] + p.floodDepth;
            left[i] = Room(pts[i], normal, floor, maxScan, height, sizeX, sizeZ);
            right[i] = Room(pts[i], -normal, floor, maxScan, height, sizeX, sizeZ);
            int a = Mathf.Max(0, i - window), b = Mathf.Min(n - 1, i + window);
            float run = (b - a) * p.spacing;
            slope[i] = (ground[a] - ground[b]) / Mathf.Max(run, 1f);
        }

        // Carrying capacity per point, and its best value over the reach upstream.
        var capacity = new float[n];
        for (int i = 0; i < n; i++)
            capacity[i] = Mathf.Max(slope[i], p.minSlope) / Mathf.Max(left[i] + right[i], width);
        capacity = MovingAverage(capacity, Mathf.CeilToInt(width / p.spacing));
        int upstreamPoints = Mathf.CeilToInt(p.upstreamWidths * width / p.spacing);
        var upstreamBest = new float[n];
        for (int i = 1; i < n; i++)
        {
            for (int k = Mathf.Max(1, i - upstreamPoints); k < i; k++)
                upstreamBest[i] = Mathf.Max(upstreamBest[i], capacity[k]);
        }

        bool Splits(int i)
        {
            Vector2 v = pts[i];
            bool inside = v.x > width && v.y > width && v.x < sizeX - width && v.y < sizeZ - width;
            bool depositing = capacity[i] <= p.depositionRatio * upstreamBest[i];
            return inside && (left[i] + right[i]) / width >= p.splitConfinement && depositing && erodibility(v) >= 0.2f;
        }

        float islandLength = Mathf.Max(p.minIslandLength, p.islandWidths * width);
        int islandPoints = Mathf.CeilToInt(islandLength / p.spacing);
        float branchWidth = width * p.branchWidthShare;
        // An island starts where the river begins to deposit and runs its own length; the next may start after it.
        for (int from = 1; from + islandPoints < n - 1; from++)
        {
            if (!Splits(from))
                continue;
            int to = from + islandPoints;
            float leftRoom = float.MaxValue, rightRoom = float.MaxValue;
            for (int k = from; k <= to; k++)
            {
                leftRoom = Mathf.Min(leftRoom, left[k]);
                rightRoom = Mathf.Min(rightRoom, right[k]);
            }
            float side = leftRoom >= rightRoom ? 1f : -1f;
            float room = Mathf.Max(leftRoom, rightRoom);
            float offset = Mathf.Min(room - branchWidth, 2f * width);
            if (offset < branchWidth + 3f)
                continue;
            var branch = new RiverPlan.SideBranch { Width = branchWidth, From = from, To = to };
            for (int k = from; k <= to; k++)
            {
                float f = (k - from) / (float)(to - from);
                Vector2 t = (pts[Mathf.Min(k + 1, n - 1)] - pts[Mathf.Max(k - 1, 0)]).normalized;
                var normal = new Vector2(-t.y, t.x);
                branch.Points.Add(pts[k] + normal * side * offset * Mathf.Pow(Mathf.Sin(Mathf.PI * f), 0.6f));
                plan.MainWidth[k] = branchWidth;
            }
            plan.Sides.Add(branch);
            from = to + islandPoints / 2;
        }
    }

    /// <summary>Mean over <paramref name="radius"/> points either way (fewer at the ends).</summary>
    private static float[] MovingAverage(float[] values, int radius)
    {
        int n = values.Length;
        var result = new float[n];
        for (int i = 0; i < n; i++)
        {
            int a = Mathf.Max(0, i - radius), b = Mathf.Min(n - 1, i + radius);
            float sum = 0f;
            for (int k = a; k <= b; k++)
                sum += values[k];
            result[i] = sum / (b - a + 1);
        }
        return result;
    }

    /// <summary>
    /// The valley floor under every cell: the height of the original course (made never to rise downstream) at the
    /// nearest point of the course. Height above it tells floodplain from valley wall.
    /// </summary>
    private static float[] ValleyFloor(float[] height, int sizeX, int sizeZ, List<Vector2> course)
    {
        List<Vector2> line = Resample(course, 4f);
        var level = new float[line.Count];
        float lowest = float.MaxValue;
        for (int i = 0; i < line.Count; i++)
        {
            Vector2 v = line[i];
            bool inside = v.x >= 0f && v.y >= 0f && v.x < sizeX && v.y < sizeZ;
            if (inside)
                lowest = Mathf.Min(lowest, height[Index(v, sizeX, sizeZ)]);
            level[i] = lowest;
        }
        // Upstream of the map, use the first height inside it.
        int firstInside = System.Array.FindIndex(level, l => l < float.MaxValue);
        for (int i = 0; i < firstInside; i++)
            level[i] = firstInside >= 0 ? level[firstInside] : 0f;

        var floor = new float[sizeX * sizeZ];
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                var cell = new Vector2(x + 0.5f, z + 0.5f);
                float best = float.MaxValue;
                int nearest = 0;
                for (int i = 0; i < line.Count; i++)
                {
                    float d = (line[i] - cell).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        nearest = i;
                    }
                }
                floor[z * sizeX + x] = level[nearest];
            }
        }
        return floor;
    }

    /// <summary>Cells from a point, along a direction, until the ground rises above the flood level.</summary>
    private static float Room(Vector2 from, Vector2 direction, float floodLevel, float maxScan, float[] height, int sizeX, int sizeZ)
    {
        for (float s = 1f; s <= maxScan; s += 1f)
        {
            Vector2 v = from + direction * s;
            if (v.x < 0f || v.y < 0f || v.x >= sizeX || v.y >= sizeZ)
                return s;
            if (height[Index(v, sizeX, sizeZ)] > floodLevel)
                return s;
        }
        return maxScan;
    }

    public static int Index(Vector2 v, int sizeX, int sizeZ) =>
        Mathf.Clamp((int)v.y, 0, sizeZ - 1) * sizeX + Mathf.Clamp((int)v.x, 0, sizeX - 1);

    /// <summary>Points along a polyline at even spacing, keeping both ends.</summary>
    public static List<Vector2> Resample(List<Vector2> line, float spacing)
    {
        var result = new List<Vector2> { line[0] };
        float carry = 0f;
        for (int i = 0; i < line.Count - 1; i++)
        {
            Vector2 a = line[i], b = line[i + 1];
            float length = (b - a).magnitude;
            float d = spacing - carry;
            while (d <= length)
            {
                result.Add(Vector2.Lerp(a, b, d / length));
                d += spacing;
            }
            carry = length - (d - spacing);
        }
        if ((result[result.Count - 1] - line[line.Count - 1]).sqrMagnitude > 0.01f)
            result.Add(line[line.Count - 1]);
        return result;
    }
}
