using UnityEngine;
using Verse;

namespace LivingLandscapes.Fields;

/// <summary>
/// Builds the MVP environment fields from vanilla data only: existing water, tile rainfall and the fertility grid.
/// Water is never placed here, only read. Heavier terms (height above drainage, wetness index) come later.
/// </summary>
public static class EnvironmentFieldComputer
{
    /// <summary>Distance (cells) over which water proximity falls to 1/e.</summary>
    private const float WaterReachCells = 10f;

    /// <summary>Rainfall (mm) treated as fully wet for the regional baseline.</summary>
    private const float WetRainfallMm = 2000f;

    /// <summary>Weight of the regional rainfall baseline against local water proximity.</summary>
    private const float RainfallWeight = 0.6f;

    /// <summary>Baseline used when the tile reports no rainfall (pocket maps, mod-made tiles).</summary>
    private const float UnknownRainfallBaseline = 0.4f;

    /// <summary>Fertility value mapped to a soil score of 1.</summary>
    private const float RichFertility = 1.2f;

    public static void Compute(Map map, EnvironmentFields target)
    {
        int sizeX = map.Size.x;
        int sizeZ = map.Size.z;
        int count = map.cellIndices.NumGridCells;

        var distance = DistanceToWater(map, sizeX, sizeZ, count);

        float rainfall = map.TileInfo?.rainfall ?? 0f;
        float baseline = rainfall > 0f ? Mathf.Clamp01(rainfall / WetRainfallMm) : UnknownRainfallBaseline;
        float dry = 1f - RainfallWeight * baseline;

        var moisture = new float[count];
        var soil = new float[count];
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                var c = new IntVec3(x, 0, z);
                int i = map.cellIndices.CellToIndex(c);
                float proximity = Mathf.Exp(-distance[i] / WaterReachCells);
                moisture[i] = 1f - dry * (1f - proximity);
                soil[i] = Mathf.Clamp01(map.fertilityGrid.FertilityAt(c) / RichFertility);
            }
        }

        target.Set(moisture, soil);
    }

    /// <summary>Two-pass chamfer distance (cells) to the nearest water or river cell. Large when the map has no water.</summary>
    private static float[] DistanceToWater(Map map, int sizeX, int sizeZ, int count)
    {
        const float Orthogonal = 1f;
        const float Diagonal = 1.4142135f;
        const float Far = 1e6f;

        var dist = new float[count];
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                int i = map.cellIndices.CellToIndex(x, z);
                TerrainDef terrain = map.terrainGrid.TerrainAt(i);
                dist[i] = terrain != null && (terrain.IsWater || terrain.IsRiver) ? 0f : Far;
            }
        }

        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                int i = map.cellIndices.CellToIndex(x, z);
                float best = dist[i];
                if (x > 0) best = Mathf.Min(best, dist[map.cellIndices.CellToIndex(x - 1, z)] + Orthogonal);
                if (z > 0)
                {
                    best = Mathf.Min(best, dist[map.cellIndices.CellToIndex(x, z - 1)] + Orthogonal);
                    if (x > 0) best = Mathf.Min(best, dist[map.cellIndices.CellToIndex(x - 1, z - 1)] + Diagonal);
                    if (x < sizeX - 1) best = Mathf.Min(best, dist[map.cellIndices.CellToIndex(x + 1, z - 1)] + Diagonal);
                }
                dist[i] = best;
            }
        }

        for (int z = sizeZ - 1; z >= 0; z--)
        {
            for (int x = sizeX - 1; x >= 0; x--)
            {
                int i = map.cellIndices.CellToIndex(x, z);
                float best = dist[i];
                if (x < sizeX - 1) best = Mathf.Min(best, dist[map.cellIndices.CellToIndex(x + 1, z)] + Orthogonal);
                if (z < sizeZ - 1)
                {
                    best = Mathf.Min(best, dist[map.cellIndices.CellToIndex(x, z + 1)] + Orthogonal);
                    if (x < sizeX - 1) best = Mathf.Min(best, dist[map.cellIndices.CellToIndex(x + 1, z + 1)] + Diagonal);
                    if (x > 0) best = Mathf.Min(best, dist[map.cellIndices.CellToIndex(x - 1, z + 1)] + Diagonal);
                }
                dist[i] = best;
            }
        }

        return dist;
    }
}
