using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Development aids for dev quicktest, inactive without their launch arguments:
/// "-ll-seed=&lt;text&gt;" fixes the world seed and start tile, so the same test maps can be regenerated before and after
/// a change; "-ll-mutators=A+B" adds tile mutators to the start tile; "-ll-hilliness=Mountainous" sets its hilliness.
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
        ForceStartTile();
    }

    private static void ForceStartTile()
    {
        PlanetTile start = Find.GameInitData.startingTile;
        if (!start.Valid)
            return;
        Tile tile = Find.WorldGrid[start];
        if (GenCommandLine.TryGetCommandLineArg("ll-mutators", out string mutators))
        {
            foreach (string name in mutators.Split('+'))
            {
                TileMutatorDef mutator = DefDatabase<TileMutatorDef>.GetNamedSilentFail(name);
                if (mutator == null)
                    Log.Warning($"[LivingLandscapes] -ll-mutators: no tile mutator named {name}.");
                else
                    tile.AddMutator(mutator);
            }
        }
        if (GenCommandLine.TryGetCommandLineArg("ll-hilliness", out string hilliness) && System.Enum.TryParse(hilliness, out Hilliness value))
            tile.hilliness = value;
    }
}
