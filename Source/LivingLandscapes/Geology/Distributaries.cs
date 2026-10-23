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

    /// <summary>
    /// The backwater length, channel depth over slope, times this (Chatanantavet et al. 2012; Ganti et al. 2014): the avulsion
    /// node lies within half to one and a half of it from the shore. At least <see cref="minBackwater"/> cells, and no longer
    /// than the low reach of the river (it cannot back up water beyond where the river runs near sea level).
    /// </summary>
    public float backwaterFactor = 1f;
    public float minBackwater = 10f;

    /// <summary>
    /// Courses the river takes over the delta plain: the plain's area over the area one course spreads its deposits over,
    /// a backwater length long and this many channel widths wide. At most <see cref="maxCourses"/>.
    /// </summary>
    public float courseWidths = 4f;
    public int maxCourses = 8;

    /// <summary>Share of the river the newest course takes where it leaves the one before.</summary>
    public float newChannelShare = 0.6f;

    /// <summary>
    /// Mouth bars (Edmonds and Slingerland 2007): where the sea this many channel widths off a mouth is shallower than
    /// barDepthRatio times the channel's depth, the jet drops a bar there and the channel splits round it into two halves of
    /// the water, splitAngle (degrees) to either side, again at each new mouth, at most maxBarLevels deep.
    /// </summary>
    public float barDistance = 3f;
    public float barDepthRatio = 2f;
    public float splitAngle = 30f;
    public int maxBarLevels = 4;

    /// <summary>How much a branch round a mouth bar turns as it goes (degrees per cell, at most).</summary>
    public float branchCurl = 8f;

    /// <summary>
    /// The land a mouth bar builds: longer seaward than wide, its top this far (metres) above the sea, its front sloping down
    /// to the old sea floor over this share of its radius, its outline wandering by this share.
    /// </summary>
    public float lobeElongation = 1.5f;
    public float lobeHeight = 0.4f;
    public float frontShare = 0.8f;
    public float outlineNoise = 0.3f;

    /// <summary>
    /// Path cost of a new course: per cell 1, plus this for crossing the raised bed and levees of an earlier course (the old
    /// channel is perched above the plain, which is why the river leaves it), plus noise, plus this per metre climbed.
    /// </summary>
    public float leveePenalty = 6f;

    /// <summary>
    /// Extra cost per cell of running along an earlier course's own bed (within its half-width): the bed is perched above the
    /// plain, so the water that leaves it does not follow it back down.
    /// </summary>
    public float bedPenalty = 40f;
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

    /// <summary>Channels narrower than this (cells) carry no water of their own: courses are abandoned, bars stop splitting.</summary>
    public float minWidth = 1.5f;

    /// <summary>The trench of a course lies this far (metres) below its water level.</summary>
    public float trenchDepth = 0.4f;
}

/// <summary>
/// Distributaries on the delta land deposition built (issue #58; Docs/DELTA-PLAN.md step 4). The land comes from the erosion
/// model; this stage routes the water across it from the data. The river's bed rises above its plain, and it breaks out at the
/// avulsion node (where, within a backwater length of the shore, its water stands highest above the plain beside it) along
/// the cheapest way over the plain to the sea that avoids its own raised bed (Ganti et al. 2014, 2016). The plain's area sets
/// how many courses it takes; the latest that still carry a channel's worth of water stay as distributaries, and where the
/// sea off a mouth is shallow a bar splits it (Edmonds and Slingerland 2007).
/// </summary>
public static class Distributaries
{
    /// <returns>Courses and branches carved (distributaries).</returns>
    public static int Carve(float[] height, float[] filled, int[] routed, int[] main, float[] discharge, float[] width, float[] depth, float[] valley,
        bool[] sea, float[] spread, float deltaSpread, int sizeX, int sizeZ, DistributarySettings p, RiverSystem system,
        out int lobeCells, out List<(int at, int a, int b, float share)> splits)
    {
        lobeCells = 0;
        splits = new List<(int, int, int, float)>();
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

        // The river backs its water up only as far as it runs within a channel depth of sea level (its low reach), so the
        // backwater length is no longer than that.
        float depthAbove = Mathf.Max(channelDepth, 0.5f);
        float lowReach = 0f;
        for (int k = mouth; k >= 0 && height[river[k]] <= seaLevel + depthAbove; k--)
            lowReach = Mathf.Max(lowReach, toSea[river[k]]);
        float backwater = Mathf.Max(p.minBackwater, Mathf.Min(p.backwaterFactor * depthAbove / slope, lowReach));
        // The delta plain: land the river could flood, within a channel depth of its graded surface (sea level rising inland
        // along the river's slope).
        var plain = new bool[n];
        int plainCells = 0;
        for (int i = 0; i < n; i++)
        {
            plain[i] = !open[i] && toSea[i] <= 1.5f * backwater && height[i] <= seaLevel + slope * toSea[i] + depthAbove;
            if (plain[i])
                plainCells++;
        }

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
        if (!p.enabled)
        {
            Log.Message($"[LivingLandscapes] Delta: river {river.Count} cells, {channelWidth:F1} wide, {channelDepth:F2} m deep, slope {slope:F4}; none drawn; {setting}.");
            return 0;
        }

        // How many courses: the plain's area over what one course covers (a backwater length by a few channel widths).
        int cycles = Mathf.Clamp(Mathf.RoundToInt(plainCells / Mathf.Max(backwater * p.courseWidths * channelWidth, 1f)), 1, p.maxCourses);
        var levee = new float[n];
        var bed = new bool[n];
        var noise = new Perlin(1.0 / p.pathNoiseWavelength, 2.0, 0.5, 3, Rand.Int, QualityMode.Medium);
        var courses = new List<List<int>>();
        var nodes = new List<int>();
        system.DeltaLand ??= new bool[n];
        float besideReach = channelWidth * (0.5f + p.leveeWidths);

        // Course 0 is the river as it runs now, from its node to the sea.
        int node = Node(river, toSea, filled, height, plain, backwater, besideReach, sizeX, sizeZ);
        List<int> course = river.GetRange(river.IndexOf(node), river.Count - river.IndexOf(node));
        Extend(course, open, toSea, sizeX, sizeZ);
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            if (cycle > 0)
            {
                // The bed has risen: the river leaves at the node by the cheapest way over the plain round its raised course.
                node = Node(course, toSea, filled, height, plain, backwater, besideReach, sizeX, sizeZ);
                List<int> next = Path(node, open, plain, filled, levee, bed, noise, sizeX, sizeZ, p);
                if (next == null || next.Count < 3)
                    break;
                course = Curve(next, open, noise, sizeX, sizeZ, p);
            }
            courses.Add(course);
            nodes.Add(node);
            Raise(course, levee, bed, channelWidth * 0.5f + 1f, channelWidth * p.leveeWidths, sizeX, sizeZ);
        }

        // The latest courses carry the water: the newest takes newChannelShare where it leaves the one before, which keeps the
        // rest, and so on back. Courses whose share is narrower than a channel are abandoned below the node where the next one
        // left them; their reach above it is the trunk the water still runs down.
        int count = courses.Count;
        float Share(int k, int oldest) => k < oldest ? 1f
            : k == oldest ? Mathf.Pow(1f - p.newChannelShare, count - 1 - oldest)
            : p.newChannelShare * Mathf.Pow(1f - p.newChannelShare, count - 1 - k);
        int first = 0;
        while (first < count - 1 && channelWidth * Mathf.Sqrt(Mathf.Min(Share(first, first), Share(first + 1, first))) < p.minWidth)
            first++;

        // Mouth bars at each active course's mouth, splitting it where the sea off it is shallow; the bars build land.
        var branches = new List<(int at, List<int> cells, float share)>();
        var splitAt = new HashSet<int>();
        for (int k = first; k < count; k++)
            lobeCells += Bars(courses[k], Share(k, first), 0, open, height, filled, seaLevel, channelWidth, channelDepth, noise, sizeX, sizeZ, p,
                system.DeltaLand, branches, splitAt);
        if (branches.Count > 0)
            toSea = GridMath.DistanceTo(i => open[i], sizeX, sizeZ);

        float apexLevel = filled[nodes[0]];
        float apexToSea = Mathf.Max(toSea[nodes[0]], 1f);
        var trench = new float[n];
        var level = new float[n];
        for (int i = 0; i < n; i++)
        {
            trench[i] = float.MaxValue;
            level[i] = float.MaxValue;
        }
        int carved = 0;
        // Water falls from the apex toward the sea along each path, strictly, so the water at a split always has two ways down.
        float Lay(List<int> cells, float start, float w, int stopAt)
        {
            float l = start;
            foreach (int c in cells)
            {
                if (c == stopAt)
                    break;
                float field = Mathf.Lerp(seaLevel, apexLevel, Mathf.Clamp01(toSea[c] / apexToSea));
                l = Mathf.Min(l - 0.003f, field);
                level[c] = Mathf.Min(level[c], l);
                Cut(c, l - p.trenchDepth, w * 0.5f + 1f, trench, sizeX, sizeZ);
            }
            return l;
        }
        // Where each course leaves the one before: the last cell they share (the new course may follow the old one for a few
        // cells after the node), and the next cell on each.
        var divide = new (int at, int old, int next)[count];
        for (int k = 1; k < count; k++)
        {
            List<int> parent = courses[k - 1], cells = courses[k];
            int from = parent.IndexOf(nodes[k]), j = 0;
            while (from >= 0 && j + 1 < cells.Count && from + j + 1 < parent.Count && cells[j + 1] == parent[from + j + 1])
                j++;
            divide[k] = from >= 0 && j + 1 < cells.Count && from + j + 1 < parent.Count ? (cells[j], parent[from + j + 1], j + 1) : (-1, -1, -1);
        }
        for (int k = 0; k < count; k++)
        {
            float share = Share(k, first);
            float w = channelWidth * Mathf.Sqrt(share);
            List<int> cells = courses[k];
            // A course takes its water at the level of the course it leaves, and falls from there; an abandoned course is laid
            // only down to where the next one leaves it.
            bool leaves = k > 0 && divide[k].at >= 0;
            List<int> laid = leaves ? cells.GetRange(divide[k].next, cells.Count - divide[k].next) : cells;
            float end = Lay(laid, leaves ? level[divide[k].at] : float.MaxValue, w, k < first && k + 1 < count ? divide[k + 1].old : -1);
            if (k < first)
                continue;
            if (!splitAt.Contains(cells[cells.Count - 1]))
                ThroughFront(cells, end - p.trenchDepth, w * 0.5f + 1f, p.barDistance * w * 2f + 10f, height, trench, sizeX, sizeZ);
            carved++;
            if (k > first && leaves)
            {
                // Where the course leaves the one before, the water divides.
                splits.Add((divide[k].at, divide[k].old, cells[divide[k].next], p.newChannelShare));
                MarkSpread(divide[k].at, spread, deltaSpread, sizeX, sizeZ);
            }
        }
        // Branches round the bars, parents before children: each starts at its parent's level where they divide.
        for (int j = 0; j < branches.Count; j++)
        {
            (int at, List<int> cells, float share) = branches[j];
            float w = channelWidth * Mathf.Sqrt(share);
            float end = Lay(cells, level[at] < float.MaxValue ? level[at] : filled[at], w, -1);
            if (!splitAt.Contains(cells[cells.Count - 1]))
                ThroughFront(cells, end - p.trenchDepth, w * 0.5f + 1f, p.barDistance * w * 2f + 10f, height, trench, sizeX, sizeZ);
            carved++;
            // Branches are listed in pairs by the bar they go round.
            if (j + 1 < branches.Count && branches[j + 1].at == at && cells.Count > 1 && branches[j + 1].cells.Count > 1)
            {
                splits.Add((at, cells[1], branches[j + 1].cells[1], 0.5f));
                MarkSpread(at, spread, deltaSpread, sizeX, sizeZ);
            }
        }
        for (int i = 0; i < n; i++)
        {
            // Lowering only: land, and the sill of a bar's front under the water.
            if (trench[i] < float.MaxValue)
            {
                height[i] = Mathf.Min(height[i], trench[i]);
                filled[i] = Mathf.Min(filled[i], trench[i]);
            }
        }
        // A neighbouring course's trench can cut a split cell below the cells it divides into; the cell keeps a notch above
        // both, so its water has two ways down.
        foreach ((int at, int a, int b, float _) in splits)
        {
            float above = Mathf.Max(filled[a], filled[b]) + 0.003f;
            if (filled[at] < above)
                height[at] = filled[at] = above;
        }
        Log.Message($"[LivingLandscapes] Delta: river {river.Count} cells into the sea at ({river[river.Count - 1] % sizeX}, {river[river.Count - 1] / sizeX}), " +
                    $"{channelWidth:F1} wide, {channelDepth:F2} m deep, slope {slope:F4}; backwater {backwater:F0} cells (low reach {lowReach:F0}), plain {plainCells} cells; " +
                    $"{count} of {cycles} courses ({count - first} carry water), {branches.Count} branches round {splitAt.Count} mouth bars, {lobeCells} cells of bar land, " +
                    $"{carved} carved, {splits.Count} splits; {setting}.");
        return carved;
    }

    /// <summary>
    /// The avulsion node on a course: within half to one and a half backwater lengths of the shore, the cell whose water stands
    /// highest above the plain beside it (superelevation); the cell a backwater length from the shore if none lies there.
    /// </summary>
    private static int Node(List<int> course, float[] toSea, float[] filled, float[] height, bool[] plain, float backwater, float beside,
        int sizeX, int sizeZ)
    {
        int best = -1;
        float highest = float.MinValue;
        int reach = Mathf.CeilToInt(beside) + 1;
        for (int k = 0; k < course.Count - 3; k++)
        {
            int c = course[k];
            if (toSea[c] < 0.5f * backwater || toSea[c] > 1.5f * backwater)
                continue;
            // The plain beside: plain cells at about the channel's edge plus its levees.
            float sum = 0f;
            int cells = 0, cx = c % sizeX, cz = c / sizeX;
            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    float r = Mathf.Sqrt(dx * dx + dz * dz);
                    if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ || r < beside - 1f || r > beside + 1f || !plain[z * sizeX + x])
                        continue;
                    sum += height[z * sizeX + x];
                    cells++;
                }
            }
            if (cells == 0)
                continue;
            float rise = filled[c] - sum / cells;
            if (rise > highest)
            {
                highest = rise;
                best = c;
            }
        }
        return best >= 0 ? best : NodeOn(course, toSea, backwater);
    }

    /// <summary>
    /// A mouth bar (Edmonds and Slingerland 2007): where the sea barDistance channel widths off the path's mouth is shallower
    /// than barDepthRatio channel depths, the jet slows and drops a bar there. The bar builds land out to it, and the channel
    /// divides round it into two branches with half the water each, running on to the new shore; each branch's mouth may
    /// get its own bar. Branches are added in pairs (or one, if only one finds the sea). Returns the cells of bar land.
    /// </summary>
    private static int Bars(List<int> path, float share, int barLevel, bool[] open, float[] height, float[] filled, float seaLevel,
        float channelWidth, float channelDepth, Perlin noise, int sizeX, int sizeZ, DistributarySettings p, bool[] deltaLand,
        List<(int at, List<int> cells, float share)> branches, HashSet<int> splitAt)
    {
        if (barLevel >= p.maxBarLevels || path.Count < 2 || channelWidth * Mathf.Sqrt(share * 0.5f) < p.minWidth)
            return 0;
        // Width and depth follow discharge (hydraulic geometry: width as its square root, depth as its 0.4 power).
        float w = channelWidth * Mathf.Sqrt(share), d = channelDepth * Mathf.Pow(share, 0.4f);
        int mouth = path[path.Count - 1], before = path[Mathf.Max(0, path.Count - 6)];
        var at = new Vector2(mouth % sizeX + 0.5f, mouth / sizeX + 0.5f);
        Vector2 heading = at - new Vector2(before % sizeX + 0.5f, before / sizeX + 0.5f);
        if (heading == Vector2.zero)
            return 0;
        heading.Normalize();
        float reach = p.barDistance * w;
        Vector2 bar = at + heading * reach;
        int bx = Mathf.FloorToInt(bar.x), bz = Mathf.FloorToInt(bar.y);
        if (bx < 0 || bz < 0 || bx >= sizeX || bz >= sizeZ || !open[bz * sizeX + bx] || seaLevel - height[bz * sizeX + bx] > p.barDepthRatio * d)
            return 0;
        int land = Lobe(mouth, heading, reach * 0.5f, open, height, filled, seaLevel, noise, sizeX, sizeZ, p, deltaLand);
        var found = new List<List<int>>();
        foreach (float side in new[] { 1f, -1f })
        {
            float angle = side * p.splitAngle * Mathf.Deg2Rad;
            var h = new Vector2(heading.x * Mathf.Cos(angle) - heading.y * Mathf.Sin(angle), heading.x * Mathf.Sin(angle) + heading.y * Mathf.Cos(angle));
            List<int> branch = Straight(mouth, h, open, noise, p.branchCurl, sizeX, sizeZ, Mathf.CeilToInt(reach * 3f) + 4);
            if (branch != null && branch.Count >= 3)
                found.Add(branch);
        }
        if (found.Count == 0)
            return land;
        splitAt.Add(mouth);
        float each = found.Count == 2 ? share * 0.5f : share;
        foreach (List<int> branch in found)
            branches.Add((mouth, branch, each));
        foreach (List<int> branch in found)
            land += Bars(branch, each, barLevel + 1, open, height, filled, seaLevel, channelWidth, channelDepth, noise, sizeX, sizeZ, p, deltaLand, branches, splitAt);
        return land;
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

    /// <summary>The cheapest way from <paramref name="from"/> over the delta plain to open sea (Dijkstra over 8 neighbours).</summary>
    private static List<int> Path(int from, bool[] open, bool[] plain, float[] filled, float[] levee, bool[] bed, Perlin noise, int sizeX, int sizeZ, DistributarySettings p)
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
                if (!plain[next] && !open[next])
                    continue;
                float step = (d >= 4 ? 1.4142f : 1f) * (1f + levee[next] * p.leveePenalty + (bed[next] ? p.bedPenalty : 0f) + p.pathNoise * (0.5f + 0.5f * (float)noise.GetValue(nx, 0.0, nz)))
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
    private static void ThroughFront(List<int> cells, float bottom, float radius, float reach, float[] height, float[] trench, int sizeX, int sizeZ)
    {
        if (cells.Count < 2)
            return;
        int last = cells[cells.Count - 1], back = cells[Mathf.Max(0, cells.Count - 5)];
        var heading = new Vector2(last % sizeX - back % sizeX, last / sizeX - back / sizeX);
        if (heading == Vector2.zero)
            return;
        heading.Normalize();
        var v = new Vector2(last % sizeX + 0.5f, last / sizeX + 0.5f);
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
    /// Builds the land a mouth bar makes off <paramref name="mouth"/>: sea cells ahead of it, longer seaward than wide, rise
    /// above the water, with a front sloping down to the old sea floor. Returns the cells that became land.
    /// </summary>
    private static int Lobe(int mouth, Vector2 heading, float radius, bool[] open, float[] height, float[] filled, float seaLevel, Perlin noise,
        int sizeX, int sizeZ, DistributarySettings p, bool[] deltaLand)
    {
        var at = new Vector2(mouth % sizeX + 0.5f, mouth / sizeX + 0.5f);
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
    private static void Raise(List<int> course, float[] levee, bool[] bed, float bedCells, float reachCells, int sizeX, int sizeZ)
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
                    {
                        float r = Mathf.Sqrt(dx * dx + dz * dz);
                        levee[z * sizeX + x] = Mathf.Max(levee[z * sizeX + x], 1f - r / (reachCells + 1f));
                        bed[z * sizeX + x] |= r <= bedCells;
                    }
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
