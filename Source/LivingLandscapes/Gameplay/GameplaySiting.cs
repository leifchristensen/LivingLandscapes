using Verse;

namespace LivingLandscapes.Gameplay;

/// <summary>Whether gameplay siting applies: the player setting, with <c>-ll-siting=off</c> for test runs (A/B against vanilla's picks).</summary>
public static class GameplaySiting
{
    private static bool argsRead;
    private static bool argOff;

    public static bool Enabled
    {
        get
        {
            if (!argsRead)
            {
                argsRead = true;
                argOff = GenCommandLine.TryGetCommandLineArg("ll-siting", out string mode) && mode == "off";
            }
            return !argOff && LivingLandscapesMod.Settings.gameplaySiting;
        }
    }
}
