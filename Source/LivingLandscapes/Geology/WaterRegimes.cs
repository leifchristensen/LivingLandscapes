using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

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

    /// <summary>The world river rises from a lake near the map's centre, this many channel widths across (0: none).</summary>
    public FloatRange sourceLakeWidths = FloatRange.Zero;

    /// <summary>
    /// The world river fans into distributaries where it nears its exit (a delta), not only by the sea: this many
    /// branches, spread up to this many channel widths apart at the edge.
    /// </summary>
    public bool deltaAtExit;
    public IntRange deltaBranches = new IntRange(2, 2);
    public FloatRange deltaSpreadWidths = new FloatRange(2f, 2f);

    /// <summary>
    /// The world river splits around one large island near the map's centre: its length as a share of the map's size
    /// (0: none) and how far its side channel bows out, in channel widths.
    /// </summary>
    public FloatRange centralIsland = FloatRange.Zero;
    public FloatRange centralIslandWidths = new FloatRange(2f, 3f);

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

    /// <summary>Lateral wobble amplitude and wavelength, in channel widths.</summary>
    public FloatRange wobbleWidths = new FloatRange(0.6f, 1.2f);
    public FloatRange wobbleWavelengthWidths = new FloatRange(4f, 7f);

    /// <summary>Meandering: the share of full bend migration (0 keeps the course, 1 grows loops and cutoffs).</summary>
    public FloatRange meander = new FloatRange(0.1f, 0.3f);

    /// <summary>Chance that a place where the valley opens out splits around an island, and the island's length in widths.</summary>
    public FloatRange splitChance = new FloatRange(0.1f, 0.3f);
    public FloatRange islandWidths = new FloatRange(3f, 4f);

    /// <summary>Channel depth relative to hydraulic geometry, and whether wide channels alternate pools and riffles.</summary>
    public FloatRange depthScale = new FloatRange(1f, 1f);
    public bool riffles = true;

    /// <summary>Valley floor reach beyond the banks, in channel widths at full erodibility.</summary>
    public FloatRange floorWidths = new FloatRange(2f, 4f);
}

/// <summary>A channel regime's parameters as drawn for one channel.</summary>
public class ReachRegime
{
    public ChannelRegimeDef Def;
    public float Wobble, Wavelength, Meander, SplitChance, IslandWidths, Depth, Floor;
    public bool Riffles;

    public static ReachRegime Draw(ChannelRegimeDef def) => new ReachRegime
    {
        Def = def,
        Wobble = def.wobbleWidths.RandomInRange,
        Wavelength = def.wobbleWavelengthWidths.RandomInRange,
        Meander = def.meander.RandomInRange,
        SplitChance = def.splitChance.RandomInRange,
        IslandWidths = def.islandWidths.RandomInRange,
        Depth = def.depthScale.RandomInRange,
        Floor = def.floorWidths.RandomInRange,
        Riffles = def.riffles,
    };
}

/// <summary>Chooses a map's water regime and classifies channel reaches.</summary>
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

    /// <summary>The matching channel regime the water regime weighs highest (earlier order wins ties); null if none.</summary>
    public static ChannelRegimeDef Classify(IReadOnlyDictionary<string, float> reach, WaterRegimeDef water)
    {
        ChannelRegimeDef best = null;
        float bestWeight = 0f;
        foreach (ChannelRegimeDef def in DefDatabase<ChannelRegimeDef>.AllDefsListForReading.OrderBy(d => d.order))
        {
            float weight = water != null ? water.Weight(def) : (def.exclusive ? 0f : 1f);
            if (weight > bestWeight && FieldCondition.All(def.conditions, reach))
            {
                best = def;
                bestWeight = weight;
            }
        }
        return best;
    }
}
