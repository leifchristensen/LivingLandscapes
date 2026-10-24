using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// Gentle nudges on the channel processes (Docs/WATER-AUDIT.md, step 4): factors on width and depth (hydraulic
/// geometry), on the width, depth and bank noise, on flow spreading (above 1: water divides more readily where the
/// ground falls away more than one way, so channels split around islands), and (water regimes only) on the discharge
/// where channels start (below 1: a denser network). 1 leaves a process as it is; keep factors within about 0.5..3.
/// </summary>
public class ChannelNudges
{
    public float width = 1f, depth = 1f, widthNoise = 1f, depthNoise = 1f, bankNoise = 1f, spread = 1f, discharge = 1f;

    /// <summary>How readily sediment settles into bars where the flow loses power (islands, fords).</summary>
    public float deposition = 1f;

    /// <summary>How winding the channel wants to be (channel length over valley length): 1 straight, 1.5 or more meandering. See <see cref="Meanders"/>.</summary>
    public float sinuosity = 1f;

    public static readonly ChannelNudges None = new ChannelNudges();

    /// <summary>The weighted geometric mean of each factor; the neutral weight pulls every factor toward 1.</summary>
    public static ChannelNudges Blend(List<(ChannelNudges nudges, float weight)> parts, float neutralWeight)
    {
        float total = neutralWeight;
        float w = 0f, d = 0f, wn = 0f, dn = 0f, bn = 0f, sp = 0f, q = 0f, dep = 0f, sin = 0f;
        foreach ((ChannelNudges n, float weight) in parts)
        {
            if (weight <= 0f)
                continue;
            total += weight;
            w += weight * Mathf.Log(n.width);
            d += weight * Mathf.Log(n.depth);
            wn += weight * Mathf.Log(n.widthNoise);
            dn += weight * Mathf.Log(n.depthNoise);
            bn += weight * Mathf.Log(n.bankNoise);
            sp += weight * Mathf.Log(n.spread);
            dep += weight * Mathf.Log(n.deposition);
            q += weight * Mathf.Log(n.discharge);
            sin += weight * Mathf.Log(n.sinuosity);
        }
        if (total <= 0f)
            return None;
        return new ChannelNudges
        {
            width = Mathf.Exp(w / total), depth = Mathf.Exp(d / total), widthNoise = Mathf.Exp(wn / total),
            depthNoise = Mathf.Exp(dn / total), bankNoise = Mathf.Exp(bn / total), spread = Mathf.Exp(sp / total),
            discharge = Mathf.Exp(q / total), deposition = Mathf.Exp(dep / total), sinuosity = Mathf.Exp(sin / total),
        };
    }

    public ChannelNudges Times(ChannelNudges o) => new ChannelNudges
    {
        width = width * o.width, depth = depth * o.depth, widthNoise = widthNoise * o.widthNoise,
        depthNoise = depthNoise * o.depthNoise, bankNoise = bankNoise * o.bankNoise, spread = spread * o.spread,
        discharge = discharge * o.discharge, deposition = deposition * o.deposition, sinuosity = sinuosity * o.sinuosity,
    };
}

/// <summary>How strongly a water regime favours a channel regime (0 forbids it; regimes not listed weigh 1).</summary>
public class RegimeWeight
{
    public ChannelRegimeDef regime;
    public float weight = 1f;
}

/// <summary>
/// The character of a map's water (map scale, see Docs/WATER-REGIMES.md). Regimes are tried in <see cref="order"/>;
/// the first whose mutators and conditions match the tile is the map's. Regimes tied to a tile mutator come first,
/// so those maps are distinct. Tile fields: Rainfall (mm a year), Temperature (C), Hilliness (1 flat .. 5
/// impassable), Elevation (m), River (vanilla width of the world river, 0 if none), Coast (1 by the sea).
/// </summary>
public class WaterRegimeDef : Def
{
    public int order;

    /// <summary>The tile must have one of these mutators (by defName); empty: no mutator needed.</summary>
    public List<string> mutators = new List<string>();

    public List<FieldCondition> conditions = new List<FieldCondition>();

    /// <summary>Channels start where drainage area reaches this share of the map's area: lower is a denser network.</summary>
    public FloatRange sourceShare = new FloatRange(0.015f, 0.025f);

    /// <summary>
    /// Floodplain wetness: multiplies how high above a channel the water table stays up (LL_Wetness), so a wetland's
    /// ground between its threads is marsh and an arid map's channels have only a thin damp strip.
    /// </summary>
    public float floodplainScale = 1f;

    /// <summary>Added to the tile's 0..1 climate wetness from rainfall (wetness, wet ground rules, streams).</summary>
    public float climateOffset;

    /// <summary>0..1: how much damp ground becomes marsh (ground rules field Marshland), for wetlands.</summary>
    public float marshland;

    /// <summary>
    /// Terrain nudges for the world river's mutator structures (step 5, <see cref="WaterTerrainNudges"/>): a
    /// depression (metres) or a knob of resistant rock (metres, rock strength) at the river centre, this share of the
    /// map's size across.
    /// </summary>
    public FloatRange centreDepression = FloatRange.Zero;
    public FloatRange centreKnob = FloatRange.Zero;
    public float knobRockStrength = 0.9f;
    public float centreRadiusShare = 0.08f;

    /// <summary>Map-wide nudges on the channel processes (multiplied with the channel regimes' blend).</summary>
    public ChannelNudges nudges = new ChannelNudges();

    /// <summary>Channel regimes this water regime favours or forbids.</summary>
    public List<RegimeWeight> channelRegimes = new List<RegimeWeight>();

    /// <summary>
    /// This regime models its mutators' water (a delta, a headwater lake, a wetland's channels): those mutators' own
    /// water painting stands aside.
    /// </summary>
    public bool replacesMutatorTerrain;

    // World river structure (tied to the river mutators).

    /// <summary>The world river enters from beyond the map. False at a headwater: it rises on the map instead.</summary>
    public bool riverInflow = true;

    /// <summary>Every river link of the tile enters the map, not only the main one (a confluence).</summary>
    public bool allRiverLinks;

    public float Weight(ChannelRegimeDef regime)
    {
        foreach (RegimeWeight w in channelRegimes)
        {
            if (w.regime == regime)
                return w.weight;
        }
        return regime.exclusive ? 0f : 1f;
    }
}

/// <summary>
/// A channel pattern for one reach (reach scale). Every reach takes the matching regime its map's water regime weighs
/// highest (ties: the earlier in <see cref="order"/>). Reach fields: Discharge (log10 of drainage area times the
/// climate's wetness), Slope (water drop per cell), Confinement (valley floor width over channel width), Opening
/// (confinement over its narrowest in the reaches just upstream: above 1 where the valley opens out),
/// BankStrength (rock strength 0..1), Sediment (upstream erodibility times relief), BaseLevel (distance to the sea or
/// where the channel leaves the map, in widths), Order (stream order). Parameters are ranges, drawn once per channel.
/// </summary>
public class ChannelRegimeDef : Def
{
    public int order;
    public List<FieldCondition> conditions = new List<FieldCondition>();

    /// <summary>Only used where a water regime lists it (marsh threads, washes).</summary>
    public bool exclusive;

    /// <summary>What this regime does to the channel where it holds, in proportion to how fully it holds.</summary>
    public ChannelNudges nudges = new ChannelNudges();

    /// <summary>How fully the channel's data matches, 0..1 (soft conditions; a missing field counts as not matching).</summary>
    public float Membership(IReadOnlyDictionary<string, float> fields)
    {
        float m = 1f;
        foreach (FieldCondition condition in conditions)
        {
            if (!fields.TryGetValue(condition.field, out float value))
                return 0f;
            m *= condition.Membership(value);
        }
        return m;
    }
}

/// <summary>Chooses a map's water regime and the channel nudges at a place.</summary>
public static class WaterRegimes
{
    public static WaterRegimeDef Select(Map map, WorldContext world)
    {
        var fields = new Dictionary<string, float>
        {
            ["Rainfall"] = map.TileInfo?.rainfall ?? 0f,
            ["Temperature"] = map.TileInfo?.temperature ?? 10f,
            ["Hilliness"] = (float)world.Hilliness,
            ["Elevation"] = world.Elevation,
            ["River"] = RiverCourse.WidthOnMap(map.TileInfo),
            ["Coast"] = world.SeaDirection != Vector2.zero ? 1f : 0f,
        };
        var mutators = new HashSet<string>(map.TileInfo?.Mutators?.Select(m => m.defName) ?? Enumerable.Empty<string>());
        foreach (WaterRegimeDef def in DefDatabase<WaterRegimeDef>.AllDefsListForReading.OrderBy(d => d.order))
        {
            if (def.mutators.Count > 0 && !def.mutators.Any(mutators.Contains))
                continue;
            if (FieldCondition.All(def.conditions, fields))
                return def;
        }
        return null;
    }

    /// <summary>
    /// The channel nudges at one place: every channel regime weighs in by how fully the place matches it, times the
    /// water regime's weight for it, blended with a neutral weight of 1 (so a weak match nudges little); then the water
    /// regime's own nudges. <paramref name="dominant"/> is the regime weighing most (null if none), for logs.
    /// </summary>
    public static ChannelNudges Nudges(IReadOnlyDictionary<string, float> fields, WaterRegimeDef water, out ChannelRegimeDef dominant)
    {
        var parts = new List<(ChannelNudges, float)>();
        dominant = null;
        float top = 0f;
        foreach (ChannelRegimeDef def in DefDatabase<ChannelRegimeDef>.AllDefsListForReading)
        {
            float weight = (water != null ? water.Weight(def) : (def.exclusive ? 0f : 1f)) * def.Membership(fields);
            if (weight <= 0f)
                continue;
            parts.Add((def.nudges, weight));
            if (weight > top)
            {
                top = weight;
                dominant = def;
            }
        }
        ChannelNudges blend = ChannelNudges.Blend(parts, 1f);
        return water != null ? blend.Times(water.nudges) : blend;
    }
}
