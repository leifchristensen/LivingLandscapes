using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>River bed and valley settings (part of <see cref="GeologyGenerationExtension"/>).</summary>
public class RiverBedSettings
{
    /// <summary>
    /// Channel depth (metres) at the deepest point: this times the square root of the channel width in cells
    /// (hydraulic geometry: depth grows more slowly than width). Creeks stay shallow enough to wade; rivers from about
    /// six cells wide carry a continuous deep thread (rule V6).
    /// </summary>
    public float depthPerSqrtWidth = 0.35f;

    /// <summary>Water depth (metres) at the channel's edge: a shallow rim along both banks.</summary>
    public float rimDepth = 0.2f;

    /// <summary>
    /// Pools and riffles, for channels at least <see cref="riffleMinWidth"/> cells wide: deep pools at bends, shallow
    /// riffles (fords) where the flow crosses from one bend to the next. A riffle keeps this share of a pool's depth;
    /// on straight reaches pools repeat every <see cref="poolSpacingWidths"/> widths. Narrower channels keep their
    /// deep thread unbroken.
    /// </summary>
    public float riffleMinWidth = 12f;
    public float riffleShare = 0.35f;
    public float poolSpacingWidths = 6f;

    /// <summary>At a sharp bend the deepest line moves this share of the half-width toward the outer bank (inner point bar).</summary>
    public float thalwegShift = 0.6f;

    /// <summary>Cells beyond the channel over which the bank rises from the water to the valley floor.</summary>
    public float bankWidth = 4f;

    /// <summary>
    /// Bank asymmetry (W5): at a bend the outer bank is cut steep and the inner bank slopes gently. Bank width is
    /// multiplied by up to these factors on the outer and inner side of a bend, by how sharp the bend is.
    /// </summary>
    public float outerBankFactor = 0.3f;
    public float innerBankFactor = 2f;

    /// <summary>Bends tighter than this radius (in channel widths) count as sharp: full asymmetry, a full pool.</summary>
    public float sharpBendWidths = 3f;

    /// <summary>
    /// Valley floors (rule V1): channels come first, valleys follow them. Beyond the banks a floor lies this height
    /// above the water (metres, times the square root of the width: bankfull depth) and reaches out the reach regime's
    /// floor widths times the rock's erodibility (soft rock: basins; hard rock: narrows), at most
    /// <see cref="maxFloorCells"/>. Beyond the floor the valley walls ease back to the natural ground over
    /// <see cref="wallWidth"/> cells. Ground is only ever lowered, so on flat tiles the floor is the plain itself.
    /// </summary>
    public float floorHeightPerSqrtWidth = 0.25f;

    /// <summary>The floor rises this much (metres per cell) away from the channel, so it drains to it like a floodplain.</summary>
    public float floorSlope = 0.003f;
    public float maxFloorCells = 40f;
    public float wallWidth = 10f;

    /// <summary>Cut-off loops are carved this deep (metres) below their surroundings and this share of the river's width.</summary>
    public float oxbowDepth = 0.6f;
    public float oxbowWidthShare = 0.7f;
}

/// <summary>
/// Carves the channel network into the landform, larger channels first. Water flows downhill or level (rule W1):
/// each channel's water surface follows the closest never-rising profile of the ground along it, and a tributary's
/// never lies below its parent's where they join. Under the surface the bed has a cross-section: a deep core shifted
/// to the outside of bends, a shallow rim and an inner bar; wide rivers alternate deep pools with shallow riffles
/// (fords). Banks rise steep on the outside of bends and gently on the inside to a valley floor whose width follows
/// the rock (V1), and the valley walls ease back to the natural ground. Side branches run between the main channel's
/// water levels where they leave and rejoin it. Cut-off loops are carved as closed hollows, plugged where they met the
/// river, for the lake model to fill or leave dry.
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

        /// <summary>The reach regime at each point of the line.</summary>
        public ReachRegime[] Regime;

        public ReachRegime RegimeAt(Nearest n) => Regime[Mathf.Min(n.T < 0.5f ? n.Segment : n.Segment + 1, Regime.Length - 1)];

        public float At(float[] values, Nearest n) =>
            Mathf.Lerp(values[n.Segment], values[Mathf.Min(n.Segment + 1, values.Length - 1)], n.T);

        public Vector2 Direction(Nearest n) => (Line[Mathf.Min(n.Segment + 1, Line.Count - 1)] - Line[n.Segment]).normalized;
    }

    /// <summary>What every carving pass reads and writes.</summary>
    private class Canvas
    {
        public float[] Natural, Carved, Level, RockStrength;
        public Vector2[] Flow;
        public int[] ChannelOf;
        public int SizeX, SizeZ;
    }

    /// <summary>Carves every channel into <paramref name="height"/> and fills the system's per-cell water level, flow and channel.</summary>
    public static void Carve(float[] height, float[] rockStrength, int sizeX, int sizeZ, RiverSystem system, RiverBedSettings p)
    {
        if (system == null || system.Channels.Count == 0)
            return;
        int n = sizeX * sizeZ;
        var canvas = new Canvas
        {
            Natural = height, Carved = (float[])height.Clone(), Level = new float[n], RockStrength = rockStrength,
            Flow = new Vector2[n], ChannelOf = new int[n], SizeX = sizeX, SizeZ = sizeZ,
        };
        for (int i = 0; i < n; i++)
        {
            canvas.Level[i] = float.NaN;
            canvas.ChannelOf[i] = -1;
        }
        for (int k = 0; k < system.Channels.Count; k++)
            CarvePlan(canvas, k, system.Channels[k], p);

        System.Array.Copy(canvas.Carved, height, n);
        system.WaterLevel = canvas.Level;
        system.Flow = canvas.Flow;
        system.ChannelOf = canvas.ChannelOf;
    }

    private static void CarvePlan(Canvas canvas, int index, RiverPlan plan, RiverBedSettings p)
    {
        if (plan.Main.Count < 2)
            return;
        float reach = plan.NominalWidth * 0.5f + p.bankWidth * p.innerBankFactor + p.maxFloorCells + p.wallWidth;
        Channel main = Build(plan.Main, plan.MainRegime.ToArray(), plan.NominalWidth, reach, canvas, p);

        // Water surface along the channel: the ground's never-rising profile, a bankfull depth below the valley floor,
        // and never below the parent's water where the channel joins it.
        float floorAbove = p.floorHeightPerSqrtWidth * Mathf.Sqrt(plan.NominalWidth);
        int bins = Mathf.CeilToInt(main.Cumulative[main.Line.Count - 1]) + 1;
        float[] surface = Profile(canvas.Natural, main.Near, plan.NominalWidth * 0.5f, bins);
        if (surface == null)
            return;
        float junction = float.MinValue;
        if (plan.Parent >= 0)
        {
            float parentLevel = canvas.Level[RiverChannels.Index(plan.Main[plan.Main.Count - 1], canvas.SizeX, canvas.SizeZ)];
            if (!float.IsNaN(parentLevel))
                junction = parentLevel;
        }
        for (int b = 0; b < bins; b++)
            surface[b] = Mathf.Max(surface[b] - floorAbove, junction);
        float SurfaceAt(float along) => surface[Mathf.Clamp((int)along, 0, bins - 1)];

        float[] mainWidth = plan.MainWidth.ToArray();
        CarveChannel(canvas, index, main, i => SurfaceAt(main.Near[i].Along), i => main.At(mainWidth, main.Near[i]),
            plan.NominalWidth, floorAbove, p);

        // A side branch's point j lies beside main point From + j: it shares the main channel's water surface there,
        // so where the two overlap their levels agree.
        foreach (RiverPlan.SideBranch side in plan.Sides)
        {
            Channel branch = Build(side.Points, plan.MainRegime.GetRange(side.From, side.Points.Count).ToArray(), side.Width, reach, canvas, p);
            float BranchSurface(int i)
            {
                Nearest near = branch.Near[i];
                int a = Mathf.Min(side.From + near.Segment, main.Cumulative.Length - 1), b = Mathf.Min(a + 1, main.Cumulative.Length - 1);
                return SurfaceAt(Mathf.Lerp(main.Cumulative[a], main.Cumulative[b], near.T));
            }
            CarveChannel(canvas, index, branch, BranchSurface, _ => side.Width, side.Width, floorAbove, p);
        }

        // Cut-off loops: sediment plugs the ends where the loop met the river, so only the part well away from every
        // live channel is carved, and never into a live channel.
        float loopWidth = plan.NominalWidth * p.oxbowWidthShare;
        float plug = plan.NominalWidth * 0.5f + p.bankWidth * p.innerBankFactor + loopWidth * 0.5f + p.bankWidth;
        foreach (List<Vector2> loop in plan.Oxbows)
        {
            List<Vector2> body = LongestRun(loop, v => main.Near[RiverChannels.Index(v, canvas.SizeX, canvas.SizeZ)].Distance >= plug);
            if (body.Count < 4)
                continue;
            Channel oxbow = Build(body, null, loopWidth, loopWidth + p.bankWidth, canvas, p);
            float floor = float.MaxValue;
            for (int i = 0; i < canvas.Natural.Length; i++)
            {
                if (oxbow.Near[i].Distance <= loopWidth * 0.5f && float.IsNaN(canvas.Level[i]))
                    floor = Mathf.Min(floor, canvas.Natural[i]);
            }
            if (floor < float.MaxValue)
                CarveHollow(canvas, oxbow, floor - p.oxbowDepth, loopWidth * 0.5f, p);
        }
    }

    private static Channel Build(List<Vector2> line, ReachRegime[] regime, float width, float reach, Canvas canvas, RiverBedSettings p)
    {
        var c = new Channel { Line = line, Cumulative = Cumulative(line), Regime = regime };
        c.Curvature = Curvature(line, width * 0.5f / Mathf.Max(c.Cumulative[line.Count - 1] / Mathf.Max(line.Count - 1, 1), 0.1f));
        c.Pool = Pools(c.Cumulative, c.Curvature, width, p);
        // Reaches whose regime has no riffles keep an unbroken deep thread.
        for (int i = 0; regime != null && i < c.Pool.Length; i++)
        {
            if (!regime[Mathf.Min(i, regime.Length - 1)].Riffles)
                c.Pool[i] = 1f;
        }
        c.Near = NearestTo(line, c.Cumulative, reach, canvas.SizeX, canvas.SizeZ);
        return c;
    }

    /// <summary>
    /// Inside the channel the bed lies below the water surface by the cross-section's depth. Beyond it the bank rises
    /// to the valley floor, the floor reaches out by the rock's erodibility, and the walls ease back to the natural
    /// ground (never raising it). Cells another channel already carved keep that channel.
    /// </summary>
    private static void CarveChannel(Canvas canvas, int index, Channel channel, System.Func<int, float> surfaceAt,
        System.Func<int, float> widthAt, float nominalWidth, float floorAbove, RiverBedSettings p)
    {
        float sharp = 1f / Mathf.Max(1f, p.sharpBendWidths * nominalWidth);
        for (int i = 0; i < canvas.Natural.Length; i++)
        {
            Nearest near = channel.Near[i];
            if (near.Distance == float.MaxValue)
                continue;
            if (canvas.ChannelOf[i] >= 0 && canvas.ChannelOf[i] != index)
                continue;
            float width = widthAt(i), half = width * 0.5f;
            float curvature = channel.At(channel.Curvature, near);
            // Outer bank: the side away from the bend's centre (the centre lies on the side the curvature points to).
            float bend = Mathf.Clamp01(Mathf.Abs(curvature) / sharp);
            bool outer = curvature * near.Side < 0f;
            float bank = p.bankWidth * Mathf.Lerp(1f, outer ? p.outerBankFactor : p.innerBankFactor, bend);
            float erodibility = canvas.RockStrength == null ? 0.5f : Mathf.Clamp01(1f - canvas.RockStrength[i]);
            ReachRegime regime = channel.RegimeAt(near);
            float floorEdge = half + bank + Mathf.Min(regime.Floor * nominalWidth * erodibility, p.maxFloorCells);
            if (near.Distance > floorEdge + p.wallWidth)
                continue;
            float surface = surfaceAt(i);
            if (near.Distance <= half)
            {
                float bed = surface - regime.Depth * Depth(near.Distance / half, outer, bend, channel.At(channel.Pool, near), width, p);
                bool mine = canvas.ChannelOf[i] == index;
                canvas.Carved[i] = mine ? Mathf.Min(canvas.Carved[i], bed) : bed;
                canvas.Level[i] = mine ? Mathf.Max(canvas.Level[i], surface) : surface;
                canvas.ChannelOf[i] = index;
                canvas.Flow[i] = channel.Direction(near);
                continue;
            }
            if (canvas.ChannelOf[i] == index)
                continue;
            float floor = surface + floorAbove + p.floorSlope * Mathf.Max(0f, near.Distance - half - bank), target;
            if (near.Distance <= half + bank)
                target = Mathf.Lerp(surface, floor, Smooth((near.Distance - half) / bank));
            else if (near.Distance <= floorEdge)
                target = floor;
            else
                target = Mathf.Lerp(floor, canvas.Natural[i], Smooth((near.Distance - floorEdge) / p.wallWidth));
            canvas.Carved[i] = Mathf.Min(canvas.Carved[i], target);
        }
    }

    private static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
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
    /// Pool (1) or riffle (0) at each point, for channels wide enough to have them (otherwise all pool: an unbroken
    /// deep thread). Where the channel bends, pools lie at the bends and riffles at the crossings between them; on
    /// straight reaches they alternate every <see cref="RiverBedSettings.poolSpacingWidths"/>.
    /// </summary>
    private static float[] Pools(float[] cumulative, float[] curvature, float width, RiverBedSettings p)
    {
        int n = cumulative.Length;
        var pool = new float[n];
        if (width < p.riffleMinWidth)
        {
            for (int i = 0; i < n; i++)
                pool[i] = 1f;
            return pool;
        }
        float sharp = 1f / Mathf.Max(1f, p.sharpBendWidths * width);
        float spacing = p.poolSpacingWidths * width;
        var bend = new float[n];
        for (int i = 0; i < n; i++)
            bend[i] = Mathf.Clamp01(Mathf.Abs(curvature[i]) / sharp);
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
    private static void CarveHollow(Canvas canvas, Channel channel, float floor, float half, RiverBedSettings p)
    {
        for (int i = 0; i < canvas.Natural.Length; i++)
        {
            float d = channel.Near[i].Distance;
            if (d > half + p.bankWidth || canvas.ChannelOf[i] >= 0)
                continue;
            canvas.Carved[i] = Mathf.Min(canvas.Carved[i], Mathf.Lerp(floor, canvas.Natural[i], Smooth((d - half) / p.bankWidth)));
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
            if (nearest[i].Distance > Mathf.Max(halfWidth, 1f))
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

    /// <summary>
    /// Each cell's distance to a polyline, position along it, nearest segment and which side the cell is on. Cells
    /// farther than <paramref name="reach"/> from the line's bounds are skipped (distance <see cref="float.MaxValue"/>).
    /// </summary>
    private static Nearest[] NearestTo(List<Vector2> line, float[] cumulative, float reach, int sizeX, int sizeZ)
    {
        var result = new Nearest[sizeX * sizeZ];
        for (int i = 0; i < result.Length; i++)
            result[i].Distance = float.MaxValue;
        Vector2 lo = line[0], hi = line[0];
        foreach (Vector2 v in line)
        {
            lo = Vector2.Min(lo, v);
            hi = Vector2.Max(hi, v);
        }
        int x0 = Mathf.Max(0, Mathf.FloorToInt(lo.x - reach)), x1 = Mathf.Min(sizeX - 1, Mathf.CeilToInt(hi.x + reach));
        int z0 = Mathf.Max(0, Mathf.FloorToInt(lo.y - reach)), z1 = Mathf.Min(sizeZ - 1, Mathf.CeilToInt(hi.y + reach));
        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
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
