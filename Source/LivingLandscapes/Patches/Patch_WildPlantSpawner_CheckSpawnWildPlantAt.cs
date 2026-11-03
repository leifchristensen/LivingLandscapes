using HarmonyLib;
using LivingLandscapes.Flora;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Carrying capacity (Docs/FLORA-REVIEW.md, 3.4): a spawn attempt goes ahead with a chance set by how well the biome's
/// plants do on the cell, so ground that suits many species fills more densely and hostile ground thins out. The target
/// total stays vanilla's: while the map is under it the spawner simply keeps trying other cells. Map generation makes one
/// pass (vanilla's GenStep_Plants, with random growth), so the chance fills each place to its target in that pass; in play
/// attempts repeat and the local saturation (<see cref="Patch_WildPlantSpawner_SaturatedAt"/>) caps each place at the
/// same target, so the chance there is <see cref="PlantEcologyDef.runtimeAcceptance"/> of it.
/// </summary>
[HarmonyPatch(typeof(WildPlantSpawner), nameof(WildPlantSpawner.CheckSpawnWildPlantAt))]
public static class Patch_WildPlantSpawner_CheckSpawnWildPlantAt
{
    private static readonly AccessTools.FieldRef<WildPlantSpawner, Map> MapOf = AccessTools.FieldRefAccess<WildPlantSpawner, Map>("map");

    public static bool Prefix(WildPlantSpawner __instance, IntVec3 c, bool setRandomGrowth, ref bool __result)
    {
        Map map = MapOf(__instance);
        var ecology = map.GetComponent<MapComponent_Ecology>();
        // Cave plants (under a natural roof) follow vanilla's own rules.
        if (ecology == null || !ecology.Ready || (c.GetRoof(map)?.isNatural ?? false))
            return true;
        bool watched = DevTools.SpawnerTrace.Watching(c);
        if (watched)
            DevTools.SpawnerTrace.Attempts++;
        if (Rand.Chance(ecology.Acceptance(c, generation: setRandomGrowth)))
            return true;
        if (watched)
            DevTools.SpawnerTrace.Rejected++;
        __result = false;
        return false;
    }

    public static void Postfix(IntVec3 c, bool __result)
    {
        if (__result && DevTools.SpawnerTrace.Watching(c))
            DevTools.SpawnerTrace.Spawned++;
    }
}
