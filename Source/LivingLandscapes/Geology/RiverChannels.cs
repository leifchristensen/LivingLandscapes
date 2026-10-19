using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>
/// Channel process settings (part of <see cref="GeologyGenerationExtension"/>). Lengths in map cells. How much each
/// process acts on a reach comes from its channel regime (<see cref="ChannelRegimeDef"/>); these are the physics.
/// </summary>
public class RiverChannelSettings
{
    /// <summary>Largest spacing of the centre line's points while a channel is shaped (half a width for narrow ones).</summary>
    public float spacing = 2f;

    /// <summary>Channels are classified in reaches this many widths long (at least <see cref="minReachLength"/> cells).</summary>
    public float reachWidths = 5f;
    public float minReachLength = 20f;

    /// <summary>
    /// Meander migration (Howard-Knutson). Each step moves the bank outward at bends, weighted by the curvature
    /// upstream, so bends grow and travel downstream. A reach's regime sets the share of full migration it gets.
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
    /// Where a split can happen (rule V5): valley floor (ground within <see cref="floodDepth"/> metres of the channel)
    /// at least <see cref="splitConfinement"/> channel widths wide. The reach's regime decides how likely a split is
    /// and how long its island; regimes that split where the valley opens out test the reach field Opening (floor
    /// width over its narrowest in the last <see cref="upstreamWidths"/> widths upstream).
    /// </summary>
    public float splitConfinement = 6f;
    public float upstreamWidths = 10f;
    public float floodDepth = 1.5f;

    /// <summary>Within this many channel widths of the coast (base level) the channel fans into distributaries that reach the sea.</summary>
    public float deltaWidths = 8f;

    /// <summary>The coastline lies this share of the map's size inside the map edge facing the sea (vanilla's coast: 0.1-0.2).</summary>
    public float coastOffset = 0.15f;

    /// <summary>Shortest island (cells), and the width each branch keeps.</summary>
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

    /// <summary>The channel regime at each main point.</summary>
    public List<ReachRegime> MainRegime = new List<ReachRegime>();

    public List<SideBranch> Sides = new List<SideBranch>();

    /// <summary>Abandoned loops (oxbows) left by cutoffs; the lake model decides whether they hold water.</summary>
    public List<List<Vector2>> Oxbows = new List<List<Vector2>>();

    public float NominalWidth;

    /// <summary>Index of the channel this one joins at its last point (-1: it leaves the map).</summary>
    public int Parent = -1;

    /// <summary>Drainage area (map cells) the channel brings in from beyond the map; 0 if it starts inside.</summary>
    public float EntryArea;

    public bool WorldRiver;

    /// <summary>Vanilla's width for a world river along this channel (0 for the map's own channels).</summary>
    public float VanillaWidth;

    /// <summary>A lake the channel rises from (headwater): its centre and radius; radius 0 if none.</summary>
    public Vector2 SourceLake;
    public float SourceLakeRadius;

    /// <summary>Each reach's fields and regime, upstream to downstream (for logs and the variety check).</summary>
    public List<Dictionary<string, float>> ReachFields = new List<Dictionary<string, float>>();
    public List<ReachRegime> Reaches = new List<ReachRegime>();

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
    public WaterRegimeDef Regime;

    public List<RiverPlan> Channels = new List<RiverPlan>();

    /// <summary>Water surface height in each cell of a live channel (NaN elsewhere), set when the channels are carved.</summary>
    public float[] WaterLevel;

    /// <summary>Downstream direction (unit) in each cell of a live channel, set when the channels are carved.</summary>
    public Vector2[] Flow;

    /// <summary>Channel index in each cell of a live channel (-1 elsewhere).</summary>
    public int[] ChannelOf;

    /// <summary>Cells of a channel's still water (a source lake): no current.</summary>
    public bool[] Still;

    /// <summary>
    /// Drainage lines whose water the ground absorbs before it can run as a channel at least two cells wide: seeps,
    /// saturated ground (marsh, mud) rather than moving water.
    /// </summary>
    public bool[] Seep;

    public RiverPlan WorldRiver => Channels.Find(c => c.WorldRiver);
}

/// <summary>
/// Channel patterns. Every channel of the network is shaped by the same processes: a wobble at its own scale, meander
/// migration (bends erode their outer bank, held back by rock and valley walls), and splits where the valley opens
/// out. How much each process acts is set per reach by its channel regime, chosen from the reach's own data
/// (discharge, slope, confinement, bank strength, sediment, base level) and weighed by the map's water regime, so
/// channels differ between reaches and between maps (Docs/WATER-REGIMES.md). Widths come from the world river's
/// vanilla class or the stream order of the map's own channels. Near the sea the channel fans into distributaries.
/// </summary>
public static class RiverChannels
{
    /// <summary>What the planning reads besides each channel's course.</summary>
    public class Context
    {
        public float[] Height, RockStrength;
        public int SizeX, SizeZ;
        public float Climate, Relief;
        public Vector2 SeaDirection;

        /// <summary>Where the world rivers meet, in map cells (a headwater's lake lies here).</summary>
        public Vector2 RiverCentre;
        public WaterRegimeDef Water;
        public RiverNetworkSettings Network;
        public RiverChannelSettings Channels;
    }

    /// <summary>Plans every channel of the network, parents before tributaries, each tributary ending on its parent.</summary>
    public static RiverSystem PlanNetwork(List<ChannelCourse> courses, Context c)
    {
        var system = new RiverSystem { Regime = c.Water };
        if (courses == null)
            return system;
        bool worldRiver = courses.Exists(course => course.VanillaWidth > 0f);
        var planned = new RiverPlan[courses.Count];
        for (int k = 0; k < courses.Count; k++)
        {
            ChannelCourse course = courses[k];
            RiverPlan parent = course.Parent >= 0 ? planned[course.Parent] : null;
            // World rivers keep vanilla's order of classes, compressed to map scale; the map's own channels by stream order.
            float width = course.VanillaWidth > 0f
                ? c.Network.worldRiverWidth.Evaluate(course.VanillaWidth)
                : c.Network.widthByOrder.Evaluate(course.Order);
            if (parent != null && course.VanillaWidth <= 0f)
                width = Mathf.Min(width, parent.NominalWidth * c.Network.tributaryShare);
            if (!worldRiver)
                width = Mathf.Min(width, c.Network.maxWidthWithoutWorldRiver);
            List<Vector2> line = RiverCourse.FromPlan(course.Points, c.SizeX, c.SizeZ);
            if (line == null)
                continue;
            // Headwater: the world river rises from a lake where the rivers would meet (the river centre); its course
            // above the lake is dropped, and channels that joined it there now feed the lake.
            float sourceLake = course.WorldRiver && c.Water != null ? c.Water.sourceLakeWidths.RandomInRange * width * 0.5f : 0f;
            Vector2 source = Vector2.zero;
            if (sourceLake > 0f)
            {
                Vector2 centre = c.RiverCentre;
                int nearest = 0;
                for (int i = 1; i < line.Count; i++)
                {
                    if ((line[i] - centre).sqrMagnitude < (line[nearest] - centre).sqrMagnitude)
                        nearest = i;
                }
                line = line.GetRange(nearest, line.Count - nearest);
                if (line.Count < 2)
                    continue;
                source = line[0];
            }
            // A tributary ends on its parent's channel as planned, not where the model's grid put the junction: it is
            // cut where it comes closest to the parent and joined to it there. A junction beyond the map is left alone.
            if (parent != null && Inside(line[line.Count - 1], c.SizeX, c.SizeZ))
                JoinParent(line, parent.Main);
            else
                parent = null;
            bool fromEdge = sourceLake <= 0f && !Inside(course.Points[0], c.SizeX, c.SizeZ);
            RiverPlan plan = Plan(line, width, fromEdge, parent == null, course, c);
            if (plan.Main.Count < 2)
                continue;
            plan.Parent = parent != null ? system.Channels.IndexOf(parent) : -1;
            plan.EntryArea = fromEdge ? course.EntryArea : 0f;
            plan.WorldRiver = course.WorldRiver;
            plan.VanillaWidth = course.VanillaWidth;
            plan.SourceLake = source;
            plan.SourceLakeRadius = sourceLake;
            planned[k] = plan;
            system.Channels.Add(plan);
        }
        return system;
    }

    private static RiverPlan Plan(List<Vector2> course, float width, bool fromEdge, bool leavesMap, ChannelCourse source, Context c)
    {
        RiverChannelSettings p = c.Channels;
        int sizeX = c.SizeX, sizeZ = c.SizeZ;
        var plan = new RiverPlan { NominalWidth = width };
        if (course == null || course.Count < 2)
            return plan;
        float spacing = Mathf.Clamp(width * 0.5f, 1f, p.spacing);
        float BankErodibility(Vector2 v)
        {
            float strength = c.RockStrength == null ? 0.5f : c.RockStrength[Index(v, sizeX, sizeZ)];
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
        float[] floor = ValleyFloor(c.Height, sizeX, sizeZ, course);
        float AboveFloor(Vector2 v) => c.Height[Index(v, sizeX, sizeZ)] - floor[Index(v, sizeX, sizeZ)];

        // The channel's regimes, reach by reach along the course as the model drew it.
        List<Vector2> initial = Resample(course, spacing);
        List<float> reachEnds = ClassifyReaches(plan, initial, width, spacing, leavesMap, source, c, BankErodibility);
        ReachRegime RegimeAt(float fraction)
        {
            for (int r = 0; r < reachEnds.Count; r++)
            {
                if (fraction <= reachEnds[r])
                    return plan.Reaches[r];
            }
            return plan.Reaches[plan.Reaches.Count - 1];
        }

        var extended = new List<Vector2>(course);
        float extension = 0f;
        if (fromEdge)
        {
            Vector2 back = (course[0] - course[1]).normalized;
            extension = p.upstreamMaps * Mathf.Max(sizeX, sizeZ);
            extended.Insert(0, course[0] + back * extension);
        }
        List<Vector2> points = Resample(extended, spacing);
        float courseLength = Length(course);
        // Along the extended line, the part upstream of the map takes the first reach's regime.
        ReachRegime RegimeAlong(float along) => RegimeAt(Mathf.Clamp01((along - extension) / Mathf.Max(courseLength, 1f)));

        Wobble(points, width, RegimeAlong);
        for (int step = 0; step < p.meanderSteps; step++)
        {
            float[] cumulative = Cumulative(points);
            float total = cumulative[cumulative.Length - 1];
            float scaleToCourse = (courseLength + extension) / Mathf.Max(total, 1f);
            Migrate(points, width, spacing, p, i => Erodibility(points[i]) * RegimeAlong(cumulative[i] * scaleToCourse).Meander,
                AboveFloor, v => Inside(v, sizeX, sizeZ));
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
        float[] along = Cumulative(points);
        float length = Mathf.Max(along[along.Length - 1], 1f);
        for (int i = 0; i < points.Count; i++)
        {
            plan.MainWidth.Add(width);
            plan.MainRegime.Add(RegimeAt(along[i] / length));
        }
        Split(plan, c, width, spacing, source.WorldRiver, Erodibility);
        return plan;
    }

    /// <summary>
    /// Cuts the course into reaches, measures each reach's fields from the map's data and gives it the channel regime
    /// the water regime favours among those that match. A lone reach between two of the same regime takes theirs, so
    /// regimes come in stretches. Returns each reach's end as a fraction of the course length.
    /// </summary>
    private static List<float> ClassifyReaches(RiverPlan plan, List<Vector2> points, float width, float spacing, bool leavesMap,
        ChannelCourse source, Context c, System.Func<Vector2, float> erodibility)
    {
        RiverChannelSettings p = c.Channels;
        int n = points.Count, sizeX = c.SizeX, sizeZ = c.SizeZ;
        float[] cumulative = Cumulative(points);
        float total = Mathf.Max(cumulative[n - 1], 1f);
        float reachLength = Mathf.Max(p.minReachLength, p.reachWidths * width);
        int reaches = Mathf.Max(1, Mathf.RoundToInt(total / reachLength));

        var ground = new float[n];
        for (int i = 0; i < n; i++)
            ground[i] = c.Height[Index(points[i], sizeX, sizeZ)];
        ground = MovingAverage(ground, Mathf.CeilToInt(Mathf.Max(10f, 2f * width) / spacing));

        var regimes = new ChannelRegimeDef[reaches];
        var confinements = new float[reaches];
        int upstreamReaches = Mathf.Max(1, Mathf.RoundToInt(p.upstreamWidths * width / reachLength));
        var ends = new List<float>();
        float upstreamErodibility = 0f;
        int upstreamCount = 0;
        float maxScan = 4f * width + 20f;
        for (int r = 0; r < reaches; r++)
        {
            int a = Mathf.Clamp(Mathf.FloorToInt((float)r / reaches * (n - 1)), 0, n - 1);
            int b = Mathf.Clamp(Mathf.FloorToInt((float)(r + 1) / reaches * (n - 1)), 0, n - 1);
            float confinement = 0f, strength = 0f;
            int samples = 0;
            for (int i = a; i <= b; i++)
            {
                Vector2 v = points[i];
                float e = erodibility(v);
                upstreamErodibility += e;
                upstreamCount++;
                strength += 1f - e;
                Vector2 t = (points[Mathf.Min(i + 1, n - 1)] - points[Mathf.Max(i - 1, 0)]).normalized;
                var normal = new Vector2(-t.y, t.x);
                float flood = c.Height[Index(v, sizeX, sizeZ)] + p.floodDepth;
                confinement += (Room(v, normal, flood, maxScan, c.Height, sizeX, sizeZ) + Room(v, -normal, flood, maxScan, c.Height, sizeX, sizeZ)) / width;
                samples++;
            }
            confinements[r] = confinement / Mathf.Max(samples, 1);
            float narrowest = confinements[r];
            for (int k = Mathf.Max(0, r - upstreamReaches); k < r; k++)
                narrowest = Mathf.Min(narrowest, confinements[k]);
            Vector2 middle = points[(a + b) / 2];
            float remaining = (total - cumulative[(a + b) / 2]) / width;
            float baseLevel = Mathf.Min(leavesMap ? remaining : 999f, CoastDistance(middle, c) / width);
            var fields = new Dictionary<string, float>
            {
                ["Discharge"] = Mathf.Log10(Mathf.Max(1f, source.AreaNear(middle) * c.Climate)),
                ["Slope"] = Mathf.Max(0f, (ground[a] - ground[b]) / Mathf.Max(cumulative[b] - cumulative[a], 1f)),
                ["Confinement"] = confinements[r],
                ["Opening"] = confinements[r] / Mathf.Max(narrowest, 0.1f),
                ["BankStrength"] = strength / Mathf.Max(samples, 1),
                ["Sediment"] = upstreamErodibility / Mathf.Max(upstreamCount, 1) * c.Relief / 100f,
                ["BaseLevel"] = baseLevel,
                ["Order"] = source.Order,
            };
            plan.ReachFields.Add(fields);
            regimes[r] = WaterRegimes.Classify(fields, c.Water);
            ends.Add(cumulative[b] / total);
        }
        for (int r = 1; r < reaches - 1; r++)
        {
            if (regimes[r - 1] == regimes[r + 1] && regimes[r] != regimes[r - 1])
                regimes[r] = regimes[r - 1];
        }

        // Each regime's parameters are drawn once per channel, so its reaches look alike.
        var drawn = new Dictionary<ChannelRegimeDef, ReachRegime>();
        var fallback = new ChannelRegimeDef { defName = "Default" };
        foreach (ChannelRegimeDef def in regimes)
        {
            ChannelRegimeDef key = def ?? fallback;
            if (!drawn.TryGetValue(key, out ReachRegime regime))
                drawn[key] = regime = ReachRegime.Draw(key);
            plan.Reaches.Add(regime);
        }
        return ends;
    }

    /// <summary>
    /// The wobble every channel has (rule V4): amplitude and wavelength in channel widths from each reach's regime.
    /// The noise is read by phase, so the wavelength can change along the channel without breaking it.
    /// </summary>
    private static void Wobble(List<Vector2> points, float width, System.Func<float, ReachRegime> regimeAlong)
    {
        var noise = new Perlin(1.0, 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);
        float along = 0f, phase = 0f;
        var moved = new Vector2[points.Count];
        for (int i = 1; i < points.Count - 1; i++)
        {
            float step = (points[i] - points[i - 1]).magnitude;
            along += step;
            ReachRegime regime = regimeAlong(along);
            phase += step / Mathf.Max(regime.Wavelength * width, 1f);
            Vector2 t = (points[i + 1] - points[i - 1]).normalized;
            moved[i] = new Vector2(-t.y, t.x) * (float)noise.GetValue(phase, 0.0, 0.0) * regime.Wobble * width;
        }
        for (int i = 1; i < points.Count - 1; i++)
            points[i] += moved[i];
    }

    /// <summary>One migration step: each point moves toward the outside of its bend, by local and upstream curvature.</summary>
    private static void Migrate(List<Vector2> points, float scale, float spacing, RiverChannelSettings p,
        System.Func<int, float> mobility, System.Func<Vector2, float> aboveFloor, System.Func<Vector2, bool> inside)
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
            float distance = Mathf.Clamp(p.migrationRate * m * scale, -1f, 1f) * p.migrationStepWidths * scale * mobility(i);
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
    /// Islands (rule V5). Where the valley floor is wide enough, the reach's regime decides whether the channel
    /// splits and how long the island is: the main channel narrows and a side branch takes part of the flow,
    /// bowing toward the side with more room and rejoining downstream. Near a sea edge the channel fans into
    /// distributaries on both sides that reach the sea.
    /// </summary>
    private static void Split(RiverPlan plan, Context c, float width, float spacing, bool worldRiver, System.Func<Vector2, float> erodibility)
    {
        RiverChannelSettings p = c.Channels;
        int sizeX = c.SizeX, sizeZ = c.SizeZ;
        List<Vector2> pts = plan.Main;
        int n = pts.Count;
        var left = new float[n];
        var right = new float[n];
        float maxScan = 4f * width + 20f;
        for (int i = 1; i < n - 1; i++)
        {
            Vector2 t = (pts[i + 1] - pts[i - 1]).normalized;
            var normal = new Vector2(-t.y, t.x);
            float flood = c.Height[Index(pts[i], sizeX, sizeZ)] + p.floodDepth;
            left[i] = Room(pts[i], normal, flood, maxScan, c.Height, sizeX, sizeZ);
            right[i] = Room(pts[i], -normal, flood, maxScan, c.Height, sizeX, sizeZ);
        }

        // Confinement (floor width over channel width), smoothed over a channel width.
        var confinement = new float[n];
        for (int i = 0; i < n; i++)
            confinement[i] = (left[i] + right[i]) / width;
        confinement = MovingAverage(confinement, Mathf.CeilToInt(width / spacing));

        bool Interior(Vector2 v) => v.x > width && v.y > width && v.x < sizeX - width && v.y < sizeZ - width;
        bool HasRoom(int i) => Interior(pts[i]) && confinement[i] >= p.splitConfinement && erodibility(pts[i]) >= p.minIslandErodibility;
        float branchWidth = width * p.branchWidthShare;

        // Delta: distributaries from where the channel comes within reach of the sea, or of its exit on a delta map.
        float[] along = Cumulative(pts);
        float total = along[n - 1];
        bool exitDelta = worldRiver && c.Water != null && c.Water.deltaAtExit;
        int delta = -1;
        for (int i = 0; i < n && delta < 0; i++)
        {
            if (!Inside(pts[i], sizeX, sizeZ))
                continue;
            float reach = Mathf.Min(CoastDistance(pts[i], c), exitDelta ? total - along[i] : float.MaxValue);
            if (reach <= p.deltaWidths * width)
                delta = i;
        }
        if (delta > 0 && delta < n - 3)
        {
            int to = n - 1;
            int branches = exitDelta ? c.Water.deltaBranches.RandomInRange : 2;
            float spread = (exitDelta ? c.Water.deltaSpreadWidths.RandomInRange : 2f) * width;
            int perSide = Mathf.Max(1, Mathf.CeilToInt(branches * 0.5f));
            for (int b = 0; b < branches; b++)
            {
                float side = b % 2 == 0 ? 1f : -1f;
                float offset = spread * (b / 2 + 1) / perSide;
                var branch = new RiverPlan.SideBranch { Width = branchWidth, From = delta, To = to, ToBaseLevel = true };
                for (int k = delta; k <= to; k++)
                {
                    float f = (k - delta) / (float)(to - delta);
                    Vector2 t = (pts[Mathf.Min(k + 1, n - 1)] - pts[Mathf.Max(k - 1, 0)]).normalized;
                    branch.Points.Add(pts[k] + new Vector2(-t.y, t.x) * side * Mathf.Pow(f, 0.6f) * offset);
                }
                // Every distributary runs on to the edge (the base level), even where the fan's spread pulls it inward.
                Vector2 heading = (pts[to] - pts[Mathf.Max(to - 1, 0)]).normalized;
                Vector2 tail = branch.Points[branch.Points.Count - 1];
                for (int guard = 0; Inside(tail, sizeX, sizeZ) && guard < sizeX + sizeZ; guard++)
                    tail += heading * spacing;
                if (tail != branch.Points[branch.Points.Count - 1])
                    branch.Points.Add(tail);
                plan.Sides.Add(branch);
            }
            for (int k = delta; k <= to; k++)
                plan.MainWidth[k] = branchWidth;
            n = delta;
        }

        // River island: one large island near the map's centre.
        int islandFrom = -1, islandTo = -1;
        float islandShare = worldRiver && c.Water != null ? c.Water.centralIsland.RandomInRange : 0f;
        if (islandShare > 0f)
        {
            var centre = new Vector2(sizeX * 0.5f, sizeZ * 0.5f);
            int middle = 1;
            for (int i = 1; i < n - 1; i++)
            {
                if ((pts[i] - centre).sqrMagnitude < (pts[middle] - centre).sqrMagnitude)
                    middle = i;
            }
            int half = Mathf.CeilToInt(islandShare * Mathf.Max(sizeX, sizeZ) * 0.5f / spacing);
            islandFrom = Mathf.Max(1, middle - half);
            islandTo = Mathf.Min(n - 2, middle + half);
            if (islandTo - islandFrom > 4)
            {
                float side = left[middle] >= right[middle] ? 1f : -1f;
                float offset = c.Water.centralIslandWidths.RandomInRange * width;
                var branch = new RiverPlan.SideBranch { Width = branchWidth, From = islandFrom, To = islandTo };
                for (int k = islandFrom; k <= islandTo; k++)
                {
                    float f = (k - islandFrom) / (float)(islandTo - islandFrom);
                    Vector2 t = (pts[k + 1] - pts[k - 1]).normalized;
                    branch.Points.Add(pts[k] + new Vector2(-t.y, t.x) * side * offset * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * f)), 0.6f));
                    plan.MainWidth[k] = branchWidth;
                }
                plan.Sides.Add(branch);
            }
        }

        // Islands that rejoin, where there is room and the reach's regime splits; the next may start after one ends.
        for (int from = 1; from < n - 2; from++)
        {
            if (!HasRoom(from) || plan.MainRegime[from].SplitChance <= 0f)
                continue;
            ReachRegime regime = plan.MainRegime[from];
            int islandPoints = Mathf.CeilToInt(Mathf.Max(p.minIslandLength, regime.IslandWidths * width) / spacing);
            int to = from + islandPoints;
            if (to >= n - 1)
                break;
            if (islandTo >= 0 && to >= islandFrom && from <= islandTo)
            {
                from = islandTo;
                continue;
            }
            if (Rand.Value >= regime.SplitChance)
            {
                from += islandPoints / 2;
                continue;
            }
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

    /// <summary>Distance (cells) to the coastline: the map edge facing the sea, less vanilla's coast offset.</summary>
    private static float CoastDistance(Vector2 v, Context c) =>
        c.SeaDirection == Vector2.zero ? float.MaxValue
            : Mathf.Max(0f, SeaDistance(v, c.SeaDirection, c.SizeX, c.SizeZ) - c.Channels.coastOffset * Mathf.Max(c.SizeX, c.SizeZ));

    private static float SeaDistance(Vector2 v, Vector2 seaDirection, int sizeX, int sizeZ)
    {
        float d = float.MaxValue;
        if (seaDirection.x > 0.5f) d = Mathf.Min(d, sizeX - v.x);
        if (seaDirection.x < -0.5f) d = Mathf.Min(d, v.x);
        if (seaDirection.y > 0.5f) d = Mathf.Min(d, sizeZ - v.y);
        if (seaDirection.y < -0.5f) d = Mathf.Min(d, v.y);
        return d;
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

    private static float[] Cumulative(List<Vector2> line)
    {
        var cumulative = new float[line.Count];
        for (int i = 1; i < line.Count; i++)
            cumulative[i] = cumulative[i - 1] + (line[i] - line[i - 1]).magnitude;
        return cumulative;
    }

    private static float Length(List<Vector2> line)
    {
        float length = 0f;
        for (int i = 1; i < line.Count; i++)
            length += (line[i] - line[i - 1]).magnitude;
        return length;
    }

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
