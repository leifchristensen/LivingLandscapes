using HarmonyLib;
using LivingLandscapes.Gameplay;
using Verse;
using Verse.AI;

namespace LivingLandscapes.Patches;

/// <summary>
/// Route planning over slopes: the static path grid (what the pathfinder plans with) gets an extra cost by a cell's steepest
/// grade, so pawns prefer gentle ground, passes and steps (<see cref="SlopeMovement.PlanningCost"/>). Movement itself is costed
/// by direction in <see cref="Patch_Pawn_PathFollower_CostToMoveIntoCell"/>, so the live (non-static) cost is left alone.
/// </summary>
[HarmonyPatch(typeof(PathGrid), nameof(PathGrid.CalculatedCostAt))]
public static class Patch_PathGrid_CalculatedCostAt
{
    public static void Postfix(PathGrid __instance, IntVec3 c, bool perceivedStatic, ref int __result)
    {
        if (!perceivedStatic || __result >= 10000 || !SlopeMovement.Enabled)
            return;
        Map map = __instance.map;
        if (map == null)
            return;
        __result += SlopeMovement.PlanningCost(map, c);
    }
}
