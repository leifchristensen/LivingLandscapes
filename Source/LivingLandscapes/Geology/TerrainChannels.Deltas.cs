using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>
/// Stage 4, land at base level: load a river drops where it meets the sea or a lake builds the floor up; ground built above the
/// sea becomes delta land the coast mutator leaves alone. (Distributaries are drawn by <see cref="Distributaries"/>.)
/// </summary>
public static partial class TerrainChannels
{
    /// <summary>
    /// Deltas from the sediment budget (Docs/SEDIMENT-PLAN.md, step 3): where rivers drop their load into the sea or a lake the
    /// floor rises by the alluvium laid on it; ground built above the water is delta land, flow over it spreads (distributaries),
    /// and the water is routed again over the new ground. Returns the number of delta land cells.
    /// </summary>
    private static int BuildDeltas(float[] height, float[] filled, List<int> cells, int[] receiver, float[] discharge, float dischargeScale, float[] width,
        float[] inflow, float[] rockStrength, float[] depth, bool[] sea, float[] spread, System.Func<int, bool> isOutlet, int sizeX, int sizeZ,
        TerrainChannelSettings p, RiverSystem system, ref int[] receiverOut)
    {
        int n = height.Length;
        var baseLevel = new bool[n];
        float seaLevel = float.MinValue;
        bool anyBase = false;
        for (int i = 0; i < n; i++)
        {
            bool inSea = sea != null && sea[i];
            baseLevel[i] = inSea || filled[i] - height[i] > Mathf.Max(depth[i], p.minDepth) + 0.05f;
            anyBase |= baseLevel[i];
            if (inSea)
                seaLevel = Mathf.Max(seaLevel, height[i]);
        }
        if (!anyBase)
            return 0;
        Sediment.Deposits(cells, receiver, discharge, dischargeScale, filled, inflow, rockStrength, baseLevel, sizeX, p.sediment,
            out float[] deposit, out float[] grain, out _, out float atBase, out _);
        if (atBase <= 0f)
            return 0;
        // Only what settled at base level builds land: rivers drop everything there; floodplains upstream keep their shape.
        for (int i = 0; i < n; i++)
        {
            if (!baseLevel[i])
                deposit[i] = 0f;
        }
        Sediment.Spread(cells, receiver, null, deposit, grain, width, filled, height, sizeX, sizeZ, p.sediment, out float[] built, out _);
        system.DeltaLand = new bool[n];
        int land = 0;
        for (int i = 0; i < n; i++)
        {
            if (!baseLevel[i] || built[i] <= 0f)
                continue;
            float raise = built[i] * p.sediment.landBuilding;
            height[i] += raise;
            filled[i] = Mathf.Max(filled[i], height[i]);
            spread[i] = Mathf.Max(spread[i], 1f) * p.sediment.deltaSpread;
            if (sea != null && sea[i] && height[i] > seaLevel + 0.05f)
            {
                system.DeltaLand[i] = true;
                land++;
            }
        }
        receiverOut = Refill(filled, sizeX, sizeZ, isOutlet, p);
        var raised = new bool[n];
        for (int i = 0; i < n; i++)
            raised[i] = baseLevel[i] && built[i] > 0f;
        Log.Message($"[LivingLandscapes] Channel deltas raised {System.Array.FindAll(raised, r => r).Length} cells at base level ({GridMath.Strips(raised, sizeX, sizeZ)} in strips), {land} above the sea ({GridMath.Strips(system.DeltaLand, sizeX, sizeZ)} in strips).");
        return land;
    }

    /// <summary>
    /// Deltas grown at every river mouth that still carries a channel (<see cref="DeltaGrowth"/>), largest first, each on the ground
    /// the ones before left. The grown channels become the routing over the deltas: their water surface falls from the river's level
    /// at its mouth to the sea along the channels, each channel cell drains the way most of its water went, and where a second way
    /// took a share of it the water divides there. Returns the cells of land built over the sea.
    /// </summary>
    private static int GrowDeltas(float[] height, float[] filled, int[] routed, int[] main, float[] discharge, float[] width, float[] depth,
        bool[] sea, float seaLevel, System.Func<int, bool> isOutlet, int sizeX, int sizeZ, TerrainChannelSettings p, RiverSystem system, ref int[] receiver,
        out Dictionary<int, (int second, float share)> forced, out int channelCells, string model = "grown", float[] rockStrength = null, bool[] drowned = null)
    {
        forced = null;
        channelCells = 0;
        int n = height.Length;
        DeltaGrowthSettings g = p.deltaGrowth;
        bool hydro = model == "hydro" || model == "stream";
        if (sea == null || float.IsNaN(seaLevel))
            return 0;
        // Mouths: channel cells running into the sea, wide enough to carry water of their own; one per river (the cell with the
        // most water among mouths within a few widths).
        // A mouth's width is the river's over its last few cells (the very last can thin where the channel fans into the shore).
        float RiverWidth(int c)
        {
            float w = width[c];
            for (int k = 0; k < 6 && main[c] >= 0; k++)
            {
                c = main[c];
                w = Mathf.Max(w, width[c]);
            }
            return w;
        }
        var mouths = new List<int>();
        for (int i = 0; i < n; i++)
        {
            int r = routed[i];
            if (!sea[i] && r >= 0 && sea[r] && RiverWidth(i) >= g.minMouthWidth)
                mouths.Add(i);
        }
        mouths.Sort((a, b) => discharge[b].CompareTo(discharge[a]));
        var rivers = new List<int>();
        foreach (int m in mouths)
        {
            bool near = rivers.Exists(o =>
            {
                float dx = o % sizeX - m % sizeX, dz = o / sizeX - m / sizeX;
                return dx * dx + dz * dz < Mathf.Pow(2f * width[o] + 4f, 2f);
            });
            if (!near && rivers.Count < g.maxMouths)
                rivers.Add(m);
        }
        if (rivers.Count == 0)
        {
            int strongest = -1;
            for (int i = 0; i < n; i++)
            {
                int r = routed[i];
                if (!sea[i] && r >= 0 && sea[r] && (strongest < 0 || discharge[i] > discharge[strongest]))
                    strongest = i;
            }
            Log.Message($"[LivingLandscapes] Delta growth: no channel reaches the sea" + (strongest >= 0
                ? $" (the most water into it, {discharge[strongest]:F0}, at ({strongest % sizeX}, {strongest / sizeX}), {width[strongest]:F1} wide)." : "."));
            return 0;
        }

        var before = (float[])height.Clone();
        var grown = new List<(int mouth, int start, float apex, int[] transitions, float[] flow, float[] stage)>();
        var reports = new List<string>();
        int land = 0;
        foreach (int mouth in rivers)
        {
            // The river's width and depth over its last cells, and its heading into the sea.
            int up = mouth;
            float w = width[mouth], d = depth[mouth];
            for (int k = 0; k < 6 && main[up] >= 0; k++)
            {
                up = main[up];
                w = Mathf.Max(w, width[up]);
                d = Mathf.Max(d, depth[up]);
            }
            int start = routed[mouth];
            Vector2 heading = new Vector2(start % sizeX - up % sizeX, start / sizeX - up / sizeX);
            heading = heading == Vector2.zero ? Vector2.right : heading.normalized;
            // Dev: the largest delta's inputs, for tuning the model outside the game (tools/delta-lab).
            if (grown.Count == 0 && GenCommandLine.CommandLineArgPassed("ll-delta-dump"))
            {
                System.IO.Directory.CreateDirectory(DevTools.LayerImageExporter.Folder);
                using var dump = new System.IO.BinaryWriter(System.IO.File.Create(System.IO.Path.Combine(DevTools.LayerImageExporter.Folder, "delta-input.bin")));
                dump.Write(sizeX); dump.Write(sizeZ); dump.Write(start); dump.Write(heading.x); dump.Write(heading.y); dump.Write(w); dump.Write(d); dump.Write(seaLevel);
                for (int i = 0; i < n; i++)
                {
                    dump.Write(height[i]);
                    dump.Write(sea[i]);
                }
            }
            if (hydro)
            {
                // The solved surface and sediment balance: the bed it leaves is routed by the channel stage's own drainage.
                float[] erodibility = null;
                if (rockStrength != null)
                {
                    erodibility = new float[n];
                    for (int i = 0; i < n; i++)
                        erodibility[i] = Mathf.Clamp01(1f - rockStrength[i]);
                }
                string modelReport;
                if (model == "stream")
                {
                    // Materials for the shore-impact rule: the sea floor is loose sand, land is soil or mud, and hard rock (strong beds,
                    // intrusions) stands (rock strength already follows each bed's rock and its joints).
                    var material = new float[n];
                    for (int i = 0; i < n; i++)
                        material[i] = sea[i] ? 1f : rockStrength != null && rockStrength[i] >= p.deltaStream.rockStrengthForRock ? 0.03f : 0.5f;
                    // Its own random sequence, read without advancing the map's shared one.
                    Rand.PushState();
                    var random = new System.Random(Rand.Int ^ start);
                    Rand.PopState();
                    land += DeltaStream.Grow(height, sea, material, start, heading, w, d, seaLevel, sizeX, sizeZ, p.deltaStream, random, true,
                        out _, out _, out modelReport, drowned);
                }
                else
                    land += DeltaHydro.Grow(height, sea, erodibility, start, heading, w, d, seaLevel, sizeX, sizeZ, p.deltaHydro, out _, out _, out modelReport);
                reports.Add($"({mouth % sizeX}, {mouth / sizeX}) {modelReport}");
                continue;
            }
            // The river's water surface at its mouth: the delta's channels fall from it to the sea.
            float apex = filled[mouth] - 0.002f;
            land += DeltaGrowth.Grow(height, sea, start, heading, w, d, seaLevel, sizeX, sizeZ, g, out int[] transitions, out float[] flow,
                out float[] stage, out string report);
            grown.Add((mouth, start, apex, transitions, flow, stage));
            reports.Add($"({mouth % sizeX}, {mouth / sizeX}) {report}");
        }
        system.DeltaLand = new bool[n];
        land = 0;
        for (int i = 0; i < n; i++)
        {
            if (height[i] != before[i])
                filled[i] = height[i];
            system.DeltaLand[i] = sea[i] && height[i] > seaLevel + 0.05f;
            if (system.DeltaLand[i])
            {
                // Land the delta built is no longer sea: not base level, so water runs on across it to open water.
                sea[i] = false;
                land++;
            }
        }
        foreach (var delta in grown)
            filled[delta.mouth] = Mathf.Max(filled[delta.mouth], delta.apex + 0.002f);
        if (hydro)
        {
            receiver = Refill(filled, sizeX, sizeZ, isOutlet, p);
            Log.Message($"[LivingLandscapes] Delta {model} at {rivers.Count} mouths: {land} cells of land over the sea " +
                        $"({GridMath.Strips(system.DeltaLand, sizeX, sizeZ)} in strips); " + string.Join("; ", reports) + ".");
            return land;
        }

        int Target(int c, int k) { int x = c % sizeX + DX8[k], z = c / sizeX + DZ8[k]; return x < 0 || z < 0 || x >= sizeX || z >= sizeZ ? -1 : z * sizeX + x; }
        // Each delta's channels: wet cells carrying a channel's share of its flow. Each drains along its parcels' main way; the water
        // surface falls along the channels from the mouth to where they open into the sea (distance along the channels from there).
        // A cell an earlier (larger) delta graded keeps that delta's channel.
        var owner = new int[n];
        for (int i = 0; i < n; i++)
            owner[i] = -1;
        var distances = new List<int[]>();
        int networks = 0;
        for (int index = 0; index < grown.Count; index++)
        {
            (int mouth, int start, float apex, int[] transitions, float[] flow, float[] stage) = grown[index];
            int Out(int c) { int s = 0; for (int k = 0; k < 8; k++) s += transitions[c * 8 + k]; return s; }
            int Main(int c)
            {
                int best = -1;
                for (int k = 0; k < 8; k++)
                {
                    if (Target(c, k) >= 0 && transitions[c * 8 + k] > 0 && (best < 0 || transitions[c * 8 + k] > transitions[c * 8 + best]))
                        best = k;
                }
                return best;
            }
            var channel = new bool[n];
            for (int i = 0; i < n; i++)
                channel[i] = owner[i] < 0 && flow[i] >= g.channelShare && stage[i] - height[i] > g.dryDepth && Out(i) > 0;
            var distance = new int[n];
            var queue = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                distance[i] = -1;
                int k = channel[i] ? Main(i) : -1;
                if (k >= 0 && !channel[Target(i, k)])
                {
                    distance[i] = 0;
                    queue.Enqueue(i);
                }
            }
            int farthest = 0;
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                for (int k = 0; k < 8; k++)
                {
                    int b = Target(c, k);
                    if (b < 0 || !channel[b] || distance[b] >= 0)
                        continue;
                    // b feeds c if a fair share of b's water went to c.
                    if (transitions[b * 8 + (k + 4) % 8] < g.splitShare * Out(b))
                        continue;
                    distance[b] = distance[c] + 1;
                    farthest = Mathf.Max(farthest, distance[b]);
                    queue.Enqueue(b);
                }
            }
            distances.Add(distance);
            if (farthest == 0)
                continue;
            networks++;
            // The river's level at the mouth can lie below the beach (its trench to the sea); the channels then fall from it.
            float exitLevel = Mathf.Min(seaLevel + 0.01f, apex - 0.002f * farthest);
            for (int i = 0; i < n; i++)
            {
                if (distance[i] < 0)
                    continue;
                filled[i] = exitLevel + (apex - exitLevel) * distance[i] / farthest;
                owner[i] = index;
                channelCells++;
            }
        }

        receiver = Refill(filled, sizeX, sizeZ, isOutlet, p);
        forced = new Dictionary<int, (int second, float share)>();
        for (int index = 0; index < grown.Count; index++)
        {
            (int mouth, int start, _, int[] transitions, _, _) = grown[index];
            if (filled[start] < filled[mouth])
                receiver[mouth] = start;
            for (int c = 0; c < n; c++)
            {
                if (owner[c] != index)
                    continue;
                // The two ways most of the water went, lower than the cell.
                int first = -1, second = -1;
                for (int k = 0; k < 8; k++)
                {
                    int t = Target(c, k);
                    if (t < 0 || transitions[c * 8 + k] == 0 || filled[t] >= filled[c])
                        continue;
                    if (first < 0 || transitions[c * 8 + k] > transitions[c * 8 + first])
                    {
                        second = first;
                        first = k;
                    }
                    else if (second < 0 || transitions[c * 8 + k] > transitions[c * 8 + second])
                        second = k;
                }
                if (first < 0)
                    continue;
                receiver[c] = Target(c, first);
                int apart = second < 0 ? 0 : Mathf.Min((first - second + 8) % 8, (second - first + 8) % 8);
                if (apart >= 2)
                {
                    float share = transitions[c * 8 + second] / (float)(transitions[c * 8 + first] + transitions[c * 8 + second]);
                    if (share >= g.splitShare)
                        forced[c] = (Target(c, second), share);
                }
            }
        }
        Log.Message($"[LivingLandscapes] Delta growth at {grown.Count} mouths ({networks} with channels): {land} cells of land over the sea " +
                    $"({GridMath.Strips(system.DeltaLand, sizeX, sizeZ)} in strips), {channelCells} channel cells, {forced.Count} splits; " +
                    string.Join("; ", reports) + ".");
        return land;
    }

    private static readonly int[] DX8 = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] DZ8 = { 0, 1, 1, 1, 0, -1, -1, -1 };
}
