using System.Collections.Generic;
using System.Diagnostics;
using LivingLandscapes.Environment;
using LivingLandscapes.Layers;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Flora;

/// <summary>
/// The live state the plant ecology reads (Docs/FLORA-REVIEW.md, approach A). Nothing here is saved: it is rebuilt from
/// the saved environment layers and the plants standing on the map the first time the spawner asks, then kept current
/// as plants spawn and die.
/// - Site fitness per species: a grid computed once from the ground (wetness, soil, slope, sun, fertility).
/// - Cluster density per species: a Gaussian footprint added for every plant of a clustering species and removed when
///   it dies, so "how crowded is this cell with its own kind" is one array read.
/// - Shade: the same, summed in log space for canopy plants, so removing a tree lifts its shade exactly.
/// A map without the environment layers (or with the ecology switched off) is untouched: <see cref="Ready"/> is false and
/// vanilla's spawner runs as it always did.
/// </summary>
public class MapComponent_Ecology : MapComponent
{
    private class Kernel
    {
        public int Reach;
        public float[] Values;
    }

    private HabitatSampler sampler;
    private float[] shadeLog;
    private readonly Dictionary<ThingDef, float[]> site = new Dictionary<ThingDef, float[]>();
    private readonly Dictionary<ThingDef, float[]> density = new Dictionary<ThingDef, float[]>();
    private readonly Dictionary<int, Kernel> clusterKernels = new Dictionary<int, Kernel>();
    private readonly Dictionary<ThingDef, (Kernel kernel, (int x, int z) shift)> shadeKernels = new Dictionary<ThingDef, (Kernel, (int, int))>();
    private bool tried, built;
    private int sizeX, sizeZ;
    private float latitude, capacityReference = -1f;

    public MapComponent_Ecology(Map map) : base(map)
    {
    }

    // Player strength setting, with launch arguments for test runs: -ll-ecology=off, -ll-ecology-strength=0.5.
    private static bool argsRead;
    private static float? argStrength;

    /// <summary>How strongly the ecology applies, 0 (vanilla) to 1 (full).</summary>
    public static float Strength
    {
        get
        {
            if (!argsRead)
            {
                argsRead = true;
                if (GenCommandLine.TryGetCommandLineArg("ll-ecology", out string mode) && mode == "off")
                    argStrength = 0f;
                else if (GenCommandLine.TryGetCommandLineArg("ll-ecology-strength", out string text)
                         && float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                    argStrength = parsed;
            }
            if (argStrength.HasValue)
                return Mathf.Clamp01(argStrength.Value);
            var settings = LivingLandscapesMod.Settings;
            return settings.plantEcology ? Mathf.Clamp01(settings.habitatStrength) : 0f;
        }
    }

    /// <summary>True when the ecology applies on this map right now (switched on, and the layers it reads exist).</summary>
    public bool Ready
    {
        get
        {
            if (Strength <= 0f)
                return false;
            if (!tried)
                Build();
            return built;
        }
    }

    private void Build()
    {
        tried = true;
        var environment = map.GetComponent<MapComponent_Environment>();
        float[] Grid(EnvironmentLayerDef layer) => environment != null && environment.TryGetGrid(layer, out EnvironmentGrid grid) ? grid.ToArray() : null;
        float[] moisture = Grid(EnvironmentLayerDefOf.LL_Moisture), soilDepth = Grid(EnvironmentLayerDefOf.LL_SoilDepth),
            slope = Grid(EnvironmentLayerDefOf.LL_Slope), sun = Grid(EnvironmentLayerDefOf.LL_Sun);
        if (moisture == null || soilDepth == null || slope == null || sun == null)
            return;

        sizeX = map.Size.x;
        sizeZ = map.Size.z;
        latitude = SunWorker.LatitudeOf(map);
        sampler = new HabitatSampler(map, moisture, soilDepth, slope, sun);
        shadeLog = new float[sizeX * sizeZ];

        long start = Stopwatch.GetTimestamp();
        int count = 0;
        foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Plant))
        {
            if (thing is Plant plant)
            {
                Stamp(plant, 1);
                count++;
            }
        }
        built = true;
        Log.Message($"[LivingLandscapes] Plant ecology ready on {map}: {count} plants indexed in {(Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency:F0} ms, " +
                    $"strength {Strength:0.##}.");
    }

    public void OnSpawned(Plant plant)
    {
        if (built)
            Stamp(plant, 1);
    }

    public void OnDespawning(Plant plant)
    {
        if (built)
            Stamp(plant, -1);
    }

    /// <summary>Adds (or removes) a plant's cluster footprint and shadow.</summary>
    private void Stamp(Plant plant, int sign)
    {
        ThingDef def = plant.def;
        if (def.IsDeadPlant || !plant.Position.InBounds(map))
            return;
        PlantHabitat habitat = PlantHabitat.For(def);
        IntVec3 at = plant.Position;

        float sigma = habitat.ClusterSigma(def);
        if (sigma > 0f)
        {
            if (!density.TryGetValue(def, out float[] grid))
                density[def] = grid = new float[sizeX * sizeZ];
            Apply(grid, at.x, at.z, ClusterKernel(sigma), sign);
        }
        if (habitat.shadeCast > 0f)
        {
            (Kernel kernel, (int x, int z) shift) = ShadeKernel(def, habitat);
            Apply(shadeLog, at.x + shift.x, at.z + shift.z, kernel, sign);
        }
    }

    private void Apply(float[] grid, int cx, int cz, Kernel kernel, int sign)
    {
        int reach = kernel.Reach, width = 2 * reach + 1;
        int z0 = Mathf.Max(cz - reach, 0), z1 = Mathf.Min(cz + reach, sizeZ - 1);
        int x0 = Mathf.Max(cx - reach, 0), x1 = Mathf.Min(cx + reach, sizeX - 1);
        for (int z = z0; z <= z1; z++)
        {
            int row = (z - cz + reach) * width + reach - cx;
            int cell = z * sizeX;
            for (int x = x0; x <= x1; x++)
            {
                float v = grid[cell + x] + sign * kernel.Values[row + x];
                grid[cell + x] = v < 1e-5f ? 0f : v;
            }
        }
    }

    private Kernel ClusterKernel(float sigma)
    {
        int key = Mathf.RoundToInt(sigma * 4f);
        if (clusterKernels.TryGetValue(key, out Kernel kernel))
            return kernel;
        float s = key / 4f;
        kernel = Build(Mathf.CeilToInt(PlantEcologyDef.Current.clusterReach * s), d2 => Mathf.Exp(-d2 / (2f * s * s)));
        clusterKernels[key] = kernel;
        return kernel;
    }

    private (Kernel, (int x, int z)) ShadeKernel(ThingDef def, PlantHabitat habitat)
    {
        if (shadeKernels.TryGetValue(def, out var cached))
            return cached;
        float radius = ShadeWorker.ShadeRadius(def, habitat), strength = habitat.shadeCast;
        // Light reaches the ground through the gaps in the canopy: shade adds as -ln(transmittance), so it can be removed.
        Kernel kernel = Build(Mathf.CeilToInt(radius * 2f), d2 => -Mathf.Log(1f - Mathf.Min(strength * Mathf.Exp(-d2 / (2f * radius * radius)), 0.95f)));
        Vector2 shift = ShadeWorker.ShadowShift(latitude, radius) * (PlantEcologyDef.Current.shadowShift / 0.5f);
        var result = (kernel, (Mathf.RoundToInt(shift.x), Mathf.RoundToInt(shift.y)));
        shadeKernels[def] = result;
        return result;
    }

    private static Kernel Build(int reach, System.Func<float, float> value)
    {
        int width = 2 * reach + 1;
        var values = new float[width * width];
        for (int dz = -reach; dz <= reach; dz++)
        {
            for (int dx = -reach; dx <= reach; dx++)
                values[(dz + reach) * width + dx + reach] = value(dx * dx + dz * dz);
        }
        return new Kernel { Reach = reach, Values = values };
    }

    private float[] SiteGrid(ThingDef def, PlantHabitat habitat)
    {
        if (site.TryGetValue(def, out float[] grid))
            return grid;
        grid = new float[sizeX * sizeZ];
        for (int i = 0; i < grid.Length; i++)
            grid[i] = sampler.Site(habitat, i);
        site[def] = grid;
        return grid;
    }

    /// <summary>Shade 0..1 on a cell from the plants standing now.</summary>
    public float ShadeAt(int index) => 1f - Mathf.Exp(-shadeLog[index]);

    /// <summary>Fitness 0..1 of a species on a cell: the ground's fitness times how well it bears the shade there.</summary>
    public float Fitness(ThingDef def, int index)
    {
        PlantHabitat habitat = PlantHabitat.For(def);
        float fitness = habitat.HasSiteCurves ? SiteGrid(def, habitat)[index] : 1f;
        return fitness * Mathf.Lerp(1f - ShadeAt(index), 1f, habitat.shadeTolerance);
    }

    /// <summary>
    /// Dev check: rebuilds the shade and cluster grids from the plants standing now and compares them with the live ones
    /// kept by spawns and deaths. Returns the largest difference found (0 when the bookkeeping is exact up to rounding).
    /// </summary>
    public float VerifyAgainstRebuild(out string detail)
    {
        var fresh = new MapComponent_Ecology(map);
        fresh.Build();
        float worst = 0f;
        string where = "none";
        void Compare(string name, float[] live, float[] rebuilt)
        {
            for (int i = 0; i < live.Length; i++)
            {
                float d = Mathf.Abs(live[i] - (rebuilt?[i] ?? 0f));
                if (d > worst)
                {
                    worst = d;
                    where = $"{name} at cell {i}";
                }
            }
        }
        Compare("shade", shadeLog, fresh.shadeLog);
        foreach (KeyValuePair<ThingDef, float[]> pair in density)
            Compare(pair.Key.defName, pair.Value, fresh.density.TryGetValue(pair.Key, out float[] other) ? other : null);
        detail = where;
        return worst;
    }

    private float shareScale = 1f;
    private int shareScaleTotal = -1000;

    /// <summary>
    /// How much more than its target share an ordinary species holds because other species cannot be placed (fertility
    /// limits trees, water plants need water): the target shares of species with under a quarter of their share are
    /// unfilled, so everyone else's shares run high by 1 / (1 - the unfilled targets). Recomputed every 200 plants.
    /// </summary>
    public float ShareScale(WildPlantSpawner spawner)
    {
        int total = map.listerThings.ThingsInGroup(ThingRequestGroup.NonStumpPlant).Count;
        if (total < 300)
            return 1f;
        if (Mathf.Abs(total - shareScaleTotal) < 200)
            return shareScale;
        shareScaleTotal = total;
        float stuckShare = 0f, stuckTarget = 0f;
        foreach (BiomePlantRecord record in map.Biome.wildPlants)
        {
            float target = spawner.GetCommonalityPctOfPlant(record.plant);
            float share = map.listerThings.ThingsOfDef(record.plant).Count / (float)total;
            if (share < 0.25f * target)
            {
                stuckShare += share;
                stuckTarget += target;
            }
        }
        shareScale = (1f - stuckShare) / Mathf.Max(1f - stuckTarget, 0.1f);
        return shareScale;
    }

    /// <summary>Crowding of a cell by its own species: the sum of Gaussian weights of the plants around it.</summary>
    public float ClusterDensity(ThingDef def, int index) => density.TryGetValue(def, out float[] grid) ? grid[index] : 0f;

    /// <summary>
    /// Chance (0..1) that a spawn attempt on this cell goes ahead: low on ground where none of the biome's plants do
    /// well, 1 on the best ground (the map's upper fitness range, found by sampling).
    /// </summary>
    public float Acceptance(IntVec3 cell)
    {
        int index = map.cellIndices.CellToIndex(cell);
        List<BiomePlantRecord> plants = map.BiomeAt(cell).wildPlants;
        float weights = 0f, sum = 0f;
        foreach (BiomePlantRecord record in plants)
        {
            weights += record.commonality;
            sum += record.commonality * Fitness(record.plant, index);
        }
        if (weights <= 0f)
            return 1f;
        float capacity = sum / weights;
        if (capacityReference < 0f)
            capacityReference = ReferenceCapacity();
        float floor = PlantEcologyDef.Current.capacityFloor;
        float accept = Mathf.Lerp(floor, 1f, Mathf.Clamp01(capacity / capacityReference));
        return Mathf.Lerp(1f, accept, Strength);
    }

    /// <summary>The 80th percentile of the biome's commonality-weighted fitness over sampled cells (so 20% of ground counts as best).</summary>
    private float ReferenceCapacity()
    {
        var samples = new List<float>();
        var rng = new System.Random(map.Tile);
        List<BiomePlantRecord> plants = map.Biome.wildPlants;
        for (int k = 0; k < 600; k++)
        {
            int index = rng.Next(sizeX * sizeZ);
            float weights = 0f, sum = 0f;
            foreach (BiomePlantRecord record in plants)
            {
                weights += record.commonality;
                sum += record.commonality * Fitness(record.plant, index);
            }
            samples.Add(weights > 0f ? sum / weights : 1f);
        }
        samples.Sort();
        return Mathf.Max(samples[(int)(samples.Count * 0.8f)], 0.05f);
    }

    /// <summary>Growth rate multiplier of a standing plant from how well its site suits its species.</summary>
    public float Vitality(Plant plant)
    {
        PlantHabitat habitat = PlantHabitat.For(plant.def);
        if (!habitat.HasSiteCurves && habitat.shadeTolerance >= 1f)
            return 1f;
        if (!plant.Position.InBounds(map))
            return 1f;
        float fitness = Fitness(plant.def, map.cellIndices.CellToIndex(plant.Position));
        PlantEcologyDef def = PlantEcologyDef.Current;
        return Mathf.Lerp(1f, Mathf.Lerp(def.vitalityMin, def.vitalityMax, Mathf.Sqrt(fitness)), Strength);
    }
}
