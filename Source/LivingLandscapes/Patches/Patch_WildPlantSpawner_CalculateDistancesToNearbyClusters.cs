using System.Collections.Generic;
using HarmonyLib;
using LivingLandscapes.Flora;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Vanilla finds clusters by scanning every thing within twice the biome's largest cluster radius around each spawn
/// attempt (hundreds to thousands of cells) and takes the median distance to the species' plants. The ecology replaces
/// that with a precomputed density grid read in <see cref="Patch_WildPlantSpawner_PlantChoiceWeight"/>, so the scan is
/// skipped (its result left empty, which makes vanilla apply its "outside any cluster" weight).
/// </summary>
[HarmonyPatch(typeof(WildPlantSpawner), "CalculateDistancesToNearbyClusters")]
public static class Patch_WildPlantSpawner_CalculateDistancesToNearbyClusters
{
    private static readonly AccessTools.FieldRef<WildPlantSpawner, Map> MapOf = AccessTools.FieldRefAccess<WildPlantSpawner, Map>("map");
    private static readonly Dictionary<ThingDef, float> Distances =
        (Dictionary<ThingDef, float>)AccessTools.Field(typeof(WildPlantSpawner), "distanceSqToNearbyClusters").GetValue(null);

    public static bool Prefix(WildPlantSpawner __instance)
    {
        var ecology = MapOf(__instance).GetComponent<MapComponent_Ecology>();
        if (ecology == null || !ecology.Ready)
            return true;
        Distances.Clear();
        return false;
    }
}
