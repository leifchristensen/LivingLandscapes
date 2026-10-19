using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// A tile mutator whose water the map's water regime models (a delta, a headwater lake, a wetland's channels; the
/// regime lists the mutator and sets <see cref="WaterRegimeDef.replacesMutatorTerrain"/>) must not paint its own
/// water over the geology's afterwards. Targets every mutator worker, vanilla or modded, that overrides this step,
/// except river workers: they lay the river graph here, and their painting stands aside in its own patches.
/// </summary>
[HarmonyPatch]
public static class Patch_TileMutatorWorker_GeneratePostTerrain
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        return typeof(TileMutatorWorker).AllSubclassesNonAbstract()
            .Select(type => type.GetMethod(nameof(TileMutatorWorker.GeneratePostTerrain),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null, new[] { typeof(Map) }, null))
            .Where(method => method != null);
    }

    public static bool Prefix(TileMutatorWorker __instance, Map map)
    {
        // River workers build the river graph here; their own water painting is stood aside separately.
        if (__instance is TileMutatorWorker_River)
            return true;
        WaterRegimeDef regime = map.GetComponent<MapComponent_Environment>()?.PlannedRivers?.Regime;
        return regime == null || !regime.replacesMutatorTerrain || __instance.def == null || !regime.mutators.Contains(__instance.def.defName);
    }
}
