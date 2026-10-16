using UnityEngine;
using Verse;

namespace LivingLandscapes.DevTools;

/// <summary>
/// Automated test runs, driven by launch arguments and inactive without them:
/// "-ll-export" writes every environment layer to PNG once the first map is generated;
/// "-ll-quit" then closes the game after a pause ("-ll-quit-delay=&lt;seconds&gt;", default 10) during which the
/// camera shows the whole map, so a person watching can judge the terrain.
/// </summary>
public static class TestRun
{
    private const float DefaultQuitDelay = 10f;

    private static float quitAt = -1f;

    public static void OnMapGenerated(Map map)
    {
        if (!GenCommandLine.CommandLineArgPassed("ll-export") && !GenCommandLine.CommandLineArgPassed("ll-quit"))
            return;
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            if (GenCommandLine.CommandLineArgPassed("ll-export"))
                LayerImageExporter.ExportAll(map);
            if (!GenCommandLine.CommandLineArgPassed("ll-quit"))
                return;

            float delay = DefaultQuitDelay;
            if (GenCommandLine.TryGetCommandLineArg("ll-quit-delay", out string value) && float.TryParse(value, out float parsed))
                delay = Mathf.Max(0f, parsed);
            quitAt = Time.realtimeSinceStartup + delay;
            Find.CameraDriver?.SetRootPosAndSize(map.Center.ToVector3Shifted(), Mathf.Max(map.Size.x, map.Size.z) * 0.55f);
            Log.Message($"[LivingLandscapes] Test run: closing in {delay:F0} s.");
        });
    }

    /// <summary>Called every frame while a map is shown; closes the game when the pause is over.</summary>
    public static void Tick()
    {
        if (quitAt >= 0f && Time.realtimeSinceStartup >= quitAt)
        {
            quitAt = -1f;
            Root.Shutdown();
        }
    }
}
