using HarmonyLib;
using LivingLandscapes.Flora;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Forest layers: scales a plant's commonality by its vegetation layer in the map's biome (<see cref="PlantEcologyDef.stratumScales"/>).
/// The spawner sums this method for its shares, so the layers' shares shift together and the total stays vanilla's.
/// </summary>
[HarmonyPatch(typeof(WildPlantSpawner), "GetCommonalityOfPlant")]
public static class Patch_WildPlantSpawner_GetCommonalityOfPlant
{
    private static readonly AccessTools.FieldRef<WildPlantSpawner, Map> MapOf = AccessTools.FieldRefAccess<WildPlantSpawner, Map>("map");

    public static void Postfix(WildPlantSpawner __instance, ThingDef plant, ref float __result)
    {
        if (__result <= 0f || plant?.plant == null || plant.plant.cavePlant)
            return;
        float strength = MapComponent_Ecology.Strength;
        if (strength <= 0f)
            return;
        PlantEcologyDef def = PlantEcologyDef.Current;
        if (def.stratumScales.Count == 0)
            return;
        PlantHabitat habitat = PlantHabitat.For(plant);
        if (!habitat.HasSiteCurves)
            return;
        Map map = MapOf(__instance);
        __result *= Mathf.Lerp(1f, def.ScaleFor(map.Biome, habitat.stratum, def.SeasonOf(map)), strength);
    }
}
