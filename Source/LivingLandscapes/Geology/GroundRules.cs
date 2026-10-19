using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>Ground that the geology places itself, instead of leaving the choice to vanilla's fertility and noise.</summary>
public enum GroundCover
{
    /// <summary>Vanilla chooses: rock or gravel from the elevation value, soil types from fertility.</summary>
    None,
    ShallowWater,
    DeepWater,
    ShallowMovingWater,
    DeepMovingWater,
    Marsh,
    Mud,
}

/// <summary>
/// One test of a rule: a field's value lies in [min, max). Shared by every rule table: ground rules (fields per cell,
/// see <see cref="GroundFields"/>), water regimes (fields per tile) and channel regimes (fields per reach).
/// </summary>
public class FieldCondition
{
    /// <summary>The field's name: a layer defName (LL_SoilDepth, LL_Wetness...) or a field the table defines.</summary>
    public string field;
    public float min = float.NegativeInfinity;
    public float max = float.PositiveInfinity;

    /// <summary>
    /// Half-width of a soft edge, in the field's units: within it the condition holds partly (0..1), so neighbouring
    /// values never switch abruptly. 0: a hard edge.
    /// </summary>
    public float soft;

    public bool Holds(float value) => value >= min && value < max;

    /// <summary>How fully the condition holds, 0..1: smooth ramps of <see cref="soft"/> around min and max.</summary>
    public float Membership(float value)
    {
        if (soft <= 0f)
            return Holds(value) ? 1f : 0f;
        float m = 1f;
        if (!float.IsNegativeInfinity(min))
            m *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(min - soft, min + soft, value));
        if (!float.IsPositiveInfinity(max))
            m *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(max - soft, max + soft, value));
        return m;
    }

    /// <summary>All conditions hold; a field missing from <paramref name="fields"/> fails its condition.</summary>
    public static bool All(List<FieldCondition> conditions, IReadOnlyDictionary<string, float> fields)
    {
        foreach (FieldCondition condition in conditions)
        {
            if (!fields.TryGetValue(condition.field, out float value) || !condition.Holds(value))
                return false;
        }
        return true;
    }
}

/// <summary>A class of ground: what a cell becomes when all conditions hold.</summary>
public class GroundRule
{
    public string label;
    public List<FieldCondition> conditions = new List<FieldCondition>();

    /// <summary>
    /// Value written into vanilla's elevation grid, which vanilla reads as a class: &gt; 0.798 overhead mountain,
    /// &gt; 0.728 thin roof, &gt; 0.70 rock wall, 0.61-0.70 rough stone floor, 0.55-0.61 gravel, below that soil.
    /// </summary>
    public float elevation = 0.3f;

    /// <summary>Ground placed by the geology (water, marsh, mud); None leaves the terrain to vanilla.</summary>
    public GroundCover cover = GroundCover.None;
}

/// <summary>
/// The ordered ground classes (soil and surface rule S3): each cell takes the first rule whose conditions all hold.
/// The last rule should have no conditions (the default ground). One table decides water, rock, rough floor, gravel,
/// marsh, mud and soil, so tuning happens in one place.
/// </summary>
public class GroundRulesDef : Def
{
    public List<GroundRule> rules = new List<GroundRule>();

    public override IEnumerable<string> ConfigErrors()
    {
        foreach (string error in base.ConfigErrors())
            yield return error;
        if (rules.NullOrEmpty())
            yield return "no ground rules";
        else if (rules[rules.Count - 1].conditions.Count > 0)
            yield return "the last ground rule should have no conditions (the default ground)";
    }
}

/// <summary>
/// The per-cell values ground rules can test: every environment layer by defName, plus fields derived for the rules:
/// <list type="bullet">
/// <item>RockDepth: metres into bare rock (0 outside it). Bare rock is soil thinner than the soil model's rock wall depth.</item>
/// <item>RockDistance: metres to the nearest bare rock (0 inside it).</item>
/// <item>WaterDistance: cells to the nearest surface water (0 in it).</item>
/// <item>Hollow: depth (metres) of a closed hollow at the cell, below its spill level.</item>
/// <item>Climate: 0..1 climate wetness of the tile from rainfall (the same everywhere).</item>
/// <item>Marshland: 0..1 how much the map's water regime makes damp ground marsh (a wetland; the same everywhere).</item>
/// </list>
/// </summary>
public class GroundFields
{
    private readonly Dictionary<string, float[]> fields = new Dictionary<string, float[]>();
    private readonly Dictionary<string, float> constants = new Dictionary<string, float>();

    public void Add(string name, float[] values) => fields[name] = values;

    public void AddConstant(string name, float value) => constants[name] = value;

    public bool Has(string name) => fields.ContainsKey(name) || constants.ContainsKey(name);

    public float Value(string name, int cell) =>
        fields.TryGetValue(name, out float[] values) ? values[cell] : constants.TryGetValue(name, out float v) ? v : float.NaN;

    /// <summary>The index of the first rule that matches each cell (the last rule if none does).</summary>
    public int[] Classify(GroundRulesDef def, int cellCount)
    {
        foreach (GroundRule rule in def.rules)
        {
            foreach (FieldCondition condition in rule.conditions)
            {
                if (!Has(condition.field))
                    Log.ErrorOnce($"[LivingLandscapes] Ground rule '{rule.label}' tests unknown field '{condition.field}'; it never matches.",
                        condition.field.GetHashCode());
            }
        }
        var result = new int[cellCount];
        for (int i = 0; i < cellCount; i++)
        {
            int chosen = def.rules.Count - 1;
            for (int r = 0; r < def.rules.Count; r++)
            {
                if (Matches(def.rules[r], i))
                {
                    chosen = r;
                    break;
                }
            }
            result[i] = chosen;
        }
        return result;
    }

    private bool Matches(GroundRule rule, int cell)
    {
        foreach (FieldCondition condition in rule.conditions)
        {
            float v = Value(condition.field, cell);
            if (float.IsNaN(v) || v < condition.min || v >= condition.max)
                return false;
        }
        return true;
    }
}

/// <summary>Turns planned ground cover into the biome's own terrain, and recognises vanilla's wet ground.</summary>
public static class GroundCovers
{
    public static bool IsWater(GroundCover cover) =>
        cover == GroundCover.ShallowWater || cover == GroundCover.DeepWater || cover == GroundCover.ShallowMovingWater || cover == GroundCover.DeepMovingWater;

    public static TerrainDef TerrainFor(GroundCover cover, IntVec3 cell, Map map)
    {
        switch (cover)
        {
            case GroundCover.ShallowWater: return MapGenUtility.ShallowFreshWaterTerrainAt(cell, map);
            case GroundCover.DeepWater: return MapGenUtility.DeepFreshWaterTerrainAt(cell, map);
            case GroundCover.ShallowMovingWater: return MapGenUtility.ShallowMovingWaterTerrainAt(cell, map);
            case GroundCover.DeepMovingWater: return MapGenUtility.DeepMovingWaterTerrainAt(cell, map);
            case GroundCover.Marsh: return TerrainDefOf.Marsh;
            case GroundCover.Mud: return MapGenUtility.MudTerrainAt(cell, map);
            default: return null;
        }
    }

    /// <summary>Water, marsh or mud: ground the geology places, so vanilla's noise must not.</summary>
    public static bool IsWetGround(TerrainDef terrain) =>
        terrain != null && (terrain.IsWater || terrain.HasTag("WaterMarshy") || terrain.defName.Contains("Marsh") || terrain.defName.Contains("Mud"));
}
