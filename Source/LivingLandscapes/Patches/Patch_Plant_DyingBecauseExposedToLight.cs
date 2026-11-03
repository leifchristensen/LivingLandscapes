using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LivingLandscapes.Flora;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Forest fungi (E5, P12): vanilla kills a plant that dies in light (diesToLight) at any ground glow above 0. A species
/// can name the glow it tolerates in its habitat XML (<see cref="PlantHabitat.lightLimit"/>): it lives while vanilla's
/// ground glow (canopy-dimmed, <see cref="Patch_GlowGrid_GroundGlowAt"/>) is at most that, and dies past it at vanilla's
/// rate. Without a limit (0) vanilla's rule is unchanged. Applies to vanilla's getter and to every override (mods'
/// mushroom classes); it only ever turns "dying" into "not dying".
/// </summary>
[HarmonyPatch]
public static class Patch_Plant_DyingBecauseExposedToLight
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (System.Type type in AccessTools.AllTypes().Where(t => typeof(Plant).IsAssignableFrom(t)))
        {
            PropertyInfo property = AccessTools.DeclaredProperty(type, nameof(Plant.DyingBecauseExposedToLight));
            if (property?.GetMethod != null && !property.GetMethod.IsAbstract)
                yield return property.GetMethod;
        }
    }

    public static void Postfix(Plant __instance, ref bool __result)
    {
        if (!__result || !__instance.Spawned)
            return;
        float limit = PlantHabitat.For(__instance.def).lightLimit;
        if (limit > 0f && __instance.Map.glowGrid.GroundGlowAt(__instance.Position, ignoreCavePlants: true) <= limit)
            __result = false;
    }
}
