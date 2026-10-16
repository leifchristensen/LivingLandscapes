using System.Collections.Generic;
using LivingLandscapes.Environment;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// Turns standing water that is really a stream into moving water: still, shallow fresh water lying on a strong
/// drainage line, in a narrow body (no more than <see cref="GroundWriterExtension.streamMaxHalfWidth"/> cells from
/// its bank). Wide bodies on the same line stay still (lakes and ponds a stream passes through). Flow follows the
/// smoothed downhill direction and is written into vanilla's river flow map, so the water animates and floats items
/// downstream like a river.
/// </summary>
public static class StreamWriter
{
    /// <summary>Smoothing (cells) of the landform whose downhill direction sets the flow, so it follows the valley, not hummocks.</summary>
    private const float FlowSmoothing = 3f;

    public static void Apply(Map map, GroundWriterExtension p)
    {
        var environment = map.GetComponent<MapComponent_Environment>();
        if (environment == null
            || !environment.TryGetGrid(EnvironmentLayerDefOf.LL_DrainageArea, out EnvironmentGrid drainageGrid)
            || !environment.TryGetGrid(EnvironmentLayerDefOf.LL_Landform, out EnvironmentGrid landformGrid))
            return;
        int sizeX = map.Size.x, sizeZ = map.Size.z, n = sizeX * sizeZ;
        float[] drainage = drainageGrid.ToArray();

        bool IsStill(int i)
        {
            TerrainDef t = map.terrainGrid.TerrainAt(i);
            return t != null && t.IsWater && !t.IsRiver && !t.IsOcean && t.passability != Traversability.Impassable
                   && !t.defName.Contains("Marsh") && !t.defName.Contains("Deep");
        }

        float[] fromBank = GridMath.DistanceTo(i => !map.terrainGrid.TerrainAt(i).IsWater, sizeX, sizeZ);
        var stream = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (IsStill(i) && drainage[i] >= p.streamArea && fromBank[i] <= p.streamMaxHalfWidth)
                stream.Add(i);
        }
        if (stream.Count == 0)
            return;

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

        foreach (int i in stream)
        {
            IntVec3 c = map.cellIndices.IndexToCell(i);
            map.terrainGrid.SetTerrain(c, MapGenUtility.ShallowMovingWaterTerrainAt(c, map));
            // Vanilla's flow map is indexed x * sizeZ + z; speed grows with the area drained.
            float speed = Mathf.Lerp(0.4f, 1f, Mathf.InverseLerp(p.streamArea, p.streamArea + 1.5f, drainage[i]));
            int f = (c.x * sizeZ + c.z) * 2;
            flow[f] = Mathf.Cos(aspect[i]) * speed;
            flow[f + 1] = Mathf.Sin(aspect[i]) * speed;
        }
        Log.Message($"[LivingLandscapes] {stream.Count} cells of standing water on drainage lines became moving water on {map}.");
    }
}
