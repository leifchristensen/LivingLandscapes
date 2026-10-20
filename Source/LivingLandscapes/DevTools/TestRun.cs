using System.Linq;
using UnityEngine;
using Verse;

namespace LivingLandscapes.DevTools;

/// <summary>
/// Automated test runs, driven by launch arguments and inactive without them:
/// "-ll-export" writes every environment layer to PNG once the first map is generated;
/// "-ll-bench-plants" times the plant spawner (<see cref="PlantBenchmark"/>);
/// "-ll-quit" then closes the game after a pause ("-ll-quit-delay=&lt;seconds&gt;", default 10) during which the
/// camera shows the whole map, so a person watching can judge the terrain.
/// </summary>
public static class TestRun
{
    private const float DefaultQuitDelay = 10f;

    private static float quitAt = -1f, roundTripAt = -1f;

    /// <summary>"-ll-bench-plants" or "-ll-bench-plants=census".</summary>
    private static bool BenchRequested => GenCommandLine.CommandLineArgPassed("ll-bench-plants") || GenCommandLine.TryGetCommandLineArg("ll-bench-plants", out _);

    public static void OnMapGenerated(Map map)
    {
        // The benchmark runs vanilla's plant step itself, which would start another test run.
        if (PlantBenchmark.Running)
            return;
        if (!GenCommandLine.CommandLineArgPassed("ll-export") && !GenCommandLine.CommandLineArgPassed("ll-quit") && !BenchRequested)
            return;
        Log.Message("[LivingLandscapes] Test run: map generated, waiting for the long event to finish.");
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            Log.Message("[LivingLandscapes] Test run: exporting and benchmarking.");
            if (GenCommandLine.CommandLineArgPassed("ll-export"))
                LayerImageExporter.ExportAll(map);
            Log.Message("[LivingLandscapes] Test run: export done.");
            if (BenchRequested)
                PlantBenchmark.Run(map);
            Log.Message("[LivingLandscapes] Test run: benchmark done.");
            if (GenCommandLine.CommandLineArgPassed("ll-check-cliffs"))
                CheckCliffCost(map);
            // "-ll-roundtrip": save the game, load it again and check the plant ecology came back whole.
            if (GenCommandLine.CommandLineArgPassed("ll-roundtrip"))
            {
                Flora.MapComponent_Ecology.AfterLoad = loaded =>
                {
                    var ecology = loaded.GetComponent<Flora.MapComponent_Ecology>();
                    bool ready = ecology != null && ecology.Ready;
                    string detail = "";
                    float diff = ready ? ecology.VerifyAgainstRebuild(out detail) : -1f;
                    Log.Message($"[LivingLandscapes] Round trip: loaded map has {loaded.listerThings.ThingsInGroup(ThingRequestGroup.NonStumpPlant).Count} plants, ecology ready {ready}, rebuild difference {diff:0.#####} ({detail}).");
                    PlantBenchmark.Census(loaded);
                    int checkedPlants = 0;
                    foreach (Thing thing in loaded.listerThings.ThingsInGroup(ThingRequestGroup.NonStumpPlant))
                    {
                        if (thing is RimWorld.Plant plant && checkedPlants++ < 2000)
                            _ = plant.GrowthRate;
                    }
                    Log.Message("[LivingLandscapes] Round trip: growth rates read for " + checkedPlants + " plants without error.");
                    quitAt = Time.realtimeSinceStartup + 1f;
                };
                // The new map's drawer is not ready in this frame; save and reload a few seconds into the running game.
                roundTripAt = Time.realtimeSinceStartup + 4f;
                return;
            }
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
        if (roundTripAt >= 0f && Time.realtimeSinceStartup >= roundTripAt)
        {
            roundTripAt = -1f;
            GameDataSaveLoader.SaveGame("LL_roundtrip");
            Log.Message("[LivingLandscapes] Round trip: saved, loading again.");
            GameDataSaveLoader.LoadGame("LL_roundtrip");
            return;
        }
        if (quitAt >= 0f && Time.realtimeSinceStartup >= quitAt)
        {
            quitAt = -1f;
            Root.Shutdown();
        }
    }

    /// <summary>Dev check: what entering a cliff cell costs a colonist, as vanilla's path follower computes it.</summary>
    private static void CheckCliffCost(Map map)
    {
        Pawn pawn = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
        var method = HarmonyLib.AccessTools.Method(typeof(Verse.AI.Pawn_PathFollower), "CostToMoveIntoCell", new[] { typeof(Pawn), typeof(IntVec3) });
        int shown = 0;
        foreach (Thing thing in map.listerThings.AllThings.Where(t => t is Geology.Building_Cliff).Take(400))
        {
            IntVec3 c = thing.Position;
            int grid = map.pathing.Normal.pathGrid.Cost(c);
            int calc = map.pathing.Normal.pathGrid.CalculatedCostAt(c, false, IntVec3.Invalid);
            float move = pawn != null && method != null ? (float)method.Invoke(null, new object[] { pawn, c }) : -1f;
            if (shown++ < 4)
                Log.Message($"[LivingLandscapes] Cliff check: {thing.def.defName} at {c}: path grid {grid}, calculated {calc}, colonist move cost {move:F0} ticks (open ground {(pawn != null ? (float)method.Invoke(null, new object[] { pawn, pawn.Position + IntVec3.East }) : -1f):F0}); things here: {string.Join(", ", c.GetThingList(map).Select(x => x.def.defName))}.");
        }
    }
}
