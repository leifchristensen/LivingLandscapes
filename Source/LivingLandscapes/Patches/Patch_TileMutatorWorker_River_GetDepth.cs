using HarmonyLib;
using LivingLandscapes.Geology;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Vanilla's river step destroys edifices and (for rivers wider than 20 cells) removes roofs wherever its own depth map
/// says "river": the node's vanilla width with noise, not the channel the geology carved (#155). On a carved river that
/// depth is replaced by the carved water itself (<see cref="WaterDefinition"/>): water cells are cleared of building and
/// roof, dry cells beside them are left alone. Vanilla's other uses of this depth (painting its own water and banks) are
/// stood aside on a carved river already, so only the clearing is affected. Without a carved river the patch does nothing.
/// </summary>
[HarmonyPatch(typeof(TileMutatorWorker_River), "GetDepth")]
public static class Patch_TileMutatorWorker_River_GetDepth
{
    private const float InWater = 1f;
    private const float FarFromWater = -1000f;
    private const float WideRiverWidth = 1000f;

    /// <summary>Patches only where the private method exists, so a game update that renames it costs the fix, not the mod.</summary>
    public static bool Prepare() => AccessTools.Method(typeof(TileMutatorWorker_River), "GetDepth") != null;

    public static void Postfix(IntVec3 cell, Map map, ref float __result, ref float riverWidth)
    {
        if (!Patch_TileMutatorWorker_River_RiverTerrainAt.HasCarvedRiver(map))
            return;
        if (GroundCovers.IsWaterTerrain(map.terrainGrid.TerrainAt(cell)))
        {
            __result = InWater;
            riverWidth = WideRiverWidth;
        }
        else
        {
            __result = FarFromWater;
            riverWidth = 0f;
        }
    }
}
