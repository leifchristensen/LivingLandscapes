using System.Collections.Generic;
using System.Linq;
using RimWorld;
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
        bool[] sea, RiverSystem system, int sizeX, int sizeZ, TerrainChannelSettings p)
    {
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
}
