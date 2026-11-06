using HarmonyLib;
using LivingLandscapes.Flora;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// One light map, drawn (P12): vanilla's lighting overlay reads the alpha of a cell's visual glow as sky cover, the
/// share of sky light blocked (255 = all; vanilla gives a roofed cell at least 100, Celestial Lighting up to 255 by its
/// own occlusion and keeps the larger value). Tree crowns block 1 - transmittance of it, the same share the ground glow
/// loses (<see cref="Patch_GlowGrid_GroundGlowAt"/>): alpha at least 255 x (1 - transmittance) x visualShadeScale (drawing only: ground light keeps the full share). Open ground unchanged.
/// </summary>
[HarmonyPatch(typeof(GlowGrid), nameof(GlowGrid.VisualGlowAt), typeof(IntVec3))]
public static class Patch_GlowGrid_VisualGlowAt
{
    /// <summary>Sky cover of a cell that blocks all sky light.</summary>
    private const float FullSkyCover = 255f;

    private static readonly AccessTools.FieldRef<GlowGrid, Map> MapOf = AccessTools.FieldRefAccess<GlowGrid, Map>("map");

    public static void Postfix(GlowGrid __instance, IntVec3 c, ref Color32 __result) => Cover(MapOf(__instance), c, ref __result);

    /// <summary>Raises the sky cover of an unroofed cell under tree crowns (the lighting overlay reads the int overload).</summary>
    public static void Cover(Map map, IntVec3 c, ref Color32 glow)
    {
        float transmittance = MapComponent_Ecology.TransmittanceAt(map, c);
        if (transmittance >= 1f || map.roofGrid.Roofed(c))
            return;
        byte cover = (byte)Mathf.RoundToInt(FullSkyCover * (1f - transmittance) * PlantEcologyDef.Current.visualShadeScale);
        if (glow.a < cover)
            glow.a = cover;
    }
}
