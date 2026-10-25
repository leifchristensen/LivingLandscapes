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

    /// <summary>
    /// Point bars: in a bend the flow spirals and sweeps sediment to the inner bank. A deposit's spread leans toward the inside of
    /// the bend by this much at full curvature, and stays coarse there, while the outer (cut) bank gets little and fine.
    /// </summary>
    public float bendBias = 2.5f;

    /// <summary>
    /// Where a river meets still water (base level) its load leaves the mouth as a plane turbulent jet: the jet's half-width
    /// grows as b(x) = w / 2 + jetSpreadRate x along its axis, and the load settles over jetSettleWidths channel widths, so the
    /// deposit falls as exp(-x / L) along the axis and as a Gaussian of half-width b across it (a mouth bar and the lobe beyond),
    /// out to jetReachLengths settling lengths.
    /// </summary>
    public float jetSpreadRate = 0.12f;
    public float jetSettleWidths = 5f;
    public float jetReachLengths = 3f;

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

    /// <summary>Main river network for the log's branch count: channels carrying at least this share of the world river's water.</summary>
    public float mainRiverShare = 0.12f;
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
    /// <summary>
    /// Spreads the deposits over the floodplain into the alluvium and grain size fields of <paramref name="system"/>; returns a
    /// summary for the log.
    /// </summary>
    public static string Layers(List<int> cells, int[] receiver, float[] deposit, float[] depositGrain, float[] width, float[] filled,
        float[] natural, int sizeX, int sizeZ, SedimentSettings p, RiverSystem system, float total, float atBase, float eroded,
        float[] discharge = null, bool[] still = null)
    {
        Spread(cells, receiver, system.Flow, deposit, depositGrain, width, filled, natural, sizeX, sizeZ, p, out float[] alluvium, out float[] grainSize, out _,
            discharge, still);
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
        return $"sediment: {eroded:0} supplied, {total:0} deposited ({(total > 0f ? 100f * atBase / total : 0f):0}% at base level), " +
               $"alluvium over {100f * covered / filled.Length:0.0}% of the map, thickest {thickest:0.00} m";
    }

    /// <summary>
    /// The load and capacity walk down the network: what each channel cell deposits, and its grain size. The load is conserved:
    /// what the water erodes (its bed and banks, <paramref name="gouged"/> ground its impact cut away, in the budget's units)
    /// joins it, and what settles leaves it. On loose sand (<paramref name="sand"/>) erosion is limited only by transport: the
    /// flow takes up as much as it can carry.
    /// </summary>
    public static void Deposits(List<int> cells, int[] receiver, float[] discharge, float dischargeScale, float[] filled,
        float[] inflow, float[] rockStrength, bool[] baseLevel, int sizeX, SedimentSettings p,
        out float[] deposit, out float[] depositGrain, out float total, out float atBase, out float eroded, float[] gouged = null, bool[] sand = null)
    {
        int n = filled.Length;
        var load = new float[n];
        var grainLoad = new float[n];
        deposit = new float[n];
        depositGrain = new float[n];
        total = 0f;
        atBase = 0f;
        eroded = 0f;
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
            if (gouged != null)
                supply += gouged[c];
            if (sand != null && sand[c] && !still)
                supply = Mathf.Max(supply, capacity - load[c]);
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
    }

    /// <summary>
    /// Lays a deposit where a river enters still water as its jet carries it: exp(-x / L) along the axis, a Gaussian of half-width
    /// w / 2 + jetSpreadRate x across it, on ground no higher than <paramref name="level"/>; coarse near the mouth, finer out.
    /// </summary>
    private static void Jet(int c, Vector2 axis, float amount, float grain, float w, float level, float[] natural, int sizeX, int sizeZ, SedimentSettings p,
        float[] alluvium, float[] grainSum, List<(int cell, float weight, float d)> weights)
    {
        float settle = p.jetSettleWidths * w, reach = settle * p.jetReachLengths;
        var across = new Vector2(-axis.y, axis.x);
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
                var offset = new Vector2(dx, dz);
                float along = Vector2.Dot(offset, axis), side = Vector2.Dot(offset, across);
                if (along < -0.5f || along > reach)
                    continue;
                int i = z * sizeX + x;
                if (natural[i] > level)
                    continue;
                float half = 0.5f * w + p.jetSpreadRate * Mathf.Max(along, 0f);
                float weight = Mathf.Exp(-Mathf.Max(along, 0f) / settle) * Mathf.Exp(-side * side / (2f * half * half));
                if (weight < 1e-4f)
                    continue;
                weights.Add((i, weight, Mathf.Max(along, 0f)));
                sum += weight;
            }
        }
        if (sum <= 0f)
            return;
        foreach ((int i, float weight, float along) in weights)
        {
            float thickness = amount * weight / sum;
            alluvium[i] += thickness;
            grainSum[i] += thickness * grain * Mathf.Exp(-along / settle);
        }
    }

    /// <summary>Water drop per cell over the next few cells downstream (single steps across filled flats are nearly zero).</summary>
    public static float Slope(int c, int[] receiver, float[] filled, int sizeX)
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

    /// <summary>
    /// Spreads each channel cell's deposit over the floodplain: ground near its water, within reach, Gaussian; grain fines with
    /// distance. Where the river meets still water (<paramref name="still"/>) its deposit spreads with its jet instead, along the
    /// flow's direction (the discharge-weighted momentum carried down the network) and widening downstream.
    /// </summary>
    public static void Spread(List<int> cells, int[] receiver, Vector2[] flow, float[] deposit, float[] depositGrain, float[] width, float[] filled, float[] natural, int sizeX, int sizeZ,
        SedimentSettings p, out float[] alluvium, out float[] grainSize, out float scale, float[] discharge = null, bool[] still = null)
    {
        int n = filled.Length;
        alluvium = new float[n];
        var grainSum = new float[n];
        var weights = new List<(int cell, float weight, float d)>();
        // The momentum each cell's water carries (cells are ordered highest first, so donors come before receivers).
        Vector2[] momentum = null;
        if (discharge != null && still != null)
        {
            momentum = new Vector2[n];
            foreach (int c in cells)
            {
                int r = receiver[c];
                if (r < 0)
                    continue;
                var step = new Vector2(r % sizeX - c % sizeX, r / sizeX - c / sizeX).normalized;
                Vector2 carried = momentum[c] == Vector2.zero ? step : momentum[c].normalized;
                momentum[r] += (carried + step).normalized * discharge[c];
            }
        }
        foreach (int c in cells)
        {
            if (deposit[c] <= 0f)
                continue;
            if (momentum != null && still[c] && momentum[c] != Vector2.zero)
            {
                Jet(c, momentum[c].normalized, deposit[c], depositGrain[c], Mathf.Max(width[c], 1f), filled[c] + p.floodHeight, natural, sizeX, sizeZ, p,
                    alluvium, grainSum, weights);
                continue;
            }
            float w = Mathf.Max(width[c], 1f);
            float reach = Mathf.Min(p.floodplainWidths * w, p.maxReach);
            float sigma = Mathf.Max(reach * 0.5f, 1f);
            float level = filled[c] + p.floodHeight;
            // The inside of the bend: the side the flow turns toward over the next few cells, and how sharply.
            Vector2 inner = Vector2.zero;
            if (flow != null && flow[c] != Vector2.zero)
            {
                int ahead = c;
                for (int s = 0; s < 3 && receiver[ahead] >= 0; s++)
                    ahead = receiver[ahead];
                Vector2 f1 = flow[c], f2 = flow[ahead] != Vector2.zero ? flow[ahead] : f1;
                float turn = f1.x * f2.y - f1.y * f2.x;
                inner = new Vector2(-f1.y, f1.x) * turn;
            }
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
                    float lean = d > 0f ? Vector2.Dot(inner, new Vector2(dx, dz)) / d : 0f;
                    float weight = Mathf.Exp(-d * d / (2f * sigma * sigma)) * Mathf.Max(0.1f, 1f + p.bendBias * lean);
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
                int x = i % sizeX - cx, z = i / sizeX - cz;
                float lean = d > 0f ? Mathf.Max(0f, Vector2.Dot(inner, new Vector2(x, z)) / d) : 0f;
                float fining = Mathf.Max(p.finingWidths * w * (1f + p.bendBias * lean), 0.5f);
                grainSum[i] += thickness * depositGrain[c] * Mathf.Exp(-d / fining);
            }
        }
        var covered = new List<float>();
        foreach (float a in alluvium)
        {
            if (a > 0f)
                covered.Add(a);
        }
        scale = 0f;
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
