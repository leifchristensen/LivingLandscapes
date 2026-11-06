using HarmonyLib;
using LivingLandscapes.Climate;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// One temperature map (P23, #90, #159): vanilla's readout above the date shows the room's (or the map's outdoor)
/// temperature for the cell under the mouse. It shows vanilla's own temperature of that cell instead
/// (<see cref="GenTemperature.TryGetTemperatureForCell"/>, with the local offset where the air is outdoor), the value pawns
/// and plants on that cell feel, under vanilla's label. A fogged cell keeps vanilla's readout.
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
        // Vanilla's text is its label ("Outdoors", "Indoors (unroofed) (5)"), a space, then the temperature.
        int space = __result.LastIndexOf(' ');
        if (space <= 0)
            return;
        string label = __result.Substring(0, space);
        // What gameplay gives this cell, from vanilla's own lookup (with Patch_GenTemperature_TryGetDirectAirTemperatureForCell).
        if (!GenTemperature.TryGetTemperatureForCell(c, map, out float temperature))
            return;
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
