using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>What a cell's terrain says about the ground beneath it, for inferring height.</summary>
public enum TerrainFeature : byte
{
    Land,
    /// <summary>Waterlogged ground (marsh, mud): sits low, near the water table.</summary>
    Wet,
    StillWater,
    DeepStillWater,
    River,
    Ocean,
    /// <summary>Natural rock wall: mountain.</summary>
    Rock,
}

/// <summary>
/// Classifies each cell from its terrain and edifice using vanilla tags, so modded terrains classify without
/// per-mod rules: "River", "Ocean", "Water", "WaterMarshy", "Mud" tags and natural-rock edifices.
/// </summary>
public static class TerrainFeatures
{
    public static TerrainFeature[] Classify(Map map)
    {
        int count = map.cellIndices.NumGridCells;
        var features = new TerrainFeature[count];
        for (int i = 0; i < count; i++)
        {
            IntVec3 c = map.cellIndices.IndexToCell(i);
            Building edifice = c.GetEdifice(map);
            if (edifice != null && edifice.def.building != null && edifice.def.building.isNaturalRock)
            {
                features[i] = TerrainFeature.Rock;
                continue;
            }
            features[i] = Classify(map.terrainGrid.TerrainAt(i));
        }
        return features;
    }

    public static TerrainFeature Classify(TerrainDef terrain)
    {
        if (terrain == null)
            return TerrainFeature.Land;
        if (terrain.IsRiver)
            return TerrainFeature.River;
        if (terrain.IsOcean)
            return TerrainFeature.Ocean;
        if (terrain.HasTag("WaterMarshy") || terrain.HasTag("Mud"))
            return TerrainFeature.Wet;
        if (terrain.IsWater)
            return terrain.passability == Traversability.Impassable ? TerrainFeature.DeepStillWater : TerrainFeature.StillWater;
        return TerrainFeature.Land;
    }

    public static bool IsWater(TerrainFeature f) =>
        f == TerrainFeature.StillWater || f == TerrainFeature.DeepStillWater || f == TerrainFeature.River || f == TerrainFeature.Ocean;

    /// <summary>Average river flow direction on the map (x, z), or zero if the map has no river.</summary>
    public static Vector2 MeanRiverFlow(Map map)
    {
        var flow = map.waterInfo?.riverFlowMap;
        if (flow == null || flow.Count == 0)
            return Vector2.zero;
        Vector2 sum = Vector2.zero;
        for (int i = 0; i + 1 < flow.Count; i += 2)
            sum += new Vector2(flow[i], flow[i + 1]);
        return sum.sqrMagnitude > 1e-8f ? sum.normalized : Vector2.zero;
    }
}
