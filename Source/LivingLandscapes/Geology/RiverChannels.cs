using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>Channel pattern settings (part of <see cref="GeologyGenerationExtension"/>). Lengths in map cells.</summary>
public class RiverChannelSettings
{
    /// <summary>Largest spacing of the centre line's points while a channel is shaped (half a width for narrow ones).</summary>
    public float spacing = 2f;

    /// <summary>
    /// Meander migration steps (Howard-Knutson). Each step moves the bank outward at bends, weighted by the curvature
    /// upstream, so bends grow and travel downstream. Valley walls stop it, so only channels on open, nearly flat
    /// ground form loops (rule V4).
    /// </summary>
    public int meanderSteps = 500;
    public float migrationRate = 4f;

    /// <summary>Largest move of a bend per step, in channel widths: every channel evolves the same number of widths.</summary>
    public float migrationStepWidths = 0.02f;
    public float localWeight = -1f;
    public float upstreamWeight = 2.5f;

    /// <summary>
    /// Upstream curvature fades over this many channel widths. Bends grow only when this is short against their
    /// length (about a tenth of the wavelength), so keep it under one.
    /// </summary>
    public float decayWidths = 0.6f;

    /// <summary>
    /// Bends travel downstream as they grow. A channel entering from beyond the map is extended this many map widths
    /// upstream, so bends that formed upstream keep arriving instead of the channel straightening where it enters.
    /// </summary>
    public float upstreamMaps = 2f;

    /// <summary>
    /// The small wobble every channel has (rule V4): amplitude this many channel widths (clamped to the cell range),
    /// over a wavelength of this many widths (clamped). It also seeds bends where the ground lets them grow.
    /// </summary>
    public float wobbleWidths = 1f;
    public FloatRange wobbleCells = new FloatRange(1.5f, 3f);
    public float wobbleWavelengthWidths = 5f;
    public FloatRange wobbleWavelengthCells = new FloatRange(10f, 20f);

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
    /// Splits where confinement ends (rule V5). Valley floor width is the ground within <see cref="floodDepth"/>
    /// metres of the channel. Where it is at least <see cref="splitConfinement"/> channel widths and has grown to
    /// <see cref="releaseRatio"/> times its narrowest over the last <see cref="upstreamWidths"/> widths upstream (a
    /// gorge or narrows opening into a basin, a mountain front opening onto a plain), the channel divides around
    /// islands and rejoins.
    /// </summary>
    public float splitConfinement = 6f;
    public float releaseRatio = 2.5f;
    public float upstreamWidths = 10f;
    public float floodDepth = 1.5f;

    /// <summary>Within this many channel widths of a sea edge (base level) the channel fans into distributaries that reach the sea.</summary>
    public float deltaWidths = 8f;

    /// <summary>Island length in channel widths (at least <see cref="minIslandLength"/> cells), and the width each branch keeps.</summary>
    public float islandWidths = 3f;
    public float minIslandLength = 24f;
    public float branchWidthShare = 0.6f;

    /// <summary>Islands need ground at least this erodible (1 - rock strength) to have been built around.</summary>
    public float minIslandErodibility = 0.2f;
}

/// <summary>One channel across the map: its main course, side branches around islands, and cut-off loops.</summary>
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

    /// <summary>Index of the channel this one joins at its last point (-1: it leaves the map).</summary>
    public int Parent = -1;

    /// <summary>Drainage area (map cells) the channel brings in from beyond the map; 0 if it starts inside.</summary>
    public float EntryArea;

    public bool WorldRiver;

    public class SideBranch
    {
        public List<Vector2> Points = new List<Vector2>();
        public float Width;

        /// <summary>Indices on <see cref="Main"/> where the branch leaves and rejoins.</summary>
        public int From, To;

        /// <summary>A distributary: it reaches the sea at the map edge instead of rejoining.</summary>
        public bool ToBaseLevel;
    }
}

/// <summary>The map's channels, larger first, and what carving them left in each cell.</summary>
public class RiverSystem
{
    public List<RiverPlan> Channels = new List<RiverPlan>();

    /// <summary>Water surface height in each cell of a live channel (NaN elsewhere), set when the channels are carved.</summary>
    public float[] WaterLevel;

    /// <summary>Downstream direction (unit) in each cell of a live channel, set when the channels are carved.</summary>
    public Vector2[] Flow;

    /// <summary>Channel index in each cell of a live channel (-1 elsewhere).</summary>
    public int[] ChannelOf;

    public RiverPlan WorldRiver => Channels.Find(c => c.WorldRiver);
}

/// <summary>
/// Channel patterns (rules V3-V5, W4, W5). Every channel of the network is shaped by the same process: a small
/// wobble at its own scale, then meander migration, where bends erode their outer bank. Bank erodibility comes from
/// the rock and the valley walls, so a channel in rock or in a narrow valley keeps gentle curves, and one on an open
/// plain of weak ground winds and cuts off loops. Widths come from a ladder: the world river's vanilla class, or the
/// stream order of the map's own channels. Where confinement ends the channel splits around islands; near the sea it
/// fans into distributaries.
/// </summary>
public static class RiverChannels
{
    /// <summary>Plans every channel of the network, parents before tributaries, each tributary ending on its parent.</summary>
    public static RiverSystem PlanNetwork(float[] height, float[] rockStrength, int sizeX, int sizeZ, List<ChannelCourse> courses,
        float vanillaWidth, Vector2 seaDirection, RiverNetworkSettings network, RiverChannelSettings p)
    {
        var system = new RiverSystem();
        if (courses == null)
            return system;
        var planned = new RiverPlan[courses.Count];
        for (int k = 0; k < courses.Count; k++)
        {
            ChannelCourse course = courses[k];
            RiverPlan parent = course.Parent >= 0 ? planned[course.Parent] : null;
            float width = course.WorldRiver && vanillaWidth > 0f
                ? network.worldRiverWidth.Evaluate(vanillaWidth)
                : network.widthByOrder.Evaluate(course.Order);
            if (parent != null)
                width = Mathf.Min(width, parent.NominalWidth * network.tributaryShare);
            List<Vector2> line = RiverCourse.FromPlan(course.Points, sizeX, sizeZ);
            if (line == null)
                continue;
            // A tributary ends on its parent's channel as planned, not where the model's grid put the junction: it is
            // cut where it comes closest to the parent and joined to it there. A junction beyond the map is left alone.
            if (parent != null && Inside(line[line.Count - 1], sizeX, sizeZ))
                JoinParent(line, parent.Main);
            else
                parent = null;
            bool fromEdge = !Inside(course.Points[0], sizeX, sizeZ);
            RiverPlan plan = Plan(height, rockStrength, sizeX, sizeZ, line, width, fromEdge, seaDirection, p);
            if (plan.Main.Count < 2)
                continue;
            plan.Parent = parent != null ? system.Channels.IndexOf(parent) : -1;
            plan.EntryArea = fromEdge ? course.EntryArea : 0f;
            plan.WorldRiver = course.WorldRiver;
            planned[k] = plan;
            system.Channels.Add(plan);
        }
        return system;
    }

    public static RiverPlan Plan(float[] height, float[] rockStrength, int sizeX, int sizeZ, List<Vector2> course, float width,
        bool fromEdge, Vector2 seaDirection, RiverChannelSettings p)
    {
        var plan = new RiverPlan { NominalWidth = width };
        if (course == null || course.Count < 2)
            return plan;
        float spacing = Mathf.Clamp(width * 0.5f, 1f, p.spacing);
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
            if (!Inside(v, sizeX, sizeZ))
                continue;
            sum += BankErodibility(v);
            count++;
        }
        float outside = count > 0 ? sum / count : 0.25f;
        float Erodibility(Vector2 v) => Inside(v, sizeX, sizeZ) ? BankErodibility(v) : outside;
        float[] floor = ValleyFloor(height, sizeX, sizeZ, course);
        float AboveFloor(Vector2 v) => height[Index(v, sizeX, sizeZ)] - floor[Index(v, sizeX, sizeZ)];

        var extended = new List<Vector2>(course);
        if (fromEdge)
        {
            Vector2 back = (course[0] - course[1]).normalized;
            extended.Insert(0, course[0] + back * p.upstreamMaps * Mathf.Max(sizeX, sizeZ));
        }
        List<Vector2> points = Resample(extended, spacing);

        Wobble(points, width, p);
        for (int step = 0; step < p.meanderSteps; step++)
        {
            Migrate(points, width, spacing, p, Erodibility, AboveFloor, v => Inside(v, sizeX, sizeZ));
            points = Resample(points, spacing);
            if (step % 4 == 0)
                CutOff(points, width * p.cutoffWidths, spacing, plan.Oxbows);
        }

        // Keep the part that crosses the map, with one point beyond each edge.
        int first = points.FindIndex(v => Inside(v, sizeX, sizeZ)), last = points.FindLastIndex(v => Inside(v, sizeX, sizeZ));
        if (first < 0)
            return plan;
        first = Mathf.Max(0, first - 1);
        last = Mathf.Min(points.Count - 1, last + 1);
        points = points.GetRange(first, last - first + 1);
        plan.Oxbows.RemoveAll(loop => !loop.Exists(v => Inside(v, sizeX, sizeZ)));

        plan.Main = points;
        foreach (Vector2 _ in points)
            plan.MainWidth.Add(width);
        Split(plan, height, sizeX, sizeZ, width, spacing, seaDirection, p, Erodibility);
        return plan;
    }

    /// <summary>The small wobble of rule V4 along the course: a few cells over a few tens of cells, at the channel's own scale.</summary>
    private static void Wobble(List<Vector2> points, float width, RiverChannelSettings p)
    {
        float amplitude = p.wobbleCells.ClampToRange(p.wobbleWidths * width);
        float wavelength = p.wobbleWavelengthCells.ClampToRange(p.wobbleWavelengthWidths * width);
        var noise = new Perlin(1.0 / wavelength, 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);
        float along = 0f;
        var moved = new Vector2[points.Count];
        for (int i = 1; i < points.Count - 1; i++)
        {
            along += (points[i] - points[i - 1]).magnitude;
            Vector2 t = (points[i + 1] - points[i - 1]).normalized;
            moved[i] = new Vector2(-t.y, t.x) * (float)noise.GetValue(along, 0.0, 0.0) * amplitude;
        }
        for (int i = 1; i < points.Count - 1; i++)
            points[i] += moved[i];
    }

    /// <summary>One migration step: each point moves toward the outside of its bend, by local and upstream curvature.</summary>
    private static void Migrate(List<Vector2> points, float scale, float spacing, RiverChannelSettings p,
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
        int reach = Mathf.CeilToInt(4f * decay / spacing);
        var move = new Vector2[n];
        for (int i = 2; i < n - 2; i++)
        {
            float upstream = 0f, weights = 0f;
            for (int k = 1; k <= reach && i - k >= 0; k++)
            {
                float w = Mathf.Exp(-k * spacing / decay);
                upstream += curvature[i - k] * w;
                weights += w;
            }
            float m = p.localWeight * curvature[i] + p.upstreamWeight * (weights > 0f ? upstream / weights : 0f);
            Vector2 t = (points[i + 1] - points[i - 1]).normalized;
            var normal = new Vector2(-t.y, t.x);
            // Positive curvature turns left (centre on the normal's side): the outer bank lies opposite the normal.
            float distance = Mathf.Clamp(p.migrationRate * m * scale, -1f, 1f) * p.migrationStepWidths * scale * erodibility(points[i]);
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
    private static void CutOff(List<Vector2> points, float neck, float spacing, List<List<Vector2>> oxbows)
    {
        int minApart = Mathf.CeilToInt(2f * neck / spacing) + 2;
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
    /// Islands (rule V5). Where the valley floor opens out after narrows, the main channel narrows and a side branch
    /// takes part of the flow, bowing toward the side with more room and rejoining downstream. Near a sea edge the
    /// channel fans into distributaries on both sides that reach the sea.
    /// </summary>
    private static void Split(RiverPlan plan, float[] height, int sizeX, int sizeZ, float width, float spacing, Vector2 seaDirection,
        RiverChannelSettings p, System.Func<Vector2, float> erodibility)
    {
        List<Vector2> pts = plan.Main;
        int n = pts.Count;
        var left = new float[n];
        var right = new float[n];
        float maxScan = 4f * width + 20f;
        for (int i = 1; i < n - 1; i++)
        {
            Vector2 t = (pts[i + 1] - pts[i - 1]).normalized;
            var normal = new Vector2(-t.y, t.x);
            float flood = height[Index(pts[i], sizeX, sizeZ)] + p.floodDepth;
            left[i] = Room(pts[i], normal, flood, maxScan, height, sizeX, sizeZ);
            right[i] = Room(pts[i], -normal, flood, maxScan, height, sizeX, sizeZ);
        }

        // Confinement (floor width over channel width), smoothed over a channel width, and its narrowest upstream.
        var confinement = new float[n];
        for (int i = 0; i < n; i++)
            confinement[i] = (left[i] + right[i]) / width;
        confinement = MovingAverage(confinement, Mathf.CeilToInt(width / spacing));
        int upstreamPoints = Mathf.CeilToInt(p.upstreamWidths * width / spacing);
        var narrowest = new float[n];
        for (int i = 0; i < n; i++)
        {
            narrowest[i] = float.MaxValue;
            for (int k = Mathf.Max(1, i - upstreamPoints); k < i; k++)
                narrowest[i] = Mathf.Min(narrowest[i], confinement[k]);
        }

        float SeaDistance(Vector2 v)
        {
            float d = float.MaxValue;
            if (seaDirection.x > 0.5f) d = Mathf.Min(d, sizeX - v.x);
            if (seaDirection.x < -0.5f) d = Mathf.Min(d, v.x);
            if (seaDirection.y > 0.5f) d = Mathf.Min(d, sizeZ - v.y);
            if (seaDirection.y < -0.5f) d = Mathf.Min(d, v.y);
            return d;
        }
        bool Interior(Vector2 v) => v.x > width && v.y > width && v.x < sizeX - width && v.y < sizeZ - width;
        bool Released(int i) => Interior(pts[i]) && confinement[i] >= p.splitConfinement
                                && confinement[i] >= p.releaseRatio * narrowest[i] && erodibility(pts[i]) >= p.minIslandErodibility;

        float islandLength = Mathf.Max(p.minIslandLength, p.islandWidths * width);
        int islandPoints = Mathf.CeilToInt(islandLength / spacing);
        float branchWidth = width * p.branchWidthShare;

        // Near the sea: distributaries on both sides from where the channel comes within reach of the coast.
        int delta = pts.FindIndex(v => Inside(v, sizeX, sizeZ) && SeaDistance(v) <= p.deltaWidths * width);
        if (delta > 0 && delta < n - 3)
        {
            int to = n - 1;
            foreach (float side in new[] { 1f, -1f })
            {
                var branch = new RiverPlan.SideBranch { Width = branchWidth, From = delta, To = to, ToBaseLevel = true };
                for (int k = delta; k <= to; k++)
                {
                    float f = (k - delta) / (float)(to - delta);
                    Vector2 t = (pts[Mathf.Min(k + 1, n - 1)] - pts[Mathf.Max(k - 1, 0)]).normalized;
                    branch.Points.Add(pts[k] + new Vector2(-t.y, t.x) * side * Mathf.Pow(f, 0.6f) * 2f * width);
                }
                plan.Sides.Add(branch);
            }
            for (int k = delta; k <= to; k++)
                plan.MainWidth[k] = branchWidth;
            n = delta;
        }

        // Where the valley opens out: islands that rejoin; the next may start after one ends.
        for (int from = 1; from + islandPoints < n - 1; from++)
        {
            if (!Released(from))
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
                Vector2 t = (pts[Mathf.Min(k + 1, pts.Count - 1)] - pts[Mathf.Max(k - 1, 0)]).normalized;
                branch.Points.Add(pts[k] + new Vector2(-t.y, t.x) * side * offset * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * f)), 0.6f));
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
            if (Inside(line[i], sizeX, sizeZ))
                lowest = Mathf.Min(lowest, height[Index(line[i], sizeX, sizeZ)]);
            level[i] = lowest;
        }
        // Upstream of the map, use the first height inside it.
        int firstInside = System.Array.FindIndex(level, l => l < float.MaxValue);
        for (int i = 0; i < level.Length && (firstInside < 0 || i < firstInside); i++)
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

    /// <summary>Cuts a tributary at its closest approach to the parent's centre line and ends it on that line.</summary>
    private static void JoinParent(List<Vector2> line, List<Vector2> parent)
    {
        int closest = line.Count - 1;
        float best = float.MaxValue;
        for (int i = 0; i < line.Count; i++)
        {
            float d = (NearestOn(parent, line[i]) - line[i]).sqrMagnitude;
            if (d < best)
            {
                best = d;
                closest = i;
            }
        }
        Vector2 junction = NearestOn(parent, line[closest]);
        line.RemoveRange(closest + 1, line.Count - closest - 1);
        if ((line[closest] - junction).sqrMagnitude > 0.25f)
            line.Add(junction);
        else
            line[closest] = junction;
    }

    /// <summary>The point of a polyline nearest to <paramref name="v"/>.</summary>
    private static Vector2 NearestOn(List<Vector2> line, Vector2 v)
    {
        Vector2 best = line[0];
        float bestDistance = float.MaxValue;
        for (int s = 0; s < line.Count - 1; s++)
        {
            Vector2 a = line[s], ab = line[s + 1] - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(v - a, ab) / ab.sqrMagnitude);
            Vector2 q = a + t * ab;
            float d = (q - v).sqrMagnitude;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = q;
            }
        }
        return best;
    }

    private static bool Inside(Vector2 v, int sizeX, int sizeZ) => v.x >= 0f && v.y >= 0f && v.x < sizeX && v.y < sizeZ;

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
