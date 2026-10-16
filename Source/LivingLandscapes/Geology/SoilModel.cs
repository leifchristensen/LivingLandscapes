using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>Tuning for soil depth and bedrock exposure, set on the soil depth layer Def in XML. Lengths in metres.</summary>
public class SoilModelExtension : DefModExtension
{
    /// <summary>Soil depth on level, well-drained ground.</summary>
    public float baseSoilDepth = 1.2f;

    /// <summary>Slope (rise over run) at which no soil can stay: bare rock face. 0.84 is about 40 degrees.</summary>
    public float bareRockSlope = 0.84f;

    /// <summary>Extra soil in hollows and valley floors, where creep and wash collect it (fraction of base depth).</summary>
    public float hollowBonus = 1.0f;

    /// <summary>Soil missing on crests and noses, where it is shed (fraction of base depth).</summary>
    public float crestLoss = 0.5f;

    /// <summary>
    /// Soil depth multiplier on the weakest and the strongest rock: weak rock weathers into deep soil, strong rock
    /// stays bare, so hard beds show as rock ridges.
    /// </summary>
    public float soilOnWeakRock = 1.5f;
    public float soilOnStrongRock = 0.3f;

    /// <summary>
    /// Bare-rock slope multiplier on the weakest and strongest rock: strong rock weathers too slowly to keep soil on
    /// moderate slopes, so it is bare at gentler slopes than weak rock.
    /// </summary>
    public float bareSlopeOnWeakRock = 1.3f;
    public float bareSlopeOnStrongRock = 0.5f;

    /// <summary>Below this soil depth bedrock is bare (ground rules: RockDepth, RockDistance).</summary>
    public float rockWallDepth = 0.05f;

    /// <summary>
    /// Passes: where a cross-strike joint cuts a ridge, the ridge is broken by a gap of rough stone this many cells
    /// either side of the joint. Only rock within <see cref="passMaxDepth"/> metres of the rock edge is opened, so
    /// ridges and mountain fringes get gaps and notches while massive mountain interiors stay solid. Joints whose
    /// network is less open than <see cref="passMinOpen"/> stay closed, so passes are irregular.
    /// </summary>
    public float passHalfWidth = 3f;
    public float passMaxDepth = 12f;
    public float passMinOpen = 0.25f;

}

/// <summary>
/// Soil depth from landform: soil thins with slope (none stays above the bare-rock slope), collects where flow
/// converges (hollows, valley floors) and is shed from divergent crests. A standard steady-state hillslope
/// approximation; exact soil production models can replace it without changing callers.
/// </summary>
public static class SoilModel
{
    /// <param name="rockStrength">Rock strength (0..1) per cell, or null when unknown (no rock effect).</param>
    public static float[] SoilDepth(float[] height, int sizeX, int sizeZ, float cellSize, SoilModelExtension p, float[] rockStrength = null)
    {
        float[] slope = GridMath.Slope(height, sizeX, sizeZ);
        float[] area = GridMath.FlowAccumulationMfd(height, sizeX, sizeZ);
        var soil = new float[height.Length];
        for (int i = 0; i < soil.Length; i++)
        {
            float gradient = slope[i] / cellSize;
            // 0 on divergent crests (area ~1 cell), 1 in strongly convergent hollows (area >= 1000 cells).
            float convergence = Mathf.Clamp01(Mathf.Log10(area[i]) / 3f);
            float landform = 1f + p.hollowBonus * convergence - p.crestLoss * (1f - convergence);
            float strength = rockStrength == null ? -1f : rockStrength[i];
            float bareSlope = strength < 0f ? p.bareRockSlope : p.bareRockSlope * Mathf.Lerp(p.bareSlopeOnWeakRock, p.bareSlopeOnStrongRock, strength);
            float steepness = Mathf.Clamp01(1f - gradient / bareSlope);
            float rock = strength < 0f ? 1f : Mathf.Lerp(p.soilOnWeakRock, p.soilOnStrongRock, strength);
            soil[i] = Mathf.Max(0f, p.baseSoilDepth * landform * steepness * rock);
        }
        return soil;
    }
}
