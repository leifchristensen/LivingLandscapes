using HarmonyLib;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Vanilla rebuilds the river flow map after fog for every moving-water cell, but only fills cells near its own river,
/// leaving our streams without flow (or with NaN). Our stream flow is written again afterwards.
/// </summary>
[HarmonyPatch(typeof(TileMutatorWorker_River), nameof(TileMutatorWorker_River.GeneratePostFog))]
public static class Patch_TileMutatorWorker_River_GeneratePostFog
{
    public static void Postfix(Map map)
    {
        if (Geology.GeologicalTerrain.AppliesTo(map))
            Geology.FlowWriter.Apply(map);
    }
}
