using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Development aid, inactive without "-ll-camp": makes the player's start settlement generate its map the way vanilla generates
/// a caravan camp, with the Encounter generator instead of Base_Player (#209). Vanilla gives a camp that generator and a 200 x 200
/// map (the Camp def's overrideMapSize), so replaying a camped tile needs "-ll-camp -ll-map-size=200".
/// </summary>
[HarmonyPatch(typeof(Settlement), nameof(Settlement.MapGeneratorDef), MethodType.Getter)]
public static class Patch_Settlement_CampGenerator
{
    public static void Postfix(ref MapGeneratorDef __result)
    {
        if (GenCommandLine.CommandLineArgPassed("ll-camp"))
            __result = MapGeneratorDefOf.Encounter;
    }
}
