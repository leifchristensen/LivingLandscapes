using System;

namespace LivingLandscapes.Geology;

/// <summary>
/// The starting bed of a world river's antecedent course (issue #186, river-boundary plan W2). Pure, no game types, so unit tests link this file.
/// A river never flows uphill: the bed along the course only falls, however the ground rises and dips beside it.
/// </summary>
public static class RiverBed
{
    /// <summary>
    /// Fall per cell used when the regional gradient is zero or not a number: a river still runs downstream on flat ground.
    /// Height units of the erosion model (metres) per model cell.
    /// </summary>
    public const float MinFallPerCell = 0.001f;

    /// <summary>Fall of the bed per model cell: the regional slope (rise per metre) times the cell size, or <see cref="MinFallPerCell"/> if that is not positive.</summary>
    public static float FallPerCell(float regionalGradient, float metresPerCell)
    {
        float fall = regionalGradient * metresPerCell;
        return float.IsNaN(fall) || float.IsInfinity(fall) || fall < MinFallPerCell ? MinFallPerCell : fall;
    }

    /// <summary>
    /// bed(k) = min(bed(k-1), ground(k) - depth) - fall, in course order from upstream. Never rises, lies at least
    /// <paramref name="depth"/> + <paramref name="fall"/> below the ground at every cell, and falls at least <paramref name="fall"/> per cell.
    /// </summary>
    public static float[] Grade(float[] ground, float depth, float fall)
    {
        var bed = new float[ground.Length];
        float previous = float.PositiveInfinity;
        for (int k = 0; k < ground.Length; k++)
        {
            bed[k] = Math.Min(previous, ground[k] - depth) - fall;
            previous = bed[k];
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
