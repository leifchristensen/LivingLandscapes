using LivingLandscapes.Environment;
using LivingLandscapes.Geology;

namespace LivingLandscapes.Layers;

/// <summary>
/// Slope aspect: the direction water runs off each cell, in radians counter-clockwise from east (0 = east, π/2 = north).
/// Together with <see cref="SlopeWorker"/> (the magnitude) it forms the slope vector field used for runoff, scree,
/// cliff facing and sun exposure.
/// </summary>
public class AspectWorker : EnvironmentLayerWorker
{
    public override GenerationStage StageFor(Verse.Map map) => EnvironmentLayerDefOf.LL_Height.Worker.StageFor(map);

    public override float[] Compute(EnvironmentContext context)
    {
        float[] height = context.Get(EnvironmentLayerDefOf.LL_Height);
        return height == null ? null : GridMath.Aspect(height, context.SizeX, context.SizeZ);
    }
}
