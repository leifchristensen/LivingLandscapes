using HarmonyLib;
using LivingLandscapes.Geology;
using RimWorld.Planet;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// The world tile tab's "Average temperature" prints <c>Tile.temperature</c> itself, the value vanilla baked with the
/// cooling of its own elevation, beside a range worked out from the seasons (which carry our altitude's offset, see
/// <see cref="Patch_GenTemperature_OffsetFromSeasonCycle"/>). The two disagreed by the altitude's cooling (33 C with a
/// range of 12 to 20 C on a rainforest mountain). The label is vanilla's, with the tile's temperature from
/// <see cref="WorldAltitude.TemperatureAt"/>.
/// </summary>
[HarmonyPatch(typeof(GenTemperature), nameof(GenTemperature.GetAverageTemperatureLabel))]
public static class Patch_GenTemperature_GetAverageTemperatureLabel
{
    public static bool Prefix(PlanetTile tile, ref string __result)
    {
        if (!tile.Valid || !WorldAltitude.Active)
            return true;
        float average = WorldAltitude.TemperatureAt(tile, Find.WorldGrid[tile]);
        __result = average.ToStringTemperature() +
                   $" ({GenTemperature.MinTemperatureAtTile(tile).ToStringTemperature("F0")} {"RangeTo".Translate()} {GenTemperature.MaxTemperatureAtTile(tile).ToStringTemperature("F0")})";
        return false;
    }
}
