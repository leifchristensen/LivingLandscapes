using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>
/// Measures for the log: the world river's own network (how often it branches and rejoins itself, how many of its branches
/// reach the sea), used to judge deltas and braids.
/// </summary>
public static partial class TerrainChannels
{
    /// <summary>
    /// The world river's own network (Docs/SEDIMENT-PLAN.md): channels carrying a real share of its water. Counts how often it
    /// branches and rejoins itself and how many of its branches reach the sea, the measure of a delta or a braided reach (runoff
    /// creeks reaching the sea beside it do not count). Branch and rejoin points closer than a few cells count once.
    /// </summary>
    private static string MainRiver(List<int> cells, int[] receiver, int[] secondary, bool[] world, float[] discharge, float[] inflow, bool[] worldSource,
        bool[] sea, RiverSystem system, int sizeX, int sizeZ, TerrainChannelSettings p, out int mouthsOut)
    {
        mouthsOut = 0;
        float q = 0f;
        for (int i = 0; i < inflow.Length; i++)
        {
            if (worldSource[i])
                q += inflow[i];
        }
        if (q <= 0f)
            return "no world river";
        int n = receiver.Length;
        var main = new bool[n];
        foreach (int c in cells)
            main[c] = world[c] && discharge[c] >= p.sediment.mainRiverShare * q;
        var donors = new int[n];
        var branches = new List<Vector2>();
        foreach (int c in cells)
        {
            if (!main[c])
                continue;
            int r = receiver[c], s = secondary[c];
            if (r >= 0 && main[r])
                donors[r]++;
            if (s >= 0 && main[s])
            {
                donors[s]++;
                if (r >= 0 && main[r])
                    AddPoint(branches, c, sizeX);
            }
        }
        var rejoins = new List<Vector2>();
        foreach (int c in cells)
        {
            if (main[c] && donors[c] >= 2)
                AddPoint(rejoins, c, sizeX);
        }
        int mouths = sea == null ? 0 : Mouths(sea, system, sizeX, sizeZ, i => main[i] || (system.ChannelOf[i] >= 0 && Near(main, i, sizeX, sizeZ)));
        mouthsOut = mouths;
        int mainCells = 0;
        foreach (bool m in main)
            if (m) mainCells++;
        int worldCells = 0;
        float biggest = 0f;
        foreach (int c in cells)
        {
            if (world[c]) { worldCells++; biggest = Mathf.Max(biggest, discharge[c]); }
        }
        return $"main river ({mainCells} cells; {worldCells} carry world water, largest {biggest / q:P0} of the {q:0} entering) branches {branches.Count} times, rejoins {rejoins.Count} times, {mouths} of its branches reach the sea";
    }

    private static void AddPoint(List<Vector2> points, int c, int sizeX)
    {
        var v = new Vector2(c % sizeX, c / sizeX);
        if (!points.Exists(o => (o - v).sqrMagnitude < 36f))
            points.Add(v);
    }

    private static bool Near(bool[] mask, int i, int sizeX, int sizeZ)
    {
        int x = i % sizeX, z = i / sizeX;
        for (int dz = -3; dz <= 3; dz++)
        {
            for (int dx = -3; dx <= 3; dx++)
            {
                int nx = x + dx, nz = z + dz;
                if (nx >= 0 && nz >= 0 && nx < sizeX && nz < sizeZ && mask[nz * sizeX + nx])
                    return true;
            }
        }
        return false;
    }

    /// <summary>Distinct channels reaching the sea: groups of moving water cells (of those <paramref name="counted"/>), apart from each other, that touch it.</summary>
    private static int Mouths(bool[] sea, RiverSystem system, int sizeX, int sizeZ, System.Func<int, bool> counted)
    {
        int n = sea.Length;
        bool Mouth(int i)
        {
            if (sea[i] || system.ChannelOf[i] < 0 || system.Still[i] || !counted(i))
                return false;
            int x = i % sizeX, z = i / sizeX;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx >= 0 && nz >= 0 && nx < sizeX && nz < sizeZ && sea[nz * sizeX + nx])
                        return true;
                }
            }
            return false;
        }
        return GridMath.LabelRegions(Mouth, sizeX, sizeZ, out _);
    }

    /// <summary>Channel depths of basin that stop the jet and raise a bar (the plan's rho, 1 to 1.5); a stand-in for the measurement until D4 registers it.</summary>
    private const float BarDepthRatio = 1.25f;

    /// <summary>
    /// D1 measurement (DELTA-MODEL-PLAN.md, phase D1): what the river mouth does today, from the data the channel stage already has.
    /// The mouth is the world river's biggest channel cell touching the sea. Slope is the drop per map cell over the last 40 cells above
    /// it (height is in metres here, so no conversion); L_b the backwater length (E2), capped at the coastal plain's width; basin depth
    /// the sea floor 1, 3 and 5 mouth widths offshore (E3); the bar test (E7) uses the depth 3 widths out; sigma (E8) is taken L_b/2
    /// above the shore against the lowest dry ground within 3 widths of the channel. Read-only: nothing here changes the map.
    /// </summary>
    private static string DeltaReport(WorldContext.RiverMouth mouth, CoastlineExtension coast, Hilliness hilliness, List<int> cells, int[] receiver,
        int[] mainDonor, bool[] world, float[] discharge, float[] filled, float[] height, float[] width, float[] depth, bool[] sea, float seaLevel,
        RiverSystem system, int sizeX, int sizeZ, TerrainChannelSettings p, int branchesToSea, int seaCellsDeposited)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string head = $"DeltaFeature={mouth.DeltaFeature} links={mouth.Links}";
        if (sea == null)
            return "no mouth (" + head + ", no sea on the map)";
        int mouths = Mouths(sea, system, sizeX, sizeZ, i => true);
        int mouthCell = -1;
        foreach (int c in cells)
        {
            if (!world[c] || system.ChannelOf[c] < 0 || system.Still[c] || sea[c] || !TouchesSea(sea, c, sizeX, sizeZ))
                continue;
            if (mouthCell < 0 || discharge[c] > discharge[mouthCell])
                mouthCell = c;
        }
        if (mouthCell < 0)
            return "no mouth (" + head + ")";

        // Built land: cells of vanilla's water mask standing above the sea. Since D3 the sea mask here has them removed, so they are counted
        // from the map of built land the coast step planned.
        int built = 0;
        if (Coastline.LastBuilt != null)
        {
            foreach (bool b in Coastline.LastBuilt)
                built += b ? 1 : 0;
        }
        else if (!float.IsNaN(seaLevel))
        {
            for (int i = 0; i < sea.Length; i++)
            {
                if (sea[i] && height[i] > seaLevel)
                    built++;
            }
        }
        float w = width[mouthCell], d = depth[mouthCell];

        // Lower valley slope: the drop per map cell over the last (up to) 40 cells above the mouth cell.
        int start = StepUp(mainDonor, mouthCell, 40, out int slopeCells);
        var stop = new bool[sea.Length];
        stop[mouthCell] = true;
        float slope = Hydraulics.ReachSlope(start, receiver, filled, sizeX, 40, stop);
        float plain = coast?.PlainWidthFor(hilliness) ?? float.PositiveInfinity;
        float lb = DeltaMath.BackwaterLength(d, slope, plain, DeltaMath.MinSlope, out bool capped);

        string Basin(float widths) => coast == null ? "n/a" : coast.SeaDepth(widths * w, hilliness).ToString("0.00", inv);
        float basin3 = coast == null ? float.NaN : coast.SeaDepth(3f * w, hilliness);
        string bar = coast == null ? "n/a" : DeltaMath.BarForms(basin3, d, BarDepthRatio) ? "yes" : "no";

        // Superelevation half a backwater length above the shore.
        int node = StepUp(mainDonor, mouthCell, Mathf.RoundToInt(lb / 2f), out int nodeSteps);
        int reach = Mathf.Max(3, Mathf.CeilToInt(3f * w));
        float lateral = float.PositiveInfinity;
        int nx0 = node % sizeX, nz0 = node / sizeX;
        for (int dz = -reach; dz <= reach; dz++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                int x = nx0 + dx, z = nz0 + dz;
                if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ || dx * dx + dz * dz > reach * reach)
                    continue;
                int i = z * sizeX + x;
                if (!sea[i] && system.ChannelOf[i] < 0)
                    lateral = Mathf.Min(lateral, height[i]);
            }
        }
        string sigma = float.IsPositiveInfinity(lateral) ? "n/a"
            : DeltaMath.Superelevation(height[node], depth[node], lateral).ToString("0.00", inv);

        return string.Format(inv,
            "{0}, mouths {1}, main branches reaching the sea {2}, sea cells with deposit {3}, built land cells {4}, " +
            "mouth width {5:0.0} depth {6:0.00} m, lower slope {7:0.0000} m/cell over {8} cells, L_b {9:0.0} cells{10} (plain cap {11:0.0}), " +
            "basin depth at 1/3/5 widths {12}/{13}/{14} m, bar forms {15}, sigma at {16} cells above the shore {17}, " +
            "delivered sediment at sea vs land-edge outlets not read (the ledger is on the erosion model's grid, not kept in the channel stage)",
            head, mouths, branchesToSea, seaCellsDeposited, built, w, d, slope, slopeCells, lb, capped ? " (capped)" : "", plain,
            Basin(1f), Basin(3f), Basin(5f), bar, nodeSteps, sigma);
    }

    /// <summary>The cell <paramref name="steps"/> main-donor steps upstream of <paramref name="c"/> (fewer where the river rises sooner); <paramref name="taken"/> says how many.</summary>
    private static int StepUp(int[] mainDonor, int c, int steps, out int taken)
    {
        taken = 0;
        while (taken < steps && mainDonor[c] >= 0)
        {
            c = mainDonor[c];
            taken++;
        }
        return c;
    }

    private static bool TouchesSea(bool[] sea, int i, int sizeX, int sizeZ)
    {
        int x = i % sizeX, z = i / sizeX;
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, nz = z + dz;
                if (nx >= 0 && nz >= 0 && nx < sizeX && nz < sizeZ && sea[nz * sizeX + nx])
                    return true;
            }
        }
        return false;
    }
}
