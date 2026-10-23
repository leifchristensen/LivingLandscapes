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
    /// A delta grown at the mouth of the river that brings the most water into the sea (<see cref="DeltaGrowth"/>). The grown
    /// channels become the routing over the delta: their water surface falls from the river's level at the mouth to the sea along
    /// the channels, each channel cell drains the way most of its water went, and where a second way took a share of it the water
    /// divides there. Returns the cells of land built over the sea.
    /// </summary>
    private static int GrowDeltas(float[] height, float[] filled, int[] routed, int[] main, float[] discharge, float[] width, float[] depth,
        bool[] sea, float seaLevel, System.Func<int, bool> isOutlet, int sizeX, int sizeZ, TerrainChannelSettings p, RiverSystem system, ref int[] receiver,
        out Dictionary<int, (int second, float share)> forced, out int channelCells)
    {
        forced = null;
        channelCells = 0;
        int n = height.Length;
        if (sea == null || float.IsNaN(seaLevel))
            return 0;
        int mouth = -1;
        for (int i = 0; i < n; i++)
        {
            int r = routed[i];
            if (!sea[i] && width[i] > 0f && r >= 0 && sea[r] && (mouth < 0 || discharge[i] > discharge[mouth]))
                mouth = i;
        }
        if (mouth < 0)
        {
            Log.Message("[LivingLandscapes] Delta growth: no channel reaches the sea.");
            return 0;
        }
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
        var before = (float[])height.Clone();
        // Dev: the model's inputs, for tuning it outside the game (tools/delta-lab).
        if (GenCommandLine.CommandLineArgPassed("ll-delta-dump"))
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
        // The river's water surface at its mouth: the delta's channels fall from it to the sea.
        float apexLevel = filled[mouth] - 0.002f;
        int land = DeltaGrowth.Grow(height, sea, start, heading, w, d, seaLevel, sizeX, sizeZ, p.deltaGrowth, out int[] transitions, out float[] flow,
            out float[] stage, out string report);
        system.DeltaLand = new bool[n];
        for (int i = 0; i < n; i++)
        {
            if (height[i] != before[i])
                filled[i] = height[i];
            system.DeltaLand[i] = sea[i] && height[i] > seaLevel + 0.05f;
        }
        filled[mouth] = Mathf.Max(filled[mouth], apexLevel + 0.002f);

        // Channels: wet cells carrying a channel's share of the flow. Each drains along its parcels' main way; the water surface
        // falls along the channels from the mouth to where they open into the sea (distance along the channels from there).
        var channel = new bool[n];
        int Out(int c) { int s = 0; for (int k = 0; k < 8; k++) s += transitions[c * 8 + k]; return s; }
        int Target(int c, int k) { int x = c % sizeX + DX8[k], z = c / sizeX + DZ8[k]; return x < 0 || z < 0 || x >= sizeX || z >= sizeZ ? -1 : z * sizeX + x; }
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
        for (int i = 0; i < n; i++)
            channel[i] = flow[i] >= p.deltaGrowth.channelShare && stage[i] - height[i] > p.deltaGrowth.dryDepth && Out(i) > 0;
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
                int back = (k + 4) % 8;
                if (transitions[b * 8 + back] < p.deltaGrowth.splitShare * Out(b))
                    continue;
                distance[b] = distance[c] + 1;
                farthest = Mathf.Max(farthest, distance[b]);
                queue.Enqueue(b);
            }
        }
        // The river's level at the mouth can lie below the beach (its trench to the sea); the channels then fall from it.
        float exitLevel = Mathf.Min(seaLevel + 0.01f, apexLevel - 0.002f * Mathf.Max(farthest, 1));
        if (farthest == 0)
        {
            Log.Message($"[LivingLandscapes] Delta growth: {report}; no channel network (apex {apexLevel - seaLevel:F2} m above the sea).");
            receiver = Refill(filled, sizeX, sizeZ, isOutlet, p);
            return land;
        }
        for (int i = 0; i < n; i++)
        {
            if (distance[i] >= 0)
            {
                filled[i] = exitLevel + (apexLevel - exitLevel) * distance[i] / farthest;
                channelCells++;
            }
        }
        receiver = Refill(filled, sizeX, sizeZ, isOutlet, p);
        forced = new Dictionary<int, (int second, float share)>();
        if (filled[start] < filled[mouth])
            receiver[mouth] = start;
        for (int c = 0; c < n; c++)
        {
            if (distance[c] < 0)
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
                if (share >= p.deltaGrowth.splitShare)
                    forced[c] = (Target(c, second), share);
            }
        }
        Log.Message($"[LivingLandscapes] Delta growth: {report} ({GridMath.Strips(system.DeltaLand, sizeX, sizeZ)} in strips); {channelCells} channel cells " +
                    $"over {farthest} cells from the mouth to the sea, {forced.Count} splits.");
        return land;
    }

    private static readonly int[] DX8 = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] DZ8 = { 0, 1, 1, 1, 0, -1, -1, -1 };
}
