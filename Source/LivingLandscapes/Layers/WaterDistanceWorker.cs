using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>
/// Distance in cells to the nearest water or river cell (<see cref="WaterDefinition"/>). Water is only read,
/// never placed. On maps without water every cell gets <see cref="NoWater"/>.
/// </summary>
public class WaterDistanceWorker : EnvironmentLayerWorker
{
    public const float NoWater = WaterDefinition.NoWater;

    public override float[] Compute(EnvironmentContext context)
    {
        TerrainGrid terrains = context.Map.terrainGrid;
        return WaterDefinition.DistanceByTerrain(i => GroundCovers.IsWaterTerrain(terrains.TerrainAt(i)), context.SizeX, context.SizeZ);
    }
}
