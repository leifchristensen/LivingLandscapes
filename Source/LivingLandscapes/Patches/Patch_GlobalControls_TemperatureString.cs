using HarmonyLib;
using LivingLandscapes.Climate;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// One temperature map (P23, #90): vanilla's readout above the date shows the map's outdoor temperature for every
/// outdoor cell under the mouse. It shows the cell's own temperature instead, with its local offset from height, slope
/// and cold-air pooling, the value pawns and plants on that cell feel. Rooms keep vanilla's readout.
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
        if (room != null && !room.UsesOutdoorTemperature)
            return;
        string label = "Outdoors".Translate().CapitalizeFirst();
        if (!__result.StartsWith(label))
            return;
        float temperature = map.mapTemperature.OutdoorTemp + MapComponent_LocalClimate.OffsetAt(map, c);
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
