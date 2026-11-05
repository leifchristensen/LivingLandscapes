using HarmonyLib;
using LivingLandscapes.Climate;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// One temperature map (P23, #90): vanilla's readout above the date shows the room's (or the map's outdoor) temperature
/// for the cell under the mouse. Where the local offset applies to that cell in gameplay
/// (<see cref="MapComponent_LocalClimate.AppliesTo"/>, also a partly-roofed room that takes the outdoor temperature),
/// it shows the cell's own temperature with its offset from height, slope and cold-air pooling, the value pawns and
/// plants on that cell feel, under vanilla's label. Elsewhere the readout is vanilla's (#159).
/// </summary>
[HarmonyPatch(typeof(GlobalControls), "TemperatureString")]
public static class Patch_GlobalControls_TemperatureString
{
    private static string cachedLabel;
    private static int cachedDegrees = int.MinValue;
    private static TemperatureDisplayMode cachedMode;
    private static string cachedText;

    public static void Postfix(ref string __result)
    {
        Map map = Find.CurrentMap;
        IntVec3 c = UI.MouseCell();
        if (map == null || !c.InBounds(map) || c.Fogged(map))
            return;
        Room room = c.GetRoom(map);
        if (!MapComponent_LocalClimate.AppliesTo(room))
            return;
        // Vanilla's text is its label ("Outdoors", "Indoors (unroofed) (5)"), a space, then the temperature.
        int space = __result.LastIndexOf(' ');
        if (space <= 0)
            return;
        string label = __result.Substring(0, space);
        // What gameplay gives this cell (Patch_GenTemperature_TryGetTemperatureForCell): the room's temperature plus the offset.
        float temperature = room.Temperature + MapComponent_LocalClimate.OffsetAt(map, c);
        int degrees = Mathf.RoundToInt(GenTemperature.CelsiusTo(temperature, Prefs.TemperatureMode));
        if (degrees != cachedDegrees || label != cachedLabel || cachedMode != Prefs.TemperatureMode)
        {
            cachedDegrees = degrees;
            cachedLabel = label;
            cachedMode = Prefs.TemperatureMode;
            cachedText = label + " " + temperature.ToStringTemperature("F0");
        }
        __result = cachedText;
    }
}
