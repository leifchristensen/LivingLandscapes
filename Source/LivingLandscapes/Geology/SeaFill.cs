using System;
using System.Collections.Generic;

namespace LivingLandscapes.Geology;

/// <summary>What <see cref="SeaFill.Apply"/> did with a river's load. delivered = Retained + RemovedByWaves + NoRoom, always.</summary>
public sealed class SeaFillResult
{
    /// <summary>Load actually laid on the sea floor (the beds rose by exactly this much in total).</summary>
    public double Retained;
    /// <summary>Load the waves carry away (delivered minus the part the mouth keeps).</summary>
    public double RemovedByWaves;
    /// <summary>Kept load that no cell could take (no room, or the kernel was empty).</summary>
    public double NoRoom;
    /// <summary>Cells raised in this call whose bed is now at or above L - epsT, in kernel order: candidates to become land.</summary>
    public readonly List<int> Promoted = new List<int>();
    /// <summary>Largest distance along the jet axis of a cell that received load: how far the front reached.</summary>
    public float FrontDistance;
}

/// <summary>
/// The sea keeps part of what a river delivers and lays it on the sea floor with the jet kernel, never above the room each cell has
/// (rules E4 and E6 of DELTA-MODEL-PLAN). Game-free and not called by generation yet (step D2).
/// </summary>
public static class SeaFill
{
    /// <summary>Cap on the number of times load a full cell refused is moved on to cells that still have room.</summary>
    public const int MaxFrontPasses = 64;
    /// <summary>A cell with less room than this (metres) counts as full.</summary>
    public const double FullRoom = 1e-6;
    /// <summary>Load below this is not worth another pass; it is reported as noRoom.</summary>
    public const double NegligibleLoad = 1e-12;

    /// <summary>Room (metres) a cell has between its bed and sea level L: max(0, L - bed).</summary>
    public static double Room(double level, double bed) => Math.Max(0.0, level - bed);

    /// <summary>
    /// The share of the delivered load the mouth keeps (stand-in rule R27): Q_r = Q_s (1 - exp(-Q_s / Q_w)) with Q_w = waveCapacity x
    /// max(frontWidth, mouthWidth). No kink at Q_s = Q_w; Q_r is always below Q_s; with no wave capacity everything is kept.
    /// </summary>
    public static double RetainedLoad(double delivered, double waveCapacityPerWidth, double frontWidth, double mouthWidth)
    {
        if (delivered <= 0.0)
            return 0.0;
        double qw = waveCapacityPerWidth * Math.Max(frontWidth, mouthWidth);
        if (qw <= 0.0)
            return delivered;
        return delivered * (1.0 - Math.Exp(-delivered / qw));
    }

    /// <summary>
    /// Lays the kept part of <paramref name="delivered"/> on <paramref name="bed"/> (metres, below sea level; raised in place) along
    /// <paramref name="kernel"/> (cell, weight, distance along the axis; not normalised). Each pass spreads the remaining load over the
    /// cells that still have room in proportion to their weights; a cell takes min(share, room) and what it refuses is spread again, so
    /// the front advances. Deterministic: kernel order, no random.
    /// </summary>
    public static SeaFillResult Apply(float[] bed, float level, IReadOnlyList<(int cell, float weight, float d)> kernel, double delivered,
        double waveCapacityPerWidth, double frontWidth, double mouthWidth, float epsT = 0f)
    {
        var result = new SeaFillResult();
        if (delivered <= 0.0)
            return result;
        double kept = RetainedLoad(delivered, waveCapacityPerWidth, frontWidth, mouthWidth);
        result.RemovedByWaves = delivered - kept;
        double remaining = kept;
        var raised = new bool[kernel.Count];
        for (int pass = 0; pass < MaxFrontPasses && remaining > NegligibleLoad; pass++)
        {
            double sum = 0.0;
            for (int k = 0; k < kernel.Count; k++)
            {
                if (kernel[k].weight > 0f && Room(level, bed[kernel[k].cell]) > FullRoom)
                    sum += kernel[k].weight;
            }
            if (sum <= 0.0)
                break;
            double laidThisPass = 0.0;
            for (int k = 0; k < kernel.Count; k++)
            {
                int cell = kernel[k].cell;
                double room = Room(level, bed[cell]);
                if (kernel[k].weight <= 0f || room <= FullRoom)
                    continue;
                double dz = Math.Min(remaining * kernel[k].weight / sum, room);
                float before = bed[cell];
                float after = Math.Min((float)(before + dz), level);
                bed[cell] = after;
                double actual = (double)after - before;
                if (actual <= 0.0)
                    continue;
                laidThisPass += actual;
                raised[k] = true;
                if (kernel[k].d > result.FrontDistance)
                    result.FrontDistance = kernel[k].d;
            }
            result.Retained += laidThisPass;
            remaining -= laidThisPass;
            if (laidThisPass <= 0.0)
                break;
        }
        // Beds are floats, so the laid total can exceed the kept load by a float rounding step: it is capped here so that delivered =
        // Retained + RemovedByWaves + NoRoom closes exactly (the beds carry that rounding, far below a millimetre).
        result.Retained = Math.Min(result.Retained, kept);
        result.NoRoom = kept - result.Retained;
        for (int k = 0; k < kernel.Count; k++)
        {
            if (raised[k] && bed[kernel[k].cell] >= level - epsT)
                result.Promoted.Add(kernel[k].cell);
        }
        return result;
    }
}
