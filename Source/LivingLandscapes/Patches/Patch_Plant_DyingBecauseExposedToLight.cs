using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LivingLandscapes.Flora;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Forest fungi (E5, P11 x P12): vanilla's ground glow ignores tree crowns, so a plant that dies in light (diesToLight) dies
/// on any forest floor by day. On an unroofed cell it sees the light the habitat placed it by: the sky's glow x the ground
/// light (LL_Sun x canopy transmittance), or the glow of lamps nearby (not dimmed) if brighter. It lives while its own
/// two-sided light curve still answers that light with at least <see cref="PlantEcologyDef.lightDeathResponse"/>; a plant
/// without one (a saturating curve) keeps vanilla's rule. Applies to
/// vanilla's getter and to every override (mods' mushroom classes); it only ever turns "dying" into "not dying", so a
/// plant's own rules stay as strict as they were wherever the canopy is open. Under a roof vanilla decides.
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
        if (!__result || !__instance.Spawned || __instance.Position.Roofed(__instance.Map))
            return;
        var ecology = __instance.Map.GetComponent<MapComponent_Ecology>();
        if (ecology == null || !ecology.Ready)
            return;
        HabitatCurve curve = PlantHabitat.For(__instance.def).light;
        if (curve == null || curve.saturating)
            return;
        if (curve.Response(LightAtFeet(__instance, ecology)) >= PlantEcologyDef.Current.lightDeathResponse)
            __result = false;
    }

    /// <summary>The light a plant that dies in light sees on an unroofed cell, on its light curve's scale.</summary>
    public static float LightAtFeet(Plant plant, MapComponent_Ecology ecology)
    {
        Map map = plant.Map;
        IntVec3 c = plant.Position;
        float sky = map.skyManager.CurSkyGlow * ecology.LightAt(map.cellIndices.CellToIndex(c));
        return Mathf.Max(sky, map.glowGrid.GroundGlowAt(c, ignoreCavePlants: true, ignoreSky: true));
    }
}
