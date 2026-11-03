using HarmonyLib;
using LivingLandscapes.Flora;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Tree size (P12, P14): growth set directly on a standing plant (the succession cap at generation, the anima tree's gen
/// step, other mods) re-stamps it at its new size. Vanilla's own growth runs in the long tick (<see cref="Patch_Plant_TickLong"/>).
/// </summary>
[HarmonyPatch(typeof(Plant), nameof(Plant.Growth), MethodType.Setter)]
public static class Patch_Plant_Growth
{
    public static void Postfix(Plant __instance)
    {
        if (__instance.Spawned)
            __instance.Map.GetComponent<MapComponent_Ecology>()?.OnGrown(__instance, thin: false);
    }
}
