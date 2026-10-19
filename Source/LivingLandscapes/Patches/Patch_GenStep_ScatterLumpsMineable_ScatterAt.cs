using System;
using HarmonyLib;
using LivingLandscapes.Gameplay;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Vanilla chooses the ore of a lump by commonality alone, whatever the cell. Before it scatters, the ore is chosen for the
/// cell (<see cref="ResourceSiting"/>) and handed over through vanilla's own <c>forcedDefToScatter</c>, which is cleared
/// again afterwards. Lumps vanilla or another mod forces to one ore are left alone.
/// </summary>
[HarmonyPatch(typeof(GenStep_ScatterLumpsMineable), "ScatterAt")]
public static class Patch_GenStep_ScatterLumpsMineable_ScatterAt
{
    public static void Prefix(GenStep_ScatterLumpsMineable __instance, IntVec3 c, Map map, out bool __state)
    {
        __state = false;
        if (__instance.forcedDefToScatter != null)
            return;
        try
        {
            ThingDef ore = ResourceSiting.For(map)?.Choose(c, __instance.maxValue);
            if (ore == null)
                return;
            __instance.forcedDefToScatter = ore;
            __state = true;
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[LivingLandscapes] Resource siting failed; ore is chosen as vanilla does. {e}", 0x4c4c5253);
        }
    }

    public static void Postfix(GenStep_ScatterLumpsMineable __instance, bool __state)
    {
        if (__state)
            __instance.forcedDefToScatter = null;
    }
}
