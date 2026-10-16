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

        TestRun.OnMapGenerated(map);
    }
}
