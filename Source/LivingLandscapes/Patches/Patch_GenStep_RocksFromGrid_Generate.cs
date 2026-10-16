using System;
using HarmonyLib;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Spawns the cliffs the geology planned, right after vanilla places mountain rock and before terrain is chosen, so
/// the ground under sheer cliffs becomes rock floor like under other rock. Each cliff faces downhill.
/// </summary>
[HarmonyPatch(typeof(GenStep_RocksFromGrid), nameof(GenStep_RocksFromGrid.Generate))]
public static class Patch_GenStep_RocksFromGrid_Generate
{
    public static void Postfix(Map map)
    {
        var environment = map.GetComponent<MapComponent_Environment>();
        CliffPlan plan = environment?.PlannedCliffs;
        if (plan == null)
            return;
        environment.PlannedCliffs = null;
        try
        {
            int spawned = 0;
            for (int i = 0; i < plan.Kind.Length; i++)
            {
                if (plan.Kind[i] == CliffKind.None)
                    continue;
                IntVec3 cell = map.cellIndices.IndexToCell(i);
                if (cell.GetEdifice(map) != null)
                    continue;
                ThingDef def = plan.Kind[i] == CliffKind.Sheer ? CliffDefOf.LL_CliffSheer : CliffDefOf.LL_CliffScarp;
                GenSpawn.Spawn(def, cell, map, plan.Facing[i]);
                spawned++;
            }
            Log.Message($"[LivingLandscapes] Spawned {spawned} cliff cells on {map}.");
        }
        catch (Exception e)
        {
            Log.Error($"[LivingLandscapes] Failed to spawn cliffs on {map}: {e}");
        }
    }
}
