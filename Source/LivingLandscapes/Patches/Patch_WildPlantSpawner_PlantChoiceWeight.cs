using HarmonyLib;
using LivingLandscapes.Flora;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// The species choice (Docs/FLORA-REVIEW.md, 3.1 to 3.3). The weight vanilla computed (commonality, share bias, local
/// balance, its own cluster factor) is changed in three ways:
/// 1. Share bias: vanilla pulls each species toward its commonality with a steep curve of its current share. Species the
///    ground cannot host here (fertility-limited trees, water plants) leave their share unfilled, so every other species
///    runs "over target" and is wrongly suppressed. The bias is re-evaluated against a target adjusted for that
///    (<see cref="MapComponent_Ecology.ShareScale"/>).
/// 2. Fitness: the weight is multiplied by the species' fitness on the cell.
/// 3. Clusters: vanilla multiplies a clustering species by about 1 / (pi r^2) away from its own kind, but only while its
///    share is under 110%; above that it applies nothing, so late plants land at random and clumps stop forming. Here the
///    "away from its kind" suppression applies at all times, and weight rises with the Gaussian-weighted crowding of its
///    own kind (and at a second, larger scale for species that set one), up to a capped ratio so the share bias can still
///    balance the mix.
/// </summary>
[HarmonyPatch(typeof(WildPlantSpawner), "PlantChoiceWeight")]
public static class Patch_WildPlantSpawner_PlantChoiceWeight
{
    private static readonly AccessTools.FieldRef<WildPlantSpawner, Map> MapOf = AccessTools.FieldRefAccess<WildPlantSpawner, Map>("map");

    private static readonly SimpleCurve ShareBias =
        (SimpleCurve)AccessTools.Field(typeof(WildPlantSpawner), "GlobalPctSelectionWeightBias").GetValue(null);

    /// <summary>Vanilla stops applying its cluster factor above this share of the target.</summary>
    private const float VanillaClusterCutoff = 1.1f;

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

        // 1. Share bias against the adjusted target.
        float raw = RawShare(map, __instance, plantDef);
        float scale = ecology.ShareScale(__instance);
        if (raw >= 0f && scale > 1.01f)
            __result *= Mathf.Pow(ShareBias.Evaluate(raw / scale) / Mathf.Max(ShareBias.Evaluate(raw), 0.01f), strength);

        // 2. Fitness.
        __result *= Mathf.Lerp(1f, Mathf.Pow(ecology.Fitness(plantDef, index), tuning.fitnessExponent), strength);

        // 3. Clusters.
        PlantHabitat habitat = PlantHabitat.For(plantDef);
        if (habitat.ClusterSigma(plantDef) <= 0f)
            return;
        float radius = plantDef.plant.wildClusterRadius;
        // Make "away from its own kind" the same at every share: vanilla applied about 1 / (pi r^2) only under 110%.
        if (raw >= VanillaClusterCutoff)
            __result *= Mathf.Pow(1f / (Mathf.PI * radius * radius), strength);

        __result *= ecology.ClusterPull(plantDef, index);
    }

    /// <summary>The species' share of the map's plants over its target share (vanilla's own measure); -1 while there are no plants.</summary>
    private static float RawShare(Map map, WildPlantSpawner spawner, ThingDef plantDef)
    {
        int total = map.listerThings.ThingsInGroup(ThingRequestGroup.NonStumpPlant).Count;
        float target = spawner.GetCommonalityPctOfPlant(plantDef);
        if (total <= 0 || target <= 0f)
            return -1f;
        return map.listerThings.ThingsOfDef(plantDef).Count / (float)total / target;
    }
}
