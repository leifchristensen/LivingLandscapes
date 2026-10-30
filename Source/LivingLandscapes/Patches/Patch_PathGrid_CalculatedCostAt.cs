using HarmonyLib;
using LivingLandscapes.Flora;
using LivingLandscapes.Gameplay;
using Verse;
using Verse.AI;

namespace LivingLandscapes.Patches;

/// <summary>
/// Path costs from the landscape:
/// - Thicket drag (P18): vanilla takes the highest single path cost in a cell, so a closed thicket costs no more than one
///   bush. A cell with shrub stems costs more as the stem density around it rises (<see cref="MapComponent_Ecology.ThicketCost"/>),
///   both in the pathfinder's grid and for each actual move (Pawn_PathFollower.CostToMoveIntoCell reads the live cost).
/// - Route planning over slopes: the static path grid (what the pathfinder plans with) gets an extra cost by a cell's steepest
///   grade, so pawns prefer gentle ground, passes and steps (<see cref="SlopeMovement.PlanningCost"/>). Movement itself is costed
///   by direction in <see cref="Patch_Pawn_PathFollower_CostToMoveIntoCell"/>, so the live (non-static) cost gets no slope.
/// </summary>
[HarmonyPatch(typeof(PathGrid), nameof(PathGrid.CalculatedCostAt))]
public static class Patch_PathGrid_CalculatedCostAt
{
    private static Map lastMap;
    private static MapComponent_Ecology lastEcology;

    public static void Postfix(PathGrid __instance, IntVec3 c, bool perceivedStatic, ref int __result)
    {
        if (__result >= PathGrid.ImpassableCost)
            return;
        Map map = __instance.map;
        if (map == null)
            return;
        if (!__instance.def.flying)
        {
            // Called for every move and every recalculated cell: look the component up once per map, not each call.
            if (map != lastMap)
            {
                lastMap = map;
                lastEcology = map.GetComponent<MapComponent_Ecology>();
            }
            int thicket = lastEcology?.ThicketCost(c) ?? 0;
            if (thicket > __result)
                __result = thicket;
        }
        if (perceivedStatic && SlopeMovement.Enabled)
            __result += SlopeMovement.PlanningCost(map, c);
    }
}
