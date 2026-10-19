using System.Collections.Generic;
using HarmonyLib;
using LivingLandscapes.Flora;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Viability (Docs/FLORA-REVIEW.md, 3.1): species the ground does not suit are not candidates on a cell. Vanilla still
/// decides what can grow at all (terrain, fertility, strata order); this only removes the poor fits from its list.
/// </summary>
[HarmonyPatch(typeof(WildPlantSpawner), "CalculatePlantsWhichCanGrowAt")]
public static class Patch_WildPlantSpawner_CalculatePlantsWhichCanGrowAt
{
    private static readonly AccessTools.FieldRef<WildPlantSpawner, Map> MapOf = AccessTools.FieldRefAccess<WildPlantSpawner, Map>("map");

    public static void Postfix(WildPlantSpawner __instance, IntVec3 c, List<ThingDef> outPlants, bool cavePlants)
    {
        if (cavePlants || outPlants.Count == 0)
            return;
        Map map = MapOf(__instance);
        var ecology = map.GetComponent<MapComponent_Ecology>();
        if (ecology == null || !ecology.Ready)
            return;
        int index = map.cellIndices.CellToIndex(c);
        float threshold = PlantEcologyDef.Current.minFitness * MapComponent_Ecology.Strength;
        for (int i = outPlants.Count - 1; i >= 0; i--)
        {
            if (ecology.Fitness(outPlants[i], index) < threshold)
                outPlants.RemoveAt(i);
        }
    }
}
