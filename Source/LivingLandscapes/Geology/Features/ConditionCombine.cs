using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>How a rock arrangement contribution changes the rock it finds.</summary>
public enum RockOperation
{
    /// <summary>
    /// Changes the rock in place by a factor on its erodibility (below 1 harder, above 1 softer), fading with the region's weight.
    /// Today's landforms use it with a raw number; phase 4 replaces the number with the tile's own rock types (FEATURE-REGIONS.md 2.3).
    /// </summary>
    Alter,

    /// <summary>A younger body (plug, dike) replaces the rock it crosses: its erodibility, fading with the region's weight. Order matters.</summary>
    Intrude,
}

/// <summary>One region's contribution to uplift over the model grid.</summary>
public readonly struct UpliftContribution
{
    /// <summary>Uplift added at each cell (the region's weight already applied).</summary>
    public readonly float[] values;
    public readonly TimeWindow window;

    public UpliftContribution(float[] values, TimeWindow window)
    {
        this.values = values;
        this.window = window ?? TimeWindow.WholeRun;
    }
}

/// <summary>One region's contribution to the rock arrangement over the model grid.</summary>
public readonly struct RockContribution
{
    /// <summary>The region's weight at each cell, 0..1.</summary>
    public readonly float[] weight;
    public readonly RockOperation operation;

    /// <summary>The erodibility factor (Alter) or the body's own erodibility (Intrude).</summary>
    public readonly float factor;

    /// <summary>When it acts; its start is its age, so contributions apply oldest first.</summary>
    public readonly TimeWindow window;

    public RockContribution(float[] weight, RockOperation operation, float factor, TimeWindow window)
    {
        this.weight = weight;
        this.operation = operation;
        this.factor = factor;
        this.window = window ?? TimeWindow.WholeRun;
    }
}

/// <summary>
/// The order of operations for the conditions (FEATURE-REGIONS.md 2.1): for every condition, first combine every region's
/// contribution by the condition's own rule, then apply the limits and guarantees last, over the whole map. A guarantee
/// therefore always holds, however many contributions were added before it. Game-free.
/// </summary>
public static class ConditionCombine
{
    /// <summary>
    /// Uplift at a share <paramref name="t"/> of the run, into <paramref name="into"/>: the background, plus every contribution
    /// in list order (uplift adds), then last the floor (so no ground sinks by itself), the grading toward base level (P1: uplift
    /// fades toward the sea over the coastal plain; null for none) and the caps (computed guarantees such as the stream-power
    /// cap along a world river, U &lt;= K A^m S; null or empty for none).
    /// </summary>
    public static void Uplift(float[] background, IList<UpliftContribution> contributions, float t, float floor, float[] grade,
        IList<float[]> caps, float[] into)
    {
        for (int i = 0; i < background.Length; i++)
        {
            float u = background[i];
            for (int c = 0; c < contributions.Count; c++)
            {
                UpliftContribution add = contributions[c];
                u += add.window.IsWholeRun ? add.values[i] : add.values[i] * add.window.Weight(t);
            }
            u = Mathf.Max(floor, u);
            if (grade != null)
                u *= grade[i];
            if (caps != null)
            {
                for (int k = 0; k < caps.Count; k++)
                    u = Mathf.Min(u, caps[k][i]);
            }
            into[i] = u;
        }
    }

    /// <summary>
    /// Erodibility of the rock arrangement at a share <paramref name="t"/> of the run, into <paramref name="into"/> (1 = the
    /// beds as <see cref="RockStrata"/> lays them): contributions apply in time order, oldest first (their window's start),
    /// keeping their list order where equally old.
    /// </summary>
    public static void Rock(IList<RockContribution> contributions, float t, float[] into)
    {
        List<RockContribution> ordered = InTimeOrder(contributions);
        for (int i = 0; i < into.Length; i++)
        {
            float k = 1f;
            for (int c = 0; c < ordered.Count; c++)
            {
                RockContribution rock = ordered[c];
                float w = rock.window.IsWholeRun ? rock.weight[i] : rock.weight[i] * rock.window.Weight(t);
                k = Apply(k, rock.operation, rock.factor, w);
            }
            into[i] = k;
        }
    }

    /// <summary>One rock change at one cell, at weight <paramref name="w"/>.</summary>
    public static float Apply(float k, RockOperation operation, float factor, float w) =>
        operation == RockOperation.Intrude ? Mathf.Lerp(k, factor, w) : k * Mathf.Lerp(1f, factor, w);

    /// <summary>The contributions oldest first; equally old ones keep their order (a stable sort).</summary>
    public static List<RockContribution> InTimeOrder(IList<RockContribution> contributions)
    {
        var ordered = new List<RockContribution>(contributions);
        // Insertion sort: stable, and the lists are short.
        for (int a = 1; a < ordered.Count; a++)
        {
            RockContribution item = ordered[a];
            int b = a - 1;
            while (b >= 0 && ordered[b].window.start > item.window.start)
            {
                ordered[b + 1] = ordered[b];
                b--;
            }
            ordered[b + 1] = item;
        }
        return ordered;
    }

    /// <summary>True when any contribution acts for part of the run only, so the conditions change during it.</summary>
    public static bool ChangesDuringRun(IList<UpliftContribution> uplift, IList<RockContribution> rock)
    {
        foreach (UpliftContribution u in uplift)
        {
            if (!u.window.IsWholeRun)
                return true;
        }
        foreach (RockContribution r in rock)
        {
            if (!r.window.IsWholeRun)
                return true;
        }
        return false;
    }
}
