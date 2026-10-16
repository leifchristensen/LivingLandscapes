using LivingLandscapes.Environment;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>
/// Distance in cells to the nearest water or river cell (two-pass chamfer transform). Water is only read,
/// never placed. On maps without water every cell gets <see cref="NoWater"/>.
/// </summary>
public class WaterDistanceWorker : EnvironmentLayerWorker
{
    public const float NoWater = 999f;

    private const float Orthogonal = 1f;
    private const float Diagonal = 1.4142135f;

    public override float[] Compute(EnvironmentContext context)
    {
        Map map = context.Map;
        int sizeX = context.SizeX;
        int sizeZ = context.SizeZ;
        var dist = new float[context.CellCount];

        for (int i = 0; i < dist.Length; i++)
        {
            TerrainDef terrain = map.terrainGrid.TerrainAt(i);
            dist[i] = terrain != null && (terrain.IsWater || terrain.IsRiver) ? 0f : NoWater;
        }

        // Forward pass: neighbours to the west and south.
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                int i = z * sizeX + x;
                float best = dist[i];
                if (x > 0) best = Mathf.Min(best, dist[i - 1] + Orthogonal);
                if (z > 0)
                {
                    best = Mathf.Min(best, dist[i - sizeX] + Orthogonal);
                    if (x > 0) best = Mathf.Min(best, dist[i - sizeX - 1] + Diagonal);
                    if (x < sizeX - 1) best = Mathf.Min(best, dist[i - sizeX + 1] + Diagonal);
                }
                dist[i] = best;
            }
        }

        // Backward pass: neighbours to the east and north.
        for (int z = sizeZ - 1; z >= 0; z--)
        {
            for (int x = sizeX - 1; x >= 0; x--)
            {
                int i = z * sizeX + x;
                float best = dist[i];
                if (x < sizeX - 1) best = Mathf.Min(best, dist[i + 1] + Orthogonal);
                if (z < sizeZ - 1)
                {
                    best = Mathf.Min(best, dist[i + sizeX] + Orthogonal);
                    if (x < sizeX - 1) best = Mathf.Min(best, dist[i + sizeX + 1] + Diagonal);
                    if (x > 0) best = Mathf.Min(best, dist[i + sizeX - 1] + Diagonal);
                }
                dist[i] = Mathf.Min(best, NoWater);
            }
        }

        return dist;
    }
}
