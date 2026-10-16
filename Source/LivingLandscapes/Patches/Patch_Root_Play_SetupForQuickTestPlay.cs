using HarmonyLib;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Development aid: "-ll-seed=&lt;text&gt;" makes dev quicktest use a fixed world seed and start tile, so the same test maps can be
/// regenerated before and after a change. Has no effect without the launch argument.
/// </summary>
[HarmonyPatch(typeof(Root_Play), nameof(Root_Play.SetupForQuickTestPlay))]
public static class Patch_Root_Play_SetupForQuickTestPlay
{
    /// <summary>True while quicktest sets up with a fixed seed; read by <see cref="Patch_GenText_RandomSeedString"/>.</summary>
    internal static bool Active { get; private set; }

    public static void Prefix()
    {
        Active = GenCommandLine.TryGetCommandLineArg("ll-seed", out string seed) && !seed.NullOrEmpty();
        // The start tile is picked with the global random state, so seed that too.
        if (Active)
            Rand.PushState(GenText.StableStringHash(seed));
    }

    public static void Postfix()
    {
        if (Active)
            Rand.PopState();
        Active = false;
    }
}
