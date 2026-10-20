using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>
/// Stage 2, routing: how much water each cell carries. Runoff and inflow gather downstream, highest cells first; water divides
/// where the ground falls away two ways (or where a delta split is drawn), and channels lose water into their bed
/// (transmission loss) while the drainage line itself stays.
/// </summary>
public static partial class TerrainChannels
{
    /// <summary>
    /// Discharge: each cell's own runoff plus inflow, gathered downstream over the filled landform, highest cells
    /// first (both receivers of a cell lie lower, so its donors are done). Water follows steepest descent, but where
    /// the ground also falls away steeply a second way (at least 90 degrees apart) it divides as multiple-flow routing
    /// does: the second way takes slope2^e / (slope1^e + slope2^e), with e the spreading exponent divided by the
    /// cell's spread nudge. A division is kept only where the second share is itself a channel; otherwise all water
    /// stays on the main way, so splits form only where a lot of water meets ground falling away two ways.
    /// </summary>
    private static float[] Route(float[] filled, int[] receiver, float[] inflow, bool[] worldSource, float runoff, float threshold,
        float[] spread, float mapSpread, int sizeX, int sizeZ, TerrainChannelSettings p, Dictionary<int, (int second, float share)> forced,
        out bool[] world, out int[] secondary, out int[] branchFrom, out int splits, out float[] water, float loss = 0f, float scale = 1f)
    {
        int n = filled.Length;
        var order = new int[n];
        for (int i = 0; i < n; i++)
            order[i] = i;
        System.Array.Sort(order, (a, b) => filled[b].CompareTo(filled[a]));
        var discharge = new float[n];
        water = new float[n];
        world = new bool[n];
        secondary = new int[n];
        branchFrom = new int[n];
        for (int i = 0; i < n; i++)
        {
            discharge[i] = water[i] = runoff + inflow[i];
            world[i] = worldSource[i];
            secondary[i] = branchFrom[i] = -1;
        }
        splits = 0;
        foreach (int c in order)
        {
            int r = receiver[c];
            if (r < 0)
                continue;
            if (loss > 0f && !world[c] && discharge[c] >= threshold)
            {
                // Transmission loss into the bed, by the channel's width (at map scale). The drainage line stays (the
                // discharge it gathers decides where channels are); only the water it carries shrinks.
                float lost = loss * p.widthPerSqrtDischarge * Mathf.Sqrt(water[c] * scale) / scale;
                water[c] = Mathf.Max(0f, water[c] - lost);
            }
            float share = 0f;
            int second = -1;
            if (forced != null && forced.TryGetValue(c, out (int second, float share) split))
            {
                // A delta split drawn by the distributaries: the water divides between the branches by their widths.
                second = split.second;
                share = split.share;
            }
            else if (discharge[c] * 0.5f >= threshold)
            {
                float factor = spread != null && spread[c] > 0f ? spread[c] : mapSpread;
                second = SecondWay(filled, c, r, sizeX, sizeZ, out float main, out float other);
                if (second >= 0)
                {
                    float e = p.spreadExponent / Mathf.Max(factor, 0.1f);
                    float a = Mathf.Pow(main, e), b = Mathf.Pow(other, e);
                    share = b / (a + b);
                    if (discharge[c] * share < threshold)
                        second = -1;
                }
            }
            if (second >= 0)
            {
                discharge[second] += discharge[c] * share;
                water[second] += water[c] * share;
                world[second] |= world[c];
                secondary[c] = second;
                if (branchFrom[second] < 0 || discharge[c] > discharge[branchFrom[second]])
                    branchFrom[second] = c;
                splits++;
            }
            discharge[r] += discharge[c] * (1f - share);
            water[r] += water[c] * (1f - share);
            world[r] |= world[c];
        }
        return discharge;
    }

    /// <summary>The steepest downhill neighbour at least 90 degrees from the main way, with both slopes; -1 if none.</summary>
    private static int SecondWay(float[] h, int c, int primary, int sizeX, int sizeZ, out float mainSlope, out float slope)
    {
        int cx = c % sizeX, cz = c / sizeX;
        var toPrimary = new Vector2(primary % sizeX - cx, primary / sizeX - cz);
        mainSlope = (h[c] - h[primary]) / toPrimary.magnitude;
        slope = 0f;
        int best = -1;
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if ((dx == 0 && dz == 0) || x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                    continue;
                if (Vector2.Dot(new Vector2(dx, dz), toPrimary) > 0f)
                    continue;
                int nb = z * sizeX + x;
                float s = (h[c] - h[nb]) / Mathf.Sqrt(dx * dx + dz * dz);
                if (s > slope)
                {
                    slope = s;
                    best = nb;
                }
            }
        }
        return best;
    }
}
