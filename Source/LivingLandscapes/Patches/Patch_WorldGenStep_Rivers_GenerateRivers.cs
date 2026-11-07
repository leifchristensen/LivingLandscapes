using System;
using HarmonyLib;
using LivingLandscapes.Geology;
using RimWorld.Planet;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// The world watershed rule (<see cref="WorldWatershed"/>, #219) runs after the river step, whether vanilla's own or the one
/// Realistic Planets 2 replaces it with (it patches the same method), whatever the load order. The prefix, first in line,
/// notes the river defs' thresholds before Realistic Planets 2 scales them by its river density.
/// </summary>
[HarmonyPatch(typeof(WorldGenStep_Rivers), "GenerateRivers")]
public static class Patch_WorldGenStep_Rivers_GenerateRivers
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix() => WorldWatershed.RememberThresholds();

    public static void Postfix(PlanetLayer layer)
    {
        try
        {
            WorldWatershed.Apply(layer);
        }
        catch (Exception e)
        {
            Log.Warning($"[LivingLandscapes] Watershed rivers skipped: {e}");
        }
    }
}
