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

/// <summary>Preferred value of one environment factor, and how far from it the plant still does well.</summary>
public class HabitatCurve
{
    public float optimum;

    /// <summary>Distance from the optimum at which the response has fallen to about 61% (one standard deviation).</summary>
    public float tolerance = 0.3f;

    public float Response(float value)
    {
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

    /// <summary>Ground wetness, 0 (dry) to 1 (saturated): LL_Moisture.</summary>
    public HabitatCurve wetness;

    /// <summary>Soil depth over bedrock, metres: LL_SoilDepth.</summary>
    public HabitatCurve soilDepth;

    /// <summary>Rise over run: LL_Slope.</summary>
    public HabitatCurve slope;

    /// <summary>Sun exposure of the site, 0 (sheltered, sun-averted) to 1 (sun-facing slope): LL_Sun.</summary>
    public HabitatCurve sun;

    /// <summary>Soil fertility as vanilla's fertility grid holds it (1 is normal soil).</summary>
    public HabitatCurve fertility;

    /// <summary>Share of light this plant blocks beneath it, 0..1. Feeds LL_Shade for the strata placed after it.</summary>
    public float shadeCast;

    /// <summary>Radius in cells over which the shade spreads. 0 takes it from the plant's size.</summary>
    public float shadeRadius;

    /// <summary>0: fails in shade. 1: shade makes no difference.</summary>
    public float shadeTolerance = 0.5f;

    /// <summary>Seed dispersal: standard deviation (cells) of the clump around a parent plant.</summary>
    public float clusterRadius = 4f;

    /// <summary>How much clustering matters, 0 (even scatter) to 1 (plants only grow in clumps).</summary>
    public float clusterStrength = 0.3f;

    private static readonly Dictionary<ThingDef, PlantHabitat> derived = new Dictionary<ThingDef, PlantHabitat>();

    /// <summary>The species' habitat: its XML extension, or defaults derived from vanilla fields.</summary>
    public static PlantHabitat For(ThingDef plant)
    {
        var habitat = plant.GetModExtension<PlantHabitat>();
        if (habitat != null)
            return habitat;
        if (!derived.TryGetValue(plant, out habitat))
        {
            habitat = Derive(plant);
            derived[plant] = habitat;
        }
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
            shadeTolerance = tree ? 0.2f : 0.6f,
            clusterRadius = p != null && p.wildClusterRadius > 0 ? p.wildClusterRadius : 4f,
            clusterStrength = p != null && p.wildClusterWeight > 0 ? Mathf.Clamp01(p.wildClusterWeight / 20f) : 0.3f,
        };
    }
}
