using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>
/// Stage 1, drainage and inflow: where water comes from and which way it runs. The landform gets its micro-relief and the
/// world river's trench, hollows are filled toward their spill points (the map edge drains except near water entering,
/// and on a coast only beside the sea), and every cell gets its steepest-descent receiver. Water entering from beyond the
/// map is placed on the side the world says it comes from, at the discharge that gives its vanilla width; on a tile without
/// a world river the map's own discharge is scaled to the tile's world flow (a share of a creek).
/// </summary>
public static partial class TerrainChannels
{
    /// <summary>
    /// The drainage the channels follow: water entering the map, the depression-filled landform and each cell's
    /// steepest-descent receiver. The map edge drains, except where water enters: there the land beyond slopes in.
    /// Water reaching a draining edge leaves the map there rather than running along the edge.
    /// </summary>
    private static void Drain(float[] height, int sizeX, int sizeZ, List<ChannelCourse> courses, Vector2 riverCentre, float runoff,
        WaterRegimeDef water, TerrainChannelSettings p, SimpleCurve worldRiverWidth, out float[] filled, out int[] receiver,
        out float[] inflow, out bool[] worldSource, out float vanillaWidth, out System.Func<int, bool> outlet,
        List<WorldContext.RiverInflow> worldInflows = null, bool[] sea = null, bool riverMouth = false, float seaLevel = float.NaN)
    {
        int n = sizeX * sizeZ;
        float sourceRadius = (water?.centreRadiusShare ?? 0f) * Mathf.Max(sizeX, sizeZ) * p.sourceRadiusShare;
        inflow = Inflow(courses, sizeX, sizeZ, riverCentre, sourceRadius, runoff, water?.riverInflow ?? true, p, worldRiverWidth,
            out worldSource, out vanillaWidth, worldInflows);
        var entries = new List<Vector2>();
        for (int i = 0; i < n; i++)
        {
            int x = i % sizeX, z = i / sizeX;
            // A world river's entry cell lies a cell or two inside the edge (its course is cut to the map), so near the edge counts.
            if (inflow[i] > 0f && (x <= 2 || z <= 2 || x >= sizeX - 3 || z >= sizeZ - 3))
                entries.Add(new Vector2(x, z));
        }
        // On a coast the sea is the base level (P1): water that reaches it ends there, so rivers find their own way down to it.
        // The map edge drains only near the sea (the land beyond the other edges rises inland), so rivers run into the sea
        // rather than leaving through a land edge beside it.
        float[] toSea = sea != null ? GridMath.DistanceTo(i => sea[i], sizeX, sizeZ) : null;
        bool IsOutlet(int i)
        {
            // The sea's water, not its beach (the coast mask holds both): ground below the sea's surface.
            if (sea != null && sea[i] && (float.IsNaN(seaLevel) || height[i] < seaLevel))
                return true;
            int x = i % sizeX, z = i / sizeX;
            if (x != 0 && z != 0 && x != sizeX - 1 && z != sizeZ - 1)
                return false;
            if (toSea != null && toSea[i] > p.coastEdgeDrain)
                return false;
            var v = new Vector2(x, z);
            return entries.TrueForAll(e => (e - v).sqrMagnitude > p.entryGuardCells * p.entryGuardCells);
        }
        // Flats keep a little micro-relief, so water wanders across them instead of running straight or at 45 degrees;
        // on slopes it is far below the fall of the ground and changes nothing.
        filled = (float[])height.Clone();
        var microRelief = new Perlin(1.0 / p.flatNoiseWavelength, 2.0, 0.5, 3, p.flatNoiseSeed, QualityMode.Medium);
        for (int i = 0; i < n; i++)
            filled[i] += p.flatNoise * (float)microRelief.GetValue(i % sizeX, 0.0, i / sizeX);
        foreach (ChannelCourse course in courses ?? new List<ChannelCourse>())
        {
            // A river rising on the map (headwater) still runs its course to a mouth at the sea.
            bool toMouth = riverMouth && course.WorldRiver && toSea != null;
            if (course.VanillaWidth > 0f && ((water?.riverInflow ?? true) || toMouth))
            {
                ChannelCourse laid = toMouth ? ToTheSea(course, sea, toSea, sizeX, sizeZ) : course;
                Trench(filled, sizeX, sizeZ, laid, p);
                if (toMouth && laid.Points.Count > 0)
                {
                    Vector2 a = laid.Points[0], b = laid.Points[laid.Points.Count - 1];
                    int end = Mathf.Clamp(Mathf.RoundToInt(b.y - 0.5f), 0, sizeZ - 1) * sizeX + Mathf.Clamp(Mathf.RoundToInt(b.x - 0.5f), 0, sizeX - 1);
                    Log.Message($"[LivingLandscapes] World river course to the sea: {laid.Points.Count} points from ({a.x:F0}, {a.y:F0}) to ({b.x:F0}, {b.y:F0}), " +
                                (sea[end] ? "ending in the sea." : $"ending on land {toSea[end]:F0} cells from the sea."));
                }
            }
        }
        outlet = IsOutlet;
        GridMath.FillDepressions(filled, sizeX, sizeZ, IsOutlet, p.fillGradient, includeEdges: false);
        receiver = GridMath.Receivers(filled, sizeX, sizeZ);
        for (int i = 0; i < n; i++)
        {
            if (IsOutlet(i))
                receiver[i] = -1;
        }
    }

    /// <summary>
    /// A world river whose mouth is at the sea: the erosion model lays its course to the map's edge toward the sea before the
    /// coastline is shaped, so the course may end on land at the edge (draining there, off the map) or run on over the sea. It is
    /// cut where it first enters the sea, or else at its point nearest the sea; from there the drainage carries the river down to
    /// the sea, which is base level (P1), along the ground rather than a drawn line.
    /// </summary>
    private static ChannelCourse ToTheSea(ChannelCourse course, bool[] sea, float[] toSea, int sizeX, int sizeZ)
    {
        int Cell(Vector2 v) => Mathf.Clamp(Mathf.RoundToInt(v.y - 0.5f), 0, sizeZ - 1) * sizeX + Mathf.Clamp(Mathf.RoundToInt(v.x - 0.5f), 0, sizeX - 1);
        int nearest = 0;
        for (int k = 0; k < course.Points.Count; k++)
        {
            int c = Cell(course.Points[k]);
            if (sea[c])
            {
                nearest = k;
                break;
            }
            if (toSea[c] < toSea[Cell(course.Points[nearest])])
                nearest = k;
        }
        return new ChannelCourse { Points = course.Points.GetRange(0, nearest + 1), WorldRiver = true, VanillaWidth = course.VanillaWidth, Order = course.Order, EntryArea = course.EntryArea };
    }

    /// <summary>Lowers the ground along a course (see <see cref="TerrainChannelSettings.worldCourseTrench"/>), deepest on the line.</summary>
    private static void Trench(float[] filled, int sizeX, int sizeZ, ChannelCourse course, TerrainChannelSettings p)
    {
        int reach = Mathf.CeilToInt(p.worldCourseTrenchWidth);
        var deepest = new float[filled.Length];
        foreach (Vector2 v in course.Points)
        {
            int cx = Mathf.RoundToInt(v.x - 0.5f), cz = Mathf.RoundToInt(v.y - 0.5f);
            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                        continue;
                    float share = 1f - Mathf.Sqrt(dx * dx + dz * dz) / (p.worldCourseTrenchWidth + 0.01f);
                    if (share > 0f)
                        deepest[z * sizeX + x] = Mathf.Max(deepest[z * sizeX + x], share * share);
                }
            }
        }
        for (int i = 0; i < filled.Length; i++)
            filled[i] -= p.worldCourseTrench * deepest[i];
    }

    /// <summary>
    /// The cell of the world river's course (steepest descent from where it enters) nearest to <paramref name="point"/>,
    /// or <paramref name="point"/> itself if no world river enters. Mutator structures that sit in the river (an
    /// island's knob) are placed there.
    /// </summary>
    public static Vector2 NearestOnWorldRiver(float[] height, int sizeX, int sizeZ, List<ChannelCourse> courses, Vector2 point,
        WaterRegimeDef water, TerrainChannelSettings p, SimpleCurve worldRiverWidth)
    {
        Drain(height, sizeX, sizeZ, courses, point, 1f, water, p, worldRiverWidth, out _, out int[] receiver, out _, out bool[] worldSource, out _, out _);
        Vector2 best = point;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < worldSource.Length; i++)
        {
            if (!worldSource[i])
                continue;
            for (int c = i, steps = 0; c >= 0 && steps < worldSource.Length; c = receiver[c], steps++)
            {
                var v = new Vector2(c % sizeX + 0.5f, c / sizeX + 0.5f);
                float d = (v - point).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = v;
                }
            }
        }
        return best;
    }

    /// <summary>The discharge that gives a world river of this vanilla width its width on the map.</summary>
    private static float DischargeFor(float vanillaWidth, SimpleCurve worldRiverWidth, TerrainChannelSettings p) =>
        Mathf.Pow(worldRiverWidth.Evaluate(vanillaWidth) / p.widthPerSqrtDischarge, 2f);

    /// <summary>
    /// Scale for the map's own discharge on a tile without a world river: its largest channel (routed without losses)
    /// carries the tile's world flow as a share of a creek's (<see cref="WorldFlow"/>), at most
    /// <see cref="TerrainChannelSettings.maxWidthWithoutWorldRiver"/> wide; never more than the map's own runoff gives.
    /// </summary>
    private static float OwnDischargeScale(float[] filled, int[] receiver, float[] inflow, bool[] worldSource, float runoff, float threshold,
        float mapSpread, int sizeX, int sizeZ, TerrainChannelSettings p, SimpleCurve worldRiverWidth, float worldFlow, float loss,
        out string summary)
    {
        float target = Mathf.Pow(p.maxWidthWithoutWorldRiver / p.widthPerSqrtDischarge, 2f);
        summary = "";
        RiverDef creek = WorldFlow.Creek();
        if (!float.IsNaN(worldFlow) && creek != null)
        {
            float share = worldFlow / creek.degradeThreshold;
            target = Mathf.Min(target, DischargeFor(creek.widthOnMap, worldRiverWidth, p) * share);
            summary = $", world flow {worldFlow:F0} = {share:P0} of a creek";
        }
        // The world flow is what leaves the tile, after the losses on the way: the scale is settled over a few rounds,
        // since losses depend on the channels' width.
        float scale = 1f;
        for (int round = 0; round < 4; round++)
        {
            Route(filled, receiver, inflow, worldSource, runoff, threshold, null, mapSpread, sizeX, sizeZ, p,
                out _, out _, out _, out _, out float[] carried, round == 0 ? 0f : loss, scale);
            float largest = 1f;
            foreach (float q in carried)
                largest = Mathf.Max(largest, q);
            scale = Mathf.Min(1f, target / largest);
        }
        return scale;
    }

    /// <summary>
    /// Discharge entering the map: world rivers with the discharge that gives their vanilla width, the region's other
    /// channels with the runoff of the area they drain. On a tile the world gives no river, nothing entering is larger
    /// than a creek, as in vanilla. A world river that does not enter (headwater) rises at the river centre.
    /// </summary>
    private static float[] Inflow(List<ChannelCourse> courses, int sizeX, int sizeZ, Vector2 riverCentre, float sourceRadius, float runoff,
        bool riverEnters, TerrainChannelSettings p, SimpleCurve worldRiverWidth, out bool[] worldSource, out float vanillaWidth,
        List<WorldContext.RiverInflow> worldInflows = null)
    {
        int n = sizeX * sizeZ;
        var inflow = new float[n];
        worldSource = new bool[n];
        vanillaWidth = 0f;
        float DischargeFor(float width) => TerrainChannels.DischargeFor(width, worldRiverWidth, p);
        float creek = DischargeFor(4f);
        bool anyWorldRiver = courses != null && courses.Exists(c => c.VanillaWidth > 0f);
        bool worldEnters = false;
        foreach (ChannelCourse course in courses ?? new List<ChannelCourse>())
        {
            vanillaWidth = Mathf.Max(vanillaWidth, course.VanillaWidth);
            if (course.EntryArea <= 0f)
                continue;
            int first = course.Points.FindIndex(v => v.x >= 0f && v.y >= 0f && v.x < sizeX && v.y < sizeZ);
            if (first < 0)
                continue;
            int cell = RiverGrid.Index(course.Points[first], sizeX, sizeZ);
            if (course.VanillaWidth > 0f)
            {
                // At a headwater the world river does not come from beyond the map: it rises here.
                if (!riverEnters)
                    continue;
                // The world says which sides the river comes from. An erosion course that starts elsewhere (beside the exit, say)
                // would hand the river over on the wrong side, and it would leave again as soon as it entered.
                if (worldInflows != null && worldInflows.Count > 0 && !worldInflows.Exists(f =>
                        Vector2.Dot((course.Points[first] - riverCentre).normalized, f.From) >= p.entryAgreement))
                {
                    Log.Message($"[LivingLandscapes] World river course entering at ({course.Points[first].x:F0}, {course.Points[first].y:F0}) " +
                                $"(world river {course.WorldRiver}) disagrees with the world's inflow sides " +
                                $"[{string.Join(", ", worldInflows.Select(f => $"({f.From.x:F2}, {f.From.y:F2})"))}]; not used as an entry.");
                    continue;
                }
                inflow[cell] += DischargeFor(course.VanillaWidth);
                worldSource[cell] = worldEnters = true;
            }
            else
                inflow[cell] += anyWorldRiver && riverEnters ? course.EntryArea * runoff : Mathf.Min(course.EntryArea * runoff, creek);
        }
        if (anyWorldRiver && !worldEnters && riverEnters && worldInflows != null && worldInflows.Count > 0)
        {
            // The erosion model's river does not cross the map edge (its course starts inside): the world says the river comes in,
            // so it enters at the edge facing the neighbour it comes from, as vanilla lays it, and finds its own way from there.
            foreach (WorldContext.RiverInflow from in worldInflows)
            {
                Vector2 edge = WaterTerrainNudges.EdgeToward(riverCentre, from.From, sizeX, sizeZ);
                int cell = RiverGrid.Index(edge, sizeX, sizeZ);
                inflow[cell] += DischargeFor(from.Width);
                worldSource[cell] = worldEnters = true;
            }
        }
        if (anyWorldRiver && !worldEnters)
        {
            // Springs feeding the river's source: its discharge rises over the hollow at the river centre (the lake
            // model fills it and the river leaves at its spill point), or at the centre cell if there is no hollow.
            var springs = new List<int>();
            for (int i = 0; i < n && sourceRadius > 0f; i++)
            {
                if ((new Vector2(i % sizeX + 0.5f, i / sizeX + 0.5f) - riverCentre).sqrMagnitude <= sourceRadius * sourceRadius)
                    springs.Add(i);
            }
            if (springs.Count == 0)
                springs.Add(RiverGrid.Index(riverCentre, sizeX, sizeZ));
            float each = DischargeFor(vanillaWidth) / springs.Count;
            foreach (int cell in springs)
            {
                inflow[cell] += each;
                worldSource[cell] = true;
            }
        }
        return inflow;
    }

    /// <summary>
    /// Fills hollows of the (changed) surface toward their spill points again and returns the new steepest-descent
    /// receivers, outlets draining off the map: after meanders or distributaries were carved into it.
    /// </summary>
    private static int[] Refill(float[] filled, int sizeX, int sizeZ, System.Func<int, bool> isOutlet, TerrainChannelSettings p)
    {
        GridMath.FillDepressions(filled, sizeX, sizeZ, isOutlet, p.fillGradient, includeEdges: false);
        int[] receiver = GridMath.Receivers(filled, sizeX, sizeZ);
        for (int i = 0; i < receiver.Length; i++)
        {
            if (isOutlet(i))
                receiver[i] = -1;
        }
        return receiver;
    }
}
