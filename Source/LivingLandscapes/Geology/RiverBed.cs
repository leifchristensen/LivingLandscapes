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
