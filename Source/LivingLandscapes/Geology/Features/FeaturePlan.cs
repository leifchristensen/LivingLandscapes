using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>One region of a feature type, placed on one map: its shape resolved to a direction and origin, and what it contributes.</summary>
public class FeatureRegion : RegionShape
{
    public FeatureTypeDef type;

    /// <summary>Metres its type's draw added to the map's relief (the draw is shared by all the regions it laid).</summary>
    public float extraRelief;

    public List<FeatureContribution> contributions = new List<FeatureContribution>();
}

/// <summary>
/// The feature regions of one map, planned before terrain (FEATURE-REGIONS.md, phase 1): every feature type whose anchor the
/// tile has, placed in a fixed order (tile mutators, the world river, the water regime's centre feature, the plains choice),
/// with its requirements checked first. <see cref="Fields"/> lays them on the erosion model's grid as condition contributions.
/// </summary>
public class FeaturePlan
{
    private readonly List<FeatureRegion> regions = new List<FeatureRegion>();
    private readonly Perlin warpX;
    private readonly Perlin warpZ;

    /// <summary>The plains feature this map got, or null.</summary>
    public FeatureTypeDef PlainsFeature { get; }

    /// <summary>Metres the features add to the map's relief (0 without any).</summary>
    public float ExtraRelief { get; }

    public IReadOnlyList<FeatureRegion> Regions => regions;

    /// <param name="water">The water regime: its basin or knob at the river centre is a region (W5, <see cref="WaterFeatureMath"/>).</param>
    public FeaturePlan(Map map, WorldContext world, WaterRegimeDef water = null)
    {
        warpX = new Perlin(4.0, 2.0, 0.5, 3, Rand.Int, QualityMode.Medium);
        warpZ = new Perlin(4.0, 2.0, 0.5, 3, Rand.Int, QualityMode.Medium);
        WorldFacts facts = FactsOf(map, world);
        List<FeatureTypeDef> all = DefDatabase<FeatureTypeDef>.AllDefsListForReading;
        float extraRelief = 0f;

        // Tile mutators, in the tile's order: one region per mutator a type is anchored on.
        foreach (TileMutatorDef mutator in map.TileInfo.Mutators)
        {
            foreach (FeatureTypeDef type in FeatureTypeDef.For(mutator))
            {
                if (Formed(type, facts, $"mutator {mutator.defName}"))
                    extraRelief += Lay(type, world, map);
            }
        }
        // The world river: types laid once when the tile has one.
        if (world.HasRiver)
        {
            foreach (FeatureTypeDef type in all.Where(d => d.anchor.kind == AnchorKind.WorldRiver))
            {
                if (Formed(type, facts, "the world river"))
                    extraRelief += Lay(type, world, map);
            }
        }
        // The water regime's basin or knob at the river centre: its size and contributions come from the regime (P23, P2).
        if (water != null && world.HasRiver && (water.basinUpliftDrop > 0f || water.knobErodibility < 1f))
        {
            foreach (FeatureTypeDef type in all.Where(d => d.anchor.kind == AnchorKind.WaterRegimeCentre))
            {
                if (Formed(type, facts, $"water regime {water.defName}"))
                    LayRegimeCentre(type, world, map, water);
            }
        }
        // Low country gets its rock from one plains feature: remnant hills, an escarpment or river bluffs.
        PlainsFeature = ChoosePlains(all, facts);
        if (PlainsFeature != null)
            extraRelief += Lay(PlainsFeature, world, map);
        ExtraRelief = extraRelief;
    }

    public bool Any => regions.Count > 0;

    /// <summary>The world facts the requirements read.</summary>
    private static WorldFacts FactsOf(Map map, WorldContext world) => new WorldFacts
    {
        hasRiver = world.HasRiver,
        hasCoast = world.SeaDirection != Vector2.zero,
        hasSlope = world.Downhill != Vector2.zero,
        hilliness = world.Hilliness.ToString(),
        rocks = Find.World.NaturalRockTypesIn(map.Tile).Select(r => (r.defName, RockProperties.Of(r).solubility)).ToList(),
    };

    /// <summary>Whether an anchored type's requirements hold; if not, the log says why and the feature is not formed.</summary>
    private static bool Formed(FeatureTypeDef type, WorldFacts facts, string anchor)
    {
        List<string> unmet = FeatureRequirements.Unmet(type.requirements, facts);
        if (unmet.Count == 0)
            return true;
        Log.Message($"[LivingLandscapes] Feature {type.defName} (called by {anchor}) is not formed on this map: {string.Join("; ", unmet)}.");
        return false;
    }

    /// <summary>The plains feature for a map, drawn by weight among those whose requirements hold; null if none does.</summary>
    private static FeatureTypeDef ChoosePlains(List<FeatureTypeDef> all, WorldFacts facts)
    {
        // Test runs: -ll-plains=off compares against the map without a plains feature.
        if (GenCommandLine.TryGetCommandLineArg("ll-plains", out string mode) && mode == "off")
            return null;
        List<FeatureTypeDef> fitting = all.Where(d => d.anchor.kind == AnchorKind.PlainsChoice && FeatureRequirements.Unmet(d.requirements, facts).Count == 0).ToList();
        return fitting.Count == 0 ? null : fitting.RandomElementByWeight(d => d.anchor.weight);
    }

    /// <summary>Lays a type's regions on this map; returns the metres it adds to the relief.</summary>
    private float Lay(FeatureTypeDef type, WorldContext world, Map map)
    {
        FeatureScale scale = type.ScaleFor(world.Hilliness);
        FloatRange reliefRange = scale?.extraRelief ?? type.extraRelief;
        float extra = FeatureShape.IsSet(reliefRange) ? reliefRange.RandomInRange : 0f;
        int first = regions.Count;
        FeatureShape s = type.shape;
        switch (s.placement)
        {
            case Placement.AlongWorldRivers:
            {
                // One shape for every link: from where the rivers meet toward each river's side, and toward the exit.
                FeatureRegion template = Draw(type, scale, world, map);
                foreach (WorldContext.RiverInflow inflow in world.RiverInflows)
                    regions.Add(Placed(template, inflow.From, world.RiverCentre));
                regions.Add(Placed(template, world.RiverExit, world.RiverCentre));
                break;
            }
            case Placement.RiverCentre:
                regions.Add(Placed(Draw(type, scale, world, map), Vector2.zero, world.RiverCentre));
                break;
            default:
            {
                // A chosen feature lays several instances, each its own way; an anchored one lays one.
                int n = type.anchor.kind == AnchorKind.PlainsChoice ? (scale?.count ?? type.count).RandomInRange : 1;
                for (int k = 0; k < n; k++)
                {
                    FeatureRegion region = Draw(type, scale, world, map);
                    region.direction = Direction(s.orientation, world);
                    regions.Add(region);
                }
                break;
            }
        }
        for (int r = first; r < regions.Count; r++)
            regions[r].extraRelief = extra;
        return extra;
    }

    /// <summary>The water regime's basin or knob: a region at the river centre sized and valued from the regime (W5).</summary>
    private void LayRegimeCentre(FeatureTypeDef type, WorldContext world, Map map, WaterRegimeDef water)
    {
        float aspect = Mathf.Max(map.Size.x, map.Size.z) / (float)map.Size.x;
        FeatureTypeDef valley = world.HasRiver ? FeatureTypeDefOf.LL_WorldRiverValley : null;
        var disk = WaterFeatureMath.Disk(water.centreRadiusShare * aspect, water.basinUpliftDrop, water.knobErodibility,
            valley != null ? valley.shape.size : float.PositiveInfinity);
        regions.Add(new FeatureRegion
        {
            type = type, kind = type.shape.kind, side = type.shape.side, direction = Vector2.zero, origin = world.RiverCentre,
            size = disk.size, edge = disk.edge, warp = disk.warp,
            contributions =
            {
                new FeatureContribution { condition = Condition.Uplift, inside = disk.upliftInside },
                new FeatureContribution { condition = Condition.RockArrangement, inside = disk.erodibility },
            },
        });
    }

    /// <summary>One instance of a type's shape, its ranges drawn (offset first, then size scaled for the tile's hilliness).</summary>
    private static FeatureRegion Draw(FeatureTypeDef type, FeatureScale scale, WorldContext world, Map map)
    {
        FeatureShape s = type.shape;
        float offset = FeatureShape.IsSet(s.offsetRange) ? s.offsetRange.RandomInRange : s.offset;
        float size = (FeatureShape.IsSet(s.sizeRange) ? s.sizeRange.RandomInRange : s.size) * (scale?.sizeFactor ?? 1f);
        var region = new FeatureRegion
        {
            type = type, kind = s.kind, side = s.side, offset = offset, size = size, edge = s.edge, warp = s.warp, radius = s.radius,
            contributions = type.contributions,
        };
        if (s.kind == ShapeKind.Scatter)
            region.points = ScatterPoints(type, s, map);
        return region;
    }

    /// <summary>
    /// A scatter's points, uniformly within its radius, drawn in their own random state (seeded by the world, the tile and the
    /// type) so the rest of the map's draws do not shift.
    /// </summary>
    private static List<Vector2> ScatterPoints(FeatureTypeDef type, FeatureShape s, Map map)
    {
        Rand.PushState(Gen.HashCombineInt(Gen.HashCombineInt(Find.World.info.Seed, map.Tile.tileId), type.shortHash));
        int n = s.points.RandomInRange;
        var points = new List<Vector2>(n);
        for (int i = 0; i < n; i++)
        {
            float angle = Rand.Range(0f, 2f * Mathf.PI), r = s.radius * Mathf.Sqrt(Rand.Value);
            points.Add(new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r));
        }
        Rand.PopState();
        return points;
    }

    private static FeatureRegion Placed(FeatureRegion template, Vector2 direction, Vector2 origin) => new FeatureRegion
    {
        type = template.type, kind = template.kind, side = template.side, direction = direction, origin = origin,
        offset = template.offset, size = template.size, edge = template.edge, warp = template.warp, radius = template.radius,
        points = template.points, contributions = template.contributions,
    };

    private static Vector2 Direction(ShapeOrientation orientation, WorldContext world)
    {
        Vector2 downhill = world.Downhill != Vector2.zero ? world.Downhill : WorldContext.Heading(Rand.Range(0f, 360f));
        switch (orientation)
        {
            case ShapeOrientation.Upslope: return -downhill;
            case ShapeOrientation.Across: return new Vector2(-downhill.y, downhill.x);
            case ShapeOrientation.Random: return WorldContext.Heading(Rand.Range(0f, 360f));
            default: return downhill;
        }
    }

    /// <summary>The domain warp at a map fraction, which each region scales by its own share. Draws no random number.</summary>
    public Vector2 WarpAt(Vector2 p) => new Vector2((float)warpX.GetValue(p.x, 0.0, p.y), (float)warpZ.GetValue(p.x, 0.0, p.y));

    /// <summary>A region's weight at a map position in cells (not rounded), the same weight <see cref="Fields"/> lays there.</summary>
    public float WeightAtMapCell(RegionShape region, float mx, float mz, int sizeX, int sizeZ)
    {
        Vector2 p = FeatureShapeMath.MapFraction(mx, mz, sizeX, sizeZ);
        return FeatureShapeMath.WarpedWeight(region, p, WarpAt(p));
    }

    /// <summary>
    /// The regions' contributions laid on a grid (the erosion model's), each a weight per cell from its shape at the warped
    /// position <paramref name="mapFraction"/> gives (map-width fractions from the map centre).
    /// </summary>
    public ConditionFields Fields(int gx, int gz, System.Func<int, int, Vector2> mapFraction)
    {
        var fields = new ConditionFields(gx * gz);
        if (regions.Count == 0)
        {
            fields.regionWeights = new float[0][];
            return fields;
        }
        var weights = new float[regions.Count][];
        // A region that contributes nothing (a bay: the type only says the geology models it) needs no weights.
        for (int r = 0; r < regions.Count; r++)
            weights[r] = regions[r].contributions.Count > 0 ? new float[gx * gz] : null;
        for (int z = 0; z < gz; z++)
        {
            for (int x = 0; x < gx; x++)
            {
                Vector2 p = mapFraction(x, z);
                Vector2 warp = WarpAt(p);
                for (int r = 0; r < regions.Count; r++)
                {
                    if (weights[r] != null)
                        weights[r][z * gx + x] = FeatureShapeMath.WarpedWeight(regions[r], p, warp);
                }
            }
        }
        fields.regionWeights = weights;
        for (int r = 0; r < regions.Count; r++)
        {
            float[] w = weights[r];
            foreach (FeatureContribution c in regions[r].contributions)
            {
                if (c.condition == Condition.Uplift)
                {
                    var values = new float[w.Length];
                    for (int i = 0; i < w.Length; i++)
                        values[i] = c.inside * w[i] + c.outside * (1f - w[i]);
                    fields.uplift.Add(new UpliftContribution(values, c.window));
                }
                else if (c.condition == Condition.RockArrangement)
                    fields.rock.Add(new RockContribution(w, c.operation, c.inside, c.window));
            }
        }
        return fields;
    }
}

/// <summary>
/// The conditions the erosion model reads, over its grid: the background uplift (noise, set by the generator), every
/// region's contribution, and the limits applied last (<see cref="ConditionCombine"/>).
/// </summary>
public class ConditionFields
{
    private readonly int cells;
    public readonly List<UpliftContribution> uplift = new List<UpliftContribution>();
    public readonly List<RockContribution> rock = new List<RockContribution>();

    /// <summary>Each region's weight per cell, in the plan's order (null for a region that contributes nothing).</summary>
    public float[][] regionWeights;

    /// <summary>The map's own uplift before any region (fractal noise at landform scale, stronger upslope).</summary>
    public float[] background;

    /// <summary>Least uplift anywhere, so no ground sinks by itself.</summary>
    public float floor = 0.02f;

    /// <summary>Uplift fades toward base level over the coastal plain (P1): a factor per cell, or null.</summary>
    public float[] grade;

    /// <summary>Computed upper limits per cell (phase 2: the stream-power cap along a world river's corridor).</summary>
    public readonly List<float[]> caps = new List<float[]>();

    public ConditionFields(int cells) => this.cells = cells;

    /// <summary>True when some contribution acts for part of the run only, so the erosion model re-reads the conditions as it runs.</summary>
    public bool ChangeDuringRun => ConditionCombine.ChangesDuringRun(uplift, rock);

    public float[] UpliftAt(float t, float[] into = null)
    {
        into = into ?? new float[cells];
        ConditionCombine.Uplift(background, uplift, t, floor, grade, caps, into);
        return into;
    }

    public float[] ErodibilityAt(float t, float[] into = null)
    {
        into = into ?? new float[cells];
        ConditionCombine.Rock(rock, t, into);
        return into;
    }
}
