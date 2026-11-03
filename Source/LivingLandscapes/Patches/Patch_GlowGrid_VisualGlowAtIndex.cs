using HarmonyLib;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>The lighting overlay reads visual glow by cell index: same sky cover as <see cref="Patch_GlowGrid_VisualGlowAt"/>.</summary>
[HarmonyPatch(typeof(GlowGrid), nameof(GlowGrid.VisualGlowAt), typeof(int))]
public static class Patch_GlowGrid_VisualGlowAtIndex
{
    private static readonly AccessTools.FieldRef<GlowGrid, Map> MapOf = AccessTools.FieldRefAccess<GlowGrid, Map>("map");

    public static void Postfix(GlowGrid __instance, int index, ref Color32 __result)
    {
        Map map = MapOf(__instance);
        Patch_GlowGrid_VisualGlowAt.Cover(map, map.cellIndices.IndexToCell(index), ref __result);
    }
}
