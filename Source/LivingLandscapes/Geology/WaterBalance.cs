using System.Collections.Generic;
using LivingLandscapes.Environment;
using LivingLandscapes.Layers;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// Water balance of a closed hollow (P23, W6; seepage through the wetted floor added by #149): its lake grows until
/// evaporation from its surface and seepage through its floor take the runoff its catchment collects,
/// A_lake = runoff x rain x catchment / (evaporation - rain + S), up to the hollow's spill level. The arithmetic is
/// <see cref="WaterBalanceMath.LakeShare"/>; this class reads the climate inputs of a map. One balance for the erosion
/// model (closed basins keep their water and stop eroding, #99) and the map's lakes.
/// </summary>
public static class WaterBalance
{
    /// <summary>
    /// Lake area per unit of catchment, runoff x rain / (evaporation - rain + S), on a tile (the runoff and evaporation of
    /// <see cref="SurfaceWaterExtension"/>); infinite where the losses beat the rain and every hollow fills. The floor's
    /// seepage S is 0 for the map-wide share; a hollow's own floor gives its own S (see <see cref="WaterBalanceMath"/>).
    /// </summary>
    public static float LakeShare(Map map, out float climate) => LakeShare(map, out climate, 0f);

    /// <summary>
    /// The same balance with the seepage of one hollow's floor: <paramref name="seepage"/> (S, mm/yr) is the effective
    /// seepage through the wetted floor of that hollow, so hollows on permeable floors stand below their spill level
    /// while hollows on clay or hardpan still fill to it.
    /// </summary>
    public static float LakeShare(Map map, out float climate, float seepage)
    {
        var p = EnvironmentLayerDefOf.LL_SurfaceWater.GetModExtension<SurfaceWaterExtension>() ?? new SurfaceWaterExtension();
        climate = (EnvironmentLayerDefOf.LL_Wetness.GetModExtension<WetnessExtension>() ?? new WetnessExtension()).ClimateOf(map);
        var channels = (EnvironmentLayerDefOf.LL_Landform.GetModExtension<GeologyGenerationExtension>() ?? new GeologyGenerationExtension()).terrainChannels;
        float runoff = p.runoffShare ?? channels.RunoffOf(climate, map.Biome?.defName);
        float rainfall = map.TileInfo?.rainfall ?? 0f;
        float temperature = map.TileInfo?.temperature ?? 10f;
        float evaporation = p.Evaporation(temperature);
        return WaterBalanceMath.LakeShare(runoff, rainfall, evaporation, seepage);
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
