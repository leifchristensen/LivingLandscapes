using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LivingLandscapes.Geology;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// With geological terrain on, a tile mutator whose landform the geology models (it has a
/// <see cref="LandformExtension"/>) must not also rewrite the elevation grid afterwards. Targets every mutator worker,
/// vanilla or modded, that overrides this step.
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
        var form = __instance.def?.GetModExtension<LandformExtension>();
        return form == null || !form.replacesVanillaElevation || !GeologicalTerrain.AppliesTo(map);
    }
}
