using HarmonyLib;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// With geological terrain on, water, marsh and mud come from the ground rules (planned ground cover), not from the
/// biome's patch-maker noise: planned cover replaces vanilla's choice, and wet ground vanilla's noise would put
/// elsewhere becomes the dry ground it would otherwise have chosen. Cells under natural rock keep vanilla's choice.
/// </summary>
[HarmonyPatch(typeof(MapGenUtility), nameof(MapGenUtility.GetNaturalTerrainAt))]
public static class Patch_MapGenUtility_GetNaturalTerrainAt
{
    public static void Postfix(IntVec3 cell, Map map, ref TerrainDef __result)
    {
        GroundCover[] cover = map.GetComponent<MapComponent_Environment>()?.PlannedCover;
        if (cover == null)
            return;
        Building edifice = cell.GetEdifice(map);
        if (edifice != null && edifice.def.Fillage == FillCategory.Full)
            return;
        GroundCover planned = cover[map.cellIndices.CellToIndex(cell)];
        if (planned != GroundCover.None)
            __result = GroundCovers.TerrainFor(planned, cell, map) ?? __result;
        else if (GroundCovers.IsWetGround(__result))
            __result = DryTerrainAt(cell, map);
    }

    /// <summary>The ground vanilla picks when no patch applies: gravel or rough stone by elevation class, else soil by fertility.</summary>
    private static TerrainDef DryTerrainAt(IntVec3 cell, Map map)
    {
        BiomeDef biome = map.BiomeAt(cell);
        float elevation = MapGenerator.Elevation[cell];
        if (elevation >= 0.61f)
            return GenStep_RocksFromGrid.RockDefAt(cell).building.naturalTerrain;
        if (elevation > 0.55f && !biome.noGravel)
            return biome.gravelTerrain ?? TerrainDefOf.Gravel;
        return TerrainThreshold.TerrainAtValue(biome.terrainsByFertility, MapGenerator.Fertility[cell]) ?? TerrainDefOf.Soil;
    }
}
