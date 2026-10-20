using LivingLandscapes.Environment;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Gameplay;

/// <summary>
/// Tuning for walking on slopes, set on the LL_Height layer Def in XML. Grades are rise over run between neighbouring
/// cells of the walkable height (metres per cell).
/// </summary>
public class SlopeMovementExtension : DefModExtension
{
    /// <summary>Walking time multiplier by grade when climbing (0 = level).</summary>
    public SimpleCurve uphill = new SimpleCurve
    {
        new CurvePoint(0f, 1f), new CurvePoint(0.1f, 1.15f), new CurvePoint(0.3f, 1.6f), new CurvePoint(0.6f, 3f),
        new CurvePoint(1f, 6f), new CurvePoint(2f, 14f),
    };

    /// <summary>The same when descending (grade as a positive number): gentle descents cost nothing, steep ones some care.</summary>
    public SimpleCurve downhill = new SimpleCurve
    {
        new CurvePoint(0f, 1f), new CurvePoint(0.2f, 1f), new CurvePoint(0.5f, 1.3f), new CurvePoint(1f, 2f), new CurvePoint(2f, 4f),
    };

    /// <summary>
    /// Route planning: extra path cost (ticks) of a cell by its steepest grade to any neighbour, added to the path grid so
    /// pawns prefer gentle ground, passes and steps. The planner keeps one cost per cell, so it cannot tell up from down.
    /// </summary>
    public SimpleCurve planning = new SimpleCurve
    {
        new CurvePoint(0.1f, 0f), new CurvePoint(0.3f, 4f), new CurvePoint(0.6f, 15f), new CurvePoint(1f, 40f), new CurvePoint(2f, 120f),
    };

    /// <summary>Steps cut into a rise carry only this share of the slope's extra time.</summary>
    public float stepsShare = 0.25f;
}

/// <summary>
/// Pawns walk more slowly uphill than across or downhill, by the grade between the cell they leave and the one they enter
/// (LL_Height), and plan routes over gentle ground. Applies to maps made with Living Landscapes while the setting is on.
/// </summary>
public static class SlopeMovement
{
    private static SlopeMovementExtension tuning;

    public static SlopeMovementExtension Tuning =>
        tuning ??= EnvironmentLayerDefOf.LL_Height.GetModExtension<SlopeMovementExtension>() ?? new SlopeMovementExtension();

    public static bool Enabled => LivingLandscapesMod.Settings?.slopeMovement ?? true;

    /// <summary>Walking time multiplier for a step from <paramref name="from"/> into <paramref name="to"/>; 1 where the map has no height.</summary>
    public static float StepFactor(Map map, IntVec3 from, IntVec3 to)
    {
        float[] height = map.GetComponent<MapComponent_Environment>()?.WalkableHeight;
        if (height == null || !from.InBounds(map) || !to.InBounds(map))
            return 1f;
        float run = from.x != to.x && from.z != to.z ? 1.4142f : 1f;
        float grade = (height[map.cellIndices.CellToIndex(to)] - height[map.cellIndices.CellToIndex(from)]) / run;
        float factor = grade >= 0f ? Tuning.uphill.Evaluate(grade) : Tuning.downhill.Evaluate(-grade);
        if (to.GetEdifice(map)?.def.defName == "LL_CliffSteps")
            factor = 1f + (factor - 1f) * Tuning.stepsShare;
        return Mathf.Max(1f, factor);
    }

    /// <summary>Extra planning cost of a cell from its steepest grade to a neighbour (0 where the map has no height).</summary>
    public static int PlanningCost(Map map, IntVec3 c)
    {
        float[] height = map.GetComponent<MapComponent_Environment>()?.WalkableHeight;
        if (height == null)
            return 0;
        int i = map.cellIndices.CellToIndex(c);
        float steepest = 0f;
        for (int d = 0; d < 8; d++)
        {
            IntVec3 n = c + GenAdj.AdjacentCells[d];
            if (!n.InBounds(map))
                continue;
            float run = d >= 4 ? 1.4142f : 1f;
            steepest = Mathf.Max(steepest, Mathf.Abs(height[map.cellIndices.CellToIndex(n)] - height[i]) / run);
        }
        float cost = Tuning.planning.Evaluate(steepest);
        if (c.GetEdifice(map)?.def.defName == "LL_CliffSteps")
            cost *= Tuning.stepsShare;
        return Mathf.RoundToInt(cost);
    }
}
