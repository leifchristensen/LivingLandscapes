using HarmonyLib;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>Banks of a carved river come from the ground rules (wetness, mud at the shore), not vanilla's bank strip.</summary>
[HarmonyPatch(typeof(TileMutatorWorker_River), "RiverBankTerrainAt")]
public static class Patch_TileMutatorWorker_River_RiverBankTerrainAt
{
    public static bool Prefix(Map map, ref TerrainDef __result)
    {
        if (!Patch_TileMutatorWorker_River_RiverTerrainAt.HasCarvedRiver(map))
            return true;
        __result = null;
        return false;
    }
}
