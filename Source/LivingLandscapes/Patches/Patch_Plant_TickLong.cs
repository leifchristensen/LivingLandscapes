using HarmonyLib;
using LivingLandscapes.Flora;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Tree size (P12, P14): vanilla grows a plant in its long tick; a tree that reached a new size step is re-stamped, so a
/// sapling's shade and crowding grow with it, and crowded trees thin out (P14).
/// </summary>
[HarmonyPatch(typeof(Plant), nameof(Plant.TickLong))]
public static class Patch_Plant_TickLong
{
    public static void Postfix(Plant __instance)
    {
        if (!__instance.Spawned)
            return;
        __instance.Map.GetComponent<MapComponent_Ecology>()?.OnGrown(__instance, thin: true);
    }
}
