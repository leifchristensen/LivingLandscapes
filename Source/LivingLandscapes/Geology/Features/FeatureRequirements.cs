using System.Collections.Generic;
using System.Linq;

namespace LivingLandscapes.Geology;

/// <summary>A world fact a feature type can require.</summary>
public enum WorldFact
{
    /// <summary>The tile has a world river.</summary>
    WorldRiver,

    /// <summary>The tile is on a coast.</summary>
    Coast,

    /// <summary>The region slopes (it has a downhill direction: rougher ground, the sea or a river lies one way).</summary>
    Slope,

    /// <summary>The tile's hilliness is one of <see cref="FeatureRequirement.anyOf"/>.</summary>
    Hilliness,

    /// <summary>The tile has one of the rock types (defNames) in <see cref="FeatureRequirement.anyOf"/>.</summary>
    RockType,

    /// <summary>The tile has a rock whose solubility is at least <see cref="FeatureRequirement.atLeast"/> (limestone, marble).</summary>
    SolubleRock,
}

/// <summary>
/// A world fact that must exist for a feature to form (FEATURE-REGIONS.md 6.1, "Requirements"). If one is missing the
/// feature is not formed and the log says why: nothing is faked (cenotes on a tile without soluble rock form nothing).
/// </summary>
public class FeatureRequirement
{
    public WorldFact fact;

    /// <summary>The accepted values for <see cref="WorldFact.Hilliness"/> or <see cref="WorldFact.RockType"/>.</summary>
    public List<string> anyOf = new List<string>();

    /// <summary>The least value for <see cref="WorldFact.SolubleRock"/>.</summary>
    public float atLeast = 0.5f;
}

/// <summary>The world facts of one map, as plain values, so requirements are checked without the game.</summary>
public class WorldFacts
{
    public bool hasRiver;
    public bool hasCoast;
    public bool hasSlope;
    public string hilliness = "";

    /// <summary>The tile's rock types: defName and solubility.</summary>
    public List<(string name, float solubility)> rocks = new List<(string, float)>();
}

public static class FeatureRequirements
{
    /// <summary>Why each unmet requirement fails, in plain words; empty when every requirement holds.</summary>
    public static List<string> Unmet(IEnumerable<FeatureRequirement> requirements, WorldFacts facts)
    {
        var reasons = new List<string>();
        if (requirements == null)
            return reasons;
        foreach (FeatureRequirement r in requirements)
        {
            string reason = Reason(r, facts);
            if (reason != null)
                reasons.Add(reason);
        }
        return reasons;
    }

    /// <summary>Why one requirement fails, or null if it holds.</summary>
    public static string Reason(FeatureRequirement r, WorldFacts facts)
    {
        switch (r.fact)
        {
            case WorldFact.WorldRiver:
                return facts.hasRiver ? null : "the tile has no world river";
            case WorldFact.Coast:
                return facts.hasCoast ? null : "the tile has no coast";
            case WorldFact.Slope:
                return facts.hasSlope ? null : "the region has no slope (no rougher ground, sea or river lies one way)";
            case WorldFact.Hilliness:
                return r.anyOf.Count == 0 || r.anyOf.Contains(facts.hilliness) ? null
                    : $"the tile's hilliness is {facts.hilliness}, not {string.Join(" or ", r.anyOf)}";
            case WorldFact.RockType:
                return r.anyOf.Count == 0 || facts.rocks.Any(rock => r.anyOf.Contains(rock.name)) ? null
                    : $"the tile has none of the rock types {string.Join(", ", r.anyOf)} (it has {RockList(facts)})";
            case WorldFact.SolubleRock:
                return facts.rocks.Any(rock => rock.solubility >= r.atLeast) ? null
                    : $"the tile has no soluble rock (solubility {r.atLeast:0.##} or more; it has {RockList(facts)})";
            default:
                return null;
        }
    }

    private static string RockList(WorldFacts facts) => facts.rocks.Count == 0 ? "none" : string.Join(", ", facts.rocks.Select(r => r.name));
}
