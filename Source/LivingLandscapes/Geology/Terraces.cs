using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>Tuning for terraced height, set on the height layer Def in XML.</summary>
public class TerraceExtension : DefModExtension
{
    /// <summary>About how many height stages span the map's relief (equal stages, where the rock beds are unknown).</summary>
    public float stagesAcrossRelief = 6f;

    /// <summary>
    /// Stages follow the rock: each hard bed caps a stage, so cliffs stand where hard rock overlies soft (layer-cake walls,
    /// mesa rims) and soft beds between them weather to slopes. Off: equal stages across the relief.
    /// </summary>
    public bool stagesFromHardBeds = true;

    /// <summary>Lowest stage height (metres), so flat maps don't get a cliff for every hummock.</summary>
    public float minStageHeight = 2f;

    /// <summary>Share of a stage's height that rises gently across its level ground; the rest is the cliff.</summary>
    public float treadRise = 0.2f;

    /// <summary>
    /// A summit stage no larger than this (cells) with mostly steep sides is a knob of rock, not a ring of cliffs.
    /// </summary>
    public int knobMaxCells = 150;

    /// <summary>Steps between stages come about every this many cells along a cliff band.</summary>
    public int stepSpacing = 24;

    /// <summary>
    /// Only steep ground is stepped into stages, and only there do cliffs form, where contour lines crowd together.
    /// Below <see cref="gentleSlope"/> (rise over run) the ground keeps its natural shape; above
    /// <see cref="cliffSlope"/> it is fully stepped and stage edges get cliffs (1.0 = 45 degrees). Keep the two close,
    /// so stepped ground and cliffs coincide.
    /// </summary>
    public float gentleSlope = 0.9f;
    public float cliffSlope = 1.0f;

    /// <summary>Smoothing (cells) of the steepness used to decide where to step, so steep zones are coherent.</summary>
    public float steepnessSmoothing = 2f;
}

/// <summary>
/// Shapes steep parts of the natural landform into height stages: level benches separated by cliffs where the
/// landform crosses each stage height. Gentle ground keeps its natural shape, so cliffs form only where contour lines
/// crowd together. Used with geological terrain on.
/// </summary>
/// <summary>
/// The terrace plan of a map: stage height (metres), stage index per cell (0 = lowest) and steepness per cell (0 gentle,
/// 1 cliff-steep). Made once per map from the full-precision landform when LL_Height is computed, and kept on the
/// environment component for the cliffs (<see cref="MapComponent_Environment.PlannedTerraces"/>).
/// </summary>
public class TerracePlan
{
    /// <summary>Mean height of a stage (metres), for the log; the stages themselves may differ (hard-bed stages).</summary>
    public float StageHeight;
    public int[] Stage;
    public float[] Steepness;

    /// <summary>Height (metres) where each stage starts, by stage index: its lowest ground. Null with equal stages.</summary>
    public float[] StageBase;
}

public static class Terraces
{
    /// <summary>The single terrace plan both the walkable height and the cliffs are built from.</summary>
    /// <param name="rockStage">Hard beds below each cell (<see cref="RockStrata.HardBedsBelow"/>), or null: equal stages.</param>
    public static TerracePlan Plan(float[] landform, int sizeX, int sizeZ, float cellSize, TerraceExtension p, int[] rockStage = null)
    {
        float[] steepness = Steepness(landform, sizeX, sizeZ, cellSize, p);
        if (p.stagesFromHardBeds && rockStage != null)
        {
            TerracePlan fromRock = FromHardBeds(landform, rockStage, steepness);
            if (fromRock != null)
                return fromRock;
        }
        float stageHeight = StageHeight(landform, p);
        return new TerracePlan
        {
            StageHeight = stageHeight,
            Stage = Stages(landform, stageHeight),
            Steepness = steepness,
        };
    }

    /// <summary>
    /// Stages from the rock: a cell's stage is the number of hard beds at or below its surface, and a stage starts at its
    /// lowest ground. Null where the map shows fewer than two stages (no hard bed crops out), so equal stages apply.
    /// </summary>
    private static TerracePlan FromHardBeds(float[] landform, int[] rockStage, float[] steepness)
    {
        int lowest = int.MaxValue, highest = int.MinValue;
        foreach (int s in rockStage)
        {
            lowest = Mathf.Min(lowest, s);
            highest = Mathf.Max(highest, s);
        }
        if (highest - lowest < 1)
            return null;
        int count = highest - lowest + 1;
        var stage = new int[landform.Length];
        var stageBase = new float[count];
        for (int k = 0; k < count; k++)
            stageBase[k] = float.MaxValue;
        for (int i = 0; i < landform.Length; i++)
        {
            stage[i] = rockStage[i] - lowest;
            stageBase[stage[i]] = Mathf.Min(stageBase[stage[i]], landform[i]);
        }
        // A stage that never crops out takes the base of the one below, so bases rise with the stage.
        for (int k = 0; k < count; k++)
        {
            if (stageBase[k] == float.MaxValue)
                stageBase[k] = k > 0 ? stageBase[k - 1] : 0f;
            if (k > 0)
                stageBase[k] = Mathf.Max(stageBase[k], stageBase[k - 1]);
        }
        Range(landform, out float min, out float max);
        return new TerracePlan
        {
            StageHeight = (max - min) / count,
            Stage = stage,
            Steepness = steepness,
            StageBase = stageBase,
        };
    }

    /// <summary>Stage height (metres) for a landform, from its relief.</summary>
    public static float StageHeight(float[] landform, TerraceExtension p)
    {
        Range(landform, out float min, out float max);
        return Mathf.Max(p.minStageHeight, (max - min) / Mathf.Max(1f, p.stagesAcrossRelief));
    }

    /// <summary>Stage index of every cell (0 = lowest).</summary>
    public static int[] Stages(float[] landform, float stageHeight)
    {
        Range(landform, out float min, out _);
        var stage = new int[landform.Length];
        for (int i = 0; i < stage.Length; i++)
            stage[i] = Mathf.FloorToInt((landform[i] - min) / stageHeight);
        return stage;
    }

    /// <summary>The terraced height: steep ground stepped (stage base plus a gentle rise across each bench), gentle
    /// ground unchanged, blended by <see cref="Steepness"/>.</summary>
    public static float[] Apply(float[] landform, TerracePlan plan, TerraceExtension p)
    {
        if (plan.StageBase == null)
            return Apply(landform, plan.StageHeight, plan.Steepness, p);
        // Stages from the rock: each bench rises gently from its stage's base, steep ground stepped onto it.
        var terraced = new float[landform.Length];
        for (int i = 0; i < terraced.Length; i++)
        {
            float stageBase = plan.StageBase[plan.Stage[i]];
            float stepped = stageBase + p.treadRise * (landform[i] - stageBase);
            terraced[i] = Mathf.Lerp(landform[i], stepped, plan.Steepness[i]);
        }
        return terraced;
    }

    public static float[] Apply(float[] landform, float stageHeight, float[] steepness, TerraceExtension p)
    {
        Range(landform, out float min, out _);
        var terraced = new float[landform.Length];
        for (int i = 0; i < terraced.Length; i++)
        {
            float position = (landform[i] - min) / stageHeight;
            float stage = Mathf.Floor(position);
            float stepped = min + stageHeight * (stage + p.treadRise * (position - stage));
            terraced[i] = Mathf.Lerp(landform[i], stepped, steepness[i]);
        }
        return terraced;
    }

    /// <summary>
    /// How strongly each cell is stepped: 0 on gentle ground, 1 where the (smoothed) landform is cliff-steep.
    /// Cliffs stand where this is at least one half.
    /// </summary>
    public static float[] Steepness(float[] landform, int sizeX, int sizeZ, float cellSize, TerraceExtension p)
    {
        float[] slope = GridMath.Slope(landform, sizeX, sizeZ);
        float[] smooth = GridMath.Blur(slope, sizeX, sizeZ, p.steepnessSmoothing);
        var weight = new float[slope.Length];
        for (int i = 0; i < weight.Length; i++)
            weight[i] = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(p.gentleSlope, p.cliffSlope, smooth[i] / cellSize));
        return weight;
    }

    private static void Range(float[] values, out float min, out float max)
    {
        min = float.MaxValue;
        max = float.MinValue;
        foreach (float v in values)
        {
            min = Mathf.Min(min, v);
            max = Mathf.Max(max, v);
        }
    }
}
