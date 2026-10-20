using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>Tuning for terraced height, set on the height layer Def in XML.</summary>
public class TerraceExtension : DefModExtension
{
    /// <summary>About how many height stages span the map's relief.</summary>
    public float stagesAcrossRelief = 6f;

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
    public float StageHeight;
    public int[] Stage;
    public float[] Steepness;
}

public static class Terraces
{
    /// <summary>The single terrace plan both the walkable height and the cliffs are built from.</summary>
    public static TerracePlan Plan(float[] landform, int sizeX, int sizeZ, float cellSize, TerraceExtension p)
    {
        float stageHeight = StageHeight(landform, p);
        return new TerracePlan
        {
            StageHeight = stageHeight,
            Stage = Stages(landform, stageHeight),
            Steepness = Steepness(landform, sizeX, sizeZ, cellSize, p),
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
