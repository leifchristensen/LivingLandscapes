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
/// The scale from the erosion model's load units to metres of sea floor, fixed once per map by the first pass of the stillstand window.
/// The model's load (eroded ground plus the world rivers' inflow weights) has no physical unit of its own and is thousands of times the
/// room a mouth has, so one pass is defined to lay <c>FillPerPass</c> of the room the main mouth's jet reaches (rule R24, one deposit
/// scale per map); every other mouth and pass is then in proportion to it, and the stillstand window's length sizes the delta.
/// </summary>
public sealed class SeaSupply
{
    /// <summary>Metres of thickness per unit of delivered load; 0 until the first pass has fixed it.</summary>
    public double Scale;
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
        public double WaveShare;              // E / E_ref = 1/R: wave capacity of the main mouth as a share of its delivered load (R3 to 5 sheltered, below 1 open)
        public float SettleWidths, ReachLengths, SpreadRate;
        public float MapCellsPerModelCell;
        public float ReferenceWidthModelCells; // width of the largest mouth, model cells
        public int KeepExit;                   // the river's exit cell, never promoted
        public SeaSupply Supply;               // the load-to-metres scale, fixed by the window's first pass
        public float WindowMinX, WindowMinZ, WindowMaxX, WindowMaxZ; // the map window on the model grid (model cells); a lobe keeps a margin of open sea inside it
        public float WindowMargin;             // model cells
        public float MaxReach;                 // the jet never reaches farther than this from the mouth (model cells); load it cannot place there is noRoom
        public float FillPerPass;              // share of the room the main jet reaches that one pass lays (the unit of the load)
    }

    /// <summary>The Nth axis for a sea outlet cell: the way its biggest donor drains into it (null where nothing drains into it).</summary>
    public static bool Axis(int cell, int[] receiver, float[] area, int gx, int gz, out float ax, out float az, out int donor)
    {
        ax = az = 0f;
        donor = -1;
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
        donor = best;
        return true;
    }

    /// <summary>How far the jet reaches: its settling lengths, never beyond the cap that keeps a lobe inside the map.</summary>
    private static float Reach(float settle, Settings s, int cell, float ax, float az, int gx)
    {
        float reach = settle * s.ReachLengths;
        if (s.MaxReach > 0f)
            reach = Math.Min(reach, s.MaxReach);
        if (s.WindowMaxX > s.WindowMinX)
        {
            // Along the axis to the map window's edge, less a margin: the sea the river ends in stays on the map.
            float x = cell % gx, z = cell / gx, toEdge = float.MaxValue;
            if (ax > 1e-4f) toEdge = Math.Min(toEdge, (s.WindowMaxX - x) / ax);
            else if (ax < -1e-4f) toEdge = Math.Min(toEdge, (s.WindowMinX - x) / ax);
            if (az > 1e-4f) toEdge = Math.Min(toEdge, (s.WindowMaxZ - z) / az);
            else if (az < -1e-4f) toEdge = Math.Min(toEdge, (s.WindowMinZ - z) / az);
            reach = Math.Min(reach, Math.Max(1f, toEdge - s.WindowMargin));
        }
        return reach;
    }

    /// <summary>The metres per unit of load: the main mouth's first load lays <c>FillPerPass</c> of the room its jet reaches (0 when nothing arrives).</summary>
    private static double FixScale(SeaState sea, float[] delivered, int[] receiver, float[] area, Settings s, float[] ground)
    {
        int main = -1;
        for (int c = 0; c < delivered.Length; c++)
        {
            if (delivered[c] > 0f && sea.Sea[c] && (main < 0 || delivered[c] > delivered[main]))
                main = c;
        }
        if (main < 0 || !Axis(main, receiver, area, sea.Gx, sea.Gz, out float ax, out float az, out _))
            return 0.0;
        float w = Math.Max(1f, s.ReferenceWidthModelCells);
        float depth = Math.Max(MinMouthDepth, MouthDepthPerRootWidth * (float)Math.Sqrt(w * s.MapCellsPerModelCell));
        var kernel = new List<(int cell, float weight, float d)>();
        float settle = s.SettleWidths * w;
        JetKernel.Weights(main, ax, az, w, depth, ground, sea.Gx, sea.Gz, settle, Reach(settle, s, main, ax, az, sea.Gx), s.SpreadRate, kernel);
        double room = 0.0;
        foreach (var k in kernel)
        {
            if (k.weight > 0f)
                room += SeaFill.Room(depth, sea.Bed[k.cell]);
        }
        return room <= 0.0 ? 0.0 : s.FillPerPass * room / delivered[main];
    }

    /// <summary>
    /// One pass: every sea outlet cell holding delivered load hands it (x the supply scale) to <see cref="SeaFill.Apply"/>
    /// along its own jet; the cells filled to the promotion height (the mouth's depth) become land with the height their bed reached.
    /// <paramref name="landHeight"/> receives that height (model units) for each promoted cell. Cells are visited in index order (deterministic).
    /// </summary>
    public static SeaPassResult Run(SeaState sea, float[] delivered, bool[] outlet, int[] receiver, float[] area, float[] height, float metresPerHeight,
        Settings s, float[] landHeight)
    {
        var result = new SeaPassResult();
        int n = sea.Sea.Length;
        var kernel = new List<(int cell, float weight, float d)>();
        var ground = new float[n];
        for (int i = 0; i < n; i++)
            ground[i] = sea.Sea[i] ? sea.Bed[i] : float.PositiveInfinity;
        float reference = 0f;
        double mainLoad = 0.0; // the biggest mouth's delivered load this pass, in metres: the wave capacity is set against it, so both share one unit
        for (int c = 0; c < n; c++)
        {
            if (delivered[c] > 0f && sea.Sea[c])
            {
                reference = Math.Max(reference, area[c]);
                mainLoad = Math.Max(mainLoad, (double)delivered[c]);
            }
        }
        // Capacity per model cell of front: the main mouth (width = the reference width) has Q_w = WaveShare x its own load, so R = Q_s/Q_w = 1/WaveShare there.
        if (s.Supply.Scale <= 0.0)
            s.Supply.Scale = FixScale(sea, delivered, receiver, area, s, ground);
        double scale = s.Supply.Scale;
        double capacityPerWidth = s.WaveShare * mainLoad * scale / Math.Max(1f, s.ReferenceWidthModelCells);
        // Which cells were sea when the pass began: a cell a neighbouring mouth fills during the pass still counts as sea for its own load.
        var seaAtStart = (bool[])sea.Sea.Clone();
        for (int c = 0; c < n; c++)
        {
            if (delivered[c] <= 0f || !seaAtStart[c])
                continue;
            double metres = delivered[c] * scale;
            result.Delivered += delivered[c];
            // Width (model cells) from the drainage area against the biggest mouth's: hydraulic geometry, w ~ sqrt(Q).
            float w = Math.Max(1f, s.ReferenceWidthModelCells * (float)Math.Sqrt(area[c] / Math.Max(reference, 1f)));
            float depth = Math.Max(MinMouthDepth, MouthDepthPerRootWidth * (float)Math.Sqrt(w * s.MapCellsPerModelCell));
            if (!Axis(c, receiver, area, sea.Gx, sea.Gz, out float ax, out float az, out int donor))
            {
                result.NoRoom += delivered[c];
                continue;
            }
            // New land never stands higher than the ground behind the mouth (the land the river comes down), and never below the sea.
            float ceiling = Math.Min(depth, Math.Max(MinMouthDepth * 0.5f, height[donor] * metresPerHeight));
            float settle = s.SettleWidths * w, reach = Reach(settle, s, c, ax, az, sea.Gx);
            JetKernel.Weights(c, ax, az, w, depth, ground, sea.Gx, sea.Gz, settle, reach, s.SpreadRate, kernel);
            // Shallow cells first from the mouth: the kernel's cells are sea (finite ground) at or below the promotion height.
            SeaFillResult r = SeaFill.Apply(sea.Bed, ceiling, kernel, metres, capacityPerWidth, w, w);
            result.Retained += r.Retained / scale;
            result.RemovedByWaves += r.RemovedByWaves / scale;
            result.NoRoom += r.NoRoom / scale;
            foreach (int cell in r.Promoted)
            {
                if (sea.Promote(cell, s.KeepExit))
                {
                    landHeight[cell] = Math.Max(0f, sea.Bed[cell]) / metresPerHeight;
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
/// The river's exit as built (plan risk 5): the sea fill can wall the exit cell in with new land, so the world river has to run on across
/// that land to the first cell of sea still open along its own direction. The cells in between join the drainage as one channel.
/// </summary>
public static class SeaExit
{
    /// <summary>
    /// Walks from <paramref name="exit"/> along the direction its biggest donor drains into it, to the first cell that is still sea.
    /// Receivers of the cells walked are linked into a chain ending at that cell and their drainage area is raised to the exit's.
    /// Returns the new exit (the old one when the sea is next to it already or no sea lies along the line), and the cells walked.
    /// </summary>
    public static int Extend(SeaState sea, int[] receiver, float[] area, int exit, List<int> walked)
    {
        walked.Clear();
        if (exit < 0 || sea.Promoted == 0 || !SeaFillPass.Axis(exit, receiver, area, sea.Gx, sea.Gz, out float ax, out float az, out _))
            return exit;
        int ex = exit % sea.Gx, ez = exit / sea.Gx;
        var line = new List<int>();
        int last = exit;
        for (int step = 1; step <= sea.Gx + sea.Gz; step++)
        {
            int cx = (int)Math.Round(ex + ax * step), cz = (int)Math.Round(ez + az * step);
            if (cx < 0 || cz < 0 || cx >= sea.Gx || cz >= sea.Gz)
                return exit;
            int c = cz * sea.Gx + cx;
            if (c == last)
                continue;
            if (sea.Sea[c])
            {
                if (line.Count == 0)
                    return exit; // the sea is next to the exit already
                receiver[last] = c;
                receiver[c] = -1;
                walked.AddRange(line);
                return c;
            }
            // Land the sea fill built in front of the exit: the river crosses it.
            area[c] = Math.Max(area[c], area[exit]);
            if (line.Count > 0)
                receiver[last] = c;
            else
                receiver[exit] = c;
            line.Add(c);
            last = c;
        }
        return exit;
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
