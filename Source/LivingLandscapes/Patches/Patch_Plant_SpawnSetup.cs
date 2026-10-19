using HarmonyLib;
using LivingLandscapes.Flora;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>Keeps the ecology's cluster and shade grids current: a plant that appears is added to them.</summary>
[HarmonyPatch(typeof(Plant), nameof(Plant.SpawnSetup))]
public static class Patch_Plant_SpawnSetup
{
    public static void Postfix(Plant __instance, Map map)
    {
        map.GetComponent<MapComponent_Ecology>()?.OnSpawned(__instance);
    }
}
