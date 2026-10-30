using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Flora;

/// <summary>
/// Tuning of the plant ecology (<c>1.7/Defs/Flora/PlantEcology.xml</c>, one instance named LL_PlantEcology). The model is
/// layered on vanilla's wild plant spawner: it only reweights and filters what the spawner would place, so vanilla's
/// biome plant lists, commonality, density and regrowth pace stay the baseline. Every effect is blended by the player's
/// habitat strength (0 = vanilla, 1 = full).
/// </summary>
/// <summary>How much a biome's plants of one vegetation layer are favoured over vanilla's commonality (see <see cref="PlantEcologyDef.stratumScales"/>).</summary>
/// <summary>A biome's multiplier on its vanilla plant density (see <see cref="PlantEcologyDef.densityScales"/>).</summary>
public class BiomeScale
{
    public BiomeDef biome;
    public float scale = 1f;
}

public class StratumScale
{
    public BiomeDef biome;
    public PlantStratum stratum;
    public float scale = 1f;

    /// <summary>
    /// Growing season (P21): when set, the scale on a tile with no growing season to speak of; <see cref="scale"/> applies at
    /// a full season, with the season factor between them. Negative: the scale does not depend on the season.
    /// </summary>
    public float seasonMin = -1f;
}

/// <summary>Competition among plants of one vegetation layer: how far a plant's crowding reaches and how hard it bites.</summary>
public class StratumThinning
{
    public PlantStratum stratum;

    /// <summary>Gaussian reach (cells) of a plant's crowding; 0 turns competition off for the layer.</summary>
    public float sigma;

    /// <summary>
    /// The weight of a candidate is multiplied by exp(-strength x crowding / its own dominance): crowding is the sum over the
    /// same-layer plants around of their dominance times the Gaussian weight at their distance. With equal dominance one plant
    /// at one sigma lowers a neighbour's chance by exp(-strength x 0.6).
    /// </summary>
    public float strength = 1f;
}

/// <summary>
/// The dispersal kernel of one seed vector (P13; PLANT-TRAITS.md section 4.2), relative to a species' base clump size:
/// how far offspring land, how often a new clump starts away from the species' own kind, and how strongly a clump draws
/// newcomers. Water carries seeds downstream: its kernel is stretched along the downhill direction and shifted down it.
/// </summary>
public class DispersalKernel
{
    public PlantDispersal vector;
    public float sigmaScale = 1f;
    public float nucleationScale = 1f;
    public float clumpScale = 1f;
    public bool downstream;
}

public class PlantEcologyDef : Def
{
    /// <summary>Dispersal kernels by seed vector; a vector not listed behaves as the base kernel (all scales 1).</summary>
    public List<DispersalKernel> dispersalKernels = new List<DispersalKernel>();

    private static readonly DispersalKernel BaseKernel = new DispersalKernel();

    public DispersalKernel KernelFor(PlantDispersal vector) => dispersalKernels.Find(k => k.vector == vector) ?? BaseKernel;

    /// <summary>
    /// Self-thinning: plants of a layer around a cell suppress newcomers of that layer, large species (higher
    /// <see cref="PlantHabitat.dominance"/>) more than small ones, so trees stand apart and a big tree keeps seedlings from
    /// its crown. Grass-scale ground plants are left out (a layer with no entry or sigma 0 does not compete).
    /// </summary>
    public List<StratumThinning> thinning = new List<StratumThinning>();

    private static bool? thinningOff;

    public StratumThinning ThinningFor(PlantStratum stratum)
    {
        // Test runs: -ll-thinning=off compares against the model without competition.
        thinningOff ??= GenCommandLine.TryGetCommandLineArg("ll-thinning", out string mode) && mode == "off";
        if (thinningOff.Value)
            return null;
        foreach (StratumThinning entry in thinning)
        {
            if (entry.stratum == stratum)
                return entry.sigma > 0f ? entry : null;
        }
        return null;
    }

    /// <summary>
    /// Forest layers: multiplies the commonality of a biome's canopy, shrub or ground plants, so a forest biome can carry
    /// more trees and understory than vanilla's commonality gives (vanilla's forests are sparse). The spawner's shares are
    /// recomputed from the scaled values, so the other layers give way rather than the total growing.
    /// </summary>
    public List<StratumScale> stratumScales = new List<StratumScale>();

    public float ScaleFor(BiomeDef biome, PlantStratum stratum, float season = 1f)
    {
        float scale = 1f;
        foreach (StratumScale entry in stratumScales)
        {
            if (entry.biome == biome && entry.stratum == stratum)
                scale *= entry.seasonMin >= 0f ? Mathf.Lerp(entry.seasonMin, entry.scale, season) : entry.scale;
        }
        return scale;
    }

    /// <summary>Growing season (P21): growing temperature range (C) whose months count (vanilla's "Outdoor growing period").</summary>
    public FloatRange growingTemperature = new FloatRange(6f, 42f);

    /// <summary>Growing season (P21): share of the year growing at which the season factor starts (0) and is full (1).</summary>
    public FloatRange seasonShare = new FloatRange(0.4f, 0.7f);

    /// <summary>Growing season (P21): a shrub's thicket weight x Lerp(this, 1, season): short-season shrubs are low and easy to cross.</summary>
    public float shortSeasonStature = 0.5f;

    private static int seasonMap = -1;
    private static float seasonCached;

    /// <summary>
    /// Growing season factor 0..1 of a map's tile (P21): g = share of the year whose average temperature is in the growing
    /// range (vanilla GenTemperature.TwelfthsInAverageTemperatureRange), G = (g - start) / (full - start), clamped.
    /// RimWorld's seasons swing more than the real analogues: a 13 C temperate tile grows 8 of 12 months, so G is full by
    /// 8.5 months (start 0.4, full 0.7). 1 where the map has no tile.
    /// </summary>
    public float SeasonOf(Map map)
    {
        if (map == null || !map.Tile.Valid)
            return 1f;
        if (seasonMap == map.uniqueID)
            return seasonCached;
        int twelfths = GenTemperature.TwelfthsInAverageTemperatureRange(map.Tile, growingTemperature.min, growingTemperature.max)?.Count ?? 12;
        seasonMap = map.uniqueID;
        seasonCached = Mathf.Clamp01((twelfths / 12f - seasonShare.min) / Mathf.Max(seasonShare.max - seasonShare.min, 0.01f));
        return seasonCached;
    }

    /// <summary>A species whose fitness on a cell is below this (times the strength) is not a candidate there.</summary>
    public float minFitness = 0.08f;

    /// <summary>Choice weight grows with fitness to this power: higher makes habitat matter more among the candidates.</summary>
    public float fitnessExponent = 1.5f;

    /// <summary>
    /// Clusters: the Gaussian dispersal distance sigma of a species is vanilla's cluster radius times this (a vanilla
    /// cluster is a disc of that radius, so sigma = radius / 2 puts the cluster edge at two sigma).
    /// </summary>
    public float sigmaPerVanillaRadius = 0.5f;

    /// <summary>
    /// Weight of one neighbour of the same species at distance d is <c>gain * exp(-d^2 / 2 sigma^2)</c>; the cluster boost
    /// saturates as the sum grows. exp(2) = 7.4 gives a single neighbour at the old cluster edge (2 sigma) a boost of 63%.
    /// </summary>
    public float clusterGainPerNeighbour = 7.4f;

    /// <summary>
    /// Seeding a new clump: the weight of a clustering species away from any plant of its own kind is multiplied by this
    /// (1 = vanilla's rate). Below 1 the same number of plants gathers into fewer, larger clumps: with a share cap on each
    /// species, many weak seeds make many tiny clumps. Scaled by the habitat strength.
    /// </summary>
    public float clusterNucleation = 0.2f;

    /// <summary>The same for the larger scale (away from the species at that scale).</summary>
    public float clusterNucleation2 = 0.5f;

    /// <summary>Weight per plant (Gaussian-weighted) of the second, larger cluster scale; there are many more plants within reach, so it is smaller.</summary>
    public float clusterGain2PerNeighbour = 0.5f;

    /// <summary>
    /// Inside a clump a clustering species is at most this many times likelier than away from its own kind (vanilla's own
    /// ratio is cluster weight x pi x radius^2, often in the thousands). Capped so the share bias can still balance the
    /// species mix; the nucleation factor sets how rarely a clump starts.
    /// </summary>
    public float clusterMaxRatio = 60f;

    /// <summary>
    /// The initial planting seeds clumps first (<see cref="PlantSeeder"/>): this share of each clustering species' target
    /// is placed as parents with Gaussian offspring before vanilla's random pass fills the rest. 0 turns the seeder off.
    /// </summary>
    public float seedShare = 0.6f;

    /// <summary>A seed settles with chance (fitness / this) ^ exponent, so ground that scores this or more always takes it.</summary>
    public float seedFitnessReference = 0.5f;

    /// <summary>Offspring per parent: the share of the cells within two sigma of a parent that its clump fills.</summary>
    public float seedFill = 0.15f;

    /// <summary>The cluster footprint of a plant reaches this many sigma (beyond it the weight is under 2%).</summary>
    public float clusterReach = 3f;

    /// <summary>Shade: a canopy plant's shadow is shifted toward the pole by this share of its radius.</summary>
    public float shadowShift = 0.5f;

    /// <summary>
    /// Multiplies every canopy plant's shadeCast (the share of light one crown blocks at its centre). Below 1 the canopy
    /// lets more light through its gaps, so the understory fills in under trees (owner 2026-10-26: forests too bare).
    /// </summary>
    public float shadeCastScale = 0.6f;

    /// <summary>Stem exclusion: thicket weight of a tree up to half grown (young poles crowd close and low; shrubs 0.85-1.4).</summary>
    public float saplingThicket = 0.5f;

    /// <summary>
    /// Forest fungi (E5, P12): a plant that dies in light (cave fungi and their forest ecotypes) on an unroofed cell lives
    /// while vanilla's ground glow x the canopy transmittance is at most this: deep shade under a closed canopy is dark
    /// enough, a gap or a felled tree is not.
    /// </summary>
    public float canopyDarkGlow = 0.1f;

    /// <summary>Self-thinning (P14): a tree's top chance to die of crowding at each size step it reaches; 0 turns it off.</summary>
    public float thinningMortality = 0.5f;

    /// <summary>Succession (P22): years for a stand's canopy to close (tau); pioneers (high pace) close in less, down to 0.6 x.</summary>
    public float canopyClosureYears = 30f;

    /// <summary>Succession: how strongly a young stand favours fast-growing non-canopy species (grass, fireweed, brambles).</summary>
    public float pioneerBoost = 3f;

    /// <summary>Succession years per game year in play (owner 2026-10-26: about 10, so a burn is a thicket within a year or two).</summary>
    public float successionSpeed = 10f;

    /// <summary>
    /// Carrying capacity (P15): the share of cells that hold a plant around a cell is biome plant density x fertility x
    /// density scale x Lerp(capacityFloor, 1, (capacity / the map's 80th percentile) ^ this) / the map's mean of that
    /// share, capacity being the commonality-weighted fitness of the biome's species there. It replaces vanilla's cap (the biome's density within
    /// 20 cells), which spreads plants evenly: above 1, poor and shaded ground opens into meadows and clearings faster.
    /// </summary>
    public float densityContrast = 2f;

    /// <summary>Share of the biome's level that the poorest ground still carries (see <see cref="densityContrast"/>).</summary>
    public float capacityFloor = 0.2f;

    /// <summary>Runtime equivalence (G5): how much of the target occupancy also gates spawn attempts in play (1 = as at map generation).</summary>
    public float runtimeAcceptance = 0f;

    /// <summary>Regrowth rate (G5): attempts so births exceed deaths at the target by this factor (0: vanilla's rate).</summary>
    public float regrowthMargin = 1.5f;

    /// <summary>Regrowth rate: at most this many times vanilla's spawn attempts (spawner cost grows with it).</summary>
    public float regrowthMax = 6f;

    /// <summary>Radius in cells over which the local share of cells with a plant is counted against the target.</summary>
    public float occupancyRadius = 4f;

    /// <summary>Multiplies a biome's vanilla plant density in the carrying capacity (owner: denser understory).</summary>
    public List<BiomeScale> densityScales = new List<BiomeScale>();

    public float DensityScaleFor(BiomeDef biome)
    {
        float scale = 1f;
        foreach (BiomeScale entry in densityScales)
        {
            if (entry.biome == biome)
                scale *= entry.scale;
        }
        return scale;
    }

    /// <summary>Vitality: a plant's growth rate is multiplied by this at zero fitness and by <see cref="vitalityMax"/> at 1.</summary>
    public float vitalityMin = 0.5f;
    public float vitalityMax = 1.2f;

    /// <summary>Water: sun-facing slopes dry faster; water = moisture - this x (LL_Sun - 0.7), level ground unchanged.</summary>
    public float sunDrying = 0.15f;

    /// <summary>
    /// Establishment: how much a species' cluster pull weights it in a cell's acceptance chance (exponent on the pull):
    /// 1 = the likely recruit decides (light-limited thickets), 0 = commonality alone, as before step 3.
    /// </summary>
    public float establishmentPull = 0f;

    /// <summary>
    /// Thicket drag (P18): path cost of a cell whose 3x3 neighbourhood is full of plants of thicket weight 1. The cost rises with the
    /// stem density around a plant (drag ~ frontal area density), so a lone bush keeps vanilla's cost; 0 switches it off.
    /// </summary>
    public float thicketDragMax = 250f;

    /// <summary>Root room: fitness x (min(1, soil depth / rooting depth))^this. 0 ignores soil depth.</summary>
    public float rootRoomExponent = 0.3f;

    private static PlantEcologyDef current;

    public static PlantEcologyDef Current => current ??= WithOverrides(DefDatabase<PlantEcologyDef>.GetNamedSilentFail("LL_PlantEcology") ?? new PlantEcologyDef());

    /// <summary>Dev: "-ll-ecology-set=field:value,field:value" overrides float fields for tuning sweeps.</summary>
    private static PlantEcologyDef WithOverrides(PlantEcologyDef def)
    {
        if (!GenCommandLine.TryGetCommandLineArg("ll-ecology-set", out string arg))
            return def;
        foreach (string pair in arg.Split(','))
        {
            string[] parts = pair.Split(':');
            System.Reflection.FieldInfo field = parts.Length == 2 ? typeof(PlantEcologyDef).GetField(parts[0]) : null;
            if (field != null && field.FieldType == typeof(float) && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
            {
                field.SetValue(def, value);
                Log.Message($"[LivingLandscapes] Plant ecology: {parts[0]} = {value} (-ll-ecology-set).");
            }
            else
            {
                Log.Warning($"[LivingLandscapes] Plant ecology: cannot apply -ll-ecology-set entry '{pair}'.");
            }
        }
        return def;
    }
}
