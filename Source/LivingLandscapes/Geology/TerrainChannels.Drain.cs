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
    /// The drainage the channels follow: water entering the map, the landform filled to its spill levels and each cell's
    /// steepest-descent receiver. Water leaves where a cell has a base level (<see cref="MapBaseLevels"/>), once its surface
    /// reaches it; it leaves the map there rather than running along the edge.
    /// </summary>
    private static void Drain(float[] height, int sizeX, int sizeZ, List<ChannelCourse> courses, Vector2 riverCentre, float runoff,
        WaterRegimeDef water, TerrainChannelSettings p, SimpleCurve worldRiverWidth, out float[] filled, out int[] receiver,
        out float[] inflow, out bool[] worldSource, out float vanillaWidth, out float[] baseLevel, out float yield, out float[] lakeLoss, out WorldCourse worldCourse,
        List<WorldContext.RiverInflow> worldInflows = null, bool[] sea = null, float seaLevel = float.NaN,
        bool[] edgeOpen = null, float lakeShare = float.PositiveInfinity, bool[] sink = null,
        System.Func<float[], int[], float[], bool[], float> yieldScale = null, List<List<Vector2>> worldCourses = null)
    {
        int n = sizeX * sizeZ;
        float sourceRadius = (water?.centreRadiusShare ?? 0f) * Mathf.Max(sizeX, sizeZ) * p.sourceRadiusShare;
        inflow = Inflow(courses, sizeX, sizeZ, riverCentre, sourceRadius, runoff, water?.riverInflow ?? true, p, worldRiverWidth,
            out worldSource, out vanillaWidth, worldInflows, sea);
        // The world rivers' channels actually laid as rivers (trenched): without the model's course, these are the world course.
        var trenched = new List<ChannelCourse>();
        // Flats keep a little micro-relief, so water wanders across them instead of running straight or at 45 degrees;
        // on slopes it is far below the fall of the ground and changes nothing.
        filled = (float[])height.Clone();
        var microRelief = new Perlin(1.0 / p.flatNoiseWavelength, 2.0, 0.5, 3, p.flatNoiseSeed, QualityMode.Medium);
        for (int i = 0; i < n; i++)
            filled[i] += p.flatNoise * (float)microRelief.GetValue(i % sizeX, 0.0, i / sizeX);
        foreach (ChannelCourse course in courses ?? new List<ChannelCourse>())
        {
            if (course.VanillaWidth > 0f && (water?.riverInflow ?? true))
            {
                Trench(filled, sizeX, sizeZ, course, p);
                trenched.Add(course);
            }
        }
        // The world rivers' courses as the erosion model laid them, entry to meeting to exit (P-boundary-first, #186); without
        // them (no antecedent course) the world rivers' extracted channels laid as rivers stand in; a world river that brings no
        // water (a dry regime) is laid nowhere and binds nothing.
        worldCourse = worldCourses != null && worldCourses.Count > 0
            ? new WorldCourse(worldCourses.ConvertAll(points => (IReadOnlyList<int>)CourseCells(points, !OnMap(points[0], sizeX, sizeZ), sizeX, sizeZ)), n)
            : new WorldCourse(trenched.OrderByDescending(c => c.WorldRiver).Select(c => (IReadOnlyList<int>)CourseCells(c, sizeX, sizeZ)).ToList(), n);
        // The ground is cut to fall along the course before the base levels read it (Refill repeats the cut after every change).
        PrescribedCourse.CutDownhill(filled, worldCourse.Receivers, p.fillGradient);
        baseLevel = MapBaseLevels(height, filled, sizeX, sizeZ, worldCourse.Legs, sea, seaLevel, edgeOpen, inflow);
        var ground = (float[])filled.Clone();
        receiver = Refill(filled, sizeX, sizeZ, baseLevel, p, worldCourse);
        // The tile's one water budget (#216): where the world gives the tile no river, only the share of the map's own runoff
        // its world flow lets through reaches its channels, and the same share is what its lakes gather.
        yield = vanillaWidth <= 0f && yieldScale != null ? yieldScale(filled, receiver, inflow, worldSource) : 1f;
        lakeShare *= yield;
        // Closed basins keep their water (P23, #99), as in the erosion model: a hollow whose lake fits below its spill level
        // is an outlet at the lake's level, so no channel leaves it.
        if (!float.IsInfinity(lakeShare))
        {
            float[] fed = inflow;
            // A hollow a world river's course runs through is fed by water from beyond the map: it is no closed basin (as in the model).
            var onCourse = new bool[n];
            foreach (IReadOnlyList<int> leg in worldCourse.Legs)
            {
                foreach (int c in leg)
                    onCourse[c] = true;
            }
            int closed = Outlets.CloseBasins(ground, baseLevel, GridMath.FlowAccumulationMfd(filled, sizeX, sizeZ), receiver, lakeShare, sizeX, sizeZ,
                i => fed[i] > 0f || onCourse[i], sink);
            Log.Message($"[LivingLandscapes] Map closed basins (#99): {closed} keep their water.");
            if (closed > 0)
            {
                filled = (float[])ground.Clone();
                receiver = Refill(filled, sizeX, sizeZ, baseLevel, p, worldCourse);
            }
        }
        lakeLoss = LakeLosses(ground, baseLevel, receiver, sizeX, sizeZ, runoff, lakeShare);
    }

    /// <summary>The map cells along a course in flow order, each step to a neighbour (its points are model cells apart).</summary>
    private static List<int> CourseCells(ChannelCourse course, int sizeX, int sizeZ) => CourseCells(course.Points, course.EntryArea > 0f, sizeX, sizeZ);

    private static bool OnMap(Vector2 v, int sizeX, int sizeZ) => v.x >= 0f && v.y >= 0f && v.x < sizeX && v.y < sizeZ;

    /// <summary>The map cells along a line of points in flow order; water from beyond the map (<paramref name="entersFromOutside"/>) starts on the edge cell it crosses.</summary>
    private static List<int> CourseCells(List<Vector2> coursePoints, bool entersFromOutside, int sizeX, int sizeZ)
    {
        int Cell(Vector2 v) => Mathf.Clamp(Mathf.RoundToInt(v.y - 0.5f), 0, sizeZ - 1) * sizeX + Mathf.Clamp(Mathf.RoundToInt(v.x - 0.5f), 0, sizeX - 1);
        var cells = new List<int>();
        // Water from beyond the map enters on the edge cell the course crosses the edge at (see Inflow): the path starts there.
        List<Vector2> points = coursePoints;
        int first = entersFromOutside ? points.FindIndex(v => OnMap(v, sizeX, sizeZ)) : -1;
        if (first >= 0)
        {
            points = new List<Vector2> { EntryPoint(points, first, sizeX, sizeZ) };
            points.AddRange(coursePoints.GetRange(first, coursePoints.Count - first));
        }
        for (int k = 0; k < points.Count; k++)
        {
            Vector2 a = points[Mathf.Max(0, k - 1)], b = points[k];
            float length = (b - a).magnitude;
            for (float t = k == 0 ? 1f : 0f; t <= 1f; t += length > 0.5f ? 0.5f / length : 1f)
                cells.Add(Cell(Vector2.Lerp(a, b, t)));
        }
        return PrescribedCourse.Dedupe(cells);
    }

    /// <summary>
    /// The cells of the world rivers' courses, the one record of where a world river runs once the erosion model is done
    /// (P-boundary-first, #186). The world river's course decides first where courses share a cell, so tributaries join it.
    /// </summary>
    private sealed class WorldCourse
    {
        /// <summary>The course cells in flow order, one list per course, the world river first.</summary>
        public readonly List<IReadOnlyList<int>> Legs;

        /// <summary>The cell each course cell drains to (-1: not on a course, or the last cell).</summary>
        public readonly int[] Receivers;

        public WorldCourse(List<IReadOnlyList<int>> legs, int cellCount)
        {
            Legs = legs;
            Receivers = PrescribedCourse.PrescribedReceivers(cellCount, Legs);
        }
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

    /// <summary>
    /// The map's base levels (P1, <see cref="Outlets"/>): the sea's water (ground below the sea's surface) at sea level; the
    /// map's edge open at its own ground where the erosion model's water flows on out of the map (<paramref name="edgeOpen"/>;
    /// every edge without it), except where water enters (<paramref name="inflow"/>), and at the world river's exit, closed elsewhere; every land area keeps an outlet.
    /// </summary>
    private static float[] MapBaseLevels(float[] height, float[] ground, int sizeX, int sizeZ,
        List<IReadOnlyList<int>> worldLegs, bool[] sea, float seaLevel, bool[] edgeOpen, float[] inflow)
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
        // Water on a world river's course does not leave the map before the course does: an edge cell along it (a river running
        // along the edge) is no exit, whatever the edge's own drainage says; the course's last cell is opened below.
        foreach (IReadOnlyList<int> along in worldLegs)
        {
            for (int k = 0; k + 1 < along.Count; k++)
            {
                if (!SeaWater(along[k]) && Outlets.IsEdge(along[k], sizeX, sizeZ))
                    baseLevel[along[k]] = float.NaN;
            }
        }
        // The world river leaves where its course reaches the edge, whatever the model's coarser drainage says there.
        int exits = 0;
        // A world course that runs off the map ends on the edge cell it leaves by.
        foreach (IReadOnlyList<int> along in worldLegs)
        {
            int end = along.Count > 0 ? along[along.Count - 1] : -1;
            if (end >= 0 && !SeaWater(end) && Outlets.IsEdge(end, sizeX, sizeZ) && float.IsNaN(baseLevel[end]))
            {
                baseLevel[end] = ground[end];
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
    /// The point of the world river's course (the erosion model's path from its start to its exit, <see cref="LandscapeGenerator.WorldCourses"/>)
    /// nearest to <paramref name="point"/>, kept on the map, or <paramref name="point"/> itself if the tile has no course. Mutator
    /// structures that sit in the river (an island's knob) are placed there.
    /// </summary>
    public static Vector2 NearestOnWorldRiver(int sizeX, int sizeZ, List<List<Vector2>> worldCourses, Vector2 point)
    {
        // Course points are cell indices; a cell's centre is half a cell on.
        var paths = worldCourses.ConvertAll(c => (IReadOnlyList<(float x, float z)>)c.ConvertAll(v => (v.x + 0.5f, v.y + 0.5f)));
        if (!PrescribedCourse.NearestOnPaths(paths, point.x, point.y, out float x, out float z))
            return point;
        return new Vector2(Mathf.Clamp(x, 0.5f, sizeX - 0.5f), Mathf.Clamp(z, 0.5f, sizeZ - 0.5f));
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
                Vector2 edge = WaterFeatureMath.EdgeToward(riverCentre, from.From, sizeX, sizeZ);
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
    private static int[] Refill(float[] filled, int sizeX, int sizeZ, float[] baseLevel, TerrainChannelSettings p, WorldCourse worldCourse = null)
    {
        // Whatever the passes did to the ground, a world river's course keeps falling along itself, so it keeps its way out.
        if (worldCourse != null)
            PrescribedCourse.CutDownhill(filled, worldCourse.Receivers, p.fillGradient);
        Outlets.Fill(filled, sizeX, sizeZ, baseLevel, p.fillGradient);
        // A filled hollow's surface falls toward its spill by distance, not along the course that crosses it: cut again.
        if (worldCourse != null)
            PrescribedCourse.CutDownhill(filled, worldCourse.Receivers, p.fillGradient);
        int[] receiver = GridMath.Receivers(filled, sizeX, sizeZ);
        for (int i = 0; i < receiver.Length; i++)
        {
            if (Outlets.IsOutlet(baseLevel, i))
                receiver[i] = -1;
            // A world river's course drains to the next cell along it, as it did in the model (outlets stay outlets).
            else if (worldCourse != null && worldCourse.Receivers[i] >= 0)
                receiver[i] = worldCourse.Receivers[i];
        }
        return receiver;
    }
}
