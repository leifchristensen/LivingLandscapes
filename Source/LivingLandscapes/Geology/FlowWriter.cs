using System.Collections.Generic;
using LivingLandscapes.Environment;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// Writes the flow of our streams (LL_WaterFlow) into vanilla's river flow map, so their moving water animates,
/// turns watermills and floats items downstream. Direction is the smoothed downhill direction of the landform, so it
/// follows the valley rather than hummocks. Vanilla rebuilds the flow map for its own river after fog, filling only
/// cells near the river's segments, so this runs again then (<see cref="Patches.Patch_TileMutatorWorker_River_GeneratePostFog"/>)
/// and only fills cells vanilla left empty.
/// </summary>
public static class FlowWriter
{
    /// <summary>Smoothing (cells) of the landform whose downhill direction sets the flow.</summary>
    private const float FlowSmoothing = 3f;

    public static void Apply(Map map)
    {
        var environment = map.GetComponent<MapComponent_Environment>();
        if (environment == null
            || !environment.TryGetGrid(EnvironmentLayerDefOf.LL_WaterFlow, out EnvironmentGrid flowGrid)
            || !environment.TryGetGrid(EnvironmentLayerDefOf.LL_Landform, out EnvironmentGrid landformGrid))
            return;
        int sizeX = map.Size.x, sizeZ = map.Size.z, n = sizeX * sizeZ;
        float[] speed = flowGrid.ToArray();

        var ones = new float[n];
        for (int i = 0; i < n; i++)
            ones[i] = 1f;
        float[] aspect = GridMath.Aspect(GridMath.MaskedBlur(landformGrid.ToArray(), ones, sizeX, sizeZ, FlowSmoothing), sizeX, sizeZ);

        List<float> flow = map.waterInfo.riverFlowMap;
        if (flow == null || flow.Count != n * 2)
        {
            flow = new List<float>(n * 2);
            for (int k = 0; k < n * 2; k++)
                flow.Add(0f);
            map.waterInfo.riverFlowMap = flow;
        }

        int written = 0;
        for (int i = 0; i < n; i++)
        {
            if (speed[i] <= 0f)
                continue;
            IntVec3 c = map.cellIndices.IndexToCell(i);
            if (!c.GetTerrain(map).IsRiver)
                continue;
            // Vanilla's flow map is indexed x * sizeZ + z.
            int f = (c.x * sizeZ + c.z) * 2;
            bool empty = float.IsNaN(flow[f]) || float.IsNaN(flow[f + 1]) || (flow[f] == 0f && flow[f + 1] == 0f);
            if (!empty)
                continue;
            flow[f] = Mathf.Cos(aspect[i]) * speed[i];
            flow[f + 1] = Mathf.Sin(aspect[i]) * speed[i];
            written++;
        }
        if (written > 0)
            Log.Message($"[LivingLandscapes] Stream flow written for {written} moving water cells on {map}.");
    }
}
