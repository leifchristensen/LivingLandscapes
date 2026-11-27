using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>
/// Dev view (on demand): how strongly the region drawn on each cell acts there, 0..1 as a share of that region's peak
/// weight. The region drawn is the strongest one on the cell, or the one chosen by the debug action "Feature regions:
/// show one region" (<see cref="PlannedRegions.Shown"/>). Only a freshly generated map has the regions.
/// </summary>
public class FeatureRegionWeightWorker : EnvironmentLayerWorker
{
    public override bool CanCompute(Map map) => map.GetComponent<MapComponent_Environment>()?.PlannedRegions != null;

    public override float[] Compute(EnvironmentContext context)
    {
        PlannedRegions regions = context.Environment.PlannedRegions;
        if (regions == null)
            return null;
        return regions.Shown == null ? (float[])regions.DominantWeight.Clone() : regions.RelativeWeights(regions.Shown);
    }
}
