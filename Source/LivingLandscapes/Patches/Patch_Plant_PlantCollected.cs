using HarmonyLib;
using LivingLandscapes.Flora;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Succession (P22, owner 2026-10-26): cutting down a tree resets its cell's stand to age 0, so cleared land fills with
/// pioneers (grass, brambles) unless it is kept clear.
/// </summary>
[HarmonyPatch(typeof(Plant), nameof(Plant.PlantCollected))]
public static class Patch_Plant_PlantCollected
{
    public static void Prefix(Plant __instance)
    {
        // Still spawned here, so Map and Position are valid.
        if (!__instance.Spawned || !__instance.def.plant.IsTree || !__instance.def.plant.HarvestDestroys)
            return;
        __instance.Map.GetComponent<MapComponent_StandAge>()?.Reset(__instance.Position);
    }
}
