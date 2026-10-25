using System.Collections.Generic;
using RimWorld;
using Verse;

namespace LivingLandscapes.Flora;

/// <summary>
/// Tuning of the plant ecology (<c>1.7/Defs/Flora/PlantEcology.xml</c>, one instance named LL_PlantEcology). The model is
/// layered on vanilla's wild plant spawner: it only reweights and filters what the spawner would place, so vanilla's
/// biome plant lists, commonality, density and regrowth pace stay the baseline. Every effect is blended by the player's
/// habitat strength (0 = vanilla, 1 = full).
/// </summary>
/// <summary>How much a biome's plants of one vegetation layer are favoured over vanilla's commonality (see <see cref="PlantEcologyDef.stratumScales"/>).</summary>
public class StratumScale
{
    public BiomeDef biome;
    public PlantStratum stratum;
    public float scale = 1f;
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
    /// Forest layers: multiplies the commonality of a biome's canopy, shrub or ground plants, so a forest biome can carry
    /// more trees and understory than vanilla's commonality gives (vanilla's forests are sparse). The spawner's shares are
    /// recomputed from the scaled values, so the other layers give way rather than the total growing.
    /// </summary>
    public List<StratumScale> stratumScales = new List<StratumScale>();

    public float ScaleFor(BiomeDef biome, PlantStratum stratum)
    {
        float scale = 1f;
        foreach (StratumScale entry in stratumScales)
        {
            if (entry.biome == biome && entry.stratum == stratum)
                scale *= entry.scale;
        }
        return scale;
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
    /// Carrying capacity: a cell accepts a spawn attempt with a chance between this floor (hopeless ground) and 1 (the
    /// best ground), by the commonality-weighted fitness of the biome's species there. Raises density in lush places and
    /// thins it on dry slopes, without changing the map's target total (the spawner keeps trying elsewhere).
    /// </summary>
    public float capacityFloor = 0.35f;

    /// <summary>The capacity ratio is raised to this before the floor applies: above 1, shaded and poor ground thins faster than it gains.</summary>
    public float capacityExponent = 2f;

    /// <summary>Vitality: a plant's growth rate is multiplied by this at zero fitness and by <see cref="vitalityMax"/> at 1.</summary>
    public float vitalityMin = 0.5f;
    public float vitalityMax = 1.2f;

    /// <summary>Water: sun-facing slopes dry faster; water = moisture - this x (LL_Sun - 0.7), level ground unchanged.</summary>
    public float sunDrying = 0.15f;

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
