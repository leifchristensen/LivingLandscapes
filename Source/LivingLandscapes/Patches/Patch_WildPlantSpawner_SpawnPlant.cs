using HarmonyLib;
using LivingLandscapes.Flora;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Succession (P22): a tree planted at map generation (random growth) is no older than its stand, so a young stand shows
/// saplings rather than grown trees: growth at most the stand's maturity for that species.
/// </summary>
[HarmonyPatch(typeof(WildPlantSpawner), nameof(WildPlantSpawner.SpawnPlant))]
public static class Patch_WildPlantSpawner_SpawnPlant
{
    public static void Postfix(Map map, IntVec3 cell, bool setRandomGrowth, Plant __result)
    {
        if (!setRandomGrowth || __result == null || !__result.def.plant.IsTree)
            return;
        var ecology = map.GetComponent<MapComponent_Ecology>();
        if (ecology == null || !ecology.Ready)
            return;
        float age = ecology.StandAge(map.cellIndices.CellToIndex(cell));
        if (age >= 1e6f)
            return;
        PlantHabitat habitat = PlantHabitat.For(__result.def);
        float cap = MapComponent_Ecology.Maturity(age, PlantEcologyDef.Current.canopyClosureYears * Mathf.Lerp(1.4f, 0.6f, habitat.pace));
        if (__result.Growth > cap)
        {
            __result.Growth = Mathf.Max(0.05f, cap * Rand.Range(0.6f, 1f));
            ecology.OnGrown(__result, thin: false);
        }
    }
}
