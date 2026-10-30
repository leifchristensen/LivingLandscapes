using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LivingLandscapes.Flora;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Forest fungi (E5, P12): vanilla's ground glow ignores tree crowns, so a plant that dies in light (diesToLight) dies on
/// any forest floor by day. On an unroofed cell it sees the glow dimmed by the canopy above it (vanilla glow x the
/// ecology's transmittance) and lives while that is at most <see cref="PlantEcologyDef.canopyDarkGlow"/>. Applies to
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
        IntVec3 c = __instance.Position;
        float glow = __instance.Map.glowGrid.GroundGlowAt(c, ignoreCavePlants: true) * ecology.TransmittanceAt(__instance.Map.cellIndices.CellToIndex(c));
        if (glow <= PlantEcologyDef.Current.canopyDarkGlow)
            __result = false;
    }
}
