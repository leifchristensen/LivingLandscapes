using LivingLandscapes.Environment;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>
/// Vanilla's generator elevation (warped Perlin noise scaled by hilliness; rock above 0.7). The grid only exists
/// during map generation, so outside it this returns null and the stored copy is kept.
/// </summary>
public class ElevationWorker : EnvironmentLayerWorker
{
    public override float[] Compute(EnvironmentContext context)
    {
        // TryGetVar, not GetVar: GetVar would create an empty grid on maps whose generator never made one.
        if (!MapGenerator.TryGetVar("Elevation", out MapGenFloatGrid grid))
            return null;

        Map map = context.Map;
        var values = new float[context.CellCount];
        foreach (IntVec3 c in map.AllCells)
            values[map.cellIndices.CellToIndex(c)] = grid[c];
        return values;
    }
}
