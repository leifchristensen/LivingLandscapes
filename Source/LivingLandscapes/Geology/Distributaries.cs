using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>Tuning of <see cref="Distributaries"/>, set with the sediment settings in XML.</summary>
public class DistributarySettings
{
    public bool enabled = true;

    /// <summary>Lobes the river builds, and so courses it takes, on a delta tile (RiverDelta) and at any other river mouth.</summary>
    public int deltaCycles = 7;
    public int mouthCycles = 1;

    /// <summary>The most recent courses that still carry water (distributaries); older ones are abandoned to their lobes.</summary>
    public int activeBranches = 4;

    /// <summary>
    /// The avulsion node lies upstream of the shore by the backwater length, channel depth over slope, times this
    /// (Chatanantavet et al. 2012; Ganti et al. 2014), kept within this share of the map's size.
    /// </summary>
    public float backwaterFactor = 1f;
    public FloatRange nodeDistance = new FloatRange(0.12f, 0.35f);

    /// <summary>Share of the river the newest course takes where it leaves the old one.</summary>
    public float newChannelShare = 0.6f;

    /// <summary>
    /// Each course ends in a lobe of new land: this many channel widths across (at least <see cref="minLobeRadius"/> cells),
    /// longer than wide seaward, its top this far (metres) above the sea.
    /// </summary>
    public float lobeRadius = 1.5f;
    public float minLobeRadius = 6f;
    public float maxLobeRadius = 14f;
    public float lobeElongation = 1.5f;
    public float lobeHeight = 0.4f;

    /// <summary>The lobe's front slopes down to the old sea floor over this share of its radius (no wall under the water).</summary>
    public float frontShare = 0.8f;

    /// <summary>How much the lobe's outline wanders in and out (share of its radius).</summary>
    public float outlineNoise = 0.3f;

    /// <summary>
    /// Path cost of a new course: per cell 1, plus this for crossing the raised bed and levees of an earlier course (the old
    /// channel is perched above the plain, which is why the river leaves it), plus noise, plus this per metre climbed.
    /// </summary>
    public float leveePenalty = 6f;
    public float pathNoise = 3f;
    public float climbCost = 4f;

    /// <summary>An earlier course's raised ground reaches this many channel widths out, fading (its levees and splays).</summary>
    public float leveeWidths = 3f;

    /// <summary>Wavelength (cells) of the noise that bends new courses.</summary>
    public float pathNoiseWavelength = 25f;

    /// <summary>
    /// New courses are smoothed over this many cells each way (no grid-straight runs), then wander up to this many cells
    /// across their line over this wavelength.
    /// </summary>
    public int smoothing = 4;
    public float wobble = 4f;
    public float wobbleWavelength = 30f;

    /// <summary>
    /// Mouth-bar branching: over the last <see cref="fanLength"/> cells before the shore, each active course sends this many
    /// branches to the sea at these angles (degrees) to alternating sides, each taking fanShare of the water there.
    /// </summary>
    public float fanLength = 30f;
    public int fanBranches = 3;
    public FloatRange fanAngle = new FloatRange(25f, 45f);
    public float fanShare = 0.35f;

    /// <summary>How much a mouth-bar branch turns as it goes (degrees per cell, at most).</summary>
    public float fanCurl = 8f;

    /// <summary>Branches narrower than this (cells) are not carved.</summary>
    public float minWidth = 1.5f;

    /// <summary>The trench of a course lies this far (metres) below its water level.</summary>
    public float trenchDepth = 0.4f;
}

/// <summary>
/// Deltas by avulsion (issues #16, #46): a reduced-complexity version of how deltas grow (Liang, Voller and Paola 2015;
/// Ganti et al. 2014, 2016). The river builds a lobe of new land at its mouth; its bed rises until it lies above the plain,
/// and it breaks out at the avulsion node (a backwater length upstream of the shore) along the shortest way to the sea that
/// avoids its own raised bed. Repeated, the courses fan out from the node over their lobes; the latest few stay as
/// distributaries, carved as trenches whose water falls toward the sea, and the water divides where each leaves the one
/// before. The node moves seaward as the delta builds out.
/// </summary>
public static class Distributaries
{
    /// <returns>Courses carved (distributaries).</returns>
    public static int Carve(float[] height, float[] filled, int[] routed, int[] main, float[] discharge, float[] width, float[] depth, float[] valley,
        bool[] sea, bool deltaTile, float[] spread, float deltaSpread, int sizeX, int sizeZ, DistributarySettings p, RiverSystem system,
        out int lobeCells, out List<(int at, int a, int b, float share)> splits)
    {
        lobeCells = 0;
        splits = new List<(int, int, int, float)>();
        int cycles = deltaTile ? p.deltaCycles : p.mouthCycles;
        if (sea == null)
            return 0;
        int n = height.Length;
        // The main river: the channel that brings the most water into the sea (as the water was routed before land was
        // built at the mouths), traced upstream along its main donors.
        int mouthCell = -1;
        for (int i = 0; i < n; i++)
        {
            int r = routed[i];
            if (!sea[i] && width[i] > 0f && r >= 0 && sea[r] && (mouthCell < 0 || discharge[i] > discharge[mouthCell]))
                mouthCell = i;
        }
        if (mouthCell < 0)
        {
            Log.Message("[LivingLandscapes] Delta: no channel reaches the sea; none built.");
            return 0;
        }
        var river = new List<int> { routed[mouthCell] };
        for (int c = mouthCell, guard = 0; c >= 0 && guard < n; c = main[c], guard++)
            river.Add(c);
        river.Reverse();
        if (river.Count < 10)
        {
            Log.Message($"[LivingLandscapes] Delta: the largest river into the sea is only {river.Count} cells long; none built.");
            return 0;
        }

        // Open water: the sea, less the land already built at the mouths.
        var open = new bool[n];
        for (int i = 0; i < n; i++)
            open[i] = sea[i] && !(system.DeltaLand?[i] ?? false);
        float seaLevel = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            if (sea[i])
                seaLevel = Mathf.Max(seaLevel, filled[i]);
        }
        float[] toSea = GridMath.DistanceTo(i => open[i], sizeX, sizeZ);

        // The avulsion node: a backwater length (depth / slope) upstream of the shore.
        int mouth = river.Count - 1;
        float channelDepth = 0f, channelWidth = 0f;
        foreach (int c in river)
        {
            channelDepth = Mathf.Max(channelDepth, depth[c]);
            channelWidth = Mathf.Max(channelWidth, width[c]);
        }
        // The river's own slope over its lower reach (natural ground: the filled surface is flat over hollows).
        int last = mouth - 1, span = Mathf.Min(last, Mathf.Max(20, river.Count / 3));
        float slope = Mathf.Max((height[river[last - span]] - height[river[last]]) / Mathf.Max(span, 1), 1e-4f);
        float size = Mathf.Max(sizeX, sizeZ);
        float nodeDistance = Mathf.Clamp(p.backwaterFactor * Mathf.Max(channelDepth, 0.5f) / slope, p.nodeDistance.min * size, p.nodeDistance.max * size);

        // Delta setting (evaluation): confinement of the lower river (valley floor over channel width) and the receiving
        // basin's depth offshore of the mouth against the channel's depth.
        float confinement = 0f;
        int lower = 0;
        for (int k = river.Count * 2 / 3; k < river.Count - 1; k++)
        {
            if (width[river[k]] > 0f)
            {
                confinement += valley[river[k]] / width[river[k]];
                lower++;
            }
        }
        confinement /= Mathf.Max(1, lower);
        int seaCell = river[river.Count - 1], landCell = river[Mathf.Max(0, river.Count - 6)];
        var outward = new Vector2(seaCell % sizeX - landCell % sizeX, seaCell / sizeX - landCell / sizeX).normalized;
        var offshore = new List<string>();
        foreach (int d in new[] { 5, 10, 20, 40 })
        {
            int x = Mathf.FloorToInt(seaCell % sizeX + 0.5f + outward.x * d), z = Mathf.FloorToInt(seaCell / sizeX + 0.5f + outward.y * d);
            offshore.Add(x >= 0 && z >= 0 && x < sizeX && z < sizeZ ? (seaLevel - height[z * sizeX + x]).ToString("F1") : "-");
        }
        string setting = $"lower river confinement {confinement:F1} (valley / channel width), basin depth {string.Join(" / ", offshore)} m at 5/10/20/40 cells offshore";
        if (!p.enabled || cycles <= 0)
        {
            Log.Message($"[LivingLandscapes] Delta: river {river.Count} cells, {channelWidth:F1} wide, {channelDepth:F2} m deep, slope {slope:F4}; none drawn; {setting}.");
            return 0;
        }

        var levee = new float[n];
        var noise = new Perlin(1.0 / p.pathNoiseWavelength, 2.0, 0.5, 3, Rand.Int, QualityMode.Medium);
        var courses = new List<List<int>>();
        var nodes = new List<int>();
        system.DeltaLand ??= new bool[n];

        // Course 0 is the river as it runs now, from its node to the sea (across any land already built at its mouth).
        int node = NodeOn(river, toSea, nodeDistance);
        List<int> course = river.GetRange(river.IndexOf(node), river.Count - river.IndexOf(node));
        Extend(course, open, toSea, sizeX, sizeZ);
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            if (cycle > 0)
            {
                // The bed has risen: the river leaves at the node by the cheapest way to the sea round its raised course.
                node = NodeOn(course, toSea, nodeDistance);
                List<int> next = Path(node, open, filled, levee, noise, sizeX, sizeZ, p);
                if (next == null || next.Count < 3)
                    break;
                course = Curve(next, open, noise, sizeX, sizeZ, p);
            }
            courses.Add(course);
            nodes.Add(node);
            lobeCells += Lobe(course, open, height, filled, seaLevel, channelWidth, noise, sizeX, sizeZ, p, system.DeltaLand);
            // Across the new lobe to the new shore, then the raised bed and levees of this course.
            toSea = GridMath.DistanceTo(i => open[i], sizeX, sizeZ);
            Extend(course, open, toSea, sizeX, sizeZ);
            Raise(course, levee, channelWidth * p.leveeWidths, sizeX, sizeZ);
        }

        // Mouth bars (Edmonds and Slingerland 2007): near the shore each active course drops a bar off its mouth and divides
        // round it, again and again, so terminal distributaries fan out at the coast. Branches leave the last stretch of each
        // course to alternating sides, run to the sea and build their own small lobes.
        int firstActive = Mathf.Max(0, courses.Count - p.activeBranches);
        var fans = new List<(int parent, int at, List<int> cells)>();
        for (int k = firstActive; k < courses.Count; k++)
        {
            List<int> c = courses[k];
            var candidates = new List<int>();
            for (int j = 2; j < c.Count - 3; j++)
            {
                if (!open[c[j]] && toSea[c[j]] <= p.fanLength && toSea[c[j]] >= 3f)
                    candidates.Add(j);
            }
            for (int b = 0; b < p.fanBranches && candidates.Count > 0; b++)
            {
                int j = candidates[Mathf.Clamp(Mathf.RoundToInt((b + 0.5f) / p.fanBranches * candidates.Count), 0, candidates.Count - 1)];
                int at = c[j];
                Vector2 tangent = new Vector2(c[j + 2] % sizeX - c[j - 2] % sizeX, c[j + 2] / sizeX - c[j - 2] / sizeX).normalized;
                float angle = p.fanAngle.RandomInRange * Mathf.Deg2Rad * ((b + k) % 2 == 0 ? 1f : -1f);
                var heading = new Vector2(tangent.x * Mathf.Cos(angle) - tangent.y * Mathf.Sin(angle), tangent.x * Mathf.Sin(angle) + tangent.y * Mathf.Cos(angle));
                List<int> branch = Straight(at, heading, open, noise, p.fanCurl, sizeX, sizeZ, Mathf.CeilToInt(p.fanLength * 2f));
                if (branch == null || branch.Count < 3)
                    continue;
                branch = Curve(branch, open, noise, sizeX, sizeZ, p);
                fans.Add((k, at, branch));
                lobeCells += Lobe(branch, open, height, filled, seaLevel, channelWidth * p.fanShare, noise, sizeX, sizeZ, p, system.DeltaLand);
            }
        }
        if (fans.Count > 0)
            toSea = GridMath.DistanceTo(i => open[i], sizeX, sizeZ);

        // The latest courses carry the water: the newest takes newChannelShare where it leaves the one before, which keeps
        // the rest, and so on back; the oldest active course keeps what is left. Older courses are abandoned below the
        // node where the next one left them, but their reach above it is the trunk the water still runs down.
        int first = Mathf.Max(0, courses.Count - p.activeBranches);
        float apexLevel = filled[nodes[0]];
        float apexToSea = Mathf.Max(toSea[nodes[0]], 1f);
        var trench = new float[n];
        for (int i = 0; i < n; i++)
            trench[i] = float.MaxValue;
        int carved = 0;
        for (int k = 0; k < courses.Count; k++)
        {
            int active = courses.Count - first;
            float share = k < first ? 1f
                : k == first ? Mathf.Pow(1f - p.newChannelShare, active - 1)
                : p.newChannelShare * Mathf.Pow(1f - p.newChannelShare, courses.Count - 1 - k);
            float w = channelWidth * Mathf.Sqrt(share);
            if (w < p.minWidth)
                continue;
            float level = float.MaxValue;
            foreach (int c in courses[k])
            {
                if (k < first && c == nodes[k + 1])
                    break;
                float field = Mathf.Lerp(seaLevel, apexLevel, Mathf.Clamp01(toSea[c] / apexToSea));
                // Strictly falling, so the water at a node always has two ways down.
                level = Mathf.Min(level - 0.003f, field);
                Cut(c, level - p.trenchDepth, w * 0.5f + 1f, trench, sizeX, sizeZ);
            }
            if (k < first)
                continue;
            ThroughFront(courses[k], level - p.trenchDepth, w * 0.5f + 1f, height, trench, sizeX, sizeZ, p);
            carved++;
            if (k > first)
            {
                // Where the course leaves the one before, the water divides.
                List<int> parent = courses[k - 1];
                int at = nodes[k], from = parent.IndexOf(at);
                int a = from >= 0 && from + 1 < parent.Count ? parent[from + 1] : -1;
                int b = courses[k].Count > 1 ? courses[k][1] : -1;
                if (a >= 0 && b >= 0 && a != b)
                    splits.Add((at, a, b, p.newChannelShare));
                MarkSpread(at, spread, deltaSpread, sizeX, sizeZ);
            }
        }
        foreach ((int parent, int at, List<int> cells) in fans)
        {
            float w = channelWidth * Mathf.Sqrt(p.fanShare / Mathf.Max(1, courses.Count - first));
            if (w < p.minWidth || cells.Count < 2)
                continue;
            float level = filled[at];
            foreach (int c in cells)
            {
                float field = Mathf.Lerp(seaLevel, apexLevel, Mathf.Clamp01(toSea[c] / apexToSea));
                level = Mathf.Min(level - 0.003f, field);
                Cut(c, level - p.trenchDepth, w * 0.5f + 1f, trench, sizeX, sizeZ);
            }
            ThroughFront(cells, level - p.trenchDepth, w * 0.5f + 1f, height, trench, sizeX, sizeZ, p);
            List<int> trunk = courses[parent];
            int from = trunk.IndexOf(at);
            if (from >= 0 && from + 1 < trunk.Count && cells[1] != trunk[from + 1])
                splits.Add((at, trunk[from + 1], cells[1], p.fanShare));
            carved++;
        }
        for (int i = 0; i < n; i++)
        {
            // Lowering only: land, and the sill of a lobe's front under the water.
            if (trench[i] < float.MaxValue)
            {
                height[i] = Mathf.Min(height[i], trench[i]);
                filled[i] = Mathf.Min(filled[i], trench[i]);
            }
        }
        Log.Message($"[LivingLandscapes] Delta: river {river.Count} cells into the sea at ({river[river.Count - 1] % sizeX}, {river[river.Count - 1] / sizeX}), " +
                    $"{channelWidth:F1} wide, {channelDepth:F2} m deep, slope {slope:F4} (heights {string.Join(" ", new[] { 0, river.Count / 4, river.Count / 2, 3 * river.Count / 4, river.Count - 2 }.Select(k => height[river[k]].ToString("F2")))}, sea {seaLevel:F2}); node {nodeDistance:F0} cells from the shore; {courses.Count} of {cycles} courses, " +
                    $"{lobeCells} cells of lobes, {carved} carved, {splits.Count} splits; {setting}.");
        return carved;
    }

    /// <summary>The cell of a course nearest a backwater length from the shore (not its last cells).</summary>
    private static int NodeOn(List<int> course, float[] toSea, float distance)
    {
        int best = course[0];
        for (int k = 0; k < course.Count - 3; k++)
        {
            best = course[k];
            if (toSea[course[k]] <= distance)
                break;
        }
        return best;
    }

    /// <summary>The cheapest way from <paramref name="from"/> to open sea (Dijkstra over 8 neighbours).</summary>
    private static List<int> Path(int from, bool[] open, float[] filled, float[] levee, Perlin noise, int sizeX, int sizeZ, DistributarySettings p)
    {
        int n = open.Length;
        var cost = new float[n];
        var back = new int[n];
        for (int i = 0; i < n; i++)
        {
            cost[i] = float.MaxValue;
            back[i] = -1;
        }
        var queue = new PriorityQueue<int, float>();
        cost[from] = 0f;
        queue.Enqueue(from, 0f);
        int end = -1;
        while (queue.TryDequeue(out int c, out float at))
        {
            if (at > cost[c])
                continue;
            if (open[c])
            {
                end = c;
                break;
            }
            int x = c % sizeX, z = c / sizeX;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + GenAdj.AdjacentCells[d].x, nz = z + GenAdj.AdjacentCells[d].z;
                if (nx < 0 || nz < 0 || nx >= sizeX || nz >= sizeZ)
                    continue;
                int next = nz * sizeX + nx;
                float step = (d >= 4 ? 1.4142f : 1f) * (1f + levee[next] * p.leveePenalty + p.pathNoise * (0.5f + 0.5f * (float)noise.GetValue(nx, 0.0, nz)))
                             + p.climbCost * Mathf.Max(0f, filled[next] - filled[c]);
                if (at + step < cost[next])
                {
                    cost[next] = at + step;
                    back[next] = c;
                    queue.Enqueue(next, at + step);
                }
            }
        }
        if (end < 0)
            return null;
        var path = new List<int>();
        for (int c = end; c >= 0; c = back[c])
            path.Add(c);
        path.Reverse();
        return path;
    }

    /// <summary>
    /// The channel runs on past its mouth, down the lobe's front, until the sea floor lies below its bed: no sill dams it.
    /// </summary>
    private static void ThroughFront(List<int> cells, float bottom, float radius, float[] height, float[] trench, int sizeX, int sizeZ, DistributarySettings p)
    {
        if (cells.Count < 2)
            return;
        int last = cells[cells.Count - 1], back = cells[Mathf.Max(0, cells.Count - 5)];
        var heading = new Vector2(last % sizeX - back % sizeX, last / sizeX - back / sizeX);
        if (heading == Vector2.zero)
            return;
        heading.Normalize();
        var v = new Vector2(last % sizeX + 0.5f, last / sizeX + 0.5f);
        float reach = p.maxLobeRadius * p.lobeElongation * (1f + p.frontShare);
        for (float d = 0f; d < reach; d += 0.5f)
        {
            v += heading * 0.5f;
            int x = Mathf.FloorToInt(v.x), z = Mathf.FloorToInt(v.y);
            if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                return;
            int c = z * sizeX + x;
            if (height[c] < bottom - 0.2f)
                return;
            Cut(c, bottom, radius, trench, sizeX, sizeZ);
        }
    }

    /// <summary>
    /// Cells from <paramref name="from"/> along a heading until open water (null if none within the length); the heading
    /// drifts with the noise, so the branch curves as it goes.
    /// </summary>
    private static List<int> Straight(int from, Vector2 heading, bool[] open, Perlin noise, float curl, int sizeX, int sizeZ, int length)
    {
        var cells = new List<int> { from };
        var v = new Vector2(from % sizeX + 0.5f, from / sizeX + 0.5f);
        float seed = from * 0.37f;
        for (int s = 0; s < length * 2; s++)
        {
            float turn = curl * Mathf.Deg2Rad * 0.5f * (float)noise.GetValue(s * 0.05, seed, 17.0);
            heading = new Vector2(heading.x * Mathf.Cos(turn) - heading.y * Mathf.Sin(turn), heading.x * Mathf.Sin(turn) + heading.y * Mathf.Cos(turn));
            v += heading * 0.5f;
            int x = Mathf.FloorToInt(v.x), z = Mathf.FloorToInt(v.y);
            if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                return null;
            int c = z * sizeX + x;
            if (c == cells[cells.Count - 1])
                continue;
            cells.Add(c);
            if (open[c])
                return cells;
        }
        return null;
    }

    /// <summary>
    /// A grid path runs in straight lines at 45 and 90 degrees; a river does not. The path is smoothed (a moving average of
    /// its points), bent by a gentle wandering offset across it, and drawn again as connected cells, from the node (kept) to
    /// the first open water.
    /// </summary>
    private static List<int> Curve(List<int> path, bool[] open, Perlin noise, int sizeX, int sizeZ, DistributarySettings p)
    {
        int count = path.Count, r = p.smoothing;
        var points = new List<Vector2>(count);
        foreach (int c in path)
            points.Add(new Vector2(c % sizeX + 0.5f, c / sizeX + 0.5f));
        var smooth = new List<Vector2>(count);
        for (int k = 0; k < count; k++)
        {
            int from = Mathf.Max(0, k - r), to = Mathf.Min(count - 1, k + r);
            Vector2 sum = Vector2.zero;
            for (int j = from; j <= to; j++)
                sum += points[j];
            smooth.Add(sum / (to - from + 1));
        }
        float along = 0f;
        for (int k = 1; k < count - 1; k++)
        {
            along += (smooth[k] - smooth[k - 1]).magnitude;
            Vector2 tangent = (smooth[k + 1] - smooth[k - 1]).normalized;
            // No wobble at the node, growing to full over the first cells.
            float fade = Mathf.Clamp01(k / (float)(2 * r + 1));
            smooth[k] += new Vector2(-tangent.y, tangent.x) * p.wobble * fade * (float)noise.GetValue(along / p.wobbleWavelength, 31.0, 0.0);
        }
        smooth[0] = points[0];
        var cells = new List<int> { path[0] };
        for (int k = 1; k < count; k++)
        {
            Vector2 a = smooth[k - 1], b = smooth[k];
            int steps = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude * 2f));
            for (int s = 1; s <= steps; s++)
            {
                Vector2 v = Vector2.Lerp(a, b, s / (float)steps);
                int x = Mathf.Clamp(Mathf.FloorToInt(v.x), 0, sizeX - 1), z = Mathf.Clamp(Mathf.FloorToInt(v.y), 0, sizeZ - 1);
                int c = z * sizeX + x;
                if (c == cells[cells.Count - 1])
                    continue;
                cells.Add(c);
                if (open[c])
                    return cells;
            }
        }
        return cells;
    }

    /// <summary>
    /// Builds a lobe of new land where the course meets the sea: sea cells around the mouth, longer seaward than wide, rise
    /// above the water. Returns the cells that became land.
    /// </summary>
    private static int Lobe(List<int> course, bool[] open, float[] height, float[] filled, float seaLevel, float channelWidth, Perlin noise,
        int sizeX, int sizeZ, DistributarySettings p, bool[] deltaLand)
    {
        int mouth = course[course.Count - 1];
        int before = course[Mathf.Max(0, course.Count - 6)];
        var at = new Vector2(mouth % sizeX + 0.5f, mouth / sizeX + 0.5f);
        Vector2 heading = at - new Vector2(before % sizeX + 0.5f, before / sizeX + 0.5f);
        heading = heading.sqrMagnitude > 0f ? heading.normalized : Vector2.up;
        float radius = Mathf.Clamp(p.lobeRadius * channelWidth, p.minLobeRadius, p.maxLobeRadius);
        Vector2 centre = at + heading * radius * 0.5f;
        int reach = Mathf.CeilToInt(radius * p.lobeElongation * (1f + p.frontShare + p.outlineNoise)) + 1, land = 0;
        int cx = Mathf.FloorToInt(centre.x), cz = Mathf.FloorToInt(centre.y);
        for (int dz = -reach; dz <= reach; dz++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                    continue;
                int i = z * sizeX + x;
                if (!open[i])
                    continue;
                Vector2 v = new Vector2(x + 0.5f, z + 0.5f) - centre;
                float along = Vector2.Dot(v, heading) / p.lobeElongation, across = v.x * heading.y - v.y * heading.x;
                // An irregular outline: the radius wanders with the noise, so lobes do not read as circles.
                float edge = 1f + p.outlineNoise * (float)noise.GetValue(x * 3.0, 7.0, z * 3.0);
                float r = Mathf.Sqrt(along * along + across * across) / radius / Mathf.Max(edge, 0.3f);
                if (r >= 1f + p.frontShare)
                    continue;
                if (r >= 1f)
                {
                    // The delta front: a slope from the shore down to the old sea floor, under the water.
                    float front = Mathf.Lerp(seaLevel - 0.1f, height[i], (r - 1f) / p.frontShare);
                    height[i] = Mathf.Max(height[i], front);
                    filled[i] = Mathf.Max(filled[i], front);
                    continue;
                }
                float top = seaLevel + p.lobeHeight * (1f - r * r);
                height[i] = Mathf.Max(height[i], top);
                filled[i] = Mathf.Max(filled[i], top);
                open[i] = false;
                deltaLand[i] = true;
                land++;
            }
        }
        return land;
    }

    /// <summary>Continues a course whose mouth is now land down the distance to the new shore.</summary>
    private static void Extend(List<int> course, bool[] open, float[] toSea, int sizeX, int sizeZ)
    {
        int c = course[course.Count - 1];
        for (int guard = 0; guard < sizeX + sizeZ && !open[c]; guard++)
        {
            int x = c % sizeX, z = c / sizeX, best = -1;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + GenAdj.AdjacentCells[d].x, nz = z + GenAdj.AdjacentCells[d].z;
                if (nx < 0 || nz < 0 || nx >= sizeX || nz >= sizeZ)
                    continue;
                int next = nz * sizeX + nx;
                if (best < 0 || toSea[next] < toSea[best])
                    best = next;
            }
            if (best < 0 || toSea[best] >= toSea[c])
                break;
            course.Add(c = best);
        }
    }

    /// <summary>The course's bed and levees stand above the plain, fading over <paramref name="reachCells"/>: later courses avoid them.</summary>
    private static void Raise(List<int> course, float[] levee, float reachCells, int sizeX, int sizeZ)
    {
        int reach = Mathf.CeilToInt(reachCells) + 1;
        foreach (int c in course)
        {
            int cx = c % sizeX, cz = c / sizeX;
            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x >= 0 && z >= 0 && x < sizeX && z < sizeZ)
                        levee[z * sizeX + x] = Mathf.Max(levee[z * sizeX + x], 1f - Mathf.Sqrt(dx * dx + dz * dz) / (reachCells + 1f));
                }
            }
        }
    }

    private static void Cut(int c, float bottom, float radius, float[] trench, int sizeX, int sizeZ)
    {
        int cx = c % sizeX, cz = c / sizeX, reach = Mathf.CeilToInt(radius);
        for (int dz = -reach; dz <= reach; dz++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                    continue;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d >= radius)
                    continue;
                int i = z * sizeX + x;
                trench[i] = Mathf.Min(trench[i], bottom + 0.15f * (d / radius) * (d / radius));
            }
        }
    }

    private static void MarkSpread(int at, float[] spread, float deltaSpread, int sizeX, int sizeZ)
    {
        int sx = at % sizeX, sz = at / sizeX;
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
}
