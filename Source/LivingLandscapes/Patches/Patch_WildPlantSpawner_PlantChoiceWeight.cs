using HarmonyLib;
using LivingLandscapes.Flora;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Vitality of the choice and clusters (Docs/FLORA-REVIEW.md, 3.1 and 3.2): the weight vanilla computed (commonality,
/// share bias, local balance) is multiplied by the species' fitness on the cell, and for clustering species by a boost
/// that grows with how many of its own kind stand nearby (Gaussian-weighted), up to vanilla's own inside/outside ratio.
/// Vanilla's share bias still steers each species toward its commonality, so the map's species mix stays on target.
/// </summary>
[HarmonyPatch(typeof(WildPlantSpawner), "PlantChoiceWeight")]
public static class Patch_WildPlantSpawner_PlantChoiceWeight
{
    private static readonly AccessTools.FieldRef<WildPlantSpawner, Map> MapOf = AccessTools.FieldRefAccess<WildPlantSpawner, Map>("map");

    public static void Postfix(WildPlantSpawner __instance, ref float __result, ThingDef plantDef, IntVec3 c)
    {
        if (__result <= 0f)
            return;
        Map map = MapOf(__instance);
        var ecology = map.GetComponent<MapComponent_Ecology>();
        if (ecology == null || !ecology.Ready)
            return;
        float strength = MapComponent_Ecology.Strength;
        PlantEcologyDef tuning = PlantEcologyDef.Current;
        int index = map.cellIndices.CellToIndex(c);

        float fitness = ecology.Fitness(plantDef, index);
        __result *= Mathf.Lerp(1f, Mathf.Pow(fitness, tuning.fitnessExponent), strength);

        PlantHabitat habitat = PlantHabitat.For(plantDef);
        if (habitat.ClusterSigma(plantDef) <= 0f)
            return;
        float throttle = ShareThrottle(map, __instance, ecology, plantDef, tuning);
        if (throttle <= 0f)
            return;
        // Neighbours of the same kind near this cell pull the weight from "outside a cluster" toward "inside one".
        float crowding = ecology.ClusterDensity(plantDef, index) * tuning.clusterGainPerNeighbour;
        float ratio = Mathf.Lerp(1f, habitat.ClusterRatio(plantDef), strength * throttle);
        __result *= 1f + (ratio - 1f) * (1f - Mathf.Exp(-crowding));
    }

    /// <summary>
    /// Vanilla clusters a species only while its share of the map's plants is under 110% of its target. Species that
    /// cannot be placed (fertility-limited trees, water plants) leave a surplus, which lifts every ordinary species over
    /// 110% before the map fills and switches clustering off for most of the pass. Here the share is measured against a
    /// target adjusted for that surplus (<see cref="MapComponent_Ecology.ShareScale"/>), and clustering fades out between
    /// the def's start and end ratios (default 100% to 130%), so clumps start until a species has its fair share.
    /// </summary>
    private static float ShareThrottle(Map map, WildPlantSpawner spawner, MapComponent_Ecology ecology, ThingDef plantDef, PlantEcologyDef tuning)
    {
        int total = map.listerThings.ThingsInGroup(ThingRequestGroup.NonStumpPlant).Count;
        float target = spawner.GetCommonalityPctOfPlant(plantDef);
        if (total <= 0 || target <= 0f)
            return 1f;
        // Against the target adjusted for species the ground cannot host here.
        float share = map.listerThings.ThingsOfDef(plantDef).Count / (float)total / target / ecology.ShareScale(spawner);
        return 1f - Mathf.InverseLerp(tuning.clusterShareFadeStart, tuning.clusterShareFadeEnd, share);
    }
}
