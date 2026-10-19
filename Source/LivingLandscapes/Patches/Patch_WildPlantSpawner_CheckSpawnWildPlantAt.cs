using HarmonyLib;
using LivingLandscapes.Flora;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Carrying capacity (Docs/FLORA-REVIEW.md, 3.4): a spawn attempt goes ahead with a chance set by how well the biome's
/// plants do on the cell, so ground that suits many species fills more densely and hostile ground thins out. The target
/// total stays vanilla's: while the map is under it the spawner simply keeps trying other cells.
/// </summary>
[HarmonyPatch(typeof(WildPlantSpawner), nameof(WildPlantSpawner.CheckSpawnWildPlantAt))]
public static class Patch_WildPlantSpawner_CheckSpawnWildPlantAt
{
    private static readonly AccessTools.FieldRef<WildPlantSpawner, Map> MapOf = AccessTools.FieldRefAccess<WildPlantSpawner, Map>("map");

    public static bool Prefix(WildPlantSpawner __instance, IntVec3 c, ref bool __result)
    {
        Map map = MapOf(__instance);
        var ecology = map.GetComponent<MapComponent_Ecology>();
        // Cave plants (under a natural roof) follow vanilla's own rules.
        if (ecology == null || !ecology.Ready || (c.GetRoof(map)?.isNatural ?? false))
            return true;
        if (Rand.Chance(ecology.Acceptance(c)))
            return true;
        __result = false;
        return false;
    }
}
