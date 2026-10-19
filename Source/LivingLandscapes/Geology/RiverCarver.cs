using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>River bed settings (part of <see cref="GeologyGenerationExtension"/>).</summary>
public class RiverBedSettings
{
    /// <summary>The water surface lies this far (metres) below the valley floor beside the channel (bank height).</summary>
    public float bankHeight = 0.3f;

    /// <summary>
    /// Channel depth (metres) at the deepest point of a pool: this times the square root of the channel width in cells
    /// (hydraulic geometry: depth grows more slowly than width). Creeks stay shallow enough to wade; large rivers get a
    /// deep core.
    /// </summary>
    public float depthPerSqrtWidth = 0.35f;

    /// <summary>Water depth (metres) at the channel's edge: a shallow rim along both banks.</summary>
    public float rimDepth = 0.2f;

    /// <summary>
    /// Pools and riffles: the channel alternates between deep pools (at bends) and shallow riffles (where the flow
    /// crosses from one bend to the next). A riffle keeps this share of a pool's depth. On straight reaches pools
    /// repeat every <see cref="poolSpacingWidths"/> channel widths.
    /// </summary>
    public float riffleShare = 0.35f;
    public float poolSpacingWidths = 6f;

    /// <summary>
    /// At a sharp bend the deepest line moves this share of the half-width toward the outer bank, and the inner bank
    /// becomes a shallow bar (point bar).
    /// </summary>
    public float thalwegShift = 0.6f;

    /// <summary>Cells beyond the channel over which the banks ease back to the natural ground.</summary>
    public float bankWidth = 4f;

    /// <summary>
    /// Bank asymmetry (W5): at a bend the outer bank is cut steep and the inner bank slopes gently. Bank width is
    /// multiplied by up to these factors on the outer and inner side of a bend, by how sharp the bend is.
    /// </summary>
    public float outerBankFactor = 0.3f;
    public float innerBankFactor = 2f;

    /// <summary>Bends tighter than this radius (in channel widths) count as sharp: full asymmetry, a full pool.</summary>
    public float sharpBendWidths = 3f;

    /// <summary>Cut-off loops are carved this deep (metres) below their surroundings and this share of the river's width.</summary>
    public float oxbowDepth = 0.6f;
    public float oxbowWidthShare = 0.7f;
}

/// <summary>
/// Carves the river's channels into the landform. Water flows downhill or level (rule W1): the map's terrain is finer
/// than the erosion model, so the valley floor along a course can rise and fall by more than the river drops across
/// the map. The water surface therefore follows the closest never-rising profile of the valley floor, a little below
/// it. Under the surface the bed has a cross-section: a deep core shifted to the outside of bends, a shallow rim and an
/// inner bar, and an alternation of deep pools at bends and shallow riffles between them (fords). Banks ease back to
/// the natural ground, steep on the outside of bends and gentle on the inside (W5). Side branches run between the main
/// channel's water levels where they leave and rejoin it. Cut-off loops are carved as closed hollows, plugged where they
/// met the river, for the lake model to fill or leave dry.
/// </summary>
public static class RiverCarver
{
    private struct Nearest
    {
        public float Distance, Along, Side;
        public int Segment;
        public float T;
    }

    /// <summary>A channel's centre line and the shape values the carving needs at each point.</summary>
    private class Channel
    {
        public List<Vector2> Line;
        public float[] Cumulative, Curvature, Pool;
        public Nearest[] Near;

        public float At(float[] values, Nearest n) =>
            Mathf.Lerp(values[n.Segment], values[Mathf.Min(n.Segment + 1, values.Length - 1)], n.T);
    }

    /// <summary>Carves the plan into <paramref name="height"/> and sets <see cref="RiverPlan.WaterLevel"/>.</summary>
    public static void Carve(float[] height, int sizeX, int sizeZ, RiverPlan plan, RiverBedSettings p)
    {
        if (plan == null || plan.Main.Count < 2)
            return;
        int n = sizeX * sizeZ;
        Channel main = Build(plan.Main, plan.NominalWidth, sizeX, sizeZ, p);

        // Water surface along the main channel: the valley floor's never-rising profile, below the bank top.
        float bankHalf = plan.NominalWidth * 0.5f;
        int bins = Mathf.CeilToInt(main.Cumulative[main.Line.Count - 1]) + 1;
        float[] surface = Profile(height, main.Near, bankHalf, bins);
        if (surface == null)
            return;
        for (int b = 0; b < bins; b++)
            surface[b] -= p.bankHeight;
        float SurfaceAt(float along) => surface[Mathf.Clamp((int)along, 0, bins - 1)];

        var carved = (float[])height.Clone();
        var level = new float[n];
        for (int i = 0; i < n; i++)
            level[i] = float.NaN;

        float[] mainWidth = plan.MainWidth.ToArray();
        CarveChannel(height, carved, level, main, i => SurfaceAt(main.Near[i].Along), i => main.At(mainWidth, main.Near[i]),
            plan.NominalWidth, p);

        foreach (RiverPlan.SideBranch side in plan.Sides)
        {
            float startLevel = SurfaceAt(main.Cumulative[side.From]), endLevel = SurfaceAt(main.Cumulative[side.To]);
            Channel branch = Build(side.Points, side.Width, sizeX, sizeZ, p);
            float length = Mathf.Max(branch.Cumulative[side.Points.Count - 1], 1f);
            CarveChannel(height, carved, level, branch, i => Mathf.Lerp(startLevel, endLevel, branch.Near[i].Along / length),
                _ => side.Width, side.Width, p);
        }

        // Cut-off loops: sediment plugs the ends where the loop met the river, so only the part well away from every
        // live channel is carved, and never into a live channel.
        float loopWidth = plan.NominalWidth * p.oxbowWidthShare;
        float plug = bankHalf + p.bankWidth * p.innerBankFactor + loopWidth * 0.5f + p.bankWidth;
        foreach (List<Vector2> loop in plan.Oxbows)
        {
            List<Vector2> body = LongestRun(loop, v => main.Near[RiverChannels.Index(v, sizeX, sizeZ)].Distance >= plug);
            if (body.Count < 4)
                continue;
            Channel oxbow = Build(body, loopWidth, sizeX, sizeZ, p);
            float floor = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (oxbow.Near[i].Distance <= loopWidth * 0.5f && float.IsNaN(level[i]))
                    floor = Mathf.Min(floor, height[i]);
            }
            if (floor == float.MaxValue)
                continue;
            CarveHollow(height, carved, level, oxbow, floor - p.oxbowDepth, loopWidth * 0.5f, p);
        }

        System.Array.Copy(carved, height, n);
        plan.WaterLevel = level;
    }

    private static Channel Build(List<Vector2> line, float width, int sizeX, int sizeZ, RiverBedSettings p)
    {
        var c = new Channel { Line = line, Cumulative = Cumulative(line) };
        c.Curvature = Curvature(line, width * 0.5f / Mathf.Max(c.Cumulative[line.Count - 1] / Mathf.Max(line.Count - 1, 1), 0.1f));
        c.Pool = Pools(c.Cumulative, c.Curvature, width, p);
        c.Near = NearestTo(line, c.Cumulative, sizeX, sizeZ);
        return c;
    }

    /// <summary>
    /// Inside the channel the bed lies below the water surface by the cross-section's depth; beyond it the banks ease
    /// from the water's edge back to the natural ground (never raising it). Where channels overlap the deeper bed wins.
    /// </summary>
    private static void CarveChannel(float[] natural, float[] carved, float[] level, Channel channel,
        System.Func<int, float> surfaceAt, System.Func<int, float> widthAt, float nominalWidth, RiverBedSettings p)
    {
        float sharp = 1f / Mathf.Max(1f, p.sharpBendWidths * nominalWidth);
        for (int i = 0; i < natural.Length; i++)
        {
            Nearest near = channel.Near[i];
            float width = widthAt(i), half = width * 0.5f;
            float curvature = channel.At(channel.Curvature, near);
            // Outer bank: the side away from the bend's centre (the centre lies on the side the curvature points to).
            float bend = Mathf.Clamp01(Mathf.Abs(curvature) / sharp);
            bool outer = curvature * near.Side < 0f;
            float bank = p.bankWidth * Mathf.Lerp(1f, outer ? p.outerBankFactor : p.innerBankFactor, bend);
            if (near.Distance > half + bank)
                continue;
            float surface = surfaceAt(i);
            if (near.Distance <= half)
            {
                float bed = surface - Depth(near.Distance / half, outer, bend, channel.At(channel.Pool, near), width, p);
                carved[i] = float.IsNaN(level[i]) ? bed : Mathf.Min(carved[i], bed);
                level[i] = float.IsNaN(level[i]) ? surface : Mathf.Max(level[i], surface);
            }
            else if (float.IsNaN(level[i]))
            {
                float t = (near.Distance - half) / bank;
                carved[i] = Mathf.Min(carved[i], Mathf.Lerp(surface, natural[i], t * t * (3f - 2f * t)));
            }
        }
    }

    /// <summary>
    /// Water depth across the channel: deepest on a line shifted toward the outer bank by the bend's sharpness,
    /// shallowing to the rim at both banks; the inner side, being wider, slopes gently (a point bar).
    /// <paramref name="across"/> is the distance from the centre line in half-widths.
    /// </summary>
    private static float Depth(float across, bool outer, float bend, float pool, float width, RiverBedSettings p)
    {
        // Pools are the deep part of the cycle, riffles the long shallow rest.
        float deepest = p.depthPerSqrtWidth * Mathf.Sqrt(width) * Mathf.Lerp(p.riffleShare, 1f, pool * pool);
        float shift = p.thalwegShift * bend;
        float u = outer ? across : -across;
        float q = u >= shift ? (u - shift) / (1f - shift) : (shift - u) / (1f + shift);
        return Mathf.Max(p.rimDepth, p.rimDepth + (deepest - p.rimDepth) * (1f - q * q));
    }

    /// <summary>
    /// Pool (1) or riffle (0) at each point. Where the channel bends, pools lie at the bends and riffles at the
    /// crossings between them; on straight reaches they alternate every <see cref="RiverBedSettings.poolSpacingWidths"/>.
    /// </summary>
    private static float[] Pools(float[] cumulative, float[] curvature, float width, RiverBedSettings p)
    {
        int n = cumulative.Length;
        float sharp = 1f / Mathf.Max(1f, p.sharpBendWidths * width);
        float spacing = p.poolSpacingWidths * width;
        var bend = new float[n];
        for (int i = 0; i < n; i++)
            bend[i] = Mathf.Clamp01(Mathf.Abs(curvature[i]) / sharp);
        var pool = new float[n];
        for (int i = 0, lo = 0, hi = 0; i < n; i++)
        {
            // How strongly the reach around this point (half a pool spacing either way) bends.
            while (cumulative[i] - cumulative[lo] > spacing * 0.5f) lo++;
            while (hi < n - 1 && cumulative[hi + 1] - cumulative[i] <= spacing * 0.5f) hi++;
            float bendiness = 0f;
            for (int k = lo; k <= hi; k++)
                bendiness = Mathf.Max(bendiness, bend[k]);
            float straight = 0.5f + 0.5f * Mathf.Cos(2f * Mathf.PI * cumulative[i] / spacing);
            float bending = bendiness > 1e-3f ? bend[i] / bendiness : 0f;
            pool[i] = Mathf.Lerp(straight, bending, Mathf.Clamp01(bendiness / 0.5f));
        }
        return pool;
    }

    /// <summary>A closed hollow along a line: flat floor, banks easing back to the natural ground; live channels untouched.</summary>
    private static void CarveHollow(float[] natural, float[] carved, float[] level, Channel channel, float floor, float half, RiverBedSettings p)
    {
        for (int i = 0; i < natural.Length; i++)
        {
            float d = channel.Near[i].Distance;
            if (d > half + p.bankWidth || !float.IsNaN(level[i]))
                continue;
            float t = Mathf.Clamp01((d - half) / p.bankWidth);
            carved[i] = Mathf.Min(carved[i], Mathf.Lerp(floor, natural[i], t * t * (3f - 2f * t)));
        }
    }

    /// <summary>The longest run of consecutive points that satisfy <paramref name="keep"/>.</summary>
    private static List<Vector2> LongestRun(List<Vector2> line, System.Func<Vector2, bool> keep)
    {
        int bestStart = 0, bestLength = 0;
        for (int i = 0; i < line.Count;)
        {
            if (!keep(line[i]))
            {
                i++;
                continue;
            }
            int start = i;
            while (i < line.Count && keep(line[i]))
                i++;
            if (i - start > bestLength)
            {
                bestStart = start;
                bestLength = i - start;
            }
        }
        return line.GetRange(bestStart, bestLength);
    }

    /// <summary>Mean natural height inside the channel per cell of length, made non-increasing and interpolated over gaps.</summary>
    private static float[] Profile(float[] height, Nearest[] nearest, float halfWidth, int bins)
    {
        var sum = new float[bins];
        var count = new int[bins];
        for (int i = 0; i < height.Length; i++)
        {
            if (nearest[i].Distance > halfWidth)
                continue;
            int b = Mathf.Clamp((int)nearest[i].Along, 0, bins - 1);
            sum[b] += height[i];
            count[b]++;
        }
        var values = new List<float>();
        var position = new List<int>();
        for (int b = 0; b < bins; b++)
        {
            if (count[b] == 0)
                continue;
            values.Add(sum[b] / count[b]);
            position.Add(b);
        }
        if (values.Count < 2)
            return null;
        float[] fitted = NonIncreasing(values);
        var result = new float[bins];
        for (int k = 0, b = 0; b < bins; b++)
        {
            while (k < position.Count - 1 && position[k + 1] <= b)
                k++;
            if (b <= position[0])
                result[b] = fitted[0];
            else if (k >= position.Count - 1)
                result[b] = fitted[fitted.Length - 1];
            else
                result[b] = Mathf.Lerp(fitted[k], fitted[k + 1], (b - position[k]) / (float)(position[k + 1] - position[k]));
        }
        return result;
    }

    /// <summary>Each cell's distance to a polyline, position along it, nearest segment and which side the cell is on.</summary>
    private static Nearest[] NearestTo(List<Vector2> line, float[] cumulative, int sizeX, int sizeZ)
    {
        var result = new Nearest[sizeX * sizeZ];
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                var cell = new Vector2(x + 0.5f, z + 0.5f);
                float best = float.MaxValue;
                var nearest = new Nearest();
                for (int s = 0; s < line.Count - 1; s++)
                {
                    Vector2 a = line[s], ab = line[s + 1] - a;
                    float len2 = ab.sqrMagnitude;
                    float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(cell - a, ab) / len2);
                    float d = (cell - (a + t * ab)).sqrMagnitude;
                    if (d >= best)
                        continue;
                    best = d;
                    nearest.Along = cumulative[s] + t * Mathf.Sqrt(len2);
                    nearest.Segment = s;
                    nearest.T = t;
                    nearest.Side = Mathf.Sign(ab.x * (cell.y - a.y) - ab.y * (cell.x - a.x));
                }
                nearest.Distance = Mathf.Sqrt(best);
                result[z * sizeX + x] = nearest;
            }
        }
        return result;
    }

    private static float[] Cumulative(List<Vector2> line)
    {
        var cumulative = new float[line.Count];
        for (int i = 1; i < line.Count; i++)
            cumulative[i] = cumulative[i - 1] + (line[i] - line[i - 1]).magnitude;
        return cumulative;
    }

    /// <summary>
    /// Signed curvature (positive turning left) per point, smoothed over about <paramref name="smoothPoints"/> points
    /// (half a channel width): a channel bends at its own scale, not at every kink of its centre line.
    /// </summary>
    private static float[] Curvature(List<Vector2> line, float smoothPoints)
    {
        int n = line.Count;
        var k = new float[n];
        for (int i = 1; i < n - 1; i++)
        {
            Vector2 a = line[i] - line[i - 1], b = line[i + 1] - line[i];
            float turn = Mathf.Atan2(a.x * b.y - a.y * b.x, Vector2.Dot(a, b));
            k[i] = turn / Mathf.Max(0.5f * (a.magnitude + b.magnitude), 1e-3f);
        }
        // Repeated [1/4 1/2 1/4] passes: a Gaussian whose variance grows by half a point squared per pass.
        int passes = Mathf.Clamp(Mathf.CeilToInt(2f * smoothPoints * smoothPoints), 3, 400);
        for (int pass = 0; pass < passes; pass++)
        {
            var s = (float[])k.Clone();
            for (int i = 1; i < n - 1; i++)
                s[i] = 0.25f * k[i - 1] + 0.5f * k[i] + 0.25f * k[i + 1];
            k = s;
        }
        return k;
    }

    /// <summary>The non-increasing sequence closest to <paramref name="values"/> (least squares, pool adjacent violators).</summary>
    private static float[] NonIncreasing(List<float> values)
    {
        var means = new List<float>();
        var weights = new List<int>();
        foreach (float v in values)
        {
            means.Add(v);
            weights.Add(1);
            while (means.Count > 1 && means[means.Count - 1] > means[means.Count - 2])
            {
                int last = means.Count - 1;
                int w = weights[last - 1] + weights[last];
                means[last - 1] = (means[last - 1] * weights[last - 1] + means[last] * weights[last]) / w;
                weights[last - 1] = w;
                means.RemoveAt(last);
                weights.RemoveAt(last);
            }
        }
        var result = new float[values.Count];
        for (int block = 0, i = 0; block < means.Count; block++)
        {
            for (int k = 0; k < weights[block]; k++)
                result[i++] = means[block];
        }
        return result;
    }
}
