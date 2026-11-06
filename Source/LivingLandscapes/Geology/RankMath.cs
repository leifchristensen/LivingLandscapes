using System;

namespace LivingLandscapes.Geology;

/// <summary>Rank mapping of a score onto a reference distribution. Pure math, no game types, so it is unit-tested.</summary>
public static class RankMath
{
    /// <summary>
    /// Gives each cell the reference value of the same rank its score has (highest score, highest value). With
    /// <paramref name="topScale"/> other than 1, the reference values at or above <paramref name="topThreshold"/> are given to
    /// topScale times as many cells (the top ranks), and the values below it are spread over the rest, so the upper class
    /// (rich soil) covers topScale times its share while every other class keeps its order. The class is capped at all cells.
    /// </summary>
    public static float[] Map(float[] score, float[] reference, float topScale = 1f, float topThreshold = float.PositiveInfinity)
    {
        int n = score.Length;
        var order = new int[n];
        for (int i = 0; i < n; i++)
            order[i] = i;
        Array.Sort(order, (a, b) => score[a].CompareTo(score[b]));
        var sorted = (float[])reference.Clone();
        Array.Sort(sorted);

        int below = 0;                                // reference values under the threshold
        while (below < n && sorted[below] < topThreshold)
            below++;
        int top = n - below;
        int newTop = Math.Min(n, (int)Math.Round(top * topScale));
        int split = n - newTop;                       // ranks from here up take the upper class
        var result = new float[n];
        for (int k = 0; k < n; k++)
        {
            int source = k;
            if (topScale != 1f && top > 0 && below > 0)
                source = k < split
                    ? (split == 0 ? 0 : (int)((long)k * below / split))
                    : below + (int)((long)(k - split) * top / Math.Max(newTop, 1));
            result[order[k]] = sorted[Math.Min(source, n - 1)];
        }
        return result;
    }
}
