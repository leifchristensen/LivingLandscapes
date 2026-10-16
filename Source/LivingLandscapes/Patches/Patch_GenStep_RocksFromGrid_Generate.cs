using System;
using System.Collections.Generic;
using HarmonyLib;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Spawns the terrace cliffs the geology planned, right after vanilla places mountain rock and before terrain is
/// chosen: a band piece on every stage-edge cell (art by facing, linking like a wall), steps facing downhill, each
/// made of the rock vanilla generates at that spot. Rock fallen from the faces is planned as talus: chunks of the same
/// rock just below them, spawned once the terrain and water are final.
/// </summary>
[HarmonyPatch(typeof(GenStep_RocksFromGrid), nameof(GenStep_RocksFromGrid.Generate))]
public static class Patch_GenStep_RocksFromGrid_Generate
{
    /// <summary>Chance per cliff face cell of a rock chunk lying below it, and how far below (cells).</summary>
    private const float TalusChance = 0.06f;
    private const int TalusMaxDistance = 2;

    public static void Postfix(Map map)
    {
        var environment = map.GetComponent<MapComponent_Environment>();
        CliffPlan plan = environment?.PlannedCliffs;
        if (plan == null)
            return;
        environment.PlannedCliffs = null;
        try
        {
            var bandByFacing = new[]
            {
                DefDatabase<ThingDef>.GetNamed("LL_CliffFacingNorth"), DefDatabase<ThingDef>.GetNamed("LL_CliffFacingEast"),
                DefDatabase<ThingDef>.GetNamed("LL_CliffFacingSouth"), DefDatabase<ThingDef>.GetNamed("LL_CliffFacingWest"),
            };
            var steps = DefDatabase<ThingDef>.GetNamed("LL_CliffSteps");
            environment.PlannedTalus = new List<(IntVec3, ThingDef)>();
            int spawned = 0;
            for (int i = 0; i < plan.Kind.Length; i++)
            {
                CliffKind kind = plan.Kind[i];
                if (kind == CliffKind.None)
                    continue;
                IntVec3 cell = map.cellIndices.IndexToCell(i);
                if (cell.GetEdifice(map) != null)
                    continue;
                ThingDef def = kind == CliffKind.Steps ? steps : bandByFacing[plan.Facing[i].AsInt];
                var cliff = (Building_Cliff)ThingMaker.MakeThing(def);
                cliff.Rock = GenStep_RocksFromGrid.RockDefAt(cell);
                GenSpawn.Spawn(cliff, cell, map, kind == CliffKind.Steps ? plan.Facing[i] : Rot4.North);
                spawned++;

                ThingDef chunk = cliff.Rock?.building?.mineableThing;
                if (kind != CliffKind.Steps && chunk != null && Rand.Chance(TalusChance))
                    environment.PlannedTalus.Add((cell + plan.Facing[i].FacingCell * Rand.RangeInclusive(1, TalusMaxDistance), chunk));
            }
            Log.Message($"[LivingLandscapes] Spawned {spawned} terrace cliff cells on {map}.");
        }
        catch (Exception e)
        {
            Log.Error($"[LivingLandscapes] Failed to spawn cliffs on {map}: {e}");
        }
    }
}
