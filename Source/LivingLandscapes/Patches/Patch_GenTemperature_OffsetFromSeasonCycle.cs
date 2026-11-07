using HarmonyLib;
using LivingLandscapes.Geology;
using RimWorld.Planet;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// A tile's temperature at any time is its <c>temperature</c> plus <see cref="GenTemperature.OffsetFromSeasonCycle"/>, both in
/// the map's outdoor temperature (TileTemperaturesComp) and in the world's seasonal temperatures
/// (GenTemperature.GetTemperatureFromSeasonAtTile and everything built on it). The altitude's cooling is the same for
/// every day of the year, so it rides on this one shared term (<see cref="WorldAltitude.TemperatureOffset"/>, #201).
/// </summary>
[HarmonyPatch(typeof(GenTemperature), nameof(GenTemperature.OffsetFromSeasonCycle))]
public static class Patch_GenTemperature_OffsetFromSeasonCycle
{
    public static void Postfix(PlanetTile tile, ref float __result)
    {
        if (tile.Valid)
            __result += WorldAltitude.TemperatureOffset(tile);
    }
}
