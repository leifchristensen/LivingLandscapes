using System;
using HarmonyLib;
using LivingLandscapes.DevTools;
using LivingLandscapes.Environment;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Hook point around vanilla's plant scatter. Never skips the original, so other mods' patches on this method
/// (e.g. Custom Base Framework's before/after tree bookkeeping) keep working.
/// </summary>
[HarmonyPatch(typeof(GenStep_Plants), nameof(GenStep_Plants.Generate))]
public static class Patch_GenStep_Plants
{
    /// <summary>
    /// Elevation, terrain, water and roofs are final by now (plants run at order 900) and the generator's
    /// working grids are still alive, so this is where the environment layers are computed.
    /// </summary>
    public static void Prefix(Map map)
    {
        try
        {
            if (Geology.GeologicalTerrain.AppliesTo(map))
            {
                var ground = EnvironmentLayerDefOf.LL_Wetness.GetModExtension<Geology.GroundWriterExtension>() ?? new Geology.GroundWriterExtension();
                Geology.StreamWriter.Apply(map, ground);
            }
            map.GetComponent<MapComponent_Environment>()?.ComputeLayers(GenerationStage.Plants);
        }
        catch (Exception e)
        {
            Log.Error($"[LivingLandscapes] Failed to compute environment layers; plants will generate as vanilla. {e}");
        }
    }

    public static void Postfix(Map map)
    {
        int plants = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant).Count;
        Log.Message($"[LivingLandscapes] GenStep_Plants finished on {map} ({map.Biome?.defName}), {plants} plants on map.");

        RemoveCliffsInWater(map);
        SpawnTalus(map);
        TestRun.OnMapGenerated(map);
    }

    /// <summary>Lakes and ponds are still placed by vanilla after the cliffs; a cliff can't stand in water.</summary>
    private static void RemoveCliffsInWater(Map map)
    {
        var drowned = new System.Collections.Generic.List<Thing>();
        foreach (Thing thing in map.listerThings.AllThings)
        {
            if (Geology.CliffDefs.IsCliff(thing.def) && thing.Position.GetTerrain(map).IsWater)
                drowned.Add(thing);
        }
        foreach (Thing thing in drowned)
            thing.Destroy();
    }

    /// <summary>Scatters the rock chunks planned below cliff faces, where the ground is now dry and open.</summary>
    private static void SpawnTalus(Map map)
    {
        var environment = map.GetComponent<MapComponent_Environment>();
        var talus = environment?.PlannedTalus;
        if (talus == null)
            return;
        environment.PlannedTalus = null;
        int spawned = 0;
        foreach ((IntVec3 cell, ThingDef chunk) in talus)
        {
            if (!cell.InBounds(map) || !cell.Standable(map) || cell.GetFirstItem(map) != null || cell.GetTerrain(map).IsWater)
                continue;
            GenSpawn.Spawn(chunk, cell, map);
            spawned++;
        }
        Log.Message($"[LivingLandscapes] Scattered {spawned} talus chunks below cliffs on {map}.");
    }
}
