using System.Collections.Generic;

namespace LivingLandscapes.Geology;

/// <summary>
/// Water from beyond the map (P3, P23): a closed basin keeps only the water its own catchment collects against its lake's
/// evaporation (<see cref="WaterBalanceMath"/>). A hollow a world river runs into is not that: the river's discharge from
/// beyond the map arrives in it, so it fills and spills over its rim like any hollow on a through-flowing river (#83).
/// A catchment larger than the region itself cannot have been collected inside it, so it holds such water. Game-free so
/// it can be unit tested.
/// </summary>
public static class FedBasinRules
{
    /// <summary>
    /// Lifts every sink (a dry basin's floor kept as an outlet at its balance level) whose drainage area exceeds
    /// <paramref name="regionCells"/>: its cells go back to NaN base level and out of <paramref name="sink"/>. A sink is a
    /// connected group of sink cells, lifted whole. Returns how many sinks were lifted.
    /// </summary>
    public static int LiftFedSinks(bool[] sink, float[] baseLevel, float[] area, int regionCells, int sizeX, int sizeZ)
    {
        int n = sink.Length, lifted = 0;
        var seen = new bool[n];
        var group = new List<int>();
        for (int start = 0; start < n; start++)
        {
            if (!sink[start] || seen[start])
                continue;
            group.Clear();
            group.Add(start);
            seen[start] = true;
            bool fed = false;
            for (int head = 0; head < group.Count; head++)
            {
                int c = group[head];
                fed |= area[c] > regionCells;
                int cx = c % sizeX, cz = c / sizeX;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                            continue;
                        int nb = z * sizeX + x;
                        if (sink[nb] && !seen[nb])
                        {
                            seen[nb] = true;
                            group.Add(nb);
                        }
                    }
                }
            }
            if (!fed)
                continue;
            foreach (int c in group)
            {
                sink[c] = false;
                baseLevel[c] = float.NaN;
            }
            lifted++;
        }
        return lifted;
    }
}
