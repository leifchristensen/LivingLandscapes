using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.DevTools;

/// <summary>
/// Dev benchmark for plant placement, run by "-ll-bench-plants" after a test map is generated. It measures what
/// vanilla's plant spawner costs a running game and what the operations of the planned habitat model would add, so the
/// design is chosen on numbers:
///   A. spawner ticks on the saturated map (a game with nothing to regrow);
///   B. spawner ticks while the map regrows after 40% of its plants are removed;
///   C. the initial fill (<see cref="GenStep_Plants"/>) on a cleared map;
///   D. micro-costs: reading a precomputed fitness grid, and adding or removing a Gaussian cluster footprint in a
///      per-species density grid, for several dispersal distances (sigma).
/// Results are logged as "[LivingLandscapes] Plant benchmark: ...".
/// </summary>
public static class PlantBenchmark
{
    private static double Micros(long start) => (Stopwatch.GetTimestamp() - start) * 1e6 / Stopwatch.Frequency;

    /// <summary>True while a benchmark runs: its own plant generation step must not start another test run.</summary>
    public static bool Running { get; private set; }

    public static void Run(Map map)
    {
        if (Running)
            return;
        Running = true;
        try
        {
            RunCore(map);
        }
        finally
        {
            Running = false;
        }
    }

    private static void RunCore(Map map)
    {
        WildPlantSpawner spawner = map.wildPlantSpawner;
        int PlantCount() => map.listerThings.ThingsInGroup(ThingRequestGroup.NonStumpPlant).Count;
        Log.Message($"[LivingLandscapes] Plant benchmark: map {map.Size.x}x{map.Size.z}, biome {map.Biome.defName}, {PlantCount()} plants after generation, " +
                    $"{map.Biome.wildPlants.Count} wild species, plant density {map.Biome.plantDensity:0.##}, regrow {map.Biome.wildPlantRegrowDays:0} days.");

        // A. Saturated: how much a tick costs when the spawner has nothing to do.
        double saturated = TickCost(spawner, 20000);

        // B. Regrowth: remove 40% of the plants and time ticks while the map fills back in.
        var plants = map.listerThings.ThingsInGroup(ThingRequestGroup.NonStumpPlant).ToList();
        int removed = 0;
        foreach (Thing plant in plants)
        {
            if (Rand.Chance(0.4f))
            {
                plant.Destroy();
                removed++;
            }
        }
        double regrowth = TickCost(spawner, 20000);
        int regrown = PlantCount();

        // Calls per tick, for scale: how many cells the spawner tests and how many go on to a full spawn check.
        float calls = Mathf.CeilToInt(map.Area * 0.0001f);
        Log.Message($"[LivingLandscapes] Plant benchmark A/B: saturated {saturated:F2} us per tick, regrowing {regrowth:F2} us per tick " +
                    $"(removed {removed} plants, {regrown} after 20000 ticks); the spawner tests {calls:0} cells per tick. " +
                    $"Share of a tick's budget at 60/180/360 ticks per second: " +
                    $"saturated {Share(saturated, 60):P2}/{Share(saturated, 180):P2}/{Share(saturated, 360):P2}, " +
                    $"regrowing {Share(regrowth, 60):P2}/{Share(regrowth, 180):P2}/{Share(regrowth, 360):P2}.");

        // Direct cost of one full spawn check on an unsaturated cell (the expensive path), sampled over random cells.
        foreach (Thing plant in map.listerThings.ThingsInGroup(ThingRequestGroup.NonStumpPlant).ToList())
        {
            if (Rand.Chance(0.4f))
                plant.Destroy();
        }
        float density = spawner.CurrentPlantDensityFactor, wanted = spawner.CurrentWholeMapNumDesiredPlants;
        const int checks = 4000;
        long t0 = Stopwatch.GetTimestamp();
        int spawned = 0;
        for (int i = 0; i < checks; i++)
        {
            if (spawner.CheckSpawnWildPlantAt(map.cellsInRandomOrder.Get(Rand.Range(0, map.Area)), density, wanted))
                spawned++;
        }
        double perCheck = Micros(t0) / checks;
        Log.Message($"[LivingLandscapes] Plant benchmark A/B: a full CheckSpawnWildPlantAt costs {perCheck:F1} us on a half-empty map ({spawned} of {checks} spawned).");

        // C. Initial fill: clear the map and run vanilla's plant generation step.
        foreach (Thing plant in map.listerThings.ThingsInGroup(ThingRequestGroup.NonStumpPlant).ToList())
            plant.Destroy();
        long t1 = Stopwatch.GetTimestamp();
        new GenStep_Plants().Generate(map, default);
        double fill = Micros(t1) / 1000.0;
        int filled = PlantCount();
        Log.Message($"[LivingLandscapes] Plant benchmark C: initial fill {fill:F0} ms for {filled} plants ({fill * 1000.0 / Mathf.Max(filled, 1):F1} us per plant, {fill * 1000.0 / map.Area:F2} us per cell).");

        Micro(map);
    }

    private static double Share(double microsPerTick, int ticksPerSecond) => microsPerTick / (1e6 / ticksPerSecond);

    private static double TickCost(WildPlantSpawner spawner, int ticks)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < ticks; i++)
            spawner.WildPlantSpawnerTick();
        return Micros(start) / ticks;
    }

    /// <summary>D. Micro-costs of the planned operations, in the same runtime as the game.</summary>
    private static void Micro(Map map)
    {
        int sizeX = map.Size.x, sizeZ = map.Size.z, n = sizeX * sizeZ;
        var rng = new System.Random(1);

        // A precomputed fitness grid per species: one read per species per candidate, 15 species.
        const int species = 15;
        var fitness = new float[species][];
        for (int s = 0; s < species; s++)
        {
            fitness[s] = new float[n];
            for (int i = 0; i < n; i++)
                fitness[s][i] = (float)rng.NextDouble();
        }
        const int reads = 2_000_000;
        long t0 = Stopwatch.GetTimestamp();
        float sum = 0f;
        for (int k = 0; k < reads; k++)
        {
            int cell = rng.Next(n);
            for (int s = 0; s < species; s++)
                sum += fitness[s][cell];
        }
        double perCandidateSet = Micros(t0) / reads;
        Log.Message($"[LivingLandscapes] Plant benchmark D: reading {species} species' fitness for one cell {perCandidateSet:F3} us (checksum {sum:0}); " +
                    $"memory for {species} fitness grids {species * n * 4 / 1048576.0:F1} MB.");

        // A Gaussian cluster footprint added (or removed) when a plant of the species spawns (or dies).
        foreach (float sigma in new[] { 4f, 8f, 17f, 24f })
        {
            var density = new float[n];
            int reach = Mathf.CeilToInt(3f * sigma);
            const int footprints = 4000;
            long t = Stopwatch.GetTimestamp();
            int cells = 0;
            for (int f = 0; f < footprints; f++)
            {
                int cx = rng.Next(sizeX), cz = rng.Next(sizeZ);
                for (int z = Mathf.Max(cz - reach, 0); z <= Mathf.Min(cz + reach, sizeZ - 1); z++)
                {
                    for (int x = Mathf.Max(cx - reach, 0); x <= Mathf.Min(cx + reach, sizeX - 1); x++)
                    {
                        float d2 = (x - cx) * (x - cx) + (z - cz) * (z - cz);
                        density[z * sizeX + x] += Mathf.Exp(-d2 / (2f * sigma * sigma));
                        cells++;
                    }
                }
            }
            double per = Micros(t) / footprints;
            Log.Message($"[LivingLandscapes] Plant benchmark D: Gaussian footprint sigma {sigma:0} (reach {reach}) costs {per:F1} us per spawn or removal " +
                        $"({cells / footprints} cells, exp per cell); a density lookup is one array read. One grid is {n * 4 / 1048576.0:F2} MB.");
        }

        // The same footprint with a lookup table instead of exp per cell, as a production version would use.
        {
            float sigma = 8f;
            int reach = Mathf.CeilToInt(3f * sigma);
            var kernel = new float[(2 * reach + 1) * (2 * reach + 1)];
            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                    kernel[(dz + reach) * (2 * reach + 1) + dx + reach] = Mathf.Exp(-(dx * dx + dz * dz) / (2f * sigma * sigma));
            }
            var density = new float[n];
            const int footprints = 4000;
            long t = Stopwatch.GetTimestamp();
            for (int f = 0; f < footprints; f++)
            {
                int cx = rng.Next(sizeX), cz = rng.Next(sizeZ);
                for (int z = Mathf.Max(cz - reach, 0); z <= Mathf.Min(cz + reach, sizeZ - 1); z++)
                {
                    int row = (z - cz + reach) * (2 * reach + 1) + reach - cx;
                    for (int x = Mathf.Max(cx - reach, 0); x <= Mathf.Min(cx + reach, sizeX - 1); x++)
                        density[z * sizeX + x] += kernel[row + x];
                }
            }
            Log.Message($"[LivingLandscapes] Plant benchmark D: sigma 8 footprint with a precomputed kernel {Micros(t) / footprints:F1} us per spawn or removal.");
        }
    }
}
