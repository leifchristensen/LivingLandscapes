using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>Tuning of <see cref="Sediment"/>, set with the channel settings in XML. Units are relative; thickness comes out in metres.</summary>
public class SedimentSettings
{
    /// <summary>A channel can carry this much sediment per unit of stream power (discharge times slope).</summary>
    public float capacityPerPower = 1f;

    /// <summary>Sediment a channel cell erodes from its bed and banks per unit of stream power, on the weakest rock (none on the strongest).</summary>
    public float erosionPerPower = 0.08f;

    /// <summary>
    /// Water entering the map arrives loaded from the land upstream: this much sediment per unit of discharge (the load a river
    /// would carry on this slope, metres per cell). A lowland river is loaded from far away, whatever the slope on the map.
    /// </summary>
    public float entryLoadPerDischarge = 0.01f;

    /// <summary>Share of the excess load (load over capacity) that settles in one cell; the rest goes on.</summary>
    public float settleShare = 0.04f;

    /// <summary>Slope (metres per cell) at which eroded sediment is coarsest; gentler ground supplies finer.</summary>
    public float coarseSlope = 0.15f;

    /// <summary>
    /// Floodplain: a channel cell's deposit spreads over ground no higher than this (metres) above its water, out to
    /// <see cref="floodplainWidths"/> channel widths (Gaussian, most near the channel), at most <see cref="maxReach"/> cells.
    /// </summary>
    public float floodHeight = 1.5f;
    public float floodplainWidths = 4f;
    public float maxReach = 30f;

    /// <summary>Grain size falls off with distance from the channel over this many channel widths (levees and bars coarse, back swamps fine).</summary>
    public float finingWidths = 1.5f;

    /// <summary>
    /// Map scale over physics (owner, 2026-10-20): deposits are scaled so the alluvium of the 90th percentile of covered ground is
    /// this many metres, so every map that has deposition shows it; the pattern within the map stays the budget's.
    /// </summary>
    public float typicalThickness = 1f;
    public float maxThickness = 3f;

    /// <summary>Thinner than this (metres, after scaling) counts as no alluvium.</summary>
    public float minThickness = 0.05f;
}

/// <summary>
/// The sediment budget of the channel network (Docs/SEDIMENT-PLAN.md, issues #16 and #36). Every channel cell carries a load
/// downstream and has a capacity, its stream power (discharge x slope); where the load exceeds the capacity the excess
/// settles, coarsest first. Capacity is zero at base level (sea, lakes), so a river drops everything there (a delta); it falls
/// where the slope eases (fans, bars) and rises on steep reaches (erosion, coarse supply). Each deposit spreads over the ground
/// near its water (floodplain), coarse by the channel, fine farther out. The result is two fields: alluvium thickness and its
/// grain size, from which ground, soil and plants can follow. Nothing here names a delta, a levee or a bank: they are where
/// the budget puts sediment.
/// </summary>
public static class Sediment
{
    /// <param name="cells">Channel cells, highest water first (donors before receivers).</param>
    /// <param name="natural">Ground before the channels were carved.</param>
    /// <param name="baseLevel">Cells where the water stands (sea, lakes): capacity zero.</param>
    public static void Budget(List<int> cells, int[] receiver, float[] discharge, float dischargeScale, float[] filled, float[] natural,
        float[] width, float[] inflow, float[] rockStrength, bool[] baseLevel, int sizeX, int sizeZ, SedimentSettings p, RiverSystem system,
        out string summary)
    {
        int n = filled.Length;
        var load = new float[n];
        var grainLoad = new float[n];
        var deposit = new float[n];
        var depositGrain = new float[n];
        float total = 0f, atBase = 0f, eroded = 0f;
        foreach (int c in cells)
        {
            float q = discharge[c] * dischargeScale;
            float slope = Slope(c, receiver, filled, sizeX);
            bool still = baseLevel != null && baseLevel[c];
            float capacity = still ? 0f : p.capacityPerPower * q * slope;
            float rock = rockStrength != null ? rockStrength[c] : 0.5f;
            float supply = p.erosionPerPower * (1f - rock) * q * slope;
            if (inflow[c] > 0f && !still)
                supply += p.entryLoadPerDischarge * inflow[c] * dischargeScale;
            float grainSupply = Mathf.Clamp01(Mathf.Lerp(0.2f, 1f, slope / Mathf.Max(p.coarseSlope, 1e-4f)) * Mathf.Lerp(0.7f, 1f, rock));
            float available = load[c] + supply;
            float grain = available > 0f ? (grainLoad[c] + supply * grainSupply) / available : grainSupply;
            eroded += supply;
            float dropped = 0f;
            if (available > capacity)
            {
                dropped = still ? available : (available - capacity) * p.settleShare;
                // The coarsest settles first: a slight excess drops gravel, losing all the power drops everything.
                float share = dropped / Mathf.Max(available, 1e-6f);
                float dropGrain = Mathf.Lerp(1f, grain, share);
                deposit[c] = dropped;
                depositGrain[c] = Mathf.Clamp01(dropGrain);
                grain = available - dropped > 1e-6f ? Mathf.Clamp01((grain * available - dropGrain * dropped) / (available - dropped)) : grain;
                total += dropped;
                if (still)
                    atBase += dropped;
            }
            float carried = available - dropped;
            int r = receiver[c];
            if (r >= 0)
            {
                load[r] += carried;
                grainLoad[r] += carried * grain;
            }
        }
        Spread(cells, deposit, depositGrain, width, filled, natural, sizeX, sizeZ, p, out float[] alluvium, out float[] grainSize);
        system.Alluvium = alluvium;
        system.GrainSize = grainSize;
        int covered = 0;
        float thickest = 0f;
        foreach (float a in alluvium)
        {
            if (a > 0.05f)
                covered++;
            thickest = Mathf.Max(thickest, a);
        }
        summary = $"sediment: {eroded:0} supplied, {total:0} deposited ({(total > 0f ? 100f * atBase / total : 0f):0}% at base level), " +
                  $"alluvium over {100f * covered / n:0.0}% of the map, thickest {thickest:0.00} m";
    }

    /// <summary>Water drop per cell over the next few cells downstream (single steps across filled flats are nearly zero).</summary>
    private static float Slope(int c, int[] receiver, float[] filled, int sizeX)
    {
        int d = c;
        float run = 0f;
        for (int s = 0; s < 5 && receiver[d] >= 0; s++)
        {
            int r = receiver[d];
            run += (r % sizeX != d % sizeX && r / sizeX != d / sizeX) ? 1.414f : 1f;
            d = r;
        }
        return run > 0f ? Mathf.Max(0f, filled[c] - filled[d]) / run : 0f;
    }

    /// <summary>Spreads each channel cell's deposit over the floodplain: ground near its water, within reach, Gaussian; grain fines with distance.</summary>
    private static void Spread(List<int> cells, float[] deposit, float[] depositGrain, float[] width, float[] filled, float[] natural, int sizeX, int sizeZ,
        SedimentSettings p, out float[] alluvium, out float[] grainSize)
    {
        int n = filled.Length;
        alluvium = new float[n];
        var grainSum = new float[n];
        var weights = new List<(int cell, float weight, float d)>();
        foreach (int c in cells)
        {
            if (deposit[c] <= 0f)
                continue;
            float w = Mathf.Max(width[c], 1f);
            float reach = Mathf.Min(p.floodplainWidths * w, p.maxReach);
            float sigma = Mathf.Max(reach * 0.5f, 1f);
            float level = filled[c] + p.floodHeight;
            int cx = c % sizeX, cz = c / sizeX, r = Mathf.CeilToInt(reach);
            weights.Clear();
            float sum = 0f;
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                        continue;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    int i = z * sizeX + x;
                    if (d > reach || natural[i] > level)
                        continue;
                    float weight = Mathf.Exp(-d * d / (2f * sigma * sigma));
                    weights.Add((i, weight, d));
                    sum += weight;
                }
            }
            if (sum <= 0f)
                continue;
            foreach ((int i, float weight, float d) in weights)
            {
                float thickness = deposit[c] * weight / sum;
                alluvium[i] += thickness;
                grainSum[i] += thickness * depositGrain[c] * Mathf.Exp(-d / Mathf.Max(p.finingWidths * w, 0.5f));
            }
        }
        var covered = new List<float>();
        foreach (float a in alluvium)
        {
            if (a > 0f)
                covered.Add(a);
        }
        float scale = 0f;
        if (covered.Count > 0)
        {
            covered.Sort();
            scale = p.typicalThickness / Mathf.Max(covered[(int)(covered.Count * 0.9f)], 1e-6f);
        }
        grainSize = new float[n];
        for (int i = 0; i < n; i++)
        {
            float thickness = Mathf.Min(alluvium[i] * scale, p.maxThickness);
            if (thickness >= p.minThickness)
                grainSize[i] = Mathf.Clamp01(grainSum[i] / alluvium[i]);
            alluvium[i] = thickness >= p.minThickness ? thickness : 0f;
        }
    }
}
