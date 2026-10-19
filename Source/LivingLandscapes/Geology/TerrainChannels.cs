using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>Channel settings (part of <see cref="GeologyGenerationExtension"/>). Discharge in map cells of runoff.</summary>
public class TerrainChannelSettings
{
    /// <summary>A channel starts where the discharge (drainage area times climate, plus inflow) reaches this.</summary>
    public float channelDischarge = 1500f;

    /// <summary>
    /// Hydraulic geometry: width = this × √discharge (cells), depth = <see cref="depthPerDischarge"/> × discharge^<see cref="depthExponent"/>
    /// (metres). A world river's inflow is the discharge that gives vanilla's width for its class (compressed to map
    /// scale by <see cref="RiverNetworkSettings.worldRiverWidth"/>), so vanilla's class scales the river, nothing else.
    /// </summary>
    public float widthPerSqrtDischarge = 0.04f;
    public float minWidth = 1.5f;
    public float depthPerDischarge = 0.045f;
    public float depthExponent = 0.3f;
    public float minDepth = 0.15f;

    /// <summary>
    /// Stream power scour: depth is multiplied by (slope / the map's median channel slope)^this, within the range, so
    /// steep reaches scour deep threads and flat reaches shoal.
    /// </summary>
    public float scourExponent = 0.25f;
    public FloatRange scourRange = new FloatRange(0.7f, 1.4f);

    /// <summary>
    /// Noise, as vanilla's river: width varies along the channel, depth in patches (vanilla's "shallowizer"), and the
    /// bank line cell by cell. Amplitudes are shares; wavelengths in cells.
    /// </summary>
    public float widthNoise = 0.15f;
    public float widthNoiseWavelength = 40f;
    public float depthNoise = 0.35f;
    public float depthNoiseWavelength = 18f;
    public float bankNoise = 0.2f;
    public float bankNoiseWavelength = 6f;

    /// <summary>Steps up and down a channel over which its flow direction is averaged (D8 steps are 45 degrees apart).</summary>
    public int flowSmoothingSteps = 3;

    /// <summary>
    /// Confinement field for the channel regimes: the valley floor is ground no higher than this (metres) above the
    /// water, measured across the channel up to this many channel widths each side.
    /// </summary>
    public float valleyFloorHeight = 1.5f;
    public float maxConfinementWidths = 8f;

    /// <summary>The map edge within this many cells of where water enters does not drain (the land beyond slopes in).</summary>
    public float entryGuardCells = 15f;

    /// <summary>Gradient (metres per cell) across filled hollows, so water crosses them toward the spill point.</summary>
    public float fillGradient = 1e-3f;
}

/// <summary>
/// The map's channels, from the landform's own drainage (Docs/WATER-AUDIT.md, reset steps 1 and 2). Water runs down
/// the steepest descent of the depression-filled landform; a channel forms wherever the discharge it gathers passes a
/// threshold, so channels follow the valleys the erosion model cut and bend with the terrain's own relief. Water
/// entering from beyond the map (world rivers, the region's channels) adds its discharge where it enters. Width and
/// depth follow discharge (hydraulic geometry), depth also stream power, each roughened by low-frequency noise as
/// vanilla does. The water surface is the filled landform, which never rises downstream (rule W1); where a channel
/// crosses a hollow deeper than itself the lake model fills the hollow instead.
/// </summary>
public static class TerrainChannels
{
    /// <summary>Lowers <paramref name="height"/> into channel beds and returns the channels with their per-cell water.</summary>
    /// <param name="courses">The erosion model's channels: where water enters from beyond the map, and the world river's class.</param>
    /// <param name="riverCentre">Where world rivers meet (map cells); a world river rising on the map (headwater) rises here.</param>
    /// <param name="climate">Runoff per cell, 0..1 (the map's climate wetness).</param>
    public static RiverSystem Build(float[] height, int sizeX, int sizeZ, List<ChannelCourse> courses, Vector2 riverCentre,
        float climate, float[] rockStrength, WaterRegimeDef water, TerrainChannelSettings p, SimpleCurve worldRiverWidth)
    {
        int n = sizeX * sizeZ;
        var system = new RiverSystem
        {
            Regime = water, WaterLevel = new float[n], Flow = new Vector2[n], ChannelOf = new int[n], Still = new bool[n],
        };
        for (int i = 0; i < n; i++)
        {
            system.WaterLevel[i] = float.NaN;
            system.ChannelOf[i] = -1;
        }

        float runoff = Mathf.Max(climate, 0.05f);
        float[] inflow = Inflow(courses, sizeX, sizeZ, riverCentre, runoff, p, worldRiverWidth, out bool[] worldSource, out float vanillaWidth);

        // The map edge drains, except where water enters: there the land beyond slopes in, so it is no outlet.
        var entries = new List<Vector2>();
        for (int i = 0; i < n; i++)
        {
            if (inflow[i] > 0f)
                entries.Add(new Vector2(i % sizeX, i / sizeX));
        }
        bool IsOutlet(int i)
        {
            int x = i % sizeX, z = i / sizeX;
            if (x != 0 && z != 0 && x != sizeX - 1 && z != sizeZ - 1)
                return false;
            var v = new Vector2(x, z);
            return entries.TrueForAll(e => (e - v).sqrMagnitude > p.entryGuardCells * p.entryGuardCells);
        }
        var filled = (float[])height.Clone();
        GridMath.FillDepressions(filled, sizeX, sizeZ, IsOutlet, p.fillGradient, includeEdges: false);
        int[] receiver = GridMath.Receivers(filled, sizeX, sizeZ);
        // Water reaching a draining edge leaves the map there rather than running along the edge.
        for (int i = 0; i < n; i++)
        {
            if (IsOutlet(i))
                receiver[i] = -1;
        }

        // Discharge: each cell's own runoff plus inflow, gathered downstream. Every cell adds runoff, so a receiver
        // always carries more than its donors and sorting by discharge orders donors before receivers.
        var order = new int[n];
        for (int i = 0; i < n; i++)
            order[i] = i;
        System.Array.Sort(order, (a, b) => filled[b].CompareTo(filled[a]));
        var discharge = new float[n];
        var world = new bool[n];
        for (int i = 0; i < n; i++)
        {
            discharge[i] = runoff + inflow[i];
            world[i] = worldSource[i];
        }
        foreach (int c in order)
        {
            int r = receiver[c];
            if (r < 0)
                continue;
            discharge[r] += discharge[c];
            world[r] |= world[c];
        }

        // Where channels start; the water regime nudges it (a denser or sparser network).
        float threshold = p.channelDischarge * (water?.nudges.discharge ?? 1f);
        var cells = new List<int>();
        for (int c = 0; c < n; c++)
        {
            if (discharge[c] >= threshold)
                cells.Add(c);
        }
        if (cells.Count == 0)
            return system;
        cells.Sort((a, b) => discharge[a].CompareTo(discharge[b]));

        // The main donor of each channel cell: the channel cell upstream bringing the most water.
        var main = new int[n];
        for (int i = 0; i < n; i++)
            main[i] = -1;
        foreach (int c in cells)
        {
            int r = receiver[c];
            if (r >= 0 && discharge[r] >= threshold && (main[r] < 0 || discharge[c] > discharge[main[r]]))
                main[r] = c;
        }

        Vector2[] direction = Directions(cells, receiver, main, sizeX, p.flowSmoothingSteps);
        float[] width = new float[n], depth = new float[n];
        var regimes = new Dictionary<string, int>();
        float[] bankAmplitude = Shape(cells, height, filled, rockStrength, receiver, discharge, direction, sizeX, sizeZ, water, p,
            width, depth, regimes);

        List<RiverPlan> plans = Trace(cells, receiver, main, discharge, inflow, world, width, sizeX, sizeZ, runoff, vanillaWidth, out int[] planOf);
        int inHollows = Paint(height, filled, cells, width, depth, bankAmplitude, direction, planOf, sizeX, sizeZ, p, system);
        system.Channels = plans;
        int painted = 0;
        for (int i = 0; i < n; i++)
        {
            if (system.ChannelOf[i] >= 0)
                painted++;
        }
        float maxWidth = 0f, maxDepth = 0f;
        foreach (int c in cells)
        {
            maxWidth = Mathf.Max(maxWidth, width[c]);
            maxDepth = Mathf.Max(maxDepth, depth[c]);
        }
        Log.Message($"[LivingLandscapes] Channels: {plans.Count} channels over {cells.Count} centre cells ({inHollows} in hollows the lake model fills), " +
                    $"{painted} water cells, widest {maxWidth:F1} cells, deepest {maxDepth:F2} m, climate {climate:F2}; " +
                    $"regimes by channel cell [{string.Join(", ", regimes.OrderByDescending(r => r.Value).Select(r => $"{r.Key} {100 * r.Value / cells.Count}%"))}].");
        return system;
    }

    /// <summary>
    /// Discharge entering the map: world rivers with the discharge that gives their vanilla width, the region's other
    /// channels with the runoff of the area they drain. On a tile the world gives no river, nothing entering is larger
    /// than a creek, as in vanilla. A world river with no inflow (headwater) rises at the river centre.
    /// </summary>
    private static float[] Inflow(List<ChannelCourse> courses, int sizeX, int sizeZ, Vector2 riverCentre, float runoff,
        TerrainChannelSettings p, SimpleCurve worldRiverWidth, out bool[] worldSource, out float vanillaWidth)
    {
        int n = sizeX * sizeZ;
        var inflow = new float[n];
        worldSource = new bool[n];
        vanillaWidth = 0f;
        float DischargeFor(float width) => Mathf.Pow(worldRiverWidth.Evaluate(width) / p.widthPerSqrtDischarge, 2f);
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
            int cell = RiverChannels.Index(course.Points[first], sizeX, sizeZ);
            if (course.VanillaWidth > 0f)
            {
                inflow[cell] += DischargeFor(course.VanillaWidth);
                worldSource[cell] = worldEnters = true;
            }
            else
                inflow[cell] += anyWorldRiver ? course.EntryArea * runoff : Mathf.Min(course.EntryArea * runoff, creek);
        }
        if (anyWorldRiver && !worldEnters)
        {
            int cell = RiverChannels.Index(riverCentre, sizeX, sizeZ);
            inflow[cell] += DischargeFor(vanillaWidth);
            worldSource[cell] = true;
        }
        return inflow;
    }

    /// <summary>
    /// Width and depth of each channel cell from its discharge, stream power and noise, nudged by the channel regimes
    /// its data matches (Docs/WATER-AUDIT.md, step 4). Fields: Discharge (log10), Slope (water drop per cell over a few
    /// cells), BankStrength (rock strength 0..1), Confinement (valley floor width over channel width). Returns the bank
    /// noise amplitude per cell and counts the dominant regime per cell for the log.
    /// </summary>
    private static float[] Shape(List<int> cells, float[] natural, float[] filled, float[] rockStrength, int[] receiver, float[] discharge,
        Vector2[] direction, int sizeX, int sizeZ, WaterRegimeDef water, TerrainChannelSettings p, float[] width, float[] depth,
        Dictionary<string, int> regimes)
    {
        // Slope over a few cells downstream (single D8 steps across filled flats are nearly zero).
        var slope = new float[cells.Count];
        for (int k = 0; k < cells.Count; k++)
        {
            int c = cells[k], d = c;
            float run = 0f;
            for (int s = 0; s < 5 && receiver[d] >= 0; s++)
            {
                int r = receiver[d];
                run += (r % sizeX != d % sizeX && r / sizeX != d / sizeX) ? 1.414f : 1f;
                d = r;
            }
            slope[k] = run > 0f ? (filled[c] - filled[d]) / run : 0f;
        }
        var sorted = (float[])slope.Clone();
        System.Array.Sort(sorted);
        float reference = Mathf.Max(sorted[sorted.Length / 2], 1e-4f);

        var bankAmplitude = new float[natural.Length];
        var widthNoise = new Perlin(1.0 / p.widthNoiseWavelength, 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);
        var depthNoise = new Perlin(1.0 / p.depthNoiseWavelength, 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);
        var fields = new Dictionary<string, float>();
        for (int k = 0; k < cells.Count; k++)
        {
            int c = cells[k], x = c % sizeX, z = c / sizeX;
            float q = discharge[c];
            float baseWidth = Mathf.Max(p.minWidth, p.widthPerSqrtDischarge * Mathf.Sqrt(q));
            fields["Discharge"] = Mathf.Log10(q);
            fields["Slope"] = slope[k];
            fields["BankStrength"] = rockStrength != null ? rockStrength[c] : 0.5f;
            fields["Confinement"] = ValleyFloor(natural, filled[c], c, direction[c], baseWidth, sizeX, sizeZ, p) / baseWidth;
            ChannelNudges nudge = WaterRegimes.Nudges(fields, water, out ChannelRegimeDef dominant);
            string key = dominant?.defName ?? "none";
            regimes[key] = regimes.TryGetValue(key, out int count) ? count + 1 : 1;

            width[c] = Mathf.Max(p.minWidth, baseWidth * nudge.width
                                             * (1f + p.widthNoise * nudge.widthNoise * (float)widthNoise.GetValue(x, 0.0, z)));
            float scour = p.scourRange.ClampToRange(Mathf.Pow(Mathf.Max(slope[k], 1e-6f) / reference, p.scourExponent));
            depth[c] = Mathf.Max(p.minDepth, p.depthPerDischarge * Mathf.Pow(q, p.depthExponent) * scour * nudge.depth
                                             * (1f + Mathf.Clamp(p.depthNoise * nudge.depthNoise, 0f, 0.9f) * (float)depthNoise.GetValue(x, 0.0, z)));
            bankAmplitude[c] = Mathf.Clamp(p.bankNoise * nudge.bankNoise, 0f, 0.8f);
        }
        return bankAmplitude;
    }

    /// <summary>
    /// Width (cells) of the valley floor across a channel cell: ground no higher than the water surface plus
    /// <see cref="TerrainChannelSettings.valleyFloorHeight"/>, measured both ways across the flow, up to
    /// <see cref="TerrainChannelSettings.maxConfinementWidths"/> channel widths each side.
    /// </summary>
    private static float ValleyFloor(float[] natural, float level, int c, Vector2 direction, float width, int sizeX, int sizeZ, TerrainChannelSettings p)
    {
        var across = new Vector2(-direction.y, direction.x);
        if (across == Vector2.zero)
            return width;
        float limit = Mathf.Max(width, 2f) * p.maxConfinementWidths, floor = 1f;
        var start = new Vector2(c % sizeX + 0.5f, c / sizeX + 0.5f);
        foreach (float sign in new[] { -1f, 1f })
        {
            for (float t = 1f; t <= limit; t += 1f)
            {
                Vector2 v = start + across * (sign * t);
                int x = (int)v.x, z = (int)v.y;
                if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ || natural[z * sizeX + x] > level + p.valleyFloorHeight)
                    break;
                floor += 1f;
            }
        }
        return floor;
    }

    /// <summary>Downstream direction of each channel cell, averaged over a few steps up and down the channel.</summary>
    private static Vector2[] Directions(List<int> cells, int[] receiver, int[] main, int sizeX, int steps)
    {
        var direction = new Vector2[receiver.Length];
        Vector2 At(int c) => new Vector2(c % sizeX, c / sizeX);
        foreach (int c in cells)
        {
            int down = c, up = c;
            for (int s = 0; s < steps; s++)
            {
                if (receiver[down] >= 0)
                    down = receiver[down];
                if (main[up] >= 0)
                    up = main[up];
            }
            Vector2 v = At(down) - At(up);
            direction[c] = v == Vector2.zero ? Vector2.zero : v.normalized;
        }
        return direction;
    }

    /// <summary>
    /// Channels as courses: each runs from its head (or where it enters the map) down through every junction where it
    /// brings the most water, and ends where it joins a larger one or leaves the map. Channels carrying a world river's
    /// water take its vanilla width; the one leaving the map is the world river.
    /// </summary>
    private static List<RiverPlan> Trace(List<int> cells, int[] receiver, int[] main, float[] discharge, float[] inflow, bool[] world, float[] width,
        int sizeX, int sizeZ, float runoff, float vanillaWidth, out int[] planOf)
    {
        int n = receiver.Length;
        planOf = new int[n];
        for (int i = 0; i < n; i++)
            planOf[i] = -1;
        var plans = new List<RiverPlan>();
        var ends = new List<int>();
        var parents = new List<int>();
        Vector2 Centre(int c) => new Vector2(c % sizeX + 0.5f, c / sizeX + 0.5f);
        foreach (int c in cells)
        {
            if (planOf[c] < 0)
            {
                // A head: no channel flows into this cell as its main donor.
                planOf[c] = plans.Count;
                plans.Add(new RiverPlan());
                ends.Add(c);
                parents.Add(-1);
            }
            int k = planOf[c];
            // Water entering from beyond the map, as the drainage area that would bring it (for drainage and wetness).
            plans[k].EntryArea += inflow[c] / runoff;
            plans[k].Main.Add(Centre(c));
            plans[k].MainWidth.Add(Mathf.Max(1f, Mathf.Round(width[c])));
            plans[k].NominalWidth = width[c];
            ends[k] = c;
            if (world[c] && vanillaWidth > 0f)
                plans[k].VanillaWidth = vanillaWidth;
            int r = receiver[c];
            if (r >= 0 && main[r] == c)
                planOf[r] = k;
        }
        // Each channel that ends where a larger one passes joins it there.
        for (int k = 0; k < plans.Count; k++)
        {
            int r = receiver[ends[k]];
            if (r >= 0 && planOf[r] >= 0 && planOf[r] != k)
            {
                plans[k].Main.Add(Centre(r));
                plans[k].MainWidth.Add(plans[k].MainWidth[plans[k].MainWidth.Count - 1]);
                parents[k] = planOf[r];
            }
        }

        // The world river: the largest channel carrying world water that leaves the map.
        int worldRiver = -1;
        for (int k = 0; k < plans.Count; k++)
        {
            if (parents[k] < 0 && plans[k].VanillaWidth > 0f && (worldRiver < 0 || discharge[ends[k]] > discharge[ends[worldRiver]]))
                worldRiver = k;
        }
        if (worldRiver >= 0)
            plans[worldRiver].WorldRiver = true;

        // Larger channels first (the world river leads), parents re-indexed.
        var sorted = new List<int>();
        for (int k = 0; k < plans.Count; k++)
            sorted.Add(k);
        sorted.Sort((a, b) => a == worldRiver ? -1 : b == worldRiver ? 1 : discharge[ends[b]].CompareTo(discharge[ends[a]]));
        var index = new int[plans.Count];
        for (int i = 0; i < sorted.Count; i++)
            index[sorted[i]] = i;
        var result = new List<RiverPlan>();
        foreach (int k in sorted)
        {
            plans[k].Parent = parents[k] >= 0 ? index[parents[k]] : -1;
            result.Add(plans[k]);
        }
        for (int i = 0; i < n; i++)
        {
            if (planOf[i] >= 0)
                planOf[i] = index[planOf[i]];
        }
        return result;
    }

    /// <summary>
    /// Lays the water around each channel's centre line: a disc of the channel's width, its edge roughened by bank
    /// noise, each cell taking the channel cell that gives it the deepest water. Depth falls from the centre to the
    /// bank; the bed is lowered to the water surface less that depth (ground is only ever lowered). Where the filled
    /// surface lies deeper above the ground than the channel's own depth the channel crosses a hollow: the lake model
    /// fills it.
    /// </summary>
    private static int Paint(float[] height, float[] filled, List<int> cells, float[] width, float[] depth, float[] bankAmplitude, Vector2[] direction,
        int[] planOf, int sizeX, int sizeZ, TerrainChannelSettings p, RiverSystem system)
    {
        int n = height.Length;
        var best = new float[n];
        var owner = new int[n];
        for (int i = 0; i < n; i++)
        {
            best[i] = 0f;
            owner[i] = -1;
        }
        int inHollows = 0;
        var bank = new Perlin(1.0 / p.bankNoiseWavelength, 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);
        foreach (int c in cells)
        {
            if (filled[c] - height[c] > depth[c])
            {
                inHollows++;
                continue;
            }
            int cx = c % sizeX, cz = c / sizeX;
            float half = width[c] * 0.5f;
            int reach = Mathf.CeilToInt(half * (1f + bankAmplitude[c]) + 0.5f);
            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                        continue;
                    float edge = half * (1f + bankAmplitude[c] * (float)bank.GetValue(x, 0.0, z)) + 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dz * dz);
                    if (r >= edge)
                        continue;
                    float t = r / edge;
                    float d = Mathf.Max(p.minDepth, depth[c] * (1f - t * t));
                    int i = z * sizeX + x;
                    if (d > best[i] || (d == best[i] && owner[i] >= 0 && filled[c] < filled[owner[i]]))
                    {
                        best[i] = d;
                        owner[i] = c;
                    }
                }
            }
        }
        for (int i = 0; i < n; i++)
        {
            int c = owner[i];
            if (c < 0)
                continue;
            float level = filled[c];
            system.WaterLevel[i] = level;
            system.Flow[i] = direction[c];
            system.ChannelOf[i] = planOf[c];
            height[i] = Mathf.Min(height[i], level - best[i]);
        }
        return inHollows;
    }
}
