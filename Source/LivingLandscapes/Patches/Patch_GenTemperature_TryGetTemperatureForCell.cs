using HarmonyLib;
using LivingLandscapes.Climate;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// One temperature map (P23, #90, owner 2026-11-02): a cell open to the outdoors gets the local offset of its height,
/// slope and cold-air pooling (<see cref="MapComponent_LocalClimate"/>) on top of the map's outdoor temperature. Every
/// reader of a cell's temperature sees it: pawns' comfort and cold or heat injury, plant growth and its growing season,
/// sowing, and the growth temperature window each plant species has (PlantTemperature.xml). Rooms are unchanged.
/// </summary>
[HarmonyPatch(typeof(GenTemperature), nameof(GenTemperature.TryGetTemperatureForCell))]
public static class Patch_GenTemperature_TryGetTemperatureForCell
{
    public static void Postfix(bool __result, IntVec3 c, Map map, ref float tempResult)
    {
        if (!__result || map == null)
            return;
        Room room = c.GetRoom(map);
        if (room == null || !room.UsesOutdoorTemperature)
            return;
        tempResult += MapComponent_LocalClimate.OffsetAt(map, c);
    }
}
