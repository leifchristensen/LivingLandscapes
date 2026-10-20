using HarmonyLib;
using LivingLandscapes.Gameplay;
using UnityEngine;
using Verse;
using Verse.AI;

namespace LivingLandscapes.Patches;

/// <summary>
/// The time a step takes grows with the grade climbed (descents cost less), on top of vanilla's terrain and building costs
/// (<see cref="SlopeMovement"/>). Only the pawn's own walking time is scaled; vanilla's cap of 450 ticks a cell stays.
/// </summary>
[HarmonyPatch(typeof(Pawn_PathFollower), "CostToMoveIntoCell", typeof(Pawn), typeof(IntVec3))]
public static class Patch_Pawn_PathFollower_CostToMoveIntoCell
{
    public static void Postfix(Pawn pawn, IntVec3 c, ref float __result)
    {
        if (!SlopeMovement.Enabled || pawn?.Map == null || pawn.Flying)
            return;
        float factor = SlopeMovement.StepFactor(pawn.Map, pawn.Position, c);
        if (factor <= 1f)
            return;
        float walk = c.x != pawn.Position.x && c.z != pawn.Position.z ? pawn.TicksPerMoveDiagonal : pawn.TicksPerMoveCardinal;
        __result = Mathf.Min(450f, __result + walk * (factor - 1f));
    }
}
