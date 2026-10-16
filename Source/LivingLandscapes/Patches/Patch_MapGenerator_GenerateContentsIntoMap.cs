using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LivingLandscapes.Settings;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Leaves out the map generation steps listed in <see cref="GenStepFilterDef"/>s whose setting is off. For now only
/// scattered ruins (<c>LL_ScatteredRuins</c>), which look out of place until terrain generation is polished.
/// </summary>
[HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateContentsIntoMap))]
public static class Patch_MapGenerator_GenerateContentsIntoMap
{
    public static void Prefix(ref IEnumerable<GenStepWithParams> genStepDefs, Map map)
    {
        if (LivingLandscapesMod.Settings.scatteredRuins || genStepDefs == null)
            return;
        GenStepFilterDef filter = DefDatabase<GenStepFilterDef>.GetNamedSilentFail("LL_ScatteredRuins");
        if (filter == null || filter.genSteps.Count == 0)
            return;
        var removed = new HashSet<GenStepDef>(filter.genSteps);
        List<GenStepWithParams> steps = genStepDefs.ToList();
        int before = steps.Count;
        steps.RemoveAll(step => removed.Contains(step.def));
        genStepDefs = steps;
        if (steps.Count < before)
            Log.Message($"[LivingLandscapes] Scattered ruins off: skipped {before - steps.Count} generation step(s) on {map}.");
    }
}
