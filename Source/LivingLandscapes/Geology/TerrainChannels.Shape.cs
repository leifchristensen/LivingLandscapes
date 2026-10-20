using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>
/// Stage 3, channel shape: what the water looks like where it runs. Width and depth from discharge (hydraulic geometry) and
/// stream power, nudged by the channel regimes the place matches, roughened by noise; flow directions; the valley floor
/// around each channel; basins a world river runs into are silted up so it crosses them as a channel.
/// </summary>
public static partial class TerrainChannels
{
    /// <summary>
    /// Closed hollows the world river runs into are filled with its load up to a few channel depths below their spill level
    /// (<see cref="TerrainChannelSettings.riverInfillDepths"/>). Returns the number of cells raised.
    /// </summary>
    private static int InfillRiverHollows(float[] height, float[] filled, List<int> cells, bool[] world, float[] depth, int sizeX, int sizeZ,
        TerrainChannelSettings p)
    {
        int n = height.Length;
        GridMath.LabelRegions(i => filled[i] - height[i] > 1e-4f, sizeX, sizeZ, out int[] labels);
        var riverDepth = new Dictionary<int, float>();
        foreach (int c in cells)
        {
            if (labels[c] >= 0 && world[c])
                riverDepth[labels[c]] = Mathf.Max(riverDepth.TryGetValue(labels[c], out float d) ? d : 0f, depth[c]);
        }
        int raised = 0;
        for (int i = 0; i < n; i++)
        {
            if (labels[i] < 0 || !riverDepth.TryGetValue(labels[i], out float d))
                continue;
            float floor = filled[i] - Mathf.Max(d, p.minDepth) * p.riverInfillDepths;
            if (height[i] < floor)
            {
                height[i] = floor;
                raised++;
            }
        }
        return raised;
    }

    /// <summary>
    /// Width and depth of each channel cell from its discharge, stream power and noise, nudged by the channel regimes
    /// its data matches (Docs/WATER-AUDIT.md, step 4). Fields: Discharge (log10), Slope (water drop per cell over a few
    /// cells), BankStrength (rock strength 0..1), Confinement (valley floor width over channel width). Returns the bank
    /// noise amplitude per cell and counts the dominant regime per cell for the log.
    /// </summary>
    private static float[] Shape(List<int> cells, float[] natural, float[] filled, float[] rockStrength, int[] receiver, float[] discharge,
        float dischargeScale,
        Vector2[] direction, int sizeX, int sizeZ, WaterRegimeDef water, TerrainChannelSettings p, float[] width, float[] depth,
        float[] spread, bool[] moving, float[] power, float[] deposition, Dictionary<string, int> regimes, float[] sinuosity, float[] valley)
    {
        // Slope over a few cells downstream (single D8 steps across filled flats are nearly zero).
        var slope = new float[cells.Count];
        for (int k = 0; k < cells.Count; k++)
            slope[k] = Sediment.Slope(cells[k], receiver, filled, sizeX);
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
            float q = discharge[c] * dischargeScale;
            float baseWidth = Mathf.Max(p.minWidth, p.widthPerSqrtDischarge * Mathf.Sqrt(q));
            fields["Discharge"] = Mathf.Log10(q);
            fields["Slope"] = slope[k];
            fields["BankStrength"] = rockStrength != null ? rockStrength[c] : 0.5f;
            float floor = ValleyFloor(natural, filled[c], c, direction[c], baseWidth, sizeX, sizeZ, p);
            fields["Confinement"] = floor / baseWidth;
            valley[c] = floor;
            ChannelNudges nudge = WaterRegimes.Nudges(fields, water, out ChannelRegimeDef dominant);
            string key = dominant?.defName ?? "none";
            regimes[key] = regimes.TryGetValue(key, out int count) ? count + 1 : 1;

            // Moving water by the width without noise, so a channel near the limit does not flicker between the two.
            moving[c] = baseWidth * nudge.width >= p.minMovingWidth;
            width[c] = Mathf.Max(p.minWidth, baseWidth * nudge.width
                                             * (1f + p.widthNoise * nudge.widthNoise * (float)widthNoise.GetValue(x, 0.0, z)));
            float scour = p.scourRange.ClampToRange(Mathf.Pow(Mathf.Max(slope[k], 1e-6f) / reference, p.scourExponent));
            depth[c] = Mathf.Max(p.minDepth, p.depthPerDischarge * Mathf.Pow(q, p.depthExponent) * scour * nudge.depth
                                             * (1f + Mathf.Clamp(p.depthNoise * nudge.depthNoise, 0f, 0.9f) * (float)depthNoise.GetValue(x, 0.0, z)));
            bankAmplitude[c] = Mathf.Clamp(p.bankNoise * nudge.bankNoise, 0f, 0.8f);
            spread[c] = nudge.spread;
            sinuosity[c] = nudge.sinuosity;
            deposition[c] = nudge.deposition;
            // Stream power per unit width: the flow's capacity to carry sediment.
            power[c] = q * Mathf.Max(slope[k], 0f) / Mathf.Max(width[c], 1f);
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
}
