using HarmonyLib;
using LivingLandscapes.Environment;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Delta land: where rivers built ground above the sea (<see cref="Geology.RiverSystem.DeltaLand"/>), the coast mutator leaves the
/// terrain the geology planned instead of painting sea or beach over it.
/// </summary>
[HarmonyPatch(typeof(TileMutatorWorker_Coast), "CoastTerrainAt")]
public static class Patch_TileMutatorWorker_Coast_CoastTerrainAt
{
    public static void Postfix(IntVec3 cell, Map map, ref TerrainDef __result)
    {
        if (__result == null)
            return;
        bool[] land = map.GetComponent<MapComponent_Environment>()?.PlannedRivers?.DeltaLand;
        if (land != null && cell.InBounds(map) && land[map.cellIndices.CellToIndex(cell)])
            __result = null;
    }
}
