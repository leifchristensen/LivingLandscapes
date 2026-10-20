using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>
/// Stage 5, channel plans: the routed cells become channels (centre lines with widths, each joining its parent or leaving the
/// map) for vanilla's river painter and the logs.
/// </summary>
public static partial class TerrainChannels
{
    /// <summary>
    /// Channels as courses: each runs from its head (or where it enters the map) down through every junction where it
    /// brings the most water, and ends where it joins a larger one or leaves the map. Channels carrying a world river's
    /// water take its vanilla width; the one leaving the map is the world river.
    /// </summary>
    private static List<RiverPlan> Trace(List<int> cells, int[] receiver, int[] main, int[] branchFrom, float[] discharge, float[] inflow, bool[] world, float[] width,
        int sizeX, float runoff, float vanillaWidth, out int[] planOf)
    {
        int n = receiver.Length;
        planOf = new int[n];
        for (int i = 0; i < n; i++)
            planOf[i] = -1;
        var plans = new List<RiverPlan>();
        var ends = new List<int>();
        var parents = new List<int>();
        Vector2 Centre(int c) => new Vector2(c % sizeX + 0.5f, c / sizeX + 0.5f);
        foreach (int c in cells)
        {
            if (planOf[c] < 0)
            {
                // A head: no channel flows into this cell as its main donor.
                planOf[c] = plans.Count;
                var plan = new RiverPlan();
                // A branch starts where it divided from its channel.
                if (branchFrom[c] >= 0)
                {
                    plan.Main.Add(Centre(branchFrom[c]));
                    plan.MainWidth.Add(Mathf.Max(1f, Mathf.Round(width[c])));
                }
                plans.Add(plan);
                ends.Add(c);
                parents.Add(-1);
            }
            int k = planOf[c];
            // Water entering from beyond the map, as the drainage area that would bring it (for drainage and wetness).
            plans[k].EntryArea += inflow[c] / runoff;
            plans[k].Main.Add(Centre(c));
            plans[k].MainWidth.Add(Mathf.Max(1f, Mathf.Round(width[c])));
            plans[k].NominalWidth = width[c];
            ends[k] = c;
            if (world[c] && vanillaWidth > 0f)
                plans[k].VanillaWidth = vanillaWidth;
            int r = receiver[c];
            if (r >= 0 && main[r] == c)
                planOf[r] = k;
        }
        // Each channel that ends where a larger one passes joins it there.
        for (int k = 0; k < plans.Count; k++)
        {
            int r = receiver[ends[k]];
            if (r >= 0 && planOf[r] >= 0 && planOf[r] != k)
            {
                plans[k].Main.Add(Centre(r));
                plans[k].MainWidth.Add(plans[k].MainWidth[plans[k].MainWidth.Count - 1]);
                parents[k] = planOf[r];
            }
        }

        // The world river: the largest channel carrying world water that leaves the map.
        int worldRiver = -1;
        for (int k = 0; k < plans.Count; k++)
        {
            if (parents[k] < 0 && plans[k].VanillaWidth > 0f && (worldRiver < 0 || discharge[ends[k]] > discharge[ends[worldRiver]]))
                worldRiver = k;
        }
        if (worldRiver >= 0)
            plans[worldRiver].WorldRiver = true;

        // Larger channels first (the world river leads), parents re-indexed.
        var sorted = new List<int>();
        for (int k = 0; k < plans.Count; k++)
            sorted.Add(k);
        sorted.Sort((a, b) => a == worldRiver ? -1 : b == worldRiver ? 1 : discharge[ends[b]].CompareTo(discharge[ends[a]]));
        var index = new int[plans.Count];
        for (int i = 0; i < sorted.Count; i++)
            index[sorted[i]] = i;
        var result = new List<RiverPlan>();
        foreach (int k in sorted)
        {
            plans[k].Parent = parents[k] >= 0 ? index[parents[k]] : -1;
            result.Add(plans[k]);
        }
        for (int i = 0; i < n; i++)
        {
            if (planOf[i] >= 0)
                planOf[i] = index[planOf[i]];
        }
        return result;
    }
}
