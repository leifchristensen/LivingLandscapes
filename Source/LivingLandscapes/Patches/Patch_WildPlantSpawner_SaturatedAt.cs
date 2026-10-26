using HarmonyLib;
using LivingLandscapes.Flora;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Carrying capacity (P15): vanilla calls a cell saturated when the plants within 20 cells reach the biome's density x
/// fertility, which spreads plants evenly over the map. With the ecology on, the habitat's target occupancy decides
/// instead (<see cref="MapComponent_Ecology.Saturated"/>), so thickets, groves, meadows and clearings can form.
/// Cave plants keep vanilla's rule.
/// </summary>
[HarmonyPatch(typeof(WildPlantSpawner), "SaturatedAt")]
public static class Patch_WildPlantSpawner_SaturatedAt
{
    private static readonly AccessTools.FieldRef<WildPlantSpawner, Map> MapOf = AccessTools.FieldRefAccess<WildPlantSpawner, Map>("map");

    public static bool Prefix(WildPlantSpawner __instance, IntVec3 c, bool cavePlants, ref bool __result)
    {
        if (cavePlants)
            return true;
        var ecology = MapOf(__instance).GetComponent<MapComponent_Ecology>();
        if (ecology == null || !ecology.Ready)
            return true;
        __result = ecology.Saturated(c);
        return false;
    }
}
