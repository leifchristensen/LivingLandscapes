using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// Translates the geology into vanilla's map-generation elevation grid. Vanilla does not read that grid as a height:
/// every consumer compares it with fixed thresholds, so it is a rock/exposure classifier.
/// <list type="bullet">
/// <item>&gt; 0.70 rock wall (GenStep_RocksFromGrid); &gt; 0.728 thin roof; &gt; 0.798 thick roof (overhead mountain)</item>
/// <item>0.61-0.70 rough stone floor, 0.55-0.61 gravel (MapGenUtility.TerrainFrom); soil by fertility below</item>
/// <item>rock chunks only below 0.55; caves and cave hives only inside rock; ruins and structures only at 0.70 or below</item>
/// </list>
/// Writing bedrock exposure into it makes all of those follow the geology: rock where bedrock is exposed, roofs by
/// depth into the rock mass, scree below rock faces, caves inside rock, open-ground structures on soil.
/// </summary>
public static class VanillaTerrainWriter
{
    private const float ThickRoof = 0.82f;
    private const float ThinRoof = 0.75f;
    private const float RockNoRoof = 0.71f;
    private const float RockFloor = 0.65f;
    private const float Scree = 0.58f;
    private const float Soil = 0.3f;

    public static void Write(Map map, float[] soilDepth, float[] height, float cellSize, SoilModelExtension p)
    {
        int sizeX = map.Size.x, sizeZ = map.Size.z;
        bool IsRock(int i) => soilDepth[i] < p.rockWallDepth;

        float[] depthIntoRock = GridMath.DistanceTo(i => !IsRock(i), sizeX, sizeZ);
        float[] distanceToRock = GridMath.DistanceTo(IsRock, sizeX, sizeZ);
        float[] slope = GridMath.Slope(height, sizeX, sizeZ);

        MapGenFloatGrid elevation = MapGenerator.Elevation;
        int rock = 0, floor = 0, scree = 0;
        for (int i = 0; i < soilDepth.Length; i++)
        {
            float value;
            if (IsRock(i))
            {
                float metresIn = depthIntoRock[i] * cellSize;
                value = metresIn >= p.thickRoofDepth ? ThickRoof : metresIn >= p.thinRoofDepth ? ThinRoof : RockNoRoof;
                rock++;
            }
            else if (soilDepth[i] < p.rockFloorDepth)
            {
                value = RockFloor;
                floor++;
            }
            else if (distanceToRock[i] * cellSize <= p.screeReach && slope[i] / cellSize >= p.screeMinSlope)
            {
                value = Scree;
                scree++;
            }
            else
            {
                value = Soil;
            }
            elevation[map.cellIndices.IndexToCell(i)] = value;
        }

        int n = soilDepth.Length;
        Log.Message($"[LivingLandscapes] Geological terrain written for {map}: rock {Percent(rock, n)}, rock floor {Percent(floor, n)}, scree {Percent(scree, n)}, soil {Percent(n - rock - floor - scree, n)}.");
    }

    private static string Percent(int count, int total) => $"{100f * count / Mathf.Max(total, 1):F0}%";
}
