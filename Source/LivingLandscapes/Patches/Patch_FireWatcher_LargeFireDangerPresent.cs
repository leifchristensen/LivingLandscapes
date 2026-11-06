using System.Runtime.CompilerServices;
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

    // When large fire danger began, per map object: map IDs restart in every game, so an entry left by a fire burning when
    // the player quit would otherwise carry into the next game (#157). The weak table lets old maps be collected.
    private static readonly ConditionalWeakTable<Map, StrongBox<int>> began = new ConditionalWeakTable<Map, StrongBox<int>>();

    public static void Postfix(FireWatcher __instance, ref bool __result)
    {
        float delay = LivingLandscapesMod.Settings?.fireRainDelayHours ?? 0f;
        Map map = MapOf(__instance);
        if (map == null)
            return;
        if (!__result)
        {
            began.Remove(map);
            return;
        }
        if (delay < 0.5f)
            return;
        int now = Find.TickManager.TicksGame;
        if (!began.TryGetValue(map, out StrongBox<int> start))
            began.Add(map, start = new StrongBox<int>(now));
        if (delay >= LivingLandscapesSettings.FireRainNever || now - start.Value < delay * GenDate.TicksPerHour)
            __result = false;
    }
}
