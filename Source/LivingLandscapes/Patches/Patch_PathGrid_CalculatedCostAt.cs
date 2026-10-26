using HarmonyLib;
using LivingLandscapes.Flora;
using Verse;
using Verse.AI;

namespace LivingLandscapes.Patches;

/// <summary>
/// Thicket drag (P18): vanilla takes the highest single path cost in a cell, so a closed thicket costs no more than one
/// bush. The cost of a cell with shrub stems rises with the stem density around it. The pathfinder's grid and each
/// actual move (Pawn_PathFollower.CostToMoveIntoCell) both come from this method.
/// </summary>
[HarmonyPatch(typeof(PathGrid), nameof(PathGrid.CalculatedCostAt))]
public static class Patch_PathGrid_CalculatedCostAt
{
    private static Map lastMap;
    private static MapComponent_Ecology lastEcology;

    public static void Postfix(PathGrid __instance, IntVec3 c, ref int __result)
    {
        if (__result >= PathGrid.ImpassableCost || __instance.def.flying)
            return;
        Map map = __instance.map;
        // Called for every move and every recalculated cell: look the component up once per map, not each call.
        if (map != lastMap)
        {
            lastMap = map;
            lastEcology = map.GetComponent<MapComponent_Ecology>();
        }
        if (lastEcology == null)
            return;
        int thicket = lastEcology.ThicketCost(c);
        if (thicket > __result)
            __result = thicket;
    }
}
