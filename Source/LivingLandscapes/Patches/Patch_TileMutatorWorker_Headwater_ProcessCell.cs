using HarmonyLib;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// A headwater's lake: where the geology carved the map's rivers, its headwater water regime carves the lake the
/// river rises from, so vanilla's noise lake is not painted.
/// </summary>
[HarmonyPatch(typeof(TileMutatorWorker_Headwater), "ProcessCell")]
public static class Patch_TileMutatorWorker_Headwater_ProcessCell
{
    public static bool Prefix(Map map) => !Patch_TileMutatorWorker_River_RiverTerrainAt.HasCarvedRiver(map);
}
