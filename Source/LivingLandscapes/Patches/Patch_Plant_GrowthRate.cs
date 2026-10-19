using HarmonyLib;
using LivingLandscapes.Flora;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Vitality (Docs/FLORA-REVIEW.md, 3.4): a plant on ground that suits its species grows faster than one on marginal
/// ground, by the same fitness that placed it. Vanilla's own factors (fertility, temperature, light) still apply.
/// </summary>
[HarmonyPatch(typeof(Plant), nameof(Plant.GrowthRate), MethodType.Getter)]
public static class Patch_Plant_GrowthRate
{
    public static void Postfix(Plant __instance, ref float __result)
    {
        if (__result <= 0f || !__instance.Spawned)
            return;
        var ecology = __instance.Map.GetComponent<MapComponent_Ecology>();
        if (ecology != null && ecology.Ready)
            __result *= ecology.Vitality(__instance);
    }
}
