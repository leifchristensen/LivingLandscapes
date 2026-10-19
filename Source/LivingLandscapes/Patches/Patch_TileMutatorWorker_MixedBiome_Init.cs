using System;
using HarmonyLib;
using LivingLandscapes.Gameplay;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>After vanilla draws the mixed-biome line, the line becomes a transition zone (<see cref="Ecotone"/>).</summary>
[HarmonyPatch(typeof(TileMutatorWorker_MixedBiome), nameof(TileMutatorWorker_MixedBiome.Init))]
public static class Patch_TileMutatorWorker_MixedBiome_Init
{
    public static void Postfix(TileMutatorWorker_MixedBiome __instance, Map map)
    {
        if (!GameplaySiting.Enabled)
            return;
        try
        {
            Ecotone.Blur(map, __instance.def.GetModExtension<EcotoneExtension>() ?? new EcotoneExtension());
        }
        catch (Exception e)
        {
            Log.Error($"[LivingLandscapes] Biome transition failed; the vanilla boundary stays. {e}");
        }
    }
}
