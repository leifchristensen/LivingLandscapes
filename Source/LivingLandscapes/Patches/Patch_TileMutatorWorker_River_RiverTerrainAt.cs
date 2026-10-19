using HarmonyLib;
using LivingLandscapes.Environment;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// A river whose channels the geology carved is already in the ground: its deep and shallow water come from the carved
/// cross-section through the surface water model and the ground rules. Vanilla's river painting (its own width, wobble
/// and depth noise) leaves those cells as they are.
/// </summary>
[HarmonyPatch(typeof(TileMutatorWorker_River), "RiverTerrainAt")]
public static class Patch_TileMutatorWorker_River_RiverTerrainAt
{
    public static bool Prefix(Map map, ref TerrainDef __result)
    {
        if (!HasCarvedRiver(map))
            return true;
        __result = null;
        return false;
    }

    public static bool HasCarvedRiver(Map map) =>
        map.GetComponent<MapComponent_Environment>()?.PlannedRiver?.WaterLevel != null;
}
