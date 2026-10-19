using System;
using HarmonyLib;
using LivingLandscapes.Gameplay;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Vanilla picks the start spot as a random central cell that has room and nothing else. After it has, the spot moves to
/// the best valid cell near the settlement anchor planned from the geology (<see cref="SettlementSiting"/>). Vanilla's
/// pick stays when there is no anchor or nothing valid near it.
/// </summary>
[HarmonyPatch(typeof(GenStep_FindPlayerStartSpot), nameof(GenStep_FindPlayerStartSpot.Generate))]
public static class Patch_GenStep_FindPlayerStartSpot_Generate
{
    public static void Postfix(Map map)
    {
        try
        {
            SettlementSiting.MoveStartSpot(map);
        }
        catch (Exception e)
        {
            Log.Error($"[LivingLandscapes] Could not move the start spot; vanilla's stays. {e}");
        }
    }
}
