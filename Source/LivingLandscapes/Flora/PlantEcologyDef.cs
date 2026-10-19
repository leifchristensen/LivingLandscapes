using Verse;

namespace LivingLandscapes.Flora;

/// <summary>
/// Tuning of the plant ecology (<c>1.7/Defs/Flora/PlantEcology.xml</c>, one instance named LL_PlantEcology). The model is
/// layered on vanilla's wild plant spawner: it only reweights and filters what the spawner would place, so vanilla's
/// biome plant lists, commonality, density and regrowth pace stay the baseline. Every effect is blended by the player's
/// habitat strength (0 = vanilla, 1 = full).
/// </summary>
public class PlantEcologyDef : Def
{
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
    /// Clustering fades out as a species' share of the map's plants rises from the first of these to the second times its
    /// adjusted target share, so a species that has its fair share stops starting clumps. Vanilla cuts it off at 110% of
    /// the unadjusted target.
    /// </summary>
    public float clusterShareFadeStart = 1f;
    public float clusterShareFadeEnd = 1.3f;

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

    /// <summary>Vitality: a plant's growth rate is multiplied by this at zero fitness and by <see cref="vitalityMax"/> at 1.</summary>
    public float vitalityMin = 0.5f;
    public float vitalityMax = 1.2f;

    private static PlantEcologyDef current;

    public static PlantEcologyDef Current => current ??= DefDatabase<PlantEcologyDef>.GetNamedSilentFail("LL_PlantEcology") ?? new PlantEcologyDef();
}
