using HarmonyLib;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>Quicktest asks GenText for its random world seed; answers with the "-ll-seed" value while quicktest sets up.</summary>
[HarmonyPatch(typeof(GenText), nameof(GenText.RandomSeedString))]
public static class Patch_GenText_RandomSeedString
{
    public static void Postfix(ref string __result)
    {
        if (Patch_Root_Play_SetupForQuickTestPlay.Active && GenCommandLine.TryGetCommandLineArg("ll-seed", out string seed) && !seed.NullOrEmpty())
            __result = seed;
    }
}
