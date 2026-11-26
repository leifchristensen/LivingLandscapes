using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LivingLandscapes.Geology;
using LivingLandscapes.Settings;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Leaves out map generation steps listed in <see cref="GenStepFilterDef"/>s: scattered ruins
/// (<c>LL_ScatteredRuins</c>, which look out of place until terrain generation is polished) while that setting is off,
/// and the stand-ins' steps (<see cref="StandIns"/>) where geological terrain applies. Also logs, once per map, every
/// stand-in the geology leaves out on it, tile mutator shaping included.
/// </summary>
[HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateContentsIntoMap))]
public static class Patch_MapGenerator_GenerateContentsIntoMap
{
    public static void Prefix(ref IEnumerable<GenStepWithParams> genStepDefs, Map map)
    {
        if (genStepDefs == null)
            return;
        if (!LivingLandscapesMod.Settings.scatteredRuins)
        {
            int skipped = Remove(ref genStepDefs, DefDatabase<GenStepFilterDef>.GetNamedSilentFail("LL_ScatteredRuins")).Count;
            if (skipped > 0)
                Log.Message($"[LivingLandscapes] Scattered ruins off: skipped {skipped} generation step(s) on {map}.");
        }

        if (!GeologicalTerrain.AppliesTo(map))
            return;
        List<string> steps = Remove(ref genStepDefs, StandIns.List);
        List<string> mutators = map.TileInfo.Mutators.Where(StandIns.Replaces).Select(m => m.defName).ToList();
        if (steps.Count > 0 || mutators.Count > 0)
        {
            Log.Message($"[LivingLandscapes] Geology replaces stand-ins on {map}: shaping not run for tile features " +
                        $"[{string.Join(", ", mutators)}]; generation steps skipped [{string.Join(", ", steps)}].");
        }
    }

    /// <summary>Takes the filter's steps out of the list and returns the names of those that were in it.</summary>
    private static List<string> Remove(ref IEnumerable<GenStepWithParams> genStepDefs, GenStepFilterDef filter)
    {
        var removed = new List<string>();
        if (filter == null || filter.genSteps.Count == 0)
            return removed;
        var unwanted = new HashSet<GenStepDef>(filter.genSteps);
        List<GenStepWithParams> steps = genStepDefs.ToList();
        removed.AddRange(steps.Where(step => unwanted.Contains(step.def)).Select(step => step.def.defName));
        steps.RemoveAll(step => unwanted.Contains(step.def));
        genStepDefs = steps;
        return removed;
    }
}
