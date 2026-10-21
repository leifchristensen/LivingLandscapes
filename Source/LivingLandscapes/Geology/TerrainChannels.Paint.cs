using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>
/// Stage 6, carving: channels are cut into the ground at their water level (never rising downstream), springs pool where a
/// headwater rises in a hollow, and the sediment budget's in-channel deposits build bars and islands.
/// </summary>
public static partial class TerrainChannels
{
    /// <summary>
    /// Bars where the sediment budget deposits in the channel (<see cref="Sediment"/>): where the flow loses the power to carry
    /// its load (an easing slope, a widening, below a confluence whose tributary brings more than the joined flow carries) the bed
    /// rises into mid-channel bars; cells raised above the water become dry (islands). The channel regimes' deposition nudge
    /// scales how readily a deposit forms a bar. Returns the number of bars.
    /// </summary>
    private static int Bars(float[] height, List<int> cells, float[] width, float[] depth, bool[] moving, float[] deposit,
        float[] deposition, Vector2[] direction, int sizeX, int sizeZ, TerrainChannelSettings p, RiverSystem system, out int islands)
    {
        int n = height.Length;
        islands = 0;
        // Deposit per channel width, against the map's typical in-channel deposit (the budget's units are relative).
        var perWidth = new List<float>();
        foreach (int c in cells)
        {
            if (moving[c] && deposit[c] > 0f && width[c] >= p.minBarChannelWidth)
                perWidth.Add(deposit[c] / width[c]);
        }
        if (perWidth.Count == 0)
            return 0;
        perWidth.Sort();
        float reference = Mathf.Max(perWidth[(int)(perWidth.Count * p.barReferenceShare)], 1e-6f);
        var candidates = new List<(int cell, float strength)>();
        foreach (int c in cells)
        {
            if (!moving[c] || width[c] < p.minBarChannelWidth || deposit[c] <= 0f || float.IsNaN(system.WaterLevel[c]))
                continue;
            float drop = Mathf.Clamp01(deposit[c] / width[c] / reference * 0.5f) * deposition[c] * p.deposition;
            if (drop >= p.barThreshold)
                candidates.Add((c, Mathf.Min(drop, 1f)));
        }
        candidates.Sort((a, b) => b.strength.CompareTo(a.strength));

        var placed = new List<(Vector2 at, float length)>();
        var warp = new Perlin(0.15, 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);
        foreach ((int c, float strength) in candidates)
        {
            var at = new Vector2(c % sizeX + 0.5f, c / sizeX + 0.5f);
            float length = p.barLengthWidths * width[c], half = p.barWidthShare * width[c] * 0.5f;
            if (placed.Exists(b => (b.at - at).magnitude < Mathf.Max(b.length, length)))
                continue;
            placed.Add((at, length));
            float level = system.WaterLevel[c];
            float bed = level - depth[c];
            float crest = bed + (depth[c] + p.barRise) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(p.barThreshold, 1f, strength));
            Vector2 along = direction[c] == Vector2.zero ? Vector2.right : direction[c], across = new Vector2(-along.y, along.x);
            int reach = Mathf.CeilToInt(length * 0.5f + 1f);
            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = c % sizeX + dx, z = c / sizeX + dz;
                    if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                        continue;
                    int i = z * sizeX + x;
                    if (float.IsNaN(system.WaterLevel[i]))
                        continue;
                    var v = new Vector2(dx, dz);
                    float a = Vector2.Dot(v, along) / (length * 0.5f);
                    // Teardrop: pointed upstream (narrowing toward a < 0), round downstream, as flow-shaped islands are.
                    float taper = a < 0f ? 1f + 1.2f * -a : 1f;
                    float b = Vector2.Dot(v, across) * taper / Mathf.Max(half, 0.5f);
                    float e = Mathf.Sqrt(a * a + b * b) * (1f + 0.3f * (float)warp.GetValue(x, 0.0, z));
                    if (e >= 1f)
                        continue;
                    float surface = Mathf.Lerp(crest, bed, e * e);
                    height[i] = Mathf.Max(height[i], surface);
                    if (height[i] >= system.WaterLevel[i])
                    {
                        // Above the water (that cell's own level, which falls along the bar): an island.
                        system.WaterLevel[i] = float.NaN;
                        system.ChannelOf[i] = -1;
                        system.Flow[i] = Vector2.zero;
                        islands++;
                    }
                }
            }
        }
        return placed.Count;
    }

    /// <summary>
    /// A river rising on the map (headwater) rises from springs; where they rise in a hollow, their water pools to the
    /// hollow's spill level before it flows on: every cell connected to a spring and lying below the filled surface
    /// becomes still water at that level, part of the river that leaves it. Returns the number of lake cells.
    /// </summary>
    private static int SpringLake(float[] natural, float[] filled, float[] inflow, bool[] worldSource, int sizeX, int sizeZ,
        TerrainChannelSettings p, RiverSystem system)
    {
        int n = natural.Length;
        bool InHollow(int i) => filled[i] - natural[i] > p.springLakeDepth;
        var queue = new Queue<int>();
        var seen = new bool[n];
        int plan = -1;
        for (int i = 0; i < n; i++)
        {
            int x = i % sizeX, z = i / sizeX;
            bool edge = x == 0 || z == 0 || x == sizeX - 1 || z == sizeZ - 1;
            if (worldSource[i] && inflow[i] > 0f && !edge && InHollow(i))
            {
                seen[i] = true;
                queue.Enqueue(i);
            }
        }
        for (int i = 0; i < n && plan < 0; i++)
        {
            if (seen[i] && system.ChannelOf[i] >= 0)
                plan = system.ChannelOf[i];
        }
        if (plan < 0)
            plan = system.Channels.FindIndex(c => c.WorldRiver);
        int count = 0;
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            system.WaterLevel[c] = filled[c];
            system.Still[c] = true;
            system.Flow[c] = Vector2.zero;
            system.ChannelOf[c] = plan;
            count++;
            GridMath.ForEachNeighbour(c, sizeX, sizeZ, nb =>
            {
                if (!seen[nb] && InHollow(nb))
                {
                    seen[nb] = true;
                    queue.Enqueue(nb);
                }
            });
        }
        return count;
    }

    /// <summary>
    /// Lays the water around each channel's centre line: a disc of the channel's width, its edge roughened by bank
    /// noise, each cell taking the channel cell that gives it the deepest water. Depth falls from the centre to the
    /// bank; the bed is lowered to the water surface less that depth (ground is only ever lowered). Where the filled
    /// surface lies deeper above the ground than the channel's own depth the channel crosses a hollow: the lake model
    /// fills it.
    /// </summary>
    private static int Paint(float[] height, float[] filled, List<int> cells, float[] width, float[] depth, bool[] moving, float[] bankAmplitude, Vector2[] direction,
        int[] planOf, int sizeX, int sizeZ, TerrainChannelSettings p, RiverSystem system)
    {
        int n = height.Length;
        var best = new float[n];
        var owner = new int[n];
        var ownerT = new float[n];
        for (int i = 0; i < n; i++)
        {
            best[i] = 0f;
            owner[i] = -1;
            ownerT[i] = float.MaxValue;
        }
        int inHollows = 0;
        var bank = new Perlin(1.0 / p.bankNoiseWavelength, 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);
        var shoalNoise = new Perlin(1.0 / p.shoalWavelength, 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);
        foreach (int c in cells)
        {
            if (filled[c] - height[c] > depth[c])
            {
                inHollows++;
                continue;
            }
            if (!moving[c])
            {
                system.Seep[c] = true;
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
                    // Water spreads over ground near its surface, not through high ground: banks, knobs and valley
                    // sides higher than this stay dry, so the channel narrows there and splits around islands.
                    if (r > 0f && height[z * sizeX + x] > filled[c] + p.maxBankCut)
                        continue;
                    float t = r / edge;
                    float d = depth[c] * (1f - t * t);
                    // Shoals are sampled per cell, so their edges follow the noise, not the channel's cells.
                    if (width[c] >= p.minShoalWidth)
                        d *= Mathf.Lerp(1f, p.shoalShare, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(p.shoalThreshold, p.shoalThreshold + 0.25f, (float)shoalNoise.GetValue(x, 0.0, z))));
                    d = Mathf.Max(p.minDepth, d);
                    int i = z * sizeX + x;
                    // A cell takes the water of the channel cell it lies nearest to across the channel (distance over
                    // the channel's own edge): a centre-line cell always keeps its own, never-rising level, and a
                    // river keeps the cells within its width against a creek joining it.
                    if (t < ownerT[i] || (t == ownerT[i] && filled[c] < filled[owner[i]]))
                    {
                        best[i] = d;
                        owner[i] = c;
                        ownerT[i] = t;
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
