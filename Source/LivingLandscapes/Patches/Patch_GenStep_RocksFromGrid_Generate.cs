using System;
using System.Collections.Generic;
using HarmonyLib;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Spawns the cliffs the geology planned, right after vanilla places mountain rock and before terrain is chosen: a
/// Decorative Cliffs stone cliff on every band cell (piece by facing, linking into lines, made of the local rock's
/// blocks) and our steps facing downhill. Rock fallen from the faces is planned as talus: chunks of the same rock just
/// below them, spawned once the terrain and water are final.
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
                ThingDef rock = GenStep_RocksFromGrid.RockDefAt(cell);
                ThingDef def = kind == CliffKind.Steps ? CliffDefs.Steps : CliffDefs.BandFacing(plan.Facing[i]);
                if (def == null)
                    continue;
                ThingDef stuff = def.MadeFromStuff ? CliffDefs.BlocksOf(rock) ?? GenStuff.DefaultStuffFor(def) : null;
                Thing thing = ThingMaker.MakeThing(def, stuff);
                if (thing is Building_Cliff steps)
                    steps.Rock = rock;
                GenSpawn.Spawn(thing, cell, map, kind == CliffKind.Steps ? plan.Facing[i] : Rot4.North);
                spawned++;

                ThingDef chunk = rock?.building?.mineableThing;
                if (kind != CliffKind.Steps && chunk != null && Rand.Chance(TalusChance))
                    environment.PlannedTalus.Add((cell + plan.Facing[i].FacingCell * Rand.RangeInclusive(1, TalusMaxDistance), chunk));
            }
            Log.Message($"[LivingLandscapes] Spawned {spawned} cliff cells on {map}.");
        }
        catch (Exception e)
        {
            Log.Error($"[LivingLandscapes] Failed to spawn cliffs on {map}: {e}");
        }
    }
}
