using System;
using System.Globalization;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Dev quicktest builds its world with fixed settings (30% coverage, normal climate and population). While quicktest sets
/// up, these launch arguments replace them, so a player's world can be regenerated for a test run (the world map's tile
/// tab copies them in dev mode, <see cref="Patch_WITab_Terrain_ListMiscDetails"/>): "-ll-world-seed=&lt;text&gt;",
/// "-ll-world-coverage=0.3", "-ll-world-rainfall=Normal", "-ll-world-temperature=Normal", "-ll-world-population=Normal",
/// "-ll-world-landmarks=Normal", "-ll-world-pollution=0.05". The same world also needs the same mods (biomes, world
/// generation steps) in the same order.
/// </summary>
[HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GenerateWorld))]
public static class Patch_WorldGenerator_GenerateWorld
{
    public static void Prefix(ref float planetCoverage, ref string seedString, ref OverallRainfall overallRainfall,
        ref OverallTemperature overallTemperature, ref OverallPopulation population, ref LandmarkDensity landmarkDensity,
        ref float pollution)
    {
        if (!Patch_Root_Play_SetupForQuickTestPlay.Active)
            return;
        if (GenCommandLine.TryGetCommandLineArg("ll-world-seed", out string seed) && !seed.NullOrEmpty())
            seedString = seed;
        if (TryFloat("ll-world-coverage", out float coverage))
            planetCoverage = coverage;
        if (TryFloat("ll-world-pollution", out float worldPollution))
            pollution = worldPollution;
        TryEnum("ll-world-rainfall", ref overallRainfall);
        TryEnum("ll-world-temperature", ref overallTemperature);
        TryEnum("ll-world-population", ref population);
        TryEnum("ll-world-landmarks", ref landmarkDensity);
        Log.Message($"[LivingLandscapes] Quicktest world: seed {seedString}, coverage {planetCoverage}, rainfall {overallRainfall}, " +
                    $"temperature {overallTemperature}, population {population}, landmarks {landmarkDensity}, pollution {pollution}.");
    }

    private static bool TryFloat(string name, out float value)
    {
        value = 0f;
        return GenCommandLine.TryGetCommandLineArg(name, out string text)
               && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static void TryEnum<T>(string name, ref T value) where T : struct, Enum
    {
        if (!GenCommandLine.TryGetCommandLineArg(name, out string text))
            return;
        if (Enum.TryParse(text, out T parsed))
            value = parsed;
        else
            Log.Warning($"[LivingLandscapes] -{name}: {text} is not one of {string.Join(", ", Enum.GetNames(typeof(T)))}.");
    }
}
