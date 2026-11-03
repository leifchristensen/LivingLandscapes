using HarmonyLib;
using LivingLandscapes.Flora;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// One light map (P12, owner 2026-11-02): tree crowns shade the ground, so the sky's share of vanilla's ground glow on an
/// unroofed cell is multiplied by the canopy transmittance there; lamps nearby are not dimmed (they shine below the
/// crowns), so the cell keeps the brighter of the two. Every reader of vanilla's glow sees the result: plant growth and
/// light death, pawn darkness (work and move speed, combat, psycasts), the Lit % readout and the lighting overlay.
/// Runs last, so it scales whatever sky glow a lighting mod (latitude, clouds) has computed.
/// </summary>
[HarmonyPatch(typeof(GlowGrid), nameof(GlowGrid.GroundGlowAt))]
public static class Patch_GlowGrid_GroundGlowAt
{
    private static readonly AccessTools.FieldRef<GlowGrid, Map> MapOf = AccessTools.FieldRefAccess<GlowGrid, Map>("map");

    [HarmonyPriority(Priority.Last)]
    public static void Postfix(GlowGrid __instance, IntVec3 c, bool ignoreCavePlants, bool ignoreSky, ref float __result)
    {
        if (ignoreSky || __result <= 0f)
            return;
        Map map = MapOf(__instance);
        float transmittance = MapComponent_Ecology.TransmittanceAt(map, c);
        if (transmittance >= 1f || map.roofGrid.Roofed(c))
            return;
        float lamps = __instance.GroundGlowAt(c, ignoreCavePlants, ignoreSky: true);
        __result = Mathf.Max(lamps, __result * transmittance);
    }
}
