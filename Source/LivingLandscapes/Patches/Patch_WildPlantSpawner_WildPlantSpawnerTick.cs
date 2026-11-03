using System;
using HarmonyLib;
using LivingLandscapes.Flora;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Regrowth rate (P15, G5): after vanilla's spawner tick, runs <see cref="MapComponent_Ecology.RegrowthRate"/> - 1 more of
/// its internal ticks (the fraction by chance; 2000 per tick under fast ecology, as vanilla does), so short-lived plants are
/// replaced as fast as they die and played maps keep the density generation made. The mod clock's extra spawner ticks
/// pass through here too, so the two multiply.
/// </summary>
[HarmonyPatch(typeof(WildPlantSpawner), nameof(WildPlantSpawner.WildPlantSpawnerTick))]
public static class Patch_WildPlantSpawner_WildPlantSpawnerTick
{
    private static readonly AccessTools.FieldRef<WildPlantSpawner, Map> MapOf = AccessTools.FieldRefAccess<WildPlantSpawner, Map>("map");

    private static readonly Action<WildPlantSpawner> TickInternal =
        AccessTools.MethodDelegate<Action<WildPlantSpawner>>(AccessTools.Method(typeof(WildPlantSpawner), "WildPlantSpawnerTickInternal"));

    public static void Postfix(WildPlantSpawner __instance)
    {
        var ecology = MapOf(__instance).GetComponent<MapComponent_Ecology>();
        if (ecology == null || !ecology.Ready)
            return;
        float extra = ecology.RegrowthRate() - 1f;
        if (extra <= 0f)
            return;
        if (DebugSettings.fastEcology || DebugSettings.fastEcologyRegrowRateOnly)
            extra *= 2000f;
        int ticks = Mathf.FloorToInt(extra) + (Rand.Chance(extra - Mathf.Floor(extra)) ? 1 : 0);
        for (int i = 0; i < ticks; i++)
            TickInternal(__instance);
    }
}
