using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LivingLandscapes.Geology;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// With geological terrain on, a tile mutator whose landform the geology models (a <see cref="FeatureTypeDef"/> is
/// anchored on it) must not also rewrite the elevation grid afterwards; nor must a river mutator
/// (headwater lakes and the like) where the geology planned the map's rivers; nor a stand-in the geology leaves out
/// (<see cref="StandIns"/>: vanilla cave tunnels are cut here). Targets every mutator worker, vanilla or modded, that
/// overrides this step.
/// </summary>
[HarmonyPatch]
public static class Patch_TileMutatorWorker_GeneratePostElevationFertility
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        return typeof(TileMutatorWorker).AllSubclassesNonAbstract()
            .Select(type => type.GetMethod(nameof(TileMutatorWorker.GeneratePostElevationFertility),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null, new[] { typeof(Map) }, null))
            .Where(method => method != null);
    }

    public static bool Prefix(TileMutatorWorker __instance, Map map)
    {
        if (!GeologicalTerrain.AppliesTo(map))
            return true;
        if (StandIns.Replaces(__instance.def))
            return false;
        if (__instance is TileMutatorWorker_River && map.GetComponent<Environment.MapComponent_Environment>()?.PlannedRivers != null)
            return false;
        return __instance.def == null || !FeatureTypeDef.ReplacesVanillaElevation(__instance.def);
    }
}
