using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>Which world fact calls a feature type onto a map.</summary>
public enum AnchorKind
{
    /// <summary>One of the tile mutators in <see cref="FeatureAnchor.mutators"/> (Mountain, Valley, Cliffs...). One region per mutator.</summary>
    TileMutator,

    /// <summary>The tile's world river: the type is laid once when the tile has one (its shape says where, for example along each river link).</summary>
    WorldRiver,

    /// <summary>
    /// The water regime's feature at the river centre (a headwater basin, a river island's knob, Docs/WATER-REGIMES.md W5).
    /// Its size and contributions are computed from the regime's own numbers (<see cref="WaterFeatureMath.Disk"/>).
    /// </summary>
    WaterRegimeCentre,

    /// <summary>
    /// Low country's rock (#51): one feature type of this anchor is drawn per map, by <see cref="FeatureAnchor.weight"/>, among
    /// those whose requirements hold. Types that do not fit are simply not candidates (not logged).
    /// </summary>
    PlainsChoice,
}

/// <summary>Where a region lies on the map.</summary>
public enum Placement
{
    /// <summary>From the map centre, along the orientation (shifted by the offset).</summary>
    MapCentre,

    /// <summary>Once per world river link, from where the rivers meet toward that river's side, and once toward the exit (paths).</summary>
    AlongWorldRivers,

    /// <summary>At where the world rivers meet.</summary>
    RiverCentre,
}

/// <summary>Which way a region faces, relative to the region's downhill direction (lower neighbours, the sea).</summary>
public enum ShapeOrientation
{
    /// <summary>Along or towards the direction the region slopes down.</summary>
    Downhill,

    /// <summary>Away from the downhill direction.</summary>
    Upslope,

    /// <summary>Across the downhill direction.</summary>
    Across,

    Random,
}

public class FeatureAnchor
{
    public AnchorKind kind = AnchorKind.TileMutator;

    /// <summary>The mutators that call the type (<see cref="AnchorKind.TileMutator"/>).</summary>
    public List<TileMutatorDef> mutators = new List<TileMutatorDef>();

    /// <summary>Relative chance among the plains features that fit (<see cref="AnchorKind.PlainsChoice"/>).</summary>
    public float weight = 1f;
}

/// <summary>
/// A region's fuzzy shape (FEATURE-REGIONS.md 2.2). Positions and sizes are fractions of the map width from the region's
/// origin; the weight is 1 in the core and falls smoothly to 0 (<see cref="FeatureShapeMath"/>).
/// </summary>
public class FeatureShape
{
    public ShapeKind kind = ShapeKind.Band;
    public BandSide side = BandSide.Both;
    public Placement placement = Placement.MapCentre;
    public ShapeOrientation orientation = ShapeOrientation.Downhill;

    /// <summary>Shift of the core from the origin along the orientation (sideways for a two-sided band).</summary>
    public float offset;

    /// <summary>Half-width (band, path, ring), radius (area), transition half-width (front) or reach (point, each scatter point).</summary>
    public float size = 0.15f;

    /// <summary>Softness of the edge.</summary>
    public float edge = 0.05f;

    /// <summary>Irregularity of the outline (domain warp amplitude), so no edge is a line.</summary>
    public float warp = 0.05f;

    /// <summary>The ring's radius, or the radius a scatter's points lie within.</summary>
    public float radius;

    /// <summary>How many points a scatter has.</summary>
    public IntRange points = IntRange.Zero;

    /// <summary>Per-instance ranges, drawn on each map when set (the size varies between maps); otherwise the fixed values above.</summary>
    public FloatRange sizeRange = FloatRange.Zero;
    public FloatRange offsetRange = FloatRange.Zero;

    public static bool IsSet(FloatRange r) => r.min != 0f || r.max != 0f;
}

/// <summary>What a feature type does to one condition inside its region (FEATURE-REGIONS.md 6.1, "Contributions").</summary>
public class FeatureContribution
{
    public Condition condition = Condition.Uplift;

    /// <summary>
    /// Uplift: the rate added inside the region (1 = the strongest background uplift). Rock arrangement: the erodibility factor
    /// (Alter: below 1 resists erosion, above 1 erodes easily) or the body's erodibility (Intrude).
    /// </summary>
    public float inside;

    /// <summary>Uplift only: the rate added outside the region (the region's surroundings rise or sink).</summary>
    public float outside;

    /// <summary>Rock arrangement only: how the contribution changes the rock it finds.</summary>
    public RockOperation operation = RockOperation.Alter;

    /// <summary>When it acts (default: the whole run). For rock arrangement its start is the contribution's age.</summary>
    public TimeWindow window;
}

/// <summary>What a link connects to (read by the plan graph from phase 2).</summary>
public enum LinkRole
{
    Entry,
    MeetingPoint,
    Exit,
    Mouth,
    Coast,
}

/// <summary>How heights along a link must relate.</summary>
public enum LinkProfile
{
    Any,

    /// <summary>The profile falls or stays level from the first end to the second (a river corridor).</summary>
    FallsOrLevel,
}

/// <summary>What a feature connects to and how heights must relate (FEATURE-REGIONS.md 6.1, "Links"). Planned from phase 2.</summary>
public class FeatureLink
{
    public LinkRole from = LinkRole.Entry;
    public LinkRole to = LinkRole.Exit;
    public LinkProfile profile = LinkProfile.Any;
}

/// <summary>The shared library of checks a feature type's criteria use (FEATURE-REGIONS.md 6.1).</summary>
public enum CriterionCheck
{
    Continuity,
    NeverRisingProfile,
    ClosedBasin,
    HardnessContrast,
    CrossesOnWorldSide,
    ForecastVersusResult,
    Seam,
}

/// <summary>A check after the run, with its number; a miss fails the test battery, never the player's map. Run from phase 2.</summary>
public class FeatureCriterion
{
    public CriterionCheck check;

    /// <summary>The criterion's number (cells a river may stray from its corridor, the largest rise allowed along a profile...).</summary>
    public float limit;
}

/// <summary>Count, height and size of a chosen feature on tiles of one hilliness (flat land gets smaller, fewer features).</summary>
public class FeatureScale
{
    public Hilliness hilliness;
    public IntRange count = new IntRange(1, 1);
    public FloatRange extraRelief = new FloatRange(5f, 10f);

    /// <summary>Multiplies the drawn size.</summary>
    public float sizeFactor = 1f;
}

/// <summary>
/// One feature type, water or land (FEATURE-REGIONS.md 6.1; decision record 0002: regions set causes, processes make
/// features). It only changes natural conditions inside a fuzzy region; erosion and the other processes then run once over
/// the whole map and make the feature, so no region shows a seam. Parts that a type leaves out default to "no requirement,
/// whole run, no link, no criterion".
/// </summary>
public class FeatureTypeDef : Def
{
    /// <summary>Which world fact calls it.</summary>
    public FeatureAnchor anchor = new FeatureAnchor();

    /// <summary>World facts that must exist; if one is missing the feature is not formed and the log says why.</summary>
    public List<FeatureRequirement> requirements = new List<FeatureRequirement>();

    public FeatureShape shape = new FeatureShape();

    public List<FeatureLink> links = new List<FeatureLink>();

    /// <summary>Per condition, what the type does inside its region. Combination belongs to the condition (<see cref="FeatureConditions.RuleOf"/>).</summary>
    public List<FeatureContribution> contributions = new List<FeatureContribution>();

    public List<FeatureCriterion> criteria = new List<FeatureCriterion>();

    /// <summary>How many instances a chosen feature (<see cref="AnchorKind.PlainsChoice"/>) lays, each its own way; anchored types lay one per anchor.</summary>
    public IntRange count = new IntRange(1, 1);

    /// <summary>
    /// Metres the feature adds to the map's relief: how far it stands above (or cuts below) the plain. This widens the model's
    /// height window; it is not a condition and writes no height.
    /// </summary>
    public FloatRange extraRelief = FloatRange.Zero;

    /// <summary>Count, relief and size per hilliness, where an entry exists.</summary>
    public List<FeatureScale> byHilliness = new List<FeatureScale>();

    /// <summary>Skip the anchoring mutator's own elevation rewrite when geological terrain is on (the geology models the landform).</summary>
    public bool replacesVanillaElevation = true;

    /// <summary>Colour in the dev "Feature regions" overlay and its export; unset (transparent) draws grey, with a warning.</summary>
    public Color debugColor = Color.clear;

    public FeatureScale ScaleFor(Hilliness h) => byHilliness.Find(s => s.hilliness == h);

    public override IEnumerable<string> ConfigErrors()
    {
        foreach (string e in base.ConfigErrors())
            yield return e;
        if (anchor.kind == AnchorKind.TileMutator && anchor.mutators.Count == 0)
            yield return "anchor TileMutator names no mutators";
        if (shape.placement == Placement.AlongWorldRivers && shape.kind != ShapeKind.Path)
            yield return "placement AlongWorldRivers lays paths: shape kind must be Path";
        if (shape.kind == ShapeKind.Ring && shape.radius <= 0f)
            yield return "a ring needs a radius above 0";
        if (shape.kind == ShapeKind.Scatter && (shape.points.max <= 0 || shape.radius <= 0f))
            yield return "a scatter needs points above 0 and a radius above 0";
        foreach (FeatureContribution c in contributions)
        {
            if (!FeatureConditions.IsRead(c.condition))
                yield return $"contribution to {c.condition}: no process reads that condition yet, so it would have no effect";
            if (c.condition != Condition.RockArrangement && c.operation != RockOperation.Alter)
                yield return $"contribution to {c.condition}: operation applies to RockArrangement only";
            if (c.window != null)
            {
                foreach (string e in c.window.ConfigErrors())
                    yield return $"contribution to {c.condition}: {e}";
            }
        }
        if (links.Count > 0)
            yield return "links are read by the plan graph, which arrives in phase 2 of FEATURE-REGIONS.md; they have no effect yet";
        if (criteria.Count > 0)
            yield return "criteria run as battery invariants from phase 2 of FEATURE-REGIONS.md; they are not checked yet";
    }

    private static Dictionary<TileMutatorDef, List<FeatureTypeDef>> byMutator;

    /// <summary>The feature types a mutator calls, in definition order (empty when none).</summary>
    public static List<FeatureTypeDef> For(TileMutatorDef mutator)
    {
        if (byMutator == null)
        {
            byMutator = new Dictionary<TileMutatorDef, List<FeatureTypeDef>>();
            foreach (FeatureTypeDef def in DefDatabase<FeatureTypeDef>.AllDefsListForReading.Where(d => d.anchor.kind == AnchorKind.TileMutator))
            {
                foreach (TileMutatorDef m in def.anchor.mutators.Where(m => m != null))
                {
                    if (!byMutator.TryGetValue(m, out List<FeatureTypeDef> list))
                        byMutator[m] = list = new List<FeatureTypeDef>();
                    list.Add(def);
                }
            }
        }
        return mutator != null && byMutator.TryGetValue(mutator, out List<FeatureTypeDef> found) ? found : Empty;
    }

    private static readonly List<FeatureTypeDef> Empty = new List<FeatureTypeDef>();

    /// <summary>Whether the geology models a mutator's landform (some feature type is anchored on it).</summary>
    public static bool Models(TileMutatorDef mutator) => For(mutator).Count > 0;

    /// <summary>Whether the mutator's own elevation rewrite is skipped with geological terrain on.</summary>
    public static bool ReplacesVanillaElevation(TileMutatorDef mutator) => For(mutator).Any(d => d.replacesVanillaElevation);
}

[DefOf]
public static class FeatureTypeDefOf
{
    /// <summary>The valley world rivers run in; its width also sets where the model's river ends sit (<see cref="LandscapeGenerator"/>).</summary>
    public static FeatureTypeDef LL_WorldRiverValley;

    static FeatureTypeDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(FeatureTypeDefOf));
}
