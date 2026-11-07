using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LivingLandscapes.Compatibility;
using LivingLandscapes.Settings;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// World watershed rules (#219, Docs/WORLD-WATERSHED.md): after the world's river step, every land tile whose accumulated flow
/// Q reaches the channel threshold T = T0 / riverDensity gets a river link to the tile it drains to, so the rivers gain the
/// tributaries the river step leaves out. Realistic Planets 2 starts rivers only at the sea and follows one upstream path
/// (about 1% of land has a river); the tiles above its own River threshold that drain into another river are not drawn.
/// T0 is half of the River def's spawn threshold (the owner's choice, 2026-11-07). Joined to the rivers already there
/// (<see cref="WorldGrid.OverlayRiver"/> never narrows a link), deterministic, applied to new worlds only: a saved world keeps
/// the rivers it was made with.
/// </summary>
public static class WorldWatershed
{
    /// <summary>T0 as a share of the River def's spawn threshold with Realistic Planets 2 (its flow field, density-scaled).</summary>
    public const float TributaryShare = 0.5f;

    /// <summary>Share of existing river links that must follow Realistic Planets 2's drainage for us to take its rivers as drawn by it.</summary>
    private const float Rp2AgreementShare = 0.9f;

    // The river defs' spawn thresholds before Realistic Planets 2 scales them (its Finalizer restores them after our postfix).
    private static List<(RiverDef def, int spawn)> spawnThresholds = new List<(RiverDef, int)>();

    public static void RememberThresholds()
    {
        spawnThresholds = DefDatabase<RiverDef>.AllDefsListForReading.Where(d => d.spawnFlowThreshold > 0).Select(d => (d, d.spawnFlowThreshold)).ToList();
    }

    /// <summary>The river def a flow carries: the largest whose (density-scaled) spawn threshold it reaches, else a creek (Realistic Planets 2's lookup).</summary>
    public static RiverDef RiverFor(float flow, float density)
    {
        RiverDef best = null;
        int bestSpawn = 0;
        foreach ((RiverDef def, int spawn) in spawnThresholds)
        {
            if (spawn / Math.Max(density, 0.01f) <= flow && spawn > bestSpawn)
            {
                best = def;
                bestSpawn = spawn;
            }
        }
        return best ?? WorldFlow.Creek();
    }

    /// <summary>The River def's spawn threshold before density scaling (vanilla: 100000).</summary>
    private static float RiverSpawn()
        => spawnThresholds.Where(t => t.def.defName == "River").Select(t => (float)t.spawn).DefaultIfEmpty(100000f).First();

    public static void Apply(PlanetLayer layer)
    {
        WatershedMode mode = LivingLandscapesMod.Settings?.watershedRivers ?? WatershedMode.WithRealisticPlanets2;
        if (GenCommandLine.CommandLineArgPassed("ll-watershed-always"))
            mode = WatershedMode.Always; // dev switch for the vanilla branch in test instances
        if (mode == WatershedMode.Off || GenCommandLine.CommandLineArgPassed("ll-watershed-off") || layer != Find.WorldGrid.Surface || spawnThresholds.Count == 0)
            return;
        WorldGrid grid = Find.WorldGrid;
        int n = layer.TilesCount;
        var water = new bool[n];
        var onRiver = new bool[n];
        for (int i = 0; i < n; i++)
        {
            water[i] = grid[i].WaterCovered;
            onRiver[i] = grid[i] is SurfaceTile s && !s.potentialRivers.NullOrEmpty();
        }

        float[] flow = null;
        int[] downstream = null;
        float density = 1f, baseThreshold = 0f;
        string source = null;
        if (RealisticPlanets2Rivers.Loaded)
        {
            flow = RealisticPlanets2Rivers.Flow();
            downstream = flow == null ? null : RealisticPlanets2Rivers.Downstream(layer);
            density = RealisticPlanets2Rivers.RiverDensity();
            if (density <= 0.01f)
                return; // the player asked for no rivers
            if (flow != null && downstream != null && flow.Length >= n && DrawnByRealisticPlanets2(downstream, onRiver, n))
            {
                baseThreshold = TributaryShare * RiverSpawn();
                source = "Realistic Planets 2";
            }
        }
        if (source == null)
        {
            if (mode != WatershedMode.Always)
                return;
            // Vanilla's rivers: its own flow and downhill tree; the threshold is the River def's spawn threshold (no dice).
            flow = WorldFlow.Drainage(layer, out downstream);
            if (flow == null || downstream == null)
                return;
            density = 1f;
            baseThreshold = RiverSpawn();
            source = "vanilla";
        }

        flow = WatershedMath.NonDecreasing(downstream, flow);
        float threshold = WatershedMath.Threshold(baseThreshold, density);
        List<int> add = WatershedMath.LinksToAdd(downstream, flow, water, onRiver, threshold,
            (a, b) => grid.GetRiverDef(new PlanetTile(a, layer), new PlanetTile(b, layer), visibleOnly: false) != null,
            endInBasins: GenCommandLine.CommandLineArgPassed("ll-watershed-basins")); // dev switch to measure the closed-basin option (owner decision 2026-11-07: off)
        foreach (int i in add)
        {
            RiverDef def = RiverFor(flow[i], density);
            if (def != null)
                grid.OverlayRiver(new PlanetTile(downstream[i], layer), new PlanetTile(i, layer), def);
        }
        if (GenCommandLine.CommandLineArgPassed("ll-world-watershed"))
        {
            // Tiles to replay as tile maps (-ll-tile): inland, not too steep, evenly spaced along the added links.
            var candidates = add.Where(i => grid[i].hilliness != Hilliness.Impassable && grid[i].hilliness != Hilliness.Mountainous && !water[downstream[i]]).ToList();
            int stride = Math.Max(1, candidates.Count / 6);
            Log.Message("[LivingLandscapes] Watershed sample tiles that gained a river: " + string.Join("; ",
                Enumerable.Range(0, 6).Select(k => k * stride).Where(k => k < candidates.Count).Select(k => candidates[k])
                    .Select(i => $"{i} {grid[i].PrimaryBiome.defName} {grid[i].hilliness} {grid[i].rainfall:F0}mm flow {flow[i]:F0}")));
        }
        Log.Message($"[LivingLandscapes] Watershed rivers ({source}): {add.Count} links added where flow reaches {threshold:F0} (river density {density:P0}).");
    }

    /// <summary>True if the rivers already on the world follow Realistic Planets 2's drainage, i.e. its river step drew them (and not vanilla's after a fallback).</summary>
    private static bool DrawnByRealisticPlanets2(int[] downstream, bool[] onRiver, int n)
    {
        WorldGrid grid = Find.WorldGrid;
        int links = 0, agree = 0;
        for (int i = 0; i < n; i++)
        {
            if (!onRiver[i] || grid[i] is not SurfaceTile s)
                continue;
            foreach (SurfaceTile.RiverLink l in s.potentialRivers)
            {
                int j = l.neighbor.tileId;
                if (j < i)
                    continue;
                links++;
                if (downstream[i] == j || downstream[j] == i)
                    agree++;
            }
        }
        return links > 0 && agree >= Rp2AgreementShare * links;
    }
}
