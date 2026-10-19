using System.Collections.Generic;
using Verse;

namespace LivingLandscapes.Gameplay;

/// <summary>One thing a good settlement site needs: a derived field (or layer) and how well each of its values serves.</summary>
public class SettlementTerm
{
    /// <summary>A layer defName (LL_Slope, LL_Wetness...) or a field <see cref="SettlementSiting"/> derives (Room, Farmland, WaterDistance, Hazard, Refuge, RockDistance).</summary>
    public string field;

    /// <summary>Exponent of this term in the weighted geometric mean: how much its failure costs the site.</summary>
    public float weight = 1f;

    /// <summary>Suitability 0..1 of each value of the field.</summary>
    public SimpleCurve curve;
}

/// <summary>
/// Tuning of <see cref="Layers.SettlementWorker"/> (LL_Settlement) and of where the start spot goes. A site's suitability is
/// a weighted geometric mean of the terms, so one failing need (a swamp, a cliff face) sinks the site however good the rest.
/// </summary>
public class SettlementExtension : DefModExtension
{
    public List<SettlementTerm> terms = new List<SettlementTerm>();

    /// <summary>Gaussian radius (cells) over which Room (share of level, dry, open ground) is measured.</summary>
    public float roomSigma = 9f;

    /// <summary>Gaussian radius over which Farmland (share of ground fertile enough to farm) is measured.</summary>
    public float farmlandSigma = 14f;

    /// <summary>Fertility from which dry, open ground counts as farmland.</summary>
    public float farmlandFertility = 0.85f;

    /// <summary>Gaussian radius over which Hazard (deep water, marsh, mud) is measured.</summary>
    public float hazardSigma = 4f;

    /// <summary>Refuge: the share of barrier (rock, deep water) in the ring between these half-widths (cells) around a site: how enclosed it is.</summary>
    public float refugeInner = 10f;
    public float refugeOuter = 32f;

    /// <summary>Gaussian radius that smooths the suitability to the neighbourhood of a camp before the anchor is picked.</summary>
    public float anchorSigma = 6f;

    /// <summary>The anchor lies in the central part of the map this wide (share of the shorter side). Vanilla's start spot is within the middle seventh.</summary>
    public float centralFraction = 0.5f;

    /// <summary>Anchor cells are drawn with probability proportional to smoothed suitability to this power: high picks the best ground, low gives variety.</summary>
    public float anchorSharpness = 8f;

    /// <summary>The start spot is the best valid cell within this many cells of the anchor.</summary>
    public float searchRadius = 14f;
}
