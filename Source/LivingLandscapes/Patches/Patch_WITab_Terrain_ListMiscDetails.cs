using System.Globalization;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Dev mode: under vanilla's "Debug world tile ID" in the world map's tile tab, shows the world seed. Clicking the line
/// copies the launch arguments that regenerate this world and start a test map on this tile
/// (<see cref="Patch_WorldGenerator_GenerateWorld"/>), so a playtest map can be replayed in a test run.
/// </summary>
[HarmonyPatch(typeof(WITab_Terrain), "ListMiscDetails")]
public static class Patch_WITab_Terrain_ListMiscDetails
{
    public static void Postfix(Listing_Standard listing, PlanetTile tile)
    {
        if (!Prefs.DevMode || Find.World?.info == null)
            return;
        string args = LaunchArgs(tile);
        Rect row = listing.GetRect(Text.LineHeight);
        Widgets.DrawHighlightIfMouseover(row);
        Widgets.Label(row.LeftHalf(), "Debug world seed");
        Widgets.Label(row.RightHalf(), Find.World.info.seedString);
        TooltipHandler.TipRegion(row, "Click to copy the test launch arguments for this world and tile:\n\n" + args);
        if (Widgets.ButtonInvisible(row))
        {
            GUIUtility.systemCopyBuffer = args;
            Messages.Message("Copied test launch arguments for this world and tile.", MessageTypeDefOf.NeutralEvent, historical: false);
        }
    }

    /// <summary>The arguments that regenerate the current world (seed and world settings) and start on <paramref name="tile"/>.</summary>
    public static string LaunchArgs(PlanetTile tile)
    {
        WorldInfo info = Find.World.info;
        return $"-ll-world-seed={info.seedString} " +
               $"-ll-world-coverage={info.planetCoverage.ToString("0.###", CultureInfo.InvariantCulture)} " +
               $"-ll-world-rainfall={info.overallRainfall} -ll-world-temperature={info.overallTemperature} " +
               $"-ll-world-population={info.overallPopulation} -ll-world-landmarks={info.landmarkDensity} " +
               $"-ll-world-pollution={info.pollution.ToString("0.###", CultureInfo.InvariantCulture)} " +
               $"-ll-tile={tile} -ll-map-size={info.initialMapSize.x}";
    }
}
