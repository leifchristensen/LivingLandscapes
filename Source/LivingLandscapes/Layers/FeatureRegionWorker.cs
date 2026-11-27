using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>
/// Dev view (on demand): the kind of feature region drawn on each cell, as the index of its <see cref="FeatureTypeDef"/>;
/// -1 where no region acts. Which region is drawn, and how strongly, comes from <see cref="FeatureRegionWeightWorker"/>.
/// </summary>
public class FeatureRegionWorker : EnvironmentLayerWorker
{
    /// <summary>The value of a cell no region reaches.</summary>
    private const float NoRegion = -1f;

    public override bool CanCompute(Map map) => map.GetComponent<MapComponent_Environment>()?.PlannedRegions != null;

    public override float[] Compute(EnvironmentContext context)
    {
        PlannedRegions regions = context.Environment.PlannedRegions;
        float[] weight = context.Get(EnvironmentLayerDefOf.LL_FeatureRegionWeight);
        if (regions == null || weight == null)
            return null;
        var kinds = new float[context.CellCount];
        for (int i = 0; i < kinds.Length; i++)
        {
            PlannedRegion drawn = regions.Shown ?? (regions.Dominant[i] >= 0 ? regions.Table[regions.Dominant[i]] : null);
            kinds[i] = drawn != null && weight[i] > 0f ? drawn.region.type.index : NoRegion;
        }
        return kinds;
    }
}
