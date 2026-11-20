using System;
using System.Collections.Generic;

namespace LivingLandscapes.Geology;

/// <summary>
/// The sea of the erosion model as it fills (DELTA-MODEL-PLAN.md sections 2 and 4, step D3). Every sea cell keeps height 0 and stays an
/// outlet, so the model never sees a closed basin; its real floor is kept apart in <see cref="Bed"/> (metres relative to sea level, below
/// 0), and rises when the sea keeps river sediment. A cell whose bed reaches the promotion height becomes land, once, never again sea
/// (<see cref="Promote"/>). Game-free: tested alone.
/// </summary>
public sealed class SeaState
{
    public readonly int Gx, Gz;

    /// <summary>True where the cell is sea (a base level at 0). Only <see cref="Promote"/> turns a cell false.</summary>
    public readonly bool[] Sea;

    /// <summary>Sea floor in metres relative to sea level (negative); the bed of a promoted cell stays as it was when it became land.</summary>
    public readonly float[] Bed;

    /// <summary>Cells that began as land (never sea) or were promoted: not part of the sea.</summary>
    public int Promoted { get; private set; }

    /// <summary>Counts promotions, so a reader that caches something about the land (its regions) knows when to look again.</summary>
    public int Version { get; private set; }

    /// <summary>Sea cells at the start (the shore the model was given).</summary>
    public readonly int Initial;

    public SeaState(int gx, int gz, float[] depthMetres)
    {
        Gx = gx;
        Gz = gz;
        Sea = new bool[gx * gz];
        Bed = new float[gx * gz];
        for (int i = 0; i < Sea.Length; i++)
        {
            if (depthMetres[i] > 0f)
            {
                Sea[i] = true;
                Bed[i] = -depthMetres[i];
                Initial++;
            }
        }
    }

    public bool IsRegionEdge(int cell)
    {
        int x = cell % Gx, z = cell / Gx;
        return x == 0 || z == 0 || x == Gx - 1 || z == Gz - 1;
    }

    /// <summary>
    /// Turns a sea cell into land, once. Refused (false) for a cell that is not sea (already land or promoted), on the region's edge (a sea
    /// outlet must always remain) or <paramref name="keep"/> (the river's exit).
    /// </summary>
    public bool Promote(int cell, int keep = -1)
    {
        if (!Sea[cell] || cell == keep || IsRegionEdge(cell))
            return false;
        Sea[cell] = false;
        Promoted++;
        Version++;
        return true;
    }
}

/// <summary>What one pass of the sea fill did, in the erosion model's height units (the units of the load the model hands over).</summary>
public sealed class SeaPassResult
{
    public double Delivered, Retained, RemovedByWaves, NoRoom;
    public int PromotedCells;
    /// <summary>The cells turned into land by this pass, in the order they were promoted.</summary>
    public readonly List<int> Cells = new List<int>();
}

/// <summary>
/// The sea keeps part of what the model's rivers deliver to it during the stillstand window (rule R26) and lays it on the sea floor with the
/// jet kernel (rules E4 to E6 of the plan); cells it fills to the promotion height become land. The loop over the mouths and the metre
/// conversion live here so a test can run them without the game.
/// </summary>
public static class SeaFillPass
{
    /// <summary>Mouth depth (metres) = this x sqrt(width in map cells): hydraulic geometry fitted to the mouths D1 measured (width 9.7 cells, depth 1.0 m; 25.8 and 1.9; 4.4 and 1.0).</summary>
    public const float MouthDepthPerRootWidth = 0.35f;

    /// <summary>Least mouth depth (metres): a creek's bar still rises above the water.</summary>
    public const float MinMouthDepth = 0.15f;

    /// <summary>The settings a pass reads (game-free copy of what <c>GeologyGenerationExtension</c> holds).</summary>
    public struct Settings
    {
        public double WaveCapacityPerWidth;   // k_w x E, metres per model cell of front per pass
        public float SettleWidths, ReachLengths, SpreadRate;
        public float MapCellsPerModelCell;
        public float ReferenceWidthModelCells; // width of the largest mouth, model cells
        public int KeepExit;                   // the river's exit cell, never promoted
    }

    /// <summary>The Nth axis for a sea outlet cell: the way its biggest donor drains into it (null where nothing drains into it).</summary>
    public static bool Axis(int cell, int[] receiver, float[] area, int gx, int gz, out float ax, out float az)
    {
        ax = az = 0f;
        int cx = cell % gx, cz = cell / gx, best = -1;
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if ((dx == 0 && dz == 0) || x < 0 || z < 0 || x >= gx || z >= gz)
                    continue;
                int n = z * gx + x;
                if (receiver[n] == cell && (best < 0 || area[n] > area[best]))
                    best = n;
            }
        }
        if (best < 0)
            return false;
        float vx = cx - best % gx, vz = cz - best / gx, len = (float)Math.Sqrt(vx * vx + vz * vz);
        ax = vx / len;
        az = vz / len;
        return true;
    }

    /// <summary>
    /// One pass: every sea outlet cell holding delivered load hands it (x <paramref name="metresPerHeight"/>) to <see cref="SeaFill.Apply"/>
    /// along its own jet; the cells filled to the promotion height (the mouth's depth) become land with the height their bed reached.
    /// <paramref name="landHeight"/> receives that height (model units) for each promoted cell. Cells are visited in index order (deterministic).
    /// </summary>
    public static SeaPassResult Run(SeaState sea, float[] delivered, bool[] outlet, int[] receiver, float[] area, float metresPerHeight,
        Settings s, float[] landHeight)
    {
        var result = new SeaPassResult();
        int n = sea.Sea.Length;
        var kernel = new List<(int cell, float weight, float d)>();
        var ground = new float[n];
        for (int i = 0; i < n; i++)
            ground[i] = sea.Sea[i] ? sea.Bed[i] : float.PositiveInfinity;
        float reference = 0f;
        for (int c = 0; c < n; c++)
        {
            if (delivered[c] > 0f && sea.Sea[c] && area[c] > reference)
                reference = area[c];
        }
        // Which cells were sea when the pass began: a cell a neighbouring mouth fills during the pass still counts as sea for its own load.
        var seaAtStart = (bool[])sea.Sea.Clone();
        for (int c = 0; c < n; c++)
        {
            if (delivered[c] <= 0f || !seaAtStart[c])
                continue;
            double metres = delivered[c] * (double)metresPerHeight;
            result.Delivered += delivered[c];
            // Width (model cells) from the drainage area against the biggest mouth's: hydraulic geometry, w ~ sqrt(Q).
            float w = Math.Max(1f, s.ReferenceWidthModelCells * (float)Math.Sqrt(area[c] / Math.Max(reference, 1f)));
            float depth = Math.Max(MinMouthDepth, MouthDepthPerRootWidth * (float)Math.Sqrt(w * s.MapCellsPerModelCell));
            if (!Axis(c, receiver, area, sea.Gx, sea.Gz, out float ax, out float az))
            {
                result.NoRoom += delivered[c];
                continue;
            }
            float settle = s.SettleWidths * w, reach = settle * s.ReachLengths;
            JetKernel.Weights(c, ax, az, w, depth, ground, sea.Gx, sea.Gz, settle, reach, s.SpreadRate, kernel);
            // Shallow cells first from the mouth: the kernel's cells are sea (finite ground) at or below the promotion height.
            SeaFillResult r = SeaFill.Apply(sea.Bed, depth, kernel, metres, s.WaveCapacityPerWidth, w, w);
            result.Retained += r.Retained / metresPerHeight;
            result.RemovedByWaves += r.RemovedByWaves / metresPerHeight;
            result.NoRoom += r.NoRoom / metresPerHeight;
            foreach (int cell in r.Promoted)
            {
                if (sea.Promote(cell, s.KeepExit))
                {
                    landHeight[cell] = sea.Bed[cell] / metresPerHeight;
                    ground[cell] = float.PositiveInfinity;
                    result.PromotedCells++;
                    result.Cells.Add(cell);
                }
            }
            // The kernel for later mouths reads the beds this one raised.
            foreach (var k in kernel)
            {
                if (sea.Sea[k.cell])
                    ground[k.cell] = sea.Bed[k.cell];
            }
        }
        return result;
    }
}

/// <summary>
/// The erosion model's sediment ledger over a whole run, in model height units with double accumulators (plan section 4):
/// eroded + yield = kept + lake + retainedAtSea + removedByWaves + noRoom + landEdgeOutlet + lost. <see cref="Dropped"/> is the height the
/// outlet clamp and the bed cap take off ground that is not part of that chain; it is reported next to the closure, not inside it.
/// </summary>
public sealed class ErosionLedger
{
    public double Eroded, Yield, Kept, Lake, RetainedAtSea, RemovedByWaves, NoRoom, LandEdgeOutlet, Lost, Dropped;

    /// <summary>(everything the load went to) / (everything eroded or brought in): 1 when nothing was forgotten.</summary>
    public double Closure
    {
        get
        {
            double supply = Eroded + Yield;
            double sinks = Kept + Lake + RetainedAtSea + RemovedByWaves + NoRoom + LandEdgeOutlet + Lost;
            return supply > 0.0 ? sinks / supply : 1.0;
        }
    }

    /// <summary>The yield each non-outlet cell weighs a world river's inflow with (ErosionDeposit.ApplyCore's divisor), from this pass's outlets.</summary>
    public static double YieldDivisor(float[] eroded, bool[] outlet, out int land)
    {
        double sum = 0.0;
        land = 0;
        for (int i = 0; i < eroded.Length; i++)
        {
            if (!outlet[i])
            {
                sum += eroded[i];
                land++;
            }
        }
        return sum / Math.Max(1, land);
    }
}
