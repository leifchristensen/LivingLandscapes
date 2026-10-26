using HarmonyLib;
using LivingLandscapes.Flora;
using RimWorld;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Succession (P22): fire on the ground is a stand-replacing disturbance, so the cell's stand starts again at age 0 and
/// regrows through the stages (grass and fireweed, bramble thicket, saplings). Vanilla fire spread is unchanged.
/// </summary>
[HarmonyPatch(typeof(Fire), nameof(Fire.SpawnSetup))]
public static class Patch_Fire_SpawnSetup
{
    public static void Postfix(Fire __instance, Map map, bool respawningAfterLoad)
    {
        // A fire burning on a pawn or a thing it is attached to is not a ground fire.
        if (respawningAfterLoad || __instance.parent != null)
            return;
        map.GetComponent<MapComponent_StandAge>()?.Reset(__instance.Position);
    }
}
