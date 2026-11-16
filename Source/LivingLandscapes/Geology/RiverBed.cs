using System;

namespace LivingLandscapes.Geology;

/// <summary>
/// The starting bed of a world river's antecedent course (issue #186, river-boundary plan W2). Pure, no game types, so unit tests link this file.
/// A river never flows uphill: the bed along the course only falls, however the ground rises and dips beside it.
/// </summary>
public static class RiverBed
{
    /// <summary>
    /// The bed along a course, in course order from upstream, ending at the course's outlet at <paramref name="baseLevel"/>.
    /// Never above the ground less <paramref name="depth"/> (it is entrenched), never rising, never below the base level the water
    /// leaves at (a bed below its own outlet is a pit), and falling evenly in the share of the way left to the outlet:
    /// bed(k) = base + (U(k) - base) x (left / total), U(k) = the running minimum of ground - depth, held at or above the base level.
    /// </summary>
    public static float[] Grade(float[] ground, float depth, float baseLevel)
    {
        int m = ground.Length;
        var bed = new float[m];
        float upper = float.PositiveInfinity;
        for (int k = 0; k < m; k++)
        {
            upper = Math.Min(upper, ground[k] - depth);
            float u = Math.Max(upper, baseLevel);
            float left = m > 1 ? (m - 1 - k) / (float)(m - 1) : 0f;
            bed[k] = baseLevel + (u - baseLevel) * left;
        }
        return bed;
    }
}

/// <summary>
/// The world rivers' courses as a prescribed flow path (issue #186, river-boundary plan W2b): a boundary condition of the erosion
/// model, not a starting guess. Pure, no game types.
/// </summary>
public static class PrescribedCourse
{
    /// <summary>The cells with a cell equal to its predecessor dropped.</summary>
    public static System.Collections.Generic.List<int> Dedupe(System.Collections.Generic.IEnumerable<int> cells)
    {
        var result = new System.Collections.Generic.List<int>();
        foreach (int c in cells)
        {
            if (result.Count == 0 || result[result.Count - 1] != c)
                result.Add(c);
        }
        return result;
    }

    /// <summary>
    /// The receiver each course cell is bound to: the next cell along its leg (-1 elsewhere). <paramref name="legs"/> are cell paths
    /// in flow order, the exit leg first: where legs share a cell, the leg listed first decides (the way toward the exit), so
    /// tributaries join the exit river by construction. A step that would close a loop is skipped.
    /// </summary>
    public static int[] PrescribedReceivers(int cellCount, System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<int>> legs)
    {
        var receiver = new int[cellCount];
        for (int i = 0; i < cellCount; i++)
            receiver[i] = -1;
        foreach (var leg in legs)
        {
            for (int k = 0; k + 1 < leg.Count; k++)
            {
                int a = leg[k], b = leg[k + 1];
                if (a == b || receiver[a] >= 0 || Reaches(receiver, b, a))
                    continue;
                receiver[a] = b;
            }
        }
        return receiver;
    }

    private static bool Reaches(int[] receiver, int from, int target)
    {
        for (int c = from, step = 0; c >= 0 && step <= receiver.Length; c = receiver[c], step++)
        {
            if (c == target)
                return true;
        }
        return false;
    }
}
