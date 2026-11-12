using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>
/// Stage 1, drainage and inflow: where water comes from and which way it runs. The landform gets its micro-relief and the
/// world river's trench, hollows are filled toward their spill points (the base levels of <see cref="Outlets"/>, P1: the sea,
/// the river's exit, and the map edge where the erosion model's water flows out), and every cell gets its steepest-descent receiver. Water entering from beyond the
/// map is placed on the side the world says it comes from, at the discharge that gives its vanilla width; on a tile without
/// a world river the map's own discharge is scaled to the tile's world flow (a share of a creek).
/// </summary>
public static partial class TerrainChannels
{
    /// <summary>
    /// The drainage (see <see cref="DrainCore"/>), with the world river kept on the world's own way out. The world map says
    /// where a river leaves a tile (<paramref name="worldExit"/>, the side vanilla draws it to, or the sea). When the drainage
    /// carries an entering river out anywhere else (back out beside its entry, or off another side), the river is laid along
    /// the cheapest way from its entry to that exit, and the drainage is made again with that way trenched: the ground
    /// still decides where the river runs between the two ends, and only the ends are the world's.
    /// </summary>
    private static void Drain(float[] height, int sizeX, int sizeZ, List<ChannelCourse> courses, Vector2 riverCentre, float runoff,
        WaterRegimeDef water, TerrainChannelSettings p, SimpleCurve worldRiverWidth, out float[] filled, out int[] receiver,
        out float[] inflow, out bool[] worldSource, out float vanillaWidth, out float[] baseLevel, out float yield, out float[] lakeLoss,
        List<WorldContext.RiverInflow> worldInflows = null, bool[] sea = null, bool riverMouth = false, float seaLevel = float.NaN,
        bool[] edgeOpen = null, float lakeShare = float.PositiveInfinity, bool[] sink = null,
        System.Func<float[], int[], float[], bool[], float> yieldScale = null, bool[] openHollows = null, Vector2 worldExit = default)
    {
        DrainCore(height, sizeX, sizeZ, courses, riverCentre, runoff, water, p, worldRiverWidth, out filled, out receiver, out inflow, out worldSource,
            out vanillaWidth, out baseLevel, out yield, out lakeLoss, worldInflows, sea, riverMouth, seaLevel, edgeOpen, lakeShare, sink, yieldScale,
            openHollows);
        if (worldExit == Vector2.zero || vanillaWidth <= 0f)
            return;
        List<Vector2> guide = null;
        for (int i = 0; i < sizeX * sizeZ; i++)
        {
            if (!worldSource[i] || !Outlets.IsEdge(i, sizeX, sizeZ) || LeavesOnTheWorldsSide(receiver, i, sizeX, sizeZ, sea, worldExit))
                continue;
            List<Vector2> way = WorldWay(height, i, sizeX, sizeZ, sea, riverMouth, worldExit);
            if (way == null)
                continue;
            Log.Message($"[LivingLandscapes] World river entering at ({i % sizeX},{i / sizeX}) left the map on another side than the world's; laying it along the ground to the world's exit ({way.Count} cells).");
            guide ??= new List<Vector2>();
            guide.AddRange(way);
        }
        if (guide != null)
        {
            DrainCore(height, sizeX, sizeZ, courses, riverCentre, runoff, water, p, worldRiverWidth, out filled, out receiver, out inflow, out worldSource,
                out vanillaWidth, out baseLevel, out yield, out lakeLoss, worldInflows, sea, riverMouth, seaLevel, edgeOpen, lakeShare, sink, yieldScale,
                openHollows, guide, worldExit);
        }
    }

    /// <summary>Whether the water entering at <paramref name="entry"/> runs down the receivers to the sea or to an edge cell on the world's exit side.</summary>
    private static bool LeavesOnTheWorldsSide(int[] receiver, int entry, int sizeX, int sizeZ, bool[] sea, Vector2 worldExit)
    {
        int c = entry;
        for (int steps = 0; receiver[c] >= 0 && steps < receiver.Length; steps++)
            c = receiver[c];
        return c != entry && IsExitCell(c, sizeX, sizeZ, sea, worldExit);
    }

    private static bool IsExitCell(int c, int sizeX, int sizeZ, bool[] sea, Vector2 worldExit)
    {
        if (sea != null && sea[c])
            return true;
        if (!Outlets.IsEdge(c, sizeX, sizeZ))
            return false;
        Vector2 fromCentre = new Vector2(c % sizeX - (sizeX - 1) * 0.5f, c / sizeX - (sizeZ - 1) * 0.5f);
        return Vector2.Dot(fromCentre.normalized, worldExit.normalized) >= ExitSideDot;
    }

    /// <summary>An edge cell is on the world's exit side when the direction to it from the map's centre is within 60 degrees of the exit's heading (invariant 11 uses the same).</summary>
    private const float ExitSideDot = 0.5f;

    /// <summary>
    /// The cheapest way over the ground from <paramref name="entry"/> to the world's exit (the sea for a river with its mouth
    /// there, else the edge cells on the exit side): each step costs its length plus a heavy price per metre climbed, so the
    /// way keeps to low ground. Cell centres; null if no exit cell can be reached.
    /// </summary>
    private static List<Vector2> WorldWay(float[] height, int entry, int sizeX, int sizeZ, bool[] sea, bool toSea, Vector2 worldExit)
    {
        int n = sizeX * sizeZ;
        const float climbCost = 12f;
        var cost = new float[n];
        var from = new int[n];
        for (int i = 0; i < n; i++)
        {
            cost[i] = float.MaxValue;
            from[i] = -1;
        }
        var open = new SortedSet<(float, int)>();
        cost[entry] = 0f;
        open.Add((0f, entry));
        int goal = -1;
        while (open.Count > 0)
        {
            (float c0, int c) = open.Min;
            open.Remove(open.Min);
            if (c0 > cost[c])
                continue;
            if (c != entry && (toSea && sea != null ? sea[c] : IsExitCell(c, sizeX, sizeZ, null, worldExit)))
            {
                goal = c;
                break;
            }
            int cx = c % sizeX, cz = c / sizeX;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if ((dx == 0 && dz == 0) || x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                        continue;
                    int q = z * sizeX + x;
                    float step = (dx != 0 && dz != 0 ? 1.4142f : 1f) + climbCost * Mathf.Max(0f, height[q] - height[c]);
                    if (c0 + step < cost[q])
                    {
                        cost[q] = c0 + step;
                        from[q] = c;
                        open.Add((cost[q], q));
                    }
                }
            }
        }
        if (goal < 0)
            return null;
        var way = new List<Vector2>();
        for (int c = goal; c >= 0; c = from[c])
            way.Add(new Vector2(c % sizeX + 0.5f, c / sizeX + 0.5f));
        way.Reverse();
        return way;
    }

    /// <summary>
    /// The drainage the channels follow: water entering the map, the landform filled to its spill levels and each cell's
    /// steepest-descent receiver. Water leaves where a cell has a base level (<see cref="MapBaseLevels"/>), once its surface
    /// reaches it; it leaves the map there rather than running along the edge.
    /// </summary>
    private static void DrainCore(float[] height, int sizeX, int sizeZ, List<ChannelCourse> courses, Vector2 riverCentre, float runoff,
        WaterRegimeDef water, TerrainChannelSettings p, SimpleCurve worldRiverWidth, out float[] filled, out int[] receiver,
        out float[] inflow, out bool[] worldSource, out float vanillaWidth, out float[] baseLevel, out float yield, out float[] lakeLoss,
        List<WorldContext.RiverInflow> worldInflows = null, bool[] sea = null, bool riverMouth = false, float seaLevel = float.NaN,
        bool[] edgeOpen = null, float lakeShare = float.PositiveInfinity, bool[] sink = null,
        System.Func<float[], int[], float[], bool[], float> yieldScale = null, bool[] openHollows = null, List<Vector2> guide = null, Vector2 worldExit = default)
    {
        int n = sizeX * sizeZ;
        if (sink != null)
            System.Array.Clear(sink, 0, sink.Length);
        float sourceRadius = (water?.centreRadiusShare ?? 0f) * Mathf.Max(sizeX, sizeZ) * p.sourceRadiusShare;
        inflow = Inflow(courses, sizeX, sizeZ, riverCentre, sourceRadius, runoff, water?.riverInflow ?? true, p, worldRiverWidth,
            out worldSource, out vanillaWidth, worldInflows, sea);
        float[] toSea = sea != null ? GridMath.DistanceTo(i => sea[i], sizeX, sizeZ) : null;
        var laidCourses = new List<(ChannelCourse course, bool toMouth)>();
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
            if (course.VanillaWidth > 0f)
                laidCourses.Add((course, toMouth));
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
        // A way laid from the world's own entry to its exit (see Drain): trenched like a drawn course.
        if (guide != null && guide.Count > 0)
            Trench(filled, sizeX, sizeZ, new ChannelCourse { Points = guide, WorldRiver = true }, p);
        baseLevel = MapBaseLevels(height, filled, sizeX, sizeZ, laidCourses, sea, seaLevel, edgeOpen, inflow, guide != null ? worldExit : default);
        var ground = (float[])filled.Clone();
        receiver = Refill(filled, sizeX, sizeZ, baseLevel, p);
        // The tile's one water budget (#216): where the world gives the tile no river, only the share of the map's own runoff
        // its world flow lets through reaches its channels, and the same share is what its lakes gather.
        yield = vanillaWidth <= 0f && yieldScale != null ? yieldScale(filled, receiver, inflow, worldSource) : 1f;
        lakeShare *= yield;
        // Closed basins keep their water (P23, #99), as in the erosion model: a hollow whose lake fits below its spill level
        // is an outlet at the lake's level, so no channel leaves it.
        if (!float.IsInfinity(lakeShare))
        {
            // A hollow the world's water runs through is never a closed basin (invariant 11): follow each entry down the
            // receivers and mark every cell passed, so a river that meets a small hollow fills it and leaves over its rim.
            bool[] fed = WorldWaterPath(inflow, receiver);
            int closed = Outlets.CloseBasins(ground, baseLevel, GridMath.FlowAccumulationMfd(filled, sizeX, sizeZ), receiver, lakeShare, sizeX, sizeZ,
                i => fed[i] || (openHollows?[i] ?? false), sink);
            Log.Message($"[LivingLandscapes] Map closed basins (#99): {closed} keep their water.");
            if (closed > 0)
            {
                filled = (float[])ground.Clone();
                receiver = Refill(filled, sizeX, sizeZ, baseLevel, p);
            }
        }
        lakeLoss = LakeLosses(ground, baseLevel, receiver, sizeX, sizeZ, runoff, lakeShare);
    }

    /// <summary>
    /// What each full lake keeps of the water passing through it (#134): at the cell its hollow spills by, the lake's surface
    /// (the hollow's cells) times what evaporation and seepage take per cell of lake, in the water units a cell of catchment
    /// carries (<see cref="WaterBalanceMath.LakeLoss"/>). Nothing where rain beats evaporation (infinite share).
    /// </summary>
    private static float[] LakeLosses(float[] ground, float[] baseLevel, int[] receiver, int sizeX, int sizeZ, float runoff, float lakeShare)
    {
        int n = ground.Length;
        var loss = new float[n];
        if (float.IsPositiveInfinity(lakeShare))
            return loss;
        var filled = (float[])ground.Clone();
        var spill = new float[n];
        Outlets.Fill(filled, sizeX, sizeZ, baseLevel, 0f, spill);
        GridMath.LabelRegions(i => float.IsNaN(baseLevel[i]) && spill[i] > ground[i], sizeX, sizeZ, out int[] region);
        var cells = new Dictionary<(int, float), int>();
        for (int i = 0; i < n; i++)
        {
            if (region[i] >= 0)
            {
                var key = (region[i], spill[i]);
                cells[key] = cells.TryGetValue(key, out int c) ? c + 1 : 1;
            }
        }
        for (int i = 0; i < n; i++)
        {
            int r = receiver[i];
            if (region[i] < 0 || r < 0 || (region[r] == region[i] && spill[r] == spill[i]))
                continue;
            loss[i] += WaterBalanceMath.LakeLoss(cells[(region[i], spill[i])], runoff, lakeShare);
        }
        return loss;
    }

    /// <summary>Cells the entering water passes: from each entry cell down the receivers to where they end.</summary>
    private static bool[] WorldWaterPath(float[] inflow, int[] receiver)
    {
        var passed = new bool[inflow.Length];
        for (int i = 0; i < inflow.Length; i++)
        {
            if (!(inflow[i] > 0f))
                continue;
            for (int c = i; c >= 0 && !passed[c]; c = receiver[c])
                passed[c] = true;
        }
        return passed;
    }

    /// <summary>
    /// The map's base levels (P1, <see cref="Outlets"/>): the sea's water (ground below the sea's surface) at sea level; the
    /// map's edge open at its own ground where the erosion model's water flows on out of the map (<paramref name="edgeOpen"/>;
    /// every edge without it), except where water enters (<paramref name="inflow"/>), and at the world river's exit, closed elsewhere; every land area keeps an outlet.
    /// </summary>
    private static float[] MapBaseLevels(float[] height, float[] ground, int sizeX, int sizeZ, List<(ChannelCourse course, bool toMouth)> courses,
        bool[] sea, float seaLevel, bool[] edgeOpen, float[] inflow, Vector2 worldExit = default)
    {
        int n = sizeX * sizeZ;
        var baseLevel = new float[n];
        // The sea's water, not its beach (the coast mask holds both): ground below the sea's surface.
        bool SeaWater(int i) => sea != null && sea[i] && (float.IsNaN(seaLevel) || height[i] < seaLevel);
        for (int i = 0; i < n; i++)
        {
            if (SeaWater(i))
                baseLevel[i] = float.IsNaN(seaLevel) ? ground[i] : seaLevel;
            else
                // An edge cell water enters by is an entry, not an exit (#165): a world river the erosion model did not bring in
                // is placed on the edge facing its neighbour, and an outlet there would take it straight off the map again.
                baseLevel[i] = Outlets.IsEdge(i, sizeX, sizeZ) && (edgeOpen?[i] ?? true) && !(inflow[i] > 0f) ? ground[i] : float.NaN;
        }
        // A river the drainage carried off the wrong side was laid along the world's own way (see Drain): the edges away from the
        // world's exit then take no water, so it cannot turn aside and leave over them again; what drains to them spills elsewhere.
        if (worldExit != Vector2.zero)
        {
            for (int i = 0; i < n; i++)
            {
                if (Outlets.IsEdge(i, sizeX, sizeZ) && !SeaWater(i) && !IsExitCell(i, sizeX, sizeZ, null, worldExit))
                    baseLevel[i] = float.NaN;
            }
        }
        // The world river leaves where its course reaches the edge, whatever the model's coarser drainage says there.
        int Cell(Vector2 v) => Mathf.Clamp(Mathf.RoundToInt(v.y - 0.5f), 0, sizeZ - 1) * sizeX + Mathf.Clamp(Mathf.RoundToInt(v.x - 0.5f), 0, sizeX - 1);
        int exits = 0;
        foreach ((ChannelCourse laid, bool toMouth) in courses)
        {
            if (toMouth || laid.Points.Count == 0)
                continue;
            int last = Cell(laid.Points[laid.Points.Count - 1]);
            int x = last % sizeX, z = last / sizeX;
            if (x <= 2) x = 0; else if (x >= sizeX - 3) x = sizeX - 1;
            if (z <= 2) z = 0; else if (z >= sizeZ - 3) z = sizeZ - 1;
            int exit = z * sizeX + x;
            if (Outlets.IsEdge(exit, sizeX, sizeZ) && float.IsNaN(baseLevel[exit]))
            {
                baseLevel[exit] = ground[exit];
                exits++;
            }
        }
        int regionCount = GridMath.LabelRegions(i => !SeaWater(i), sizeX, sizeZ, out int[] regions);
        int opened = Outlets.KeepOneOutlet(baseLevel, ground, sizeX, sizeZ, regions, regionCount);
        if (edgeOpen != null)
            Log.Message($"[LivingLandscapes] Map base levels (#97): {exits} river exit(s) opened, {opened} land area(s) given their lowest edge cell; " +
                        $"edge cells draining per side: {Outlets.EdgeSummary(baseLevel, ground, sizeX, sizeZ)}.");
        return baseLevel;
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
        Drain(height, sizeX, sizeZ, courses, point, 1f, water, p, worldRiverWidth, out _, out int[] receiver, out _, out bool[] worldSource, out _, out _, out _, out _);
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
        float mapSpread, int sizeX, int sizeZ, TerrainChannelSettings p, SimpleCurve worldRiverWidth, float worldFlow, float loss, float[] runoffAt,
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
        float largestOwn = 1f;
        float[] own = null; // the last round's carried array, for the exit measurement below
        for (int round = 0; round < 4; round++)
        {
            Route(filled, receiver, inflow, worldSource, runoff, threshold, null, mapSpread, sizeX, sizeZ, p,
                out _, out _, out _, out _, out _, out float[] carried, out _, round == 0 ? 0f : loss, scale, null, runoffAt);
            float largest = 1f;
            foreach (float q in carried)
                largest = Mathf.Max(largest, q);
            scale = Mathf.Min(1f, target / largest);
            largestOwn = largest;
            own = carried;
        }
        // What the scale is made of, so a map whose own water is scaled down is readable in the log (HYDRO-S8 C2): the cap
        // (target) against what the map's own rain gathers (largestOwn), both in the discharge units of width = 0.04*sqrt(Q).
        Log.Message($"[LivingLandscapes] Own discharge (#216): " +
                    (creek != null && !float.IsNaN(worldFlow) ? $"world flow {worldFlow:F0}, a creek's {creek.degradeThreshold:F0}, " : "") +
                    $"cap {target:F0} (at most {p.maxWidthWithoutWorldRiver:F1} cells wide), the map's own largest channel " +
                    $"{largestOwn:F0} ({p.widthPerSqrtDischarge * Mathf.Sqrt(largestOwn):F1} cells) -> scale {scale:0.###}.");
        // Where that largest line ends and what reaches the end (HYDRO-S8 C2, architect's extra for #141): the biggest
        // carried value against the exit cell's own. The receiver chain ends where there is no receiver: an edge cell
        // (the water leaves the map) or a sink (it does not).
        int at = 0;
        for (int c = 1; c < own.Length; c++)
            if (own[c] > own[at])
                at = c;
        int steps = 0;
        while (receiver[at] >= 0 && steps < own.Length) // the chain descends, so it ends; the bound is belt-and-braces
        {
            at = receiver[at];
            steps++;
        }
        int exitX = at % sizeX, exitZ = at / sizeX;
        bool onEdge = exitX == 0 || exitZ == 0 || exitX == sizeX - 1 || exitZ == sizeZ - 1;
        Log.Message($"[LivingLandscapes] Own discharge exit (#216): from the largest cell, {steps} step(s) down to " +
                    $"({exitX},{exitZ}) {(onEdge ? "on the map edge" : "in a sink, not an edge")}, arriving with " +
                    $"{own[at]:F0} of the {largestOwn:F0} gathered upstream.");
        return scale;
    }

    /// <summary>
    /// Where a course brings its water onto the map: the point where it crosses the map's edge (the segment from the last point
    /// outside to the first point inside), or its first point when it starts inside (a spring, not an entry).
    /// </summary>
    private static Vector2 EntryPoint(List<Vector2> points, int first, int sizeX, int sizeZ)
    {
        if (first == 0)
            return points[first];
        RiverEntryRules.EdgeCrossing(points[first - 1].x, points[first - 1].y, points[first].x, points[first].y, sizeX, sizeZ, out float x, out float z);
        return new Vector2(x, z);
    }

    /// <summary>
    /// Discharge entering the map: world rivers with the discharge that gives their vanilla width, the region's other
    /// channels with the runoff of the area they drain. On a tile the world gives no river, nothing entering is larger
    /// than a creek, as in vanilla. A world river that does not enter (headwater) rises at the river centre.
    /// </summary>
    private static float[] Inflow(List<ChannelCourse> courses, int sizeX, int sizeZ, Vector2 riverCentre, float sourceRadius, float runoff,
        bool riverEnters, TerrainChannelSettings p, SimpleCurve worldRiverWidth, out bool[] worldSource, out float vanillaWidth,
        List<WorldContext.RiverInflow> worldInflows = null, bool[] sea = null)
    {
        int n = sizeX * sizeZ;
        var inflow = new float[n];
        worldSource = new bool[n];
        vanillaWidth = 0f;
        float DischargeFor(float width) => TerrainChannels.DischargeFor(width, worldRiverWidth, p);
        float creek = DischargeFor(4f);
        bool anyWorldRiver = courses != null && courses.Exists(c => c.VanillaWidth > 0f);
        // A channel entering beside a world river drains the runoff of its area, but never carries more than the largest world
        // river on the tile: the model's drainage area at an entry can include the water of the river it joins.
        float largestWorldRiver = anyWorldRiver ? DischargeFor(courses.Max(c => c.VanillaWidth)) : 0f;
        bool worldEnters = false;
        // Which of the world's inflows a course already brings in: each world river enters once, with its own water.
        var entered = new bool[worldInflows?.Count ?? 0];
        foreach (ChannelCourse course in courses ?? new List<ChannelCourse>())
        {
            vanillaWidth = Mathf.Max(vanillaWidth, course.VanillaWidth);
            if (course.EntryArea <= 0f)
                continue;
            int first = course.Points.FindIndex(v => v.x >= 0f && v.y >= 0f && v.x < sizeX && v.y < sizeZ);
            if (first < 0)
                continue;
            // Water from beyond the map enters where the course crosses the map's edge (boundary condition), on the edge cell: the
            // erosion model's cells are coarser than the map's, so its first point inside can lie a cell or two in from the edge.
            Vector2 entryPoint = EntryPoint(course.Points, first, sizeX, sizeZ);
            int cell = RiverGrid.Index(entryPoint, sizeX, sizeZ);
            if (course.VanillaWidth > 0f)
            {
                // At a headwater the world river does not come from beyond the map: it rises here.
                if (!riverEnters)
                    continue;
                // The world says which sides the river comes from. An erosion course that starts elsewhere (beside the exit, say)
                // would hand the river over on the wrong side, and it would leave again as soon as it entered.
                if (worldInflows != null && worldInflows.Count > 0 && !worldInflows.Exists(f =>
                        Vector2.Dot((entryPoint - riverCentre).normalized, f.From) >= p.entryAgreement))
                {
                    Log.Message($"[LivingLandscapes] World river course entering at ({entryPoint.x:F0}, {entryPoint.y:F0}) " +
                                $"(world river {course.WorldRiver}) disagrees with the world's inflow sides " +
                                $"[{string.Join(", ", worldInflows.Select(f => $"({f.From.x:F2}, {f.From.y:F2})"))}]; not used as an entry.");
                    continue;
                }
                // A world river that meets the sea on entering has reached its base level: nothing to bring over land.
                bool atSea = sea != null && sea[cell];
                if (atSea)
                    LandscapeGenerator.InflowsAtSea++;
                else
                {
                    inflow[cell] += DischargeFor(course.VanillaWidth);
                    worldSource[cell] = worldEnters = true;
                }
                if (entered.Length > 0)
                {
                    // The course brings in the inflow on the side it enters from.
                    Vector2 side = (entryPoint - riverCentre).normalized;
                    int closest = 0;
                    for (int k = 1; k < entered.Length; k++)
                    {
                        if (Vector2.Dot(side, worldInflows[k].From) > Vector2.Dot(side, worldInflows[closest].From))
                            closest = k;
                    }
                    entered[closest] = true;
                }
            }
            else
                inflow[cell] += Mathf.Min(course.EntryArea * runoff, anyWorldRiver && riverEnters ? largestWorldRiver : creek);
        }
        if (anyWorldRiver && riverEnters && worldInflows != null && worldInflows.Count > 0)
        {
            // A world river no course brings in (the erosion model's course starts inside, or several rivers share one
            // course): the world says it comes in, so it enters at the edge facing the neighbour it comes from, as vanilla
            // lays it, and finds its own way from there.
            for (int k = 0; k < worldInflows.Count; k++)
            {
                if (entered[k])
                    continue;
                WorldContext.RiverInflow from = worldInflows[k];
                Vector2 edge = WaterTerrainNudges.EdgeToward(riverCentre, from.From, sizeX, sizeZ);
                int cell = RiverGrid.Index(edge, sizeX, sizeZ);
                if (sea != null && sea[cell])
                {
                    LandscapeGenerator.InflowsAtSea++;
                    continue;
                }
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
    /// Fills hollows of the (changed) surface toward their spill levels and returns the steepest-descent receivers, outlets
    /// draining off the map: at first, and again after meanders or distributaries were carved into it.
    /// </summary>
    private static int[] Refill(float[] filled, int sizeX, int sizeZ, float[] baseLevel, TerrainChannelSettings p)
    {
        Outlets.Fill(filled, sizeX, sizeZ, baseLevel, p.fillGradient);
        int[] receiver = GridMath.Receivers(filled, sizeX, sizeZ);
        for (int i = 0; i < receiver.Length; i++)
        {
            if (Outlets.IsOutlet(baseLevel, i))
                receiver[i] = -1;
        }
        return receiver;
    }
}
