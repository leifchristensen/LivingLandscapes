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
        Census(map);
        // "-ll-bench-plants=census" stops here; the timing runs below clear and regrow the map.
        if (GenCommandLine.TryGetCommandLineArg("ll-bench-plants", out string mode) && mode == "census")
            return;
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

        // The live cluster and shade grids must equal a rebuild from the plants standing now, after all the spawning,
        // clearing and regrowth above.
        var ecology = map.GetComponent<Flora.MapComponent_Ecology>();
        if (ecology != null && ecology.Ready)
        {
            long v0 = Stopwatch.GetTimestamp();
            float worst = ecology.VerifyAgainstRebuild(out string where);
            Log.Message($"[LivingLandscapes] Plant benchmark E: live grids against a full rebuild: largest difference {worst:0.#####} ({where}); rebuild {Micros(v0) / 1000.0:F0} ms.");
        }

        Micro(map);
    }

    /// <summary>
    /// A picture of the vegetation, 4 pixels per cell: terrain tint, then each plant by stratum (grass light, shrubs
    /// mid green, trees dark with a hue per species, water plants blue); canopy shade darkens the ground.
    /// </summary>
    public static void ExportPlantMap(Map map, string path)
    {
        const int scale = 4;
        int sizeX = map.Size.x, sizeZ = map.Size.z, width = sizeX * scale, height = sizeZ * scale;
        var ecology = map.GetComponent<Flora.MapComponent_Ecology>();
        bool shade = ecology != null && ecology.Ready;
        var pixels = new Color[width * height];
        Color Ground(int i)
        {
            TerrainDef terrain = map.terrainGrid.TerrainAt(i);
            if (terrain.IsWater)
                return new Color(0.2f, 0.35f, 0.65f);
            Color c = terrain.fertility > 0.5f ? new Color(0.62f, 0.55f, 0.38f) : new Color(0.6f, 0.58f, 0.55f);
            return shade ? Color.Lerp(c, c * 0.55f, ecology.ShadeAt(i)) : c;
        }
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                Color ground = Ground(z * sizeX + x);
                for (int dz = 0; dz < scale; dz++)
                {
                    for (int dx = 0; dx < scale; dx++)
                        pixels[(z * scale + dz) * width + x * scale + dx] = ground;
                }
            }
        }
        foreach (Plant plant in map.listerThings.ThingsInGroup(ThingRequestGroup.Plant).OfType<Plant>())
        {
            if (plant.def.IsDeadPlant)
                continue;
            Flora.PlantHabitat habitat = Flora.PlantHabitat.For(plant.def);
            float hue = (plant.def.defName.GetHashCode() & 0xFF) / 255f;
            Color color;
            int size;
            if (plant.def.plant.IsTree || habitat.stratum == Flora.PlantStratum.Canopy)
            {
                color = Color.HSVToRGB(0.22f + 0.2f * hue, 0.75f, 0.35f);
                size = 4;
            }
            else if (habitat.stratum == Flora.PlantStratum.Shrub)
            {
                color = Color.HSVToRGB(0.25f + 0.1f * hue, 0.7f, 0.55f);
                size = 3;
            }
            else
            {
                color = Color.HSVToRGB(0.17f + 0.08f * hue, 0.5f, 0.85f);
                size = 2;
            }
            IntVec3 at = plant.Position;
            for (int dz = 0; dz < size; dz++)
            {
                for (int dx = 0; dx < size; dx++)
                    pixels[(at.z * scale + dz) * width + at.x * scale + dx] = color;
            }
        }
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels);
        texture.Apply();
        texture.SaveAsPNG(path);
        UnityEngine.Object.Destroy(texture);
    }

    /// <summary>
    /// What the map's vegetation looks like: per species its count, share against vanilla's target share, mean fitness
    /// where its plants stand, and Clark-Evans ratio (mean distance to the nearest plant of the same species over the
    /// distance expected if scattered at random: under 1 clustered, 1 random, over 1 even). Also the share of cells under
    /// shade and the map's plant cover.
    /// </summary>
    public static void Census(Map map)
    {
        var ecology = map.GetComponent<Flora.MapComponent_Ecology>();
        bool ready = ecology != null && ecology.Ready;
        var plants = map.listerThings.ThingsInGroup(ThingRequestGroup.NonStumpPlant).OfType<Plant>().Where(p => !p.def.IsDeadPlant).ToList();
        int total = plants.Count;
        Log.Message($"[LivingLandscapes] Plant census: {total} plants on {map.Size.x}x{map.Size.z} ({total * 100f / map.Area:F0}% of cells), biome {map.Biome.defName}, " +
                    $"ecology {(ready ? "on, strength " + Flora.MapComponent_Ecology.Strength.ToString("0.##") : "off")}.");
        foreach (IGrouping<ThingDef, Plant> group in plants.GroupBy(p => p.def).OrderByDescending(g => g.Count()).Take(18))
        {
            ThingDef def = group.Key;
            float share = group.Count() / (float)total, target = map.wildPlantSpawner.GetCommonalityPctOfPlant(def);
            double fitness = 0;
            float available = 0f;
            if (ready)
            {
                // Share of dry cells where the species would do reasonably (fitness over 0.3): what the ground offers it.
                int dry = 0, good = 0;
                for (int i = 0; i < map.Area; i++)
                {
                    if (map.terrainGrid.TerrainAt(i).IsWater)
                        continue;
                    dry++;
                    if (ecology.Fitness(def, i) >= 0.3f)
                        good++;
                }
                available = good / (float)Mathf.Max(dry, 1);
                foreach (Plant plant in group)
                    fitness += ecology.Fitness(def, map.cellIndices.CellToIndex(plant.Position));
                fitness /= group.Count();
            }
            // Clark-Evans on a sample of up to 1500 plants (nearest neighbour among all plants of the species).
            double clark = double.NaN;
            if (group.Count() >= 30)
            {
                List<IntVec3> positions = group.Select(p => p.Position).ToList();
                var sample = positions.Count > 1500 ? positions.OrderBy(_ => Rand.Value).Take(1500).ToList() : positions;
                double sum = 0;
                foreach (IntVec3 a in sample)
                {
                    float best = float.MaxValue;
                    foreach (IntVec3 b in positions)
                    {
                        if (a == b)
                            continue;
                        float d = (a - b).LengthHorizontalSquared;
                        if (d < best)
                            best = d;
                    }
                    sum += Mathf.Sqrt(best);
                }
                double density = positions.Count / (double)map.Area;
                clark = sum / sample.Count / (0.5 / Mathf.Sqrt((float)density));
            }
            Log.Message($"[LivingLandscapes] Plant census:   {def.defName,-24} {group.Count(),6}  share {share * 100f,5:F1}% (target {target * 100f,5:F1}%)  " +
                        $"fitness {(ready ? fitness.ToString("0.00") : "  - ")} (suits {(ready ? (available * 100f).ToString("F0") : " -")}% of land)  clark-evans {(double.IsNaN(clark) ? "  - " : clark.ToString("0.00"))}");
        }
        System.IO.Directory.CreateDirectory(LayerImageExporter.Folder);
        ExportPlantMap(map, System.IO.Path.Combine(LayerImageExporter.Folder, "PlantMap.png"));
        if (ready)
        {
            int shaded = 0;
            for (int i = 0; i < map.Area; i++)
            {
                if (ecology.ShadeAt(i) > 0.3f)
                    shaded++;
            }
            Log.Message($"[LivingLandscapes] Plant census: {shaded * 100f / map.Area:F0}% of cells under canopy shade above 30%.");
        }
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
