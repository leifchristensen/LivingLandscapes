using System.Collections.Generic;
using HarmonyLib;
using LivingLandscapes.Settings;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Fire rain delay (owner setting): vanilla's WeatherDecider reads LargeFireDangerPresent to end the current weather sooner
/// and make rain 15x likelier while a large fire burns. This hides the danger until it has lasted the set hours, or for
/// good when the setting is at "never". Fire spread itself is unchanged.
/// </summary>
[HarmonyPatch(typeof(FireWatcher), nameof(FireWatcher.LargeFireDangerPresent), MethodType.Getter)]
public static class Patch_FireWatcher_LargeFireDangerPresent
{
    private static readonly AccessTools.FieldRef<FireWatcher, Map> MapOf = AccessTools.FieldRefAccess<FireWatcher, Map>("map");

    // When large fire danger began, per map.
    private static readonly Dictionary<int, int> began = new Dictionary<int, int>();

    public static void Postfix(FireWatcher __instance, ref bool __result)
    {
        float delay = LivingLandscapesMod.Settings?.fireRainDelayHours ?? 0f;
        int id = MapOf(__instance)?.uniqueID ?? -1;
        if (!__result)
        {
            began.Remove(id);
            return;
        }
        if (delay < 0.5f)
            return;
        int now = Find.TickManager.TicksGame;
        if (!began.TryGetValue(id, out int start))
            began[id] = start = now;
        if (delay >= LivingLandscapesSettings.FireRainNever || now - start < delay * GenDate.TicksPerHour)
            __result = false;
    }
}
