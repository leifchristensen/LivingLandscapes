using System;
using System.Collections.Generic;
using HarmonyLib;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Spawns the cliffs the geology planned, right after vanilla places mountain rock and before terrain is chosen.
/// Each run of cliff cells with the same kind and facing is covered with the longest pieces that fit (up to 3 cells
/// along the cliff line), each rotated to face downhill and made of the rock vanilla generates at that spot.
/// Rock that falls from the faces is planned as talus: chunks of the same rock just below them, spawned once the
/// terrain and water are final.
/// </summary>
[HarmonyPatch(typeof(GenStep_RocksFromGrid), nameof(GenStep_RocksFromGrid.Generate))]
public static class Patch_GenStep_RocksFromGrid_Generate
{
    /// <summary>Chance per cliff cell of a rock chunk lying below it, and how far below (cells).</summary>
    private const float TalusChanceCliff = 0.12f;
    private const float TalusChanceScarp = 0.2f;
    private const int TalusMaxDistance = 3;

    public static void Postfix(Map map)
    {
        var environment = map.GetComponent<MapComponent_Environment>();
        CliffPlan plan = environment?.PlannedCliffs;
        if (plan == null)
            return;
        environment.PlannedCliffs = null;
        try
        {
            var done = new bool[plan.Kind.Length];
            Color lichen = Building_Cliff.LichenColorFor(map);
            environment.PlannedTalus = new List<(IntVec3, ThingDef)>();
            int pieces = 0, cells = 0;
            for (int i = 0; i < plan.Kind.Length; i++)
            {
                if (done[i] || plan.Kind[i] == CliffKind.None)
                    continue;
                int length = PlacePiece(map, plan, done, i, lichen, environment.PlannedTalus);
                if (length > 0)
                {
                    pieces++;
                    cells += length;
                }
            }
            Log.Message($"[LivingLandscapes] Spawned {pieces} cliff pieces covering {cells} cells on {map}.");
        }
        catch (Exception e)
        {
            Log.Error($"[LivingLandscapes] Failed to spawn cliffs on {map}: {e}");
        }
    }

    /// <summary>Places the longest piece starting at cell <paramref name="start"/>; returns its length, or 0.</summary>
    private static int PlacePiece(Map map, CliffPlan plan, bool[] done, int start, Color lichen, List<(IntVec3, ThingDef)> talus)
    {
        CliffKind kind = plan.Kind[start];
        Rot4 facing = plan.Facing[start];
        IntVec3 origin = map.cellIndices.IndexToCell(start);
        // The cliff line runs across the facing: east-west for north/south faces, north-south for east/west faces.
        IntVec3 step = facing.IsHorizontal ? IntVec3.North : IntVec3.East;

        int run = 0;
        while (run < CliffDefs.MaxLength)
        {
            IntVec3 c = origin + step * run;
            if (!c.InBounds(map))
                break;
            int idx = map.cellIndices.CellToIndex(c);
            if (done[idx] || plan.Kind[idx] != kind || plan.Facing[idx] != facing || c.GetEdifice(map) != null)
                break;
            run++;
        }

        for (int length = run; length >= 1; length--)
        {
            ThingDef def = CliffDefs.For(kind, length);
            // Multi-cell things are positioned by a centre cell; find the one whose footprint is exactly this run.
            for (int k = 0; k < length; k++)
            {
                IntVec3 centre = origin + step * k;
                CellRect footprint = GenAdj.OccupiedRect(centre, facing, def.size);
                if (!Matches(footprint, origin, step, length))
                    continue;
                var cliff = (Building_Cliff)ThingMaker.MakeThing(def);
                cliff.Rock = GenStep_RocksFromGrid.RockDefAt(centre);
                cliff.Lichen = lichen;
                GenSpawn.Spawn(cliff, centre, map, facing);
                ThingDef chunk = cliff.Rock?.building?.mineableThing;
                float talusChance = kind == CliffKind.Sheer ? TalusChanceCliff : TalusChanceScarp;
                for (int j = 0; j < length; j++)
                {
                    IntVec3 cell = origin + step * j;
                    done[map.cellIndices.CellToIndex(cell)] = true;
                    if (chunk != null && Rand.Chance(talusChance))
                        talus.Add((cell + facing.FacingCell * Rand.RangeInclusive(1, TalusMaxDistance), chunk));
                }
                return length;
            }
        }
        done[start] = true;
        return 0;
    }

    private static bool Matches(CellRect footprint, IntVec3 origin, IntVec3 step, int length)
    {
        if (footprint.Area != length)
            return false;
        for (int j = 0; j < length; j++)
        {
            if (!footprint.Contains(origin + step * j))
                return false;
        }
        return true;
    }
}
