using System.Collections.Generic;
using LivingLandscapes.Environment;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// Writes the flow of all moving water into vanilla's river flow map, so it animates, turns watermills and floats
/// items downstream. Vanilla stores one direction per cell (about 2 long on its own rivers) and the water shader
/// averages it with the neighbouring river cells. Cells of a planned channel take the channel's downstream direction
/// (the tangent of its centre line, as vanilla does for its river); other moving water takes the smoothed downhill
/// direction of the landform. Every moving-water cell is written, replacing whatever vanilla left: vanilla rebuilds
/// the map after fog for its own river segments only, so this runs again then
/// (<see cref="Patches.Patch_TileMutatorWorker_River_GeneratePostFog"/>).
/// </summary>
public static class FlowWriter
{
    /// <summary>Smoothing (cells) of the landform whose downhill direction sets the flow outside planned channels.</summary>
    private const float FlowSmoothing = 3f;

    /// <summary>
    /// Length of a flow vector. Vanilla samples its river line every 2 cells and stores the step between samples, so
    /// its flow vectors are about 2 long; the water shader's animation speed follows that length.
    /// </summary>
    private const float FlowLength = 2f;

    public static void Apply(Map map)
    {
        var environment = map.GetComponent<MapComponent_Environment>();
        if (environment == null || !environment.TryGetGrid(EnvironmentLayerDefOf.LL_Landform, out EnvironmentGrid landformGrid))
            return;
        int sizeX = map.Size.x, sizeZ = map.Size.z, n = sizeX * sizeZ;
        RiverSystem rivers = environment.PlannedRivers;

        float[] aspect = GridMath.Aspect(GridMath.Blur(landformGrid.ToArray(), sizeX, sizeZ, FlowSmoothing), sizeX, sizeZ);

        List<float> flow = map.waterInfo.riverFlowMap;
        if (flow == null || flow.Count != n * 2)
        {
            flow = new List<float>(n * 2);
            for (int k = 0; k < n * 2; k++)
                flow.Add(0f);
            map.waterInfo.riverFlowMap = flow;
        }

        int channel = 0, downhill = 0;
        for (int i = 0; i < n; i++)
        {
            IntVec3 c = map.cellIndices.IndexToCell(i);
            if (!c.GetTerrain(map).IsRiver)
                continue;
            Vector2 direction;
            if (rivers?.ChannelOf != null && rivers.ChannelOf[i] >= 0 && rivers.Flow[i] != Vector2.zero)
            {
                direction = rivers.Flow[i];
                channel++;
            }
            else
            {
                direction = new Vector2(Mathf.Cos(aspect[i]), Mathf.Sin(aspect[i]));
                downhill++;
            }
            // Vanilla's flow map is indexed x * sizeZ + z.
            int f = (c.x * sizeZ + c.z) * 2;
            flow[f] = direction.x * FlowLength;
            flow[f + 1] = direction.y * FlowLength;
        }
        Log.Message($"[LivingLandscapes] Flow written for {channel + downhill} moving water cells on {map} ({channel} along channels, {downhill} downhill).");
    }
}
