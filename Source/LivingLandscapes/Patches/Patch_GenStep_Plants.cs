using System;
using HarmonyLib;
using LivingLandscapes.Fields;
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
    /// working grids are still alive, so this is where the environment fields are built.
    /// </summary>
    public static void Prefix(Map map)
    {
        try
        {
            var fields = map.GetComponent<EnvironmentFields>();
            if (fields == null)
                return;
            EnvironmentFieldComputer.Compute(map, fields);
        }
        catch (Exception e)
        {
            Log.Error($"[LivingLandscapes] Failed to compute environment fields; plants will generate as vanilla. {e}");
        }
    }

    public static void Postfix(Map map)
    {
        int plants = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant).Count;
        Log.Message($"[LivingLandscapes] Harmony patch fired: GenStep_Plants finished on {map} ({map.Biome?.defName}), {plants} plants on map.");
    }
}
