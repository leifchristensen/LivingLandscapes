using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>
/// The sediment of a network carried as separate loads per grain class (HYDRO-UNIFICATION S3a-S3c, #186): gravel, sand and fines,
/// each with its own fall speed and its own speed of first movement (<see cref="SedimentSettings.grains"/>). The loads are extensive
/// (they add at a join and divide with the water at a split), the capacity is shared among the classes the water is fast enough to
/// carry, and each class settles over its own length (P27). Game-free, so unit tests can run it. Made by the caller when
/// <see cref="SedimentSettings.grainClasses"/> is on, filled by <see cref="Sediment.Deposits"/> and read by <see cref="Sediment.Spread"/>;
/// null means the old lumped load.
/// </summary>
public sealed class GrainLoads
{
    public readonly SedimentSettings P;

    /// <summary>The speed (m/s) the water runs at in each cell, from <see cref="Hydraulics.Speeds"/> (the speed Impact uses), and the depth before scour (m).</summary>
    public readonly float[] Speed, Depth;

    /// <summary>What each class deposited per cell, [class][cell], in the budget's units; filled by <see cref="Sediment.Deposits"/>.</summary>
    public float[][] Deposit;

    /// <summary>Per class, the budget's units supplied (eroded and entering), deposited, and left the network at its end.</summary>
    public float[] Supplied, Deposited, Exits;

    /// <summary>Budget units that left the map as washload (set by <see cref="Sediment.Spread"/>: the part of a class that would settle beyond the jet's reach).</summary>
    public float Washload;

    public GrainLoads(SedimentSettings p, float[] speed, float[] depth)
    {
        P = p;
        Speed = speed;
        Depth = depth;
    }

    public int Count => P.grains.Count;

    /// <summary>Reference fall speed: the middle row of the table (sand), the grain the settling constants are calibrated on.</summary>
    public float ReferenceFall => P.grains[P.grains.Count / 2].fallSpeed;

    /// <summary>Null when the grain loads are off.</summary>
    public static GrainLoads Create(SedimentSettings p, float manningN, float jetWidths, List<int> cells, int[] receiver, int[] secondary, float[] filled,
        float[] depth, float[] width, int sizeX)
    {
        if (!p.grainClasses || p.grains == null || p.grains.Count == 0)
            return null;
        float[] speed = Hydraulics.Speeds(cells, receiver, secondary, filled, depth, width, sizeX, manningN, jetWidths);
        return new GrainLoads(p, speed, depth);
    }

    /// <summary>
    /// Splits a supply of mean grain <paramref name="index"/> over the classes: the two classes either side share it so that the
    /// mean index is kept (a hat function). The shares add to 1.
    /// </summary>
    public void Mix(float index, float[] shares)
    {
        var g = P.grains;
        int k = g.Count;
        for (int i = 0; i < k; i++)
            shares[i] = 0f;
        if (k == 1)
        {
            shares[0] = 1f;
            return;
        }
        index = Mathf.Clamp(index, g[k - 1].index, g[0].index);
        for (int i = 0; i < k - 1; i++)
        {
            if (index <= g[i].index && index >= g[i + 1].index)
            {
                float t = (g[i].index - index) / Mathf.Max(g[i].index - g[i + 1].index, 1e-6f);
                shares[i] = 1f - t;
                shares[i + 1] = t;
                return;
            }
        }
        shares[k - 1] = 1f;
    }

    /// <summary>
    /// How far the water at speed <paramref name="u"/> can move class <paramref name="k"/>, 0..1: a logistic of the speed against the
    /// class's speed of first movement, so a class switches on smoothly (a class with no load can still be picked up). A single class is
    /// the old lumped load and is always competent. The width of the logistic is floored at <see cref="SedimentSettings.uMin"/> so a
    /// class that starts moving at no speed at all cannot divide by zero.
    /// </summary>
    public float Competence(int k, float u)
    {
        if (Count == 1)
            return 1f;
        float uc = P.grains[k].criticalSpeed;
        return 1f / (1f + Mathf.Exp(-P.competenceSharpness * (u - uc) / Mathf.Max(uc, P.uMin)));
    }

    /// <summary>
    /// What each class cannot carry on (the excess over its share of the capacity). The capacity <paramref name="capacity"/> is
    /// shared among the classes with load by competence times load; the water can use no more of it than its most competent class
    /// present can move, so a load of gravel alone in slow water is carried by none of it. The shares add to at most the capacity.
    /// </summary>
    public void Excess(float[] load, float capacity, float u, float[] excess)
    {
        int k = Count;
        float sum = 0f, best = 0f;
        for (int i = 0; i < k; i++)
        {
            if (load[i] <= 0f)
                continue;
            float c = Competence(i, u);
            sum += c * load[i];
            best = Mathf.Max(best, c);
        }
        for (int i = 0; i < k; i++)
        {
            float share = sum > 0f && load[i] > 0f ? capacity * best * (Competence(i, u) * load[i] / sum) : 0f;
            excess[i] = Mathf.Max(0f, load[i] - share);
        }
    }

    /// <summary>
    /// Settling length (cells) of class <paramref name="k"/> in water of speed <paramref name="u"/> and depth <paramref name="d"/>: the
    /// distance the water travels while the grain falls the depth, in map cells (P27). Both speed and depth are floored
    /// (<see cref="SedimentSettings.uMin"/>, <see cref="SedimentSettings.dMin"/>) so still or film-thin water cannot give a length of
    /// 0, which <see cref="Settled"/> divides by.
    /// </summary>
    public float Length(int k, float u, float d) =>
        Mathf.Clamp(P.settlingCompression * Mathf.Max(u, P.uMin) * Mathf.Max(d, P.dMin) / P.grains[k].fallSpeed, P.ellMin, P.ellMax);

    /// <summary>The share of a class's excess that settles over a step of <paramref name="dx"/> cells: the common <c>settleShare</c> (S3a) or 1 - exp(-dx / ell) (S3b).</summary>
    public float Settled(int k, float u, float d, float dx) =>
        P.settlingLength ? 1f - Mathf.Exp(-dx / Length(k, u, d)) : P.settleShare;

    /// <summary>
    /// The mix of a river entering the map: the supply's mix weighted by what the entry's speed can carry (a graded river carries the
    /// classes it is competent to move). Falls back to the finest class if none can be moved.
    /// </summary>
    public void EntryMix(float index, float u, float[] shares)
    {
        Mix(index, shares);
        float sum = 0f;
        for (int i = 0; i < Count; i++)
        {
            shares[i] *= Competence(i, u);
            sum += shares[i];
        }
        if (sum <= 1e-6f)
        {
            for (int i = 0; i < Count; i++)
                shares[i] = i == Count - 1 ? 1f : 0f;
            return;
        }
        for (int i = 0; i < Count; i++)
            shares[i] /= sum;
    }

    /// <summary>
    /// Invariant 9 (HYDRO-UNIFICATION S3, #186): grain along a river's deposits fines downstream. Walking the channel's cells
    /// from source to mouth, the running mean of the deposit grain index (<paramref name="grain"/>, weighted by
    /// <paramref name="deposit"/>, both per cell) must not climb above the lowest running mean already passed: a river drops its
    /// gravel first and its fines last. Cells without deposit are skipped. Returns the worst climb of the running mean
    /// (0 for a river that only fines), or NaN when fewer than two cells deposited anything and there is nothing to measure.
    /// Compared against <see cref="SedimentSettings.coarsenTolerance"/> by the DevTools invariant 9.
    /// </summary>
    public static float WorstDownstreamCoarsening(List<int> cellsDownstream, float[] grain, float[] deposit)
    {
        float sum = 0f, lowest = float.MaxValue, worst = 0f;
        int seen = 0;
        foreach (int c in cellsDownstream)
        {
            if (deposit[c] <= 0f)
                continue;
            sum += grain[c];
            seen++;
            float mean = sum / seen;
            lowest = Mathf.Min(lowest, mean);
            worst = Mathf.Max(worst, mean - lowest);
        }
        return seen < 2 ? float.NaN : worst;
    }
}

public static partial class Sediment
{
    /// <summary>
    /// <see cref="Deposits"/> with the load carried per grain class (S3a-S3c). Same walk, same capacity, same junction zones and
    /// splits; the load is a vector, the capacity is shared among the classes the water can move, and each class settles at its own
    /// rate. <paramref name="deposit"/> is the sum over the classes and <paramref name="depositGrain"/> the mean grain index of a
    /// cell's deposit, so every consumer of the lumped result still works; <paramref name="grains"/> keeps the per-class deposits.
    /// </summary>
    private static void DepositsByClass(List<int> cells, int[] receiver, float[] discharge, float dischargeScale, float[] filled,
        float[] inflow, float[] rockStrength, bool[] baseLevel, int sizeX, SedimentSettings p,
        out float[] deposit, out float[] depositGrain, out float total, out float atBase, out float eroded, out float exits, float[] gouged,
        bool[] sand, ConfluenceZones zones, int[] secondary, float[] secondaryShare, GrainLoads grains)
    {
        int n = filled.Length, k = grains.Count;
        var load = new float[k][];
        grains.Deposit = new float[k][];
        for (int i = 0; i < k; i++)
        {
            load[i] = new float[n];
            grains.Deposit[i] = new float[n];
        }
        grains.Supplied = new float[k];
        grains.Deposited = new float[k];
        grains.Exits = new float[k];
        var walked = new bool[n];
        deposit = new float[n];
        depositGrain = new float[n];
        total = 0f;
        atBase = 0f;
        eroded = 0f;
        exits = 0f;
        var avail = new float[k];
        var drop = new float[k];
        var excess = new float[k];
        var slowLoad = new float[k];
        var fastLoad = new float[k];
        var excessSlow = new float[k];
        var mix = new float[k];
        var entryMix = new float[k];
        foreach (int c in cells)
        {
            walked[c] = true;
            float q = discharge[c] * dischargeScale;
            float slope = Hydraulics.ReachSlope(c, receiver, filled, sizeX);
            bool still = baseLevel != null && baseLevel[c];
            float capacity = still ? 0f : p.capacityPerPower * q * slope;
            float rock = rockStrength != null ? rockStrength[c] : 0.5f;
            float supply = p.erosionPerPower * (1f - rock) * q * slope;
            float entry = 0f;
            if (inflow[c] > 0f && !still)
            {
                entry = p.entryGrade * p.capacityPerPower * inflow[c] * dischargeScale * Hydraulics.ReachSlope(c, receiver, filled, sizeX, filled.Length, baseLevel);
                supply += entry;
            }
            if (gouged != null)
                supply += gouged[c];
            float loadHere = 0f;
            for (int i = 0; i < k; i++)
                loadHere += load[i][c];
            // Loose sand is limited only by transport: the flow takes up as much as it can carry (the same rule, on the total).
            if (sand != null && sand[c] && !still)
                supply = Mathf.Max(supply, capacity - loadHere);
            float grainSupply = Mathf.Clamp01(Mathf.Lerp(0.2f, 1f, slope / Mathf.Max(p.coarseSlope, 1e-4f)) * Mathf.Lerp(0.7f, 1f, rock));
            grains.Mix(grainSupply, mix);
            // S3c: a river entering the map brings the classes its speed can carry; everything else arrives with the supply's mix.
            bool entryByCompetence = entry > 0f && p.jetClasses;
            if (entryByCompetence)
                grains.EntryMix(grainSupply, grains.Speed[c], entryMix);
            eroded += supply;
            for (int i = 0; i < k; i++)
            {
                float added = entryByCompetence ? (supply - entry) * mix[i] + entry * entryMix[i] : supply * mix[i];
                avail[i] = load[i][c] + added;
                grains.Supplied[i] += added;
            }
            float u = grains.Speed[c], d = grains.Depth[c];
            int r = receiver[c];
            float dx = r >= 0 ? Hydraulics.StepLength(c, r, sizeX) : 1f;
            float a = zones != null && !still ? zones.SlowShare[c] : 0f;
            for (int i = 0; i < k; i++)
                drop[i] = 0f;
            if (still)
            {
                // Base level: all of it settles (the jet lays it out in Spread).
                for (int i = 0; i < k; i++)
                    drop[i] = avail[i];
            }
            else if (a > 0f)
            {
                // Below a junction the flow splits across the channel: the slow side (share a) at (u_slow / u)^2 of the capacity
                // and the fast side at 1 / (1 - a); each side is shared among the classes it can move, at its own speed.
                float slowCap = zones.SlowCapacityAt != null ? zones.SlowCapacityAt[c] : zones.SlowCapacity;
                float uSlow = u * Mathf.Sqrt(Mathf.Max(slowCap, 0f)), uFast = u * Mathf.Sqrt(1f / (1f - a));
                for (int i = 0; i < k; i++)
                {
                    slowLoad[i] = a * avail[i];
                    fastLoad[i] = (1f - a) * avail[i];
                }
                grains.Excess(slowLoad, a * capacity * slowCap, uSlow, excessSlow);
                grains.Excess(fastLoad, capacity / (1f - a), uFast, excess);
                for (int i = 0; i < k; i++)
                    drop[i] = excessSlow[i] * grains.Settled(i, uSlow, d, dx) + excess[i] * grains.Settled(i, uFast, d, dx);
            }
            else
            {
                grains.Excess(avail, capacity, u, excess);
                for (int i = 0; i < k; i++)
                    drop[i] = excess[i] * grains.Settled(i, u, d, dx);
            }
            float dropped = 0f, indexSum = 0f;
            for (int i = 0; i < k; i++)
            {
                if (drop[i] <= 0f)
                    continue;
                grains.Deposit[i][c] = drop[i];
                grains.Deposited[i] += drop[i];
                dropped += drop[i];
                indexSum += drop[i] * p.grains[i].index;
            }
            if (dropped > 0f)
            {
                deposit[c] = dropped;
                depositGrain[c] = Mathf.Clamp01(indexSum / dropped);
                total += dropped;
                if (still)
                    atBase += dropped;
            }
            // The second way takes its share of every class, unless routing changed after the split and it was already walked
            // (then the load stays on the main way, so none is lost).
            int second = secondary != null ? secondary[c] : -1;
            bool toSecond = second >= 0 && !walked[second];
            for (int i = 0; i < k; i++)
            {
                float carried = avail[i] - drop[i];
                if (toSecond)
                {
                    float split = carried * secondaryShare[c];
                    load[i][second] += split;
                    carried -= split;
                }
                if (r >= 0)
                    load[i][r] += carried;
                else
                {
                    exits += carried;
                    grains.Exits[i] += carried;
                }
            }
        }
    }
}
