using LivingLandscapes.Environment;
using LivingLandscapes.Gameplay;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>
/// Dev view (on demand) for slope movement: how many times longer than a level step the slowest step into each cell takes
/// for the slope alone, climbing in from any neighbour (<see cref="SlopeMovement.StepFactor"/>); 1 is level, 0 impassable.
/// Vanilla's own costs (terrain, plants, cliffs) are left out: the dev-mode debug inspector shows a cell's path cost.
/// </summary>
public class WalkEffortWorker : EnvironmentLayerWorker
{
    public override float[] Compute(EnvironmentContext context)
    {
        Map map = context.Map;
        if (context.Environment.GroundHeight == null)
            return null;
        var effort = new float[context.CellCount];
        for (int i = 0; i < effort.Length; i++)
        {
            IntVec3 c = map.cellIndices.IndexToCell(i);
            float worst = 1f;
            for (int d = 0; d < 8; d++)
            {
                IntVec3 from = c + GenAdj.AdjacentCells[d];
                if (from.InBounds(map))
                    worst = Mathf.Max(worst, SlopeMovement.StepFactor(map, from, c));
            }
            int vanilla = map.pathing.Normal.pathGrid.CalculatedCostAt(c, false, IntVec3.Invalid);
            effort[i] = vanilla >= 10000 ? 0f : worst;
        }
        return effort;
    }
}
