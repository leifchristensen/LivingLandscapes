using HarmonyLib;
using LivingLandscapes.Flora;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>Keeps the ecology's cluster and shade grids current: a plant that goes (chopped, burnt, dead) is taken out of them.</summary>
[HarmonyPatch(typeof(Plant), nameof(Plant.DeSpawn))]
public static class Patch_Plant_DeSpawn
{
    public static void Prefix(Plant __instance)
    {
        // Still spawned here, so Map and Position are valid.
        __instance.Map?.GetComponent<MapComponent_Ecology>()?.OnDespawning(__instance);
        if (__instance.Spawned && DevTools.SpawnerTrace.Watching(__instance.Position))
            DevTools.SpawnerTrace.Deaths++;
    }
}
