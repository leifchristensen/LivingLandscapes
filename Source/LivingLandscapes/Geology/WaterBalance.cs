using System.Collections.Generic;
using LivingLandscapes.Environment;
using LivingLandscapes.Layers;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// Water balance of a closed hollow (P23, W6): its lake grows until evaporation from its surface takes the runoff its
/// catchment collects, A_lake = runoff x rain x catchment / (evaporation - rain), up to the hollow's spill level. One
/// balance for the erosion model (closed basins keep their water and stop eroding, #99) and the map's lakes.
/// </summary>
public static class WaterBalance
{
    /// <summary>
    /// Lake area per unit of catchment, runoff x rain / (evaporation - rain), on a tile (the runoff and evaporation of
    /// <see cref="SurfaceWaterExtension"/>); infinite where the rain beats evaporation and every hollow fills.
    /// </summary>
    public static float LakeShare(Map map, out float climate)
    {
        var p = EnvironmentLayerDefOf.LL_SurfaceWater.GetModExtension<SurfaceWaterExtension>() ?? new SurfaceWaterExtension();
        climate = (EnvironmentLayerDefOf.LL_Wetness.GetModExtension<WetnessExtension>() ?? new WetnessExtension()).ClimateOf(map);
        var channels = (EnvironmentLayerDefOf.LL_Landform.GetModExtension<GeologyGenerationExtension>() ?? new GeologyGenerationExtension()).terrainChannels;
        float runoff = p.runoffShare ?? channels.RunoffOf(climate, map.Biome?.defName);
        float rainfall = map.TileInfo?.rainfall ?? 0f;
        float temperature = map.TileInfo?.temperature ?? 10f;
        float evaporation = p.Evaporation(temperature);
        float netEvaporation = evaporation - rainfall;
        return netEvaporation <= 0f ? float.PositiveInfinity : runoff * rainfall / netEvaporation;
    }

    /// <summary>
    /// The level a hollow's water stands at: the ground of the cell where the lake reaches its balance area
    /// (<paramref name="catchment"/> x <paramref name="lakeShare"/>, in the same cells as the hollow), or the spill level
    /// when the hollow fills before then. <paramref name="cells"/> are the hollow's cells, sorted low to high.
    /// </summary>
    public static float LakeLevel(List<int> cells, float[] ground, float spill, float catchment, float lakeShare)
    {
        float balanceArea = catchment * lakeShare;
        return !(balanceArea < cells.Count) ? spill : ground[cells[Mathf.Clamp((int)balanceArea, 0, cells.Count - 1)]];
    }
}
