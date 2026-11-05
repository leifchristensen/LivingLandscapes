using HarmonyLib;
using LivingLandscapes.Climate;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// One temperature map (P23, #90, #159): the air of a cell open to the outdoors gets the local offset of its height, slope
/// and cold-air pooling (<see cref="MapComponent_LocalClimate"/>) on top of the map's outdoor temperature. Vanilla reads
/// every cell's air through this method: a cell's temperature (pawns' comfort and cold or heat injury, plant growth and its
/// growing season, sowing, each species' growth window in PlantTemperature.xml), the mean around a wall or rock cell, and
/// a room's heat exchange with the cells beyond its walls (RoomTempTracker), so a sealed room in a cold hollow loses heat to
/// the colder air there. A room's own temperature is unchanged here; its roof exchange is
/// <see cref="Patch_RoomTempTracker_TempDiffFromOutdoorsAdjusted"/>.
/// </summary>
[HarmonyPatch(typeof(GenTemperature), nameof(GenTemperature.TryGetDirectAirTemperatureForCell))]
public static class Patch_GenTemperature_TryGetDirectAirTemperatureForCell
{
    public static void Postfix(bool __result, IntVec3 c, Map map, ref float temperature)
    {
        if (!__result || map == null)
            return;
        // Vanilla looked the room up just before; the lookup is a region grid read, kept rather than replacing its method.
        if (!MapComponent_LocalClimate.AppliesTo(c.GetRoom(map)))
            return;
        temperature += MapComponent_LocalClimate.OffsetAt(map, c);
    }
}
