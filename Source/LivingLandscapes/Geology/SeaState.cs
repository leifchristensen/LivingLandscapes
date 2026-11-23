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
    /// <summary>What the pass delivered, in metres of sea floor summed over cells (delivered x scale); 0 with no scale.</summary>
    public double Metres;

    // The biggest mouth's terms of the chain, for the report: load it delivers (metres summed over cells), the waves' capacity at its width
    // (same unit), their ratio R = Q_s / Q_w, what it kept (metres), the cells its jet raised, and the mean rise of those cells.
    public double MainQs, MainQw, MainR, MainRetainedMetres, MainRaisedCells, MainThickness;
    public int PromotedCells;
    public List<string> MouthDebug = new List<string>(); // TEMP probe
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

    /// <summary>The ceiling of a new land cell is never below this share of the least mouth depth (a bar still rises above the water).</summary>
    public const float MinCeilingShare = 0.5f;

    /// <summary>The settings a pass reads (game-free copy of what <c>GeologyGenerationExtension</c> holds).</summary>
    public struct Settings
    {
        public double WaveShare;              // E / E_ref = 1/R: wave capacity of the main mouth as a share of its delivered load (R3 to 5 sheltered, below 1 open)
        public float SettleWidths, ReachLengths, SpreadRate;
        public float MapCellsPerModelCell;
        public float ReferenceWidthModelCells; // width of the largest mouth, model cells
        public int KeepExit;                   // the river's exit cell, never promoted
        public float WindowMinX, WindowMinZ, WindowMaxX, WindowMaxZ; // the map window on the model grid (model cells); a lobe keeps a margin of open sea inside it
        public float WindowMargin;             // model cells
        public float MaxReach;                 // the jet never reaches farther than this from the mouth (model cells); load it cannot place there is noRoom
        public float ReferenceRiverWidthMapCells; // map width of the reference river (the Large class): a river of this width has discharge weight 1
        public float MetresPerReferenceRiver;  // ONE constant for every map: metres of sea floor (summed over cells) a reference river (the Large class) delivers to its mouth in one pass
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

    /// <summary>
    /// The discharge of the main mouth against the reference river's: hydraulic geometry gives width ~ sqrt(discharge), so the ratio is (width /
    /// reference width) squared. The erosion model's own inflow cannot say this: every world river enters with at least the same minimum weight
    /// (riverInflow x the region's cells), so a Huge and a small river deliver the same load there. 0 when a width is missing.
    /// </summary>
    public static double DischargeWeight(Settings s)
    {
        if (!(s.ReferenceRiverWidthMapCells > 0f) || !(s.ReferenceWidthModelCells > 0f) || !(s.MapCellsPerModelCell > 0f))
            return 0.0;
        double ratio = s.ReferenceWidthModelCells * s.MapCellsPerModelCell / s.ReferenceRiverWidthMapCells;
        return ratio * ratio;
    }

    /// <summary>
    /// Metres of sea floor (summed over cells) per unit of the model's load this pass: the biggest mouth delivers
    /// <see cref="Settings.MetresPerReferenceRiver"/> times its discharge weight, every other mouth its share of the biggest's load. The model's load
    /// itself has no physical size (it is a yield weighted by an inflow floor, and varies tens of times between maps for the same river), so only
    /// its proportions between mouths are used. 0 when anything is missing or not a finite positive number (no scale: every load is booked as
    /// noRoom, never lost and never NaN).
    /// </summary>
    public static double Scale(Settings s, double mainLoad)
    {
        double weight = DischargeWeight(s);
        if (!(mainLoad > 0.0) || double.IsInfinity(mainLoad) || !(s.MetresPerReferenceRiver > 0f) || float.IsInfinity(s.MetresPerReferenceRiver)
            || !(weight > 0.0) || double.IsInfinity(weight))
            return 0.0;
        return s.MetresPerReferenceRiver * weight / mainLoad;
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
        double scale = Scale(s, mainLoad);
        if (scale <= 0.0)
        {
            // No scale: nothing can be laid, so every load that reached the sea is booked as noRoom (the ledger still closes).
            for (int c = 0; c < n; c++)
            {
                if (delivered[c] > 0f && sea.Sea[c])
                {
                    result.Delivered += delivered[c];
                    result.NoRoom += delivered[c];
                }
            }
            return result;
        }
        // Wave capacity per model cell of front: ONE absolute figure for every river. The reference river (the Large class, whose mouth is
        // ReferenceRiverWidthMapCells wide) delivers MetresPerReferenceRiver, and the waves can carry WaveShare times that over its own width, so its
        // river-to-wave ratio is 1/WaveShare. A bigger river is wider by sqrt(discharge) and carries more per width, so its ratio R and its retained
        // share rise with size; a small river on the same coast keeps less. No per-map or per-hilliness factor.
        double referenceWidthModelCells = Math.Max(1.0, s.ReferenceRiverWidthMapCells / Math.Max(1e-6f, s.MapCellsPerModelCell));
        double capacityPerWidth = s.WaveShare * s.MetresPerReferenceRiver / referenceWidthModelCells;
        int mainCell = -1;
        for (int c = 0; c < n; c++)
        {
            if (delivered[c] > 0f && sea.Sea[c] && (mainCell < 0 || delivered[c] > delivered[mainCell]))
                mainCell = c;
        }
        // Which cells were sea when the pass began: a cell a neighbouring mouth fills during the pass still counts as sea for its own load.
        var seaAtStart = (bool[])sea.Sea.Clone();
        for (int c = 0; c < n; c++)
        {
            if (delivered[c] <= 0f || !seaAtStart[c])
                continue;
            double metres = delivered[c] * scale;
            result.Metres += metres;
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
            float ceiling = Math.Min(depth, Math.Max(MinMouthDepth * MinCeilingShare, height[donor] * metresPerHeight));
            // The jet settles within the reach it is allowed (the cap that keeps the lobe on the map): a wide mouth would otherwise settle over
            // more cells than the cap holds and the lobe would end in a straight cut at the cap, a slab. With the settling length held to a third of
            // the reach the density has fallen to a twentieth by the edge, so the lobe's end is where the load runs out, not a box edge.
            float settle = s.SettleWidths * w;
            float reachCap = Reach(float.MaxValue / 8f, s, c, ax, az, sea.Gx);
            settle = Math.Min(settle, reachCap / Math.Max(s.ReachLengths, 1f));
            // The kernel is searched out to the cap, not to the settling length: the cells that fill are the densest ones, so the lobe's size is set by
            // the load kept and its edge is a contour of the density, not the edge of the box the search was limited to.
            float reach = reachCap;
            JetKernel.Weights(c, ax, az, w, depth, ground, sea.Gx, sea.Gz, settle, reach, s.SpreadRate, kernel, fan: true);
            // Shallow cells first from the mouth: the kernel's cells are sea (finite ground) at or below the promotion height.
            float[] bedBefore = null;
            if (c == mainCell)
            {
                bedBefore = new float[kernel.Count];
                for (int k = 0; k < kernel.Count; k++)
                    bedBefore[k] = sea.Bed[kernel[k].cell];
            }
            SeaFillResult r = SeaFill.Apply(sea.Bed, ceiling, kernel, metres, capacityPerWidth, w, w);
            if (bedBefore != null)
            {
                double rise = 0.0;
                int raised = 0;
                for (int k = 0; k < kernel.Count; k++)
                {
                    float d = sea.Bed[kernel[k].cell] - bedBefore[k];
                    if (d > 0f)
                    {
                        raised++;
                        rise += d;
                    }
                }
                double qw = capacityPerWidth * w;
                result.MainQs = metres;
                result.MainQw = qw;
                result.MainR = qw > 0.0 ? metres / qw : double.PositiveInfinity;
                result.MainRetainedMetres = r.Retained;
                result.MainRaisedCells = raised;
                result.MainThickness = raised > 0 ? rise / raised : 0.0;
            }
            result.MouthDebug.Add($"mouth ({c % sea.Gx},{c / sea.Gx}) w {w:F1} depth {depth:F2} ceil {ceiling:F2} reach {reach:F1} axis ({ax:F2},{az:F2}) kernel {kernel.Count} metres {metres:F2} retained {r.Retained:F3} promoted {r.Promoted.Count}");
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
/// that land to the first cell of sea still open. The cells in between join the drainage as one channel.
/// </summary>
public static class SeaExit
{
    /// <summary>A step must lie within this cosine of the held course (60 degrees either side): the river bends to the lowest ground, it does not turn back.</summary>
    public const float CourseCone = 0.5f;

    /// <summary>The sea cells joined to the region's border by sea (4-connected): the open sea. Sea the new land enclosed is not in it.</summary>
    public static bool[] OpenSea(SeaState sea)
    {
        int gx = sea.Gx, gz = sea.Gz;
        var open = new bool[gx * gz];
        var queue = new Queue<int>();
        void Seed(int c)
        {
            if (sea.Sea[c] && !open[c])
            {
                open[c] = true;
                queue.Enqueue(c);
            }
        }
        for (int x = 0; x < gx; x++)
        {
            Seed(x);
            Seed((gz - 1) * gx + x);
        }
        for (int z = 0; z < gz; z++)
        {
            Seed(z * gx);
            Seed(z * gx + gx - 1);
        }
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            int x = c % gx, z = c / gx;
            if (x > 0) Seed(c - 1);
            if (x < gx - 1) Seed(c + 1);
            if (z > 0) Seed(c - gx);
            if (z < gz - 1) Seed(c + gx);
        }
        return open;
    }

    /// <summary>True when following receivers from <paramref name="cell"/> reaches a cell in <paramref name="targets"/> (or loops: treated as reaching).</summary>
    private static bool DrainsInto(int cell, int[] receiver, HashSet<int> targets)
    {
        int c = cell;
        for (int guard = 0; guard <= receiver.Length && c >= 0; guard++)
        {
            if (targets.Contains(c))
                return true;
            c = receiver[c];
        }
        return c >= 0; // ran out of guard steps: a loop
    }

    /// <summary>
    /// Walks from <paramref name="exit"/> to the first cell that is still sea, one cell at a time: each step is the lowest ground (by
    /// <paramref name="height"/>, so open sea at 0 wins over built land) among the neighbours within <see cref="CourseCone"/> of the held course
    /// (the way the exit's biggest donor drains into it). A cell is never taken twice, and never one that already drains into the exit or into
    /// the cells walked (that would close a loop). Receivers of the cells walked are linked into a chain ending at the sea cell and their drainage
    /// area is raised to the exit's. Returns the new exit (the old one when the sea is next to it already, or no way to open sea was found, in
    /// which case nothing is changed), and the cells walked.
    /// </summary>
    public static int Extend(SeaState sea, int[] receiver, float[] area, float[] height, int exit, List<int> walked)
    {
        walked.Clear();
        if (exit < 0 || sea.Promoted == 0 || !SeaFillPass.Axis(exit, receiver, area, sea.Gx, sea.Gz, out float ax, out float az, out _))
            return exit;
        int gx = sea.Gx, gz = sea.Gz;
        bool[] open = OpenSea(sea);
        int aheadX = exit % gx + (int)Math.Round(ax), aheadZ = exit / gx + (int)Math.Round(az);
        if (aheadX < 0 || aheadZ < 0 || aheadX >= gx || aheadZ >= gz || open[aheadZ * gx + aheadX])
            return exit; // the open sea is straight ahead of the exit already (or the map ends there)
        var line = new List<int>();
        var taken = new HashSet<int> { exit };
        int last = exit;
        for (int step = 0; step < gx + gz; step++)
        {
            int lx = last % gx, lz = last / gx, best = -1;
            float bestHeight = float.MaxValue;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = lx + dx, z = lz + dz;
                    if ((dx == 0 && dz == 0) || x < 0 || z < 0 || x >= gx || z >= gz)
                        continue;
                    float len = (float)Math.Sqrt(dx * dx + dz * dz);
                    if ((dx * ax + dz * az) / len < CourseCone)
                        continue;
                    int c = z * gx + x;
                    if (taken.Contains(c) || (open[c] && line.Count == 0))
                        continue; // (open sea beside the exit with land straight ahead is not the way out: the walk goes on over the land)
                    if (!open[c] && DrainsInto(c, receiver, taken))
                        continue;
                    float hc = open[c] ? float.MinValue : height[c]; // a pond the new land enclosed is ground to cross, not the sea
                    if (hc < bestHeight || (hc == bestHeight && c < best))
                    {
                        bestHeight = hc;
                        best = c;
                    }
                }
            }
            if (best < 0)
                return exit;
            if (open[best])
            {
                // Commit: nothing was touched until a way to open sea was found.
                int from = exit;
                foreach (int c in line)
                {
                    receiver[from] = c;
                    area[c] = Math.Max(area[c], area[exit]);
                    from = c;
                }
                receiver[from] = best;
                receiver[best] = -1;
                walked.AddRange(line);
                return best;
            }
            taken.Add(best);
            line.Add(best);
            last = best;
        }
        return exit;
    }
}

/// <summary>
/// The erosion model's sediment ledger over a whole run, in model height units with double accumulators (plan section 4):
/// eroded + yield = kept + lake + retainedAtSea + removedByWaves + noRoom + landEdgeOutlet + lost + dropped. <see cref="Dropped"/> is the
/// height the outlet clamp and the bed cap take off ground; it is a sink like the others. The world rivers' yield is thousands to millions of
/// times the eroded ground, so <see cref="Closure"/> alone cannot see an error in the eroded terms; <see cref="ErodedClosure"/> is reported beside it.
/// </summary>
public sealed class ErosionLedger
{
    public double Eroded, Yield, Kept, Lake, RetainedAtSea, RemovedByWaves, NoRoom, LandEdgeOutlet, Lost, Dropped;

    private double Sinks => Kept + Lake + RetainedAtSea + RemovedByWaves + NoRoom + LandEdgeOutlet + Lost + Dropped;

    /// <summary>(everything the load went to) / (everything eroded or brought in): 1 when nothing was forgotten.</summary>
    public double Closure
    {
        get
        {
            double supply = Eroded + Yield;
            return supply > 0.0 ? Sinks / supply : 1.0;
        }
    }

    /// <summary>
    /// The closure seen against the eroded ground alone: (sinks - yield) / eroded, which is 1 when the yield is exactly accounted and nothing
    /// eroded was forgotten. A forgotten 1% of the eroded ground moves this by 0.01 although it moves <see cref="Closure"/> by 1e-9.
    /// </summary>
    public double ErodedClosure => Eroded > 0.0 ? (Sinks - Yield) / Eroded : 1.0;

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
