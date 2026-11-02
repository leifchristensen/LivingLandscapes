using HarmonyLib;
using LivingLandscapes.Flora;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// One light map, drawn (P12): vanilla's lighting overlay reads the alpha of a cell's visual glow as sky cover, and a
/// roofed cell gets <see cref="RoofSkyCover"/>. Tree crowns cover the sky the same way, in proportion to the light they
/// block: alpha at least RoofSkyCover x (1 - transmittance), so a deep forest floor looks like the shade under a roof and
/// open ground is unchanged. The ground glow itself is <see cref="Patch_GlowGrid_GroundGlowAt"/>.
/// </summary>
[HarmonyPatch(typeof(GlowGrid), nameof(GlowGrid.VisualGlowAt), typeof(IntVec3))]
public static class Patch_GlowGrid_VisualGlowAt
{
    /// <summary>Vanilla's sky cover of a roofed cell (SectionLayer_LightingOverlay.RoofedAreaMinSkyCover).</summary>
    private const float RoofSkyCover = 100f;

    private static readonly AccessTools.FieldRef<GlowGrid, Map> MapOf = AccessTools.FieldRefAccess<GlowGrid, Map>("map");

    public static void Postfix(GlowGrid __instance, IntVec3 c, ref Color32 __result) => Cover(MapOf(__instance), c, ref __result);

    /// <summary>Raises the sky cover of an unroofed cell under tree crowns (the lighting overlay reads the int overload).</summary>
    public static void Cover(Map map, IntVec3 c, ref Color32 glow)
    {
        float transmittance = MapComponent_Ecology.TransmittanceAt(map, c);
        if (transmittance >= 1f || map.roofGrid.Roofed(c))
            return;
        byte cover = (byte)Mathf.RoundToInt(RoofSkyCover * (1f - transmittance));
        if (glow.a < cover)
            glow.a = cover;
    }
}
