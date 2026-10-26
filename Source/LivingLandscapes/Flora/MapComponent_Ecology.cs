using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
/// - Thicket stems: each plant's thicket weight on its own cell, for the path cost of dense shrubs (P18).
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
    private float[] stems;
    private MapComponent_StandAge standAge;

    /// <summary>Downhill direction per cell (LL_Aspect, radians counter-clockwise from east), for seeds carried by water; null without the layer.</summary>
    private float[] aspect;

    private readonly Dictionary<(int sigma, int octant), Kernel> downstreamKernels = new Dictionary<(int, int), Kernel>();
    private readonly Dictionary<ThingDef, float[]> site = new Dictionary<ThingDef, float[]>();
    private readonly Dictionary<ThingDef, float[]> density = new Dictionary<ThingDef, float[]>();
    private readonly Dictionary<ThingDef, float[]> density2 = new Dictionary<ThingDef, float[]>();
    private readonly float[][] crowding = new float[3][];
    private readonly Dictionary<int, Kernel> clusterKernels = new Dictionary<int, Kernel>();
    private readonly Dictionary<ThingDef, (Kernel kernel, (int x, int z) shift)> shadeKernels = new Dictionary<ThingDef, (Kernel, (int, int))>();
    private bool built;
    private int sizeX, sizeZ;
    private float latitude, capacityReference = -1f, meanShare = 1f;

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

    private int nextAttempt;

    /// <summary>True when the ecology applies on this map right now (switched on, and the layers it reads exist).</summary>
    public bool Ready
    {
        get
        {
            if (Strength <= 0f)
                return false;
            if (!built)
                Build();
            return built;
        }
    }

    /// <summary>
    /// Builds the grids from the saved environment layers and the plants standing. While a save is loading the layers may
    /// not be restored yet, and maps made without Living Landscapes never have them: either way nothing is built and the
    /// attempt is repeated at most every 2000 ticks, so neither case costs anything or fails for good.
    /// </summary>
    private void Build()
    {
        int now = Find.TickManager?.TicksGame ?? 0;
        if (now < nextAttempt)
            return;
        nextAttempt = now + 2000;
        var environment = map.GetComponent<MapComponent_Environment>();
        if (environment == null)
            return;
        EnvironmentLayerDef[] needed = { EnvironmentLayerDefOf.LL_Moisture, EnvironmentLayerDefOf.LL_SoilDepth, EnvironmentLayerDefOf.LL_Sun };
        foreach (EnvironmentLayerDef layer in needed)
        {
            if (!environment.TryGetGrid(layer, out _))
                return;
        }
        float[] Grid(EnvironmentLayerDef layer) => environment.TryGetGrid(layer, out EnvironmentGrid grid) ? grid.ToArray() : null;
        float[] moisture = Grid(needed[0]), soilDepth = Grid(needed[1]), sun = Grid(needed[2]);

        sizeX = map.Size.x;
        sizeZ = map.Size.z;
        latitude = SunWorker.LatitudeOf(map);
        sampler = new HabitatSampler(map, moisture, soilDepth, sun);
        shadeLog = new float[sizeX * sizeZ];
        stems = new float[sizeX * sizeZ];
        aspect = Grid(EnvironmentLayerDefOf.LL_Aspect);

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
        standAge = map.GetComponent<MapComponent_StandAge>();
        standAge?.LayersReady();
        // The path grid was computed before the stems existed (a loaded save builds this on the spawner's first call).
        if (PlantEcologyDef.Current.thicketDragMax > 0f)
        {
            for (int i = 0; i < stems.Length; i++)
            {
                if (stems[i] > 0f)
                    map.pathing.RecalculatePerceivedPathCostAt(map.cellIndices.IndexToCell(i));
            }
        }
        Log.Message($"[LivingLandscapes] Plant ecology ready on {map}: {count} plants indexed in {(Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency:F0} ms, " +
                    $"strength {Strength:0.##}.");
    }

    /// <summary>For tests: runs the action once the map has finished loading from a save.</summary>
    public static System.Action<Map> AfterLoad;

    public override void FinalizeInit()
    {
        base.FinalizeInit();
        if (AfterLoad == null)
            return;
        System.Action<Map> action = AfterLoad;
        AfterLoad = null;
        LongEventHandler.ExecuteWhenFinished(() => action(map));
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
            int index = map.cellIndices.CellToIndex(at);
            Apply(grid, at.x, at.z, habitat.Kernel.downstream && aspect != null ? DownstreamKernel(sigma, aspect[index]) : ClusterKernel(sigma), sign);
        }
        if (habitat.clusterRadius2 > 0f && sigma > 0f)
        {
            if (!density2.TryGetValue(def, out float[] grid2))
                density2[def] = grid2 = new float[sizeX * sizeZ];
            Apply(grid2, at.x, at.z, ClusterKernel(habitat.clusterRadius2), sign);
        }
        StratumThinning thinning = PlantEcologyDef.Current.ThinningFor(habitat.stratum);
        if (thinning != null)
        {
            int layer = (int)habitat.stratum;
            crowding[layer] ??= new float[sizeX * sizeZ];
            Apply(crowding[layer], at.x, at.z, CrowdingKernel(thinning.sigma, habitat.dominance), sign);
        }
        if (habitat.shadeCast > 0f)
        {
            (Kernel kernel, (int x, int z) shift) = ShadeKernel(def, habitat);
            Apply(shadeLog, at.x + shift.x, at.z + shift.z, kernel, sign);
        }
        if (habitat.thicket > 0f)
        {
            int index = map.cellIndices.CellToIndex(at);
            // Growing season (P21): short-season shrubs stay low, so their thickets are easier to cross.
            PlantEcologyDef tuning = PlantEcologyDef.Current;
            float stature = Mathf.Lerp(tuning.shortSeasonStature, 1f, tuning.SeasonOf(map));
            float v = stems[index] + sign * habitat.thicket * stature;
            stems[index] = v < 1e-5f ? 0f : v;
            // Vanilla recalculates only the plant's own cell; the drag of every neighbour with stems changes too.
            if (built && PlantEcologyDef.Current.thicketDragMax > 0f)
            {
                foreach (IntVec3 offset in GenAdj.AdjacentCellsAndInside)
                {
                    IntVec3 c = at + offset;
                    if (c.InBounds(map))
                        map.pathing.RecalculatePerceivedPathCostAt(c);
                }
            }
        }
    }

    /// <summary>
    /// Path cost of the thicket at a cell (P18): drag grows with the frontal area density of stems, so the cost is
    /// <see cref="PlantEcologyDef.thicketDragMax"/> x the stem density of the 3x3 around a cell that holds stems itself.
    /// A path cut through a thicket is open ground again. 0 where there is no thicket or the ecology is off.
    /// </summary>
    public int ThicketCost(IntVec3 c)
    {
        if (!built || stems == null)
            return 0;
        int index = map.cellIndices.CellToIndex(c);
        if (stems[index] <= 0f)
            return 0;
        float sum = 0f;
        int x0 = Mathf.Max(c.x - 1, 0), x1 = Mathf.Min(c.x + 1, sizeX - 1);
        int z0 = Mathf.Max(c.z - 1, 0), z1 = Mathf.Min(c.z + 1, sizeZ - 1);
        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
                sum += stems[z * sizeX + x];
        }
        return Mathf.RoundToInt(PlantEcologyDef.Current.thicketDragMax * sum / 9f);
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

    private readonly Dictionary<(int, int), Kernel> crowdingKernels = new Dictionary<(int, int), Kernel>();

    private Kernel CrowdingKernel(float sigma, float dominance)
    {
        var key = (Mathf.RoundToInt(sigma * 4f), Mathf.RoundToInt(dominance * 8f));
        if (crowdingKernels.TryGetValue(key, out Kernel kernel))
            return kernel;
        float s = key.Item1 / 4f, weight = key.Item2 / 8f;
        kernel = Build(Mathf.CeilToInt(PlantEcologyDef.Current.clusterReach * s), d2 => weight * Mathf.Exp(-d2 / (2f * s * s)));
        crowdingKernels[key] = kernel;
        return kernel;
    }

    /// <summary>
    /// Competition on a cell from the plants of one layer around it (dominance-weighted, Gaussian by distance); 0 where the
    /// layer does not compete.
    /// </summary>
    public float CrowdingAt(PlantStratum stratum, int index)
    {
        float[] grid = crowding[(int)stratum];
        return grid != null ? grid[index] : 0f;
    }

    /// <summary>
    /// Seeds carried by water (hydrochory) land downstream: a Gaussian stretched along the downhill direction (sigma x
    /// <see cref="DownstreamStretch"/>), narrowed across it, and centred one sigma downhill. One kernel per eighth of the
    /// compass and sigma, cached.
    /// </summary>
    private Kernel DownstreamKernel(float sigma, float downhill)
    {
        int octant = ((Mathf.RoundToInt(downhill / (Mathf.PI / 4f)) % 8) + 8) % 8;
        int key = Mathf.RoundToInt(sigma * 4f);
        if (downstreamKernels.TryGetValue((key, octant), out Kernel kernel))
            return kernel;
        float s = key / 4f, along = s * DownstreamStretch, across = s * DownstreamNarrow;
        float angle = octant * Mathf.PI / 4f, cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
        int reach = Mathf.CeilToInt(PlantEcologyDef.Current.clusterReach * along + s);
        var values = new float[(2 * reach + 1) * (2 * reach + 1)];
        for (int dz = -reach; dz <= reach; dz++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                float a = dx * cos + dz * sin - s, c = -dx * sin + dz * cos;
                values[(dz + reach) * (2 * reach + 1) + dx + reach] = Mathf.Exp(-(a * a) / (2f * along * along) - (c * c) / (2f * across * across));
            }
        }
        kernel = new Kernel { Reach = reach, Values = values };
        downstreamKernels[(key, octant)] = kernel;
        return kernel;
    }

    private const float DownstreamStretch = 1.5f, DownstreamNarrow = 0.7f;

    /// <summary>
    /// Where a seed of a water-carried species lands from a parent at this cell: an offset drawn from the same stretched,
    /// downstream-shifted Gaussian as <see cref="DownstreamKernel"/> (isotropic for other species), used by the seeder.
    /// </summary>
    public IntVec3 OffspringCell(PlantHabitat habitat, IntVec3 parent, float sigma)
    {
        if (!habitat.Kernel.downstream || aspect == null || !parent.InBounds(map))
            return new IntVec3(Mathf.RoundToInt(parent.x + Rand.Gaussian(0f, sigma)), 0, Mathf.RoundToInt(parent.z + Rand.Gaussian(0f, sigma)));
        float angle = aspect[map.cellIndices.CellToIndex(parent)];
        float a = sigma + Rand.Gaussian(0f, sigma * DownstreamStretch), c = Rand.Gaussian(0f, sigma * DownstreamNarrow);
        return new IntVec3(Mathf.RoundToInt(parent.x + a * Mathf.Cos(angle) - c * Mathf.Sin(angle)), 0, Mathf.RoundToInt(parent.z + a * Mathf.Sin(angle) + c * Mathf.Cos(angle)));
    }

    private (Kernel, (int x, int z)) ShadeKernel(ThingDef def, PlantHabitat habitat)
    {
        if (shadeKernels.TryGetValue(def, out var cached))
            return cached;
        float radius = ShadeWorker.ShadeRadius(def, habitat), strength = habitat.shadeCast * PlantEcologyDef.Current.shadeCastScale;
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

    /// <summary>Light reaching the ground on a cell now: LL_Sun x canopy transmittance (what the light curves read).</summary>
    public float LightAt(int index) => sampler.Light(index, Mathf.Exp(-shadeLog[index]));

    /// <summary>Fitness 0..1 of a species on a cell: the ground's fitness (water, root room) times its light curve at the light reaching the ground now.</summary>
    public float Fitness(ThingDef def, int index)
    {
        PlantHabitat habitat = PlantHabitat.For(def);
        return SiteGrid(def, habitat)[index] * habitat.light.Response(sampler.Light(index, Mathf.Exp(-shadeLog[index])));
    }

    /// <summary>
    /// Succession (P22, P17): how a stand's age since its last fire or windthrow weighs a species' establishment on a
    /// cell. Canopy species establish with the stand's maturity m = 1 - exp(-age / tau), tau the canopy closure time,
    /// shorter for fast-growing pioneers (pace); other species are boosted while the stand is young, the more the faster
    /// they grow: 1 + pioneerBoost x (1 - m) x pace. 1 on old ground and where the map has no stand age.
    /// </summary>
    public float Succession(ThingDef def, int index)
    {
        if (standAge == null)
            return 1f;
        float age = standAge.Age(index);
        if (age >= 1e6f)
            return 1f;
        PlantHabitat habitat = PlantHabitat.For(def);
        PlantEcologyDef tuning = PlantEcologyDef.Current;
        float factor;
        if (habitat.stratum == PlantStratum.Canopy)
            factor = Maturity(age, tuning.canopyClosureYears * Mathf.Lerp(1.4f, 0.6f, habitat.pace));
        else
            factor = 1f + tuning.pioneerBoost * (1f - Maturity(age, tuning.canopyClosureYears)) * habitat.pace;
        return Mathf.Lerp(1f, factor, Strength);
    }

    /// <summary>Stand maturity 0..1 at an age (years) for a canopy closure time tau: 1 - exp(-age / tau).</summary>
    public static float Maturity(float age, float tau) => 1f - Mathf.Exp(-age / Mathf.Max(tau, 0.1f));

    /// <summary>Stand age in succession years on a cell (very old where the map has none).</summary>
    public float StandAge(int index) => standAge?.Age(index) ?? float.MaxValue;

    /// <summary>
    /// How likely a species is to establish on a cell: its fitness there times the stand's succession factor. Spawning
    /// and seeding read this; growth (<see cref="Vitality"/>) reads fitness alone.
    /// </summary>
    public float Establishment(ThingDef def, int index) => Fitness(def, index) * Succession(def, index);

    /// <summary>
    /// Dev check: rebuilds the shade and cluster grids from the plants standing now and compares them with the live ones
    /// kept by spawns and deaths. Returns the largest difference found (0 when the bookkeeping is exact up to rounding).
    /// </summary>
    public float VerifyAgainstRebuild(out string detail)
    {
        var fresh = new MapComponent_Ecology(map);
        fresh.Build();
        if (!fresh.built)
        {
            detail = "rebuild not possible";
            return -1f;
        }
        float worst = 0f;
        string where = "none";
        void Compare(string name, float[] live, float[] rebuilt)
        {
            if (live == null && rebuilt == null)
                return;
            for (int i = 0; i < (live ?? rebuilt).Length; i++)
            {
                float d = Mathf.Abs((live?[i] ?? 0f) - (rebuilt?[i] ?? 0f));
                if (d > worst)
                {
                    worst = d;
                    where = $"{name} at cell {i}";
                }
            }
        }
        Compare("shade", shadeLog, fresh.shadeLog);
        for (int layer = 0; layer < 3; layer++)
            Compare("crowding " + (PlantStratum)layer, crowding[layer], fresh.crowding[layer]);
        foreach (KeyValuePair<ThingDef, float[]> pair in density)
            Compare(pair.Key.defName, pair.Value, fresh.density.TryGetValue(pair.Key, out float[] other) ? other : null);
        foreach (KeyValuePair<ThingDef, float[]> pair in density2)
            Compare(pair.Key.defName + " (second scale)", pair.Value, fresh.density2.TryGetValue(pair.Key, out float[] other2) ? other2 : null);
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

    /// <summary>Crowding at the second, larger cluster scale.</summary>
    public float ClusterDensity2(ThingDef def, int index) => density2.TryGetValue(def, out float[] grid) ? grid[index] : 0f;

    /// <summary>Crowding of a cell by its own species: the sum of Gaussian weights of the plants around it.</summary>
    public float ClusterDensity(ThingDef def, int index) => density.TryGetValue(def, out float[] grid) ? grid[index] : 0f;

    /// <summary>
    /// How strongly the species' own plants draw a newcomer to this cell (P13), as a factor on its weight: the vector's
    /// nucleation rate away from its kind, rising inside a clump to the capped cluster ratio times the vector's clump
    /// strength, and at the second, larger scale for species that set one. 1 for species that do not cluster.
    /// </summary>
    public float ClusterPull(ThingDef def, int index)
    {
        PlantHabitat habitat = PlantHabitat.For(def);
        if (habitat.ClusterSigma(def) <= 0f)
            return 1f;
        PlantEcologyDef tuning = PlantEcologyDef.Current;
        float strength = Strength;
        float crowding = ClusterDensity(def, index) * tuning.clusterGainPerNeighbour;
        float inside = Mathf.Lerp(1f, Mathf.Min(habitat.ClusterRatio(def), tuning.clusterMaxRatio) * habitat.Kernel.clumpScale, strength);
        float outside = Mathf.Lerp(1f, tuning.clusterNucleation * habitat.NucleationScale, strength);
        float pull = outside + (inside - outside) * (1f - Mathf.Exp(-crowding));
        // The larger scale: forests and clearings.
        if (habitat.clusterRadius2 > 0f && habitat.clusterStrength2 > 1f)
        {
            float wide = ClusterDensity2(def, index) * tuning.clusterGain2PerNeighbour;
            float inside2 = Mathf.Lerp(1f, habitat.clusterStrength2, strength);
            float outside2 = Mathf.Lerp(1f, tuning.clusterNucleation2, strength);
            pull *= outside2 + (inside2 - outside2) * (1f - Mathf.Exp(-wide));
        }
        return pull;
    }

    /// <summary>
    /// Carrying capacity (P15): the share of cells around <paramref name="cell"/> that should hold a plant. The biome's
    /// vanilla density (x fertility, x the biome's density scale) sets the level, and the habitat shapes it: capacity is
    /// the commonality-weighted fitness of the biome's species here (each weighted by how strongly its own clumps draw
    /// recruits). Against the map's best ground (the 80th percentile) it gives a share from
    /// <see cref="PlantEcologyDef.capacityFloor"/> to 1, raised to <see cref="PlantEcologyDef.densityContrast"/>: good
    /// ground closes up, poor and shaded ground opens into meadows and clearings, and deserts stay sparse because their
    /// vanilla density is low.
    /// </summary>
    public float TargetOccupancy(IntVec3 cell)
    {
        int index = map.cellIndices.CellToIndex(cell);
        PlantEcologyDef tuning = PlantEcologyDef.Current;
        if (capacityReference < 0f)
            capacityReference = ReferenceCapacity();
        BiomeDef biome = map.BiomeAt(cell);
        float fertility = map.fertilityGrid.FertilityAt(cell);
        float level = biome.plantDensity * map.wildPlantSpawner.CurrentPlantDensityFactor * fertility * tuning.DensityScaleFor(biome);
        // Divided by the map's mean share, so the map keeps its vanilla total by default and only redistributes it;
        // densityScales is the deliberate boost.
        return Mathf.Clamp01(level * Share(Capacity(index, biome)) / meanShare);
    }

    /// <summary>Chance (0..1) that a spawn attempt goes ahead: the target occupancy, so one pass over the map fills each place to it.</summary>
    public float Acceptance(IntVec3 cell) => Mathf.Lerp(1f, TargetOccupancy(cell), Strength);

    /// <summary>
    /// Replaces vanilla's local saturation (its biome density within 20 cells): saturated when the share of fertile cells
    /// within <see cref="PlantEcologyDef.occupancyRadius"/> that hold a plant reaches the target occupancy.
    /// </summary>
    public bool Saturated(IntVec3 cell)
    {
        int count = GenRadial.NumCellsInRadius(PlantEcologyDef.Current.occupancyRadius), cells = 0, plants = 0;
        for (int i = 0; i < count; i++)
        {
            IntVec3 c = cell + GenRadial.RadialPattern[i];
            if (!c.InBounds(map) || map.fertilityGrid.FertilityAt(c) <= 0f)
                continue;
            cells++;
            if (c.GetPlant(map) != null)
                plants++;
        }
        return cells == 0 || plants >= TargetOccupancy(cell) * cells;
    }

    private float Capacity(int index, BiomeDef biome)
    {
        float weights = 0f, sum = 0f;
        foreach (BiomePlantRecord record in biome.wildPlants)
        {
            float weight = record.commonality * Mathf.Pow(ClusterPull(record.plant, index), PlantEcologyDef.Current.establishmentPull);
            weights += weight;
            sum += weight * Fitness(record.plant, index);
        }
        return weights > 0f ? sum / weights : 1f;
    }

    /// <summary>Share of the biome's level a cell of this capacity carries, from capacityFloor to 1 at the map's best ground.</summary>
    private float Share(float capacity)
    {
        PlantEcologyDef tuning = PlantEcologyDef.Current;
        return Mathf.Lerp(tuning.capacityFloor, 1f, Mathf.Pow(Mathf.Clamp01(capacity / capacityReference), tuning.densityContrast));
    }

    /// <summary>
    /// The 80th percentile of the capacity over sampled fertile cells (commonality-weighted, without cluster pull): the
    /// map's best ground. Also sets <see cref="meanShare"/>, the mean share over the same cells.
    /// </summary>
    private float ReferenceCapacity()
    {
        var rng = new System.Random(map.Tile);
        List<BiomePlantRecord> plants = map.Biome.wildPlants;
        var samples = new List<float>();
        for (int k = 0; k < 4000 && samples.Count < 800; k++)
        {
            int index = rng.Next(sizeX * sizeZ);
            if (map.fertilityGrid.FertilityAt(map.cellIndices.IndexToCell(index)) <= 0f)
                continue;
            float weights = 0f, sum = 0f;
            foreach (BiomePlantRecord record in plants)
            {
                weights += record.commonality;
                sum += record.commonality * Fitness(record.plant, index);
            }
            samples.Add(weights > 0f ? sum / weights : 1f);
        }
        if (samples.Count == 0)
            return 1f;
        samples.Sort();
        capacityReference = Mathf.Max(samples[(int)(samples.Count * 0.8f)], 0.02f);
        meanShare = Mathf.Max(samples.Average(Share), 0.05f);
        return capacityReference;
    }

    /// <summary>
    /// Growth rate multiplier of a standing plant from how well its site suits its species: 1 for plants without habitat
    /// data, so crops, modded and DLC plants grow exactly as vanilla has them.
    /// </summary>
    public float Vitality(Plant plant)
    {
        PlantHabitat habitat = PlantHabitat.For(plant.def);
        if (!habitat.HasSiteCurves || !plant.Position.InBounds(map))
            return 1f;
        float fitness = Fitness(plant.def, map.cellIndices.CellToIndex(plant.Position));
        PlantEcologyDef def = PlantEcologyDef.Current;
        return Mathf.Lerp(1f, Mathf.Lerp(def.vitalityMin, def.vitalityMax, Mathf.Sqrt(fitness)), Strength);
    }
}
