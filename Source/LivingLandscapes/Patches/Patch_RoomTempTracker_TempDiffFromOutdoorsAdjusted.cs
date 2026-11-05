using HarmonyLib;
using LivingLandscapes.Climate;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// One temperature map (P23, #159): a room that keeps its own temperature exchanges heat through its thin roof and its
/// open roof cells with the outdoor air above it. Vanilla takes that air at the map's outdoor temperature; here it is the
/// outdoor temperature plus the room's mean local offset (<see cref="MapComponent_LocalClimate.RoomOffset"/>), the same air
/// its walls exchange with. Vanilla scales differences over 100 C up; an offset of a few degrees is added as it is.
/// </summary>
[HarmonyPatch(typeof(RoomTempTracker), "TempDiffFromOutdoorsAdjusted")]
public static class Patch_RoomTempTracker_TempDiffFromOutdoorsAdjusted
{
    private static readonly AccessTools.FieldRef<RoomTempTracker, Room> RoomOf = AccessTools.FieldRefAccess<RoomTempTracker, Room>("room");

    public static void Postfix(RoomTempTracker __instance, ref float __result)
    {
        __result += MapComponent_LocalClimate.RoomOffsetOf(RoomOf(__instance));
    }
}
