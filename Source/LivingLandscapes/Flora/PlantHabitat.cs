using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Flora;

/// <summary>Which layer of the vegetation a plant belongs to. Strata are placed canopy first; each casts shade on the next.</summary>
public enum PlantStratum
{
    Canopy,
    Shrub,
    Ground,
}

/// <summary>How a plant's seeds travel (P13): the shape of its dispersal kernel.</summary>
public enum PlantDispersal
{
    Unset,
    Gravity,
    Animal,
    Wind,
    Water,
    Clonal,
}

/// <summary>Preferred value of one environment factor, and how far from it the plant still does well.</summary>
public class HabitatCurve
{
    public float optimum;

    /// <summary>Distance from the optimum at which the response has fallen to about 61% (one standard deviation).</summary>
    public float tolerance = 0.3f;

    /// <summary>
    /// True: the response stays 1 above the optimum (a saturating response, like photosynthesis against light: a sun
    /// plant gains nothing above saturation and loses below it). False: it falls on both sides (shade plants are harmed
    /// by strong light).
    /// </summary>
    public bool saturating;

    /// <summary>
    /// The fundamental response (P15): rises to 1 at the optimum and stays there, whatever <see cref="saturating"/> says.
    /// More of a limiting resource never lowers what a site can carry; it only changes which species wins it.
    /// </summary>
    public float ResponseUpTo(float value)
    {
        if (value >= optimum)
            return 1f;
        float d = (value - optimum) / Mathf.Max(tolerance, 0.001f);
        return Mathf.Exp(-0.5f * d * d);
    }

    public float Response(float value)
    {
        if (saturating && value >= optimum)
            return 1f;
        float d = (value - optimum) / Mathf.Max(tolerance, 0.001f);
        return Mathf.Exp(-0.5f * d * d);
    }
}

/// <summary>
/// What a plant needs from the ground and the sky, set per species in XML as a DefModExtension. A curve left out
/// means the plant does not care about that factor. Plants without this extension get defaults from vanilla's own
/// fields (<see cref="For"/>), so modded plants still spawn.
/// </summary>
public class PlantHabitat : DefModExtension
{
    public PlantStratum stratum = PlantStratum.Ground;

    /// <summary>Competitive weight against other plants of the same stratum on the same cell.</summary>
    public float dominance = 1f;

    /// <summary>
    /// Water in the root zone, 0 (dry) to 1 (saturated, waterlogged): LL_Moisture, drier on sun-facing slopes
    /// (<see cref="PlantEcologyDef.sunDrying"/>). Null: the plant does not care.
    /// </summary>
    public HabitatCurve water;

    /// <summary>
    /// Light at the ground, LL_Sun times canopy transmittance (P12): 0 dark, 0.7 open level ground, 1 a sun-facing slope.
    /// Null takes a default from the stratum (<see cref="Derive"/>).
    /// </summary>
    public HabitatCurve light;

    /// <summary>Metres of soil the roots want; shallower soil limits the plant (<see cref="PlantEcologyDef.rootRoomExponent"/>). 0: from the stratum.</summary>
    public float rootingDepth;

    /// <summary>Life strategy, 0 slow and stress tolerant .. 1 fast pioneer (PLANT-TRAITS.md). Negative: from vanilla's grow days.</summary>
    public float pace = -1f;

    /// <summary>0 few large seeds .. 1 many small. Negative: from the stratum.</summary>
    public float seed = -1f;

    /// <summary>How the seeds travel. Unset: from the stratum.</summary>
    public PlantDispersal dispersal;

    /// <summary>
    /// How much one plant adds to a thicket's stem density (P18): 1 a typical shrub, more for tangled thorny canes
    /// (brambles 1.4), less for open shrubs and low herbs.
    /// Negative: from the stratum (shrubs 1, others 0).
    /// </summary>
    public float thicket = -1f;

    /// <summary>
    /// Rate of new clumps away from the species' own kind, relative to its seed vector's (<see cref="NucleationScale"/>).
    /// Below 1: fewer, larger patches (the seeder grows each clump larger to keep the species' share).
    /// </summary>
    public float nucleation = 1f;

    /// <summary>Share of light this plant blocks beneath it, 0..1. Feeds LL_Shade for the strata placed after it.</summary>
    public float shadeCast;

    /// <summary>Radius in cells over which the shade spreads. 0 takes it from the plant's size.</summary>
    public float shadeRadius;

    /// <summary>
    /// Seed dispersal: standard deviation sigma (cells) of the clump around a parent plant; 0 takes it from vanilla's
    /// cluster radius (<see cref="PlantEcologyDef.sigmaPerVanillaRadius"/>). Only species vanilla clusters (a positive
    /// wildClusterRadius) are clustered.
    /// </summary>
    public float clusterRadius;

    /// <summary>
    /// How much clustering matters, 0 (even scatter) to 1 (vanilla's strength: a plant is thousands of times more
    /// likely next to its own kind than far from it). Intermediate values scale that boost down.
    /// </summary>
    public float clusterStrength = 1f;

    /// <summary>
    /// True for species with habitat XML. Derived defaults (modded, DLC and crop plants) still sort by light and root
    /// room, but vitality and the forest layer scales apply only to described wild plants.
    /// </summary>
    public bool HasSiteCurves => !derivedFromVanilla;

    private bool derivedFromVanilla;

    /// <summary>
    /// A second, larger dispersal scale (sigma, cells; 0 none) for patchiness beyond the clump: forests and clearings,
    /// meadows. A cell near many plants of the species at this scale is boosted up to <see cref="clusterStrength2"/> times.
    /// Costs one more footprint per plant, so only species that need it (canopy trees) set it.
    /// </summary>
    public float clusterRadius2;
    public float clusterStrength2 = 1f;

    private static readonly Dictionary<ThingDef, PlantHabitat> derived = new Dictionary<ThingDef, PlantHabitat>();

    private bool resolved;

    /// <summary>The species' habitat: its XML extension, or defaults derived from vanilla fields; unset fields filled in.</summary>
    public static PlantHabitat For(ThingDef plant)
    {
        var habitat = plant.GetModExtension<PlantHabitat>();
        if (habitat == null && !derived.TryGetValue(plant, out habitat))
        {
            habitat = Derive(plant);
            derived[plant] = habitat;
        }
        if (!habitat.resolved)
            habitat.Resolve(plant);
        return habitat;
    }

    private static PlantHabitat Derive(ThingDef plant)
    {
        PlantProperties p = plant.plant;
        bool tree = p != null && p.IsTree;
        bool bush = !tree && p != null && p.visualSizeRange.max > 1.2f;
        return new PlantHabitat
        {
            stratum = tree ? PlantStratum.Canopy : bush ? PlantStratum.Shrub : PlantStratum.Ground,
            shadeCast = tree ? 0.5f : 0f,
            derivedFromVanilla = true,
        };
    }

    /// <summary>Fills what the XML left out from the stratum and vanilla's fields (Docs/HABITAT-XML.md, defaults).</summary>
    private void Resolve(ThingDef plant)
    {
        light ??= new HabitatCurve { optimum = 0.75f, tolerance = stratum == PlantStratum.Canopy ? 0.3f : 0.45f, saturating = true };
        if (rootingDepth <= 0f)
            rootingDepth = stratum switch { PlantStratum.Canopy => 1.5f, PlantStratum.Shrub => 0.8f, _ => 0.3f };
        if (pace < 0f)
            pace = Mathf.Clamp01(1f - ((plant.plant?.growDays ?? 6f) - 3f) / 12f);
        if (seed < 0f)
            seed = stratum switch { PlantStratum.Canopy => 0.3f, PlantStratum.Shrub => 0.5f, _ => 0.8f };
        if (dispersal == PlantDispersal.Unset)
            dispersal = stratum switch { PlantStratum.Canopy => PlantDispersal.Gravity, PlantStratum.Shrub => PlantDispersal.Animal, _ => PlantDispersal.Wind };
        if (thicket < 0f)
            thicket = stratum == PlantStratum.Shrub ? 1f : 0f;
        resolved = true;
    }

    /// <summary>Sigma (cells) of this species' clusters, 0 if it does not cluster.</summary>
    public float ClusterSigma(ThingDef plant)
    {
        PlantProperties p = plant.plant;
        if (p == null || !p.GrowsInClusters)
            return 0f;
        if (clusterRadius > 0f)
            return clusterRadius;
        // Many small seeds travel a little further than few large ones.
        return p.wildClusterRadius * PlantEcologyDef.Current.sigmaPerVanillaRadius * Kernel.sigmaScale * Mathf.Lerp(0.8f, 1.2f, seed);
    }

    /// <summary>The dispersal kernel of this species' seed vector.</summary>
    public DispersalKernel Kernel => PlantEcologyDef.Current.KernelFor(dispersal);

    /// <summary>
    /// How often a new clump starts away from the species' own kind, relative to <see cref="PlantEcologyDef.clusterNucleation"/>:
    /// the vector's rate, raised for many small seeds (more of them land far away).
    /// </summary>
    public float NucleationScale => Kernel.nucleationScale * nucleation * Mathf.Lerp(0.6f, 1.4f, seed);

    /// <summary>
    /// How many times likelier a spawn is inside a cluster than outside, from vanilla's own numbers (cluster weight and
    /// the cluster area; vanilla gives an outside cell about 1 / (pi r^2) of the inside weight), scaled by
    /// <see cref="clusterStrength"/>.
    /// </summary>
    public float ClusterRatio(ThingDef plant)
    {
        PlantProperties p = plant.plant;
        float vanilla = Mathf.Max(1f, p.wildClusterWeight * Mathf.PI * p.wildClusterRadius * p.wildClusterRadius);
        return 1f + clusterStrength * (vanilla - 1f);
    }
}
