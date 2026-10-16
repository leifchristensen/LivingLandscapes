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

    /// <summary>Below this soil depth bedrock forms a wall (impassable natural rock).</summary>
    public float rockWallDepth = 0.05f;

    /// <summary>Below this soil depth bedrock shows through as rough stone floor.</summary>
    public float rockFloorDepth = 0.3f;

    /// <summary>Scree (gravel) collects within this distance below rock walls, on slopes steeper than <see cref="screeMinSlope"/>.</summary>
    public float screeReach = 5f;
    public float screeMinSlope = 0.2f;

    /// <summary>Bare rock this far (metres) inside a rock mass is mountain: solid, mineable rock. Nearer the edge,
    /// steep bare rock forms cliffs and gentle bare rock is rough stone floor.</summary>
    public float massiveRockDepth = 4f;

    /// <summary>Bare rock steeper than this is a scarp (climbable, slow, gives cover); 0.7 is about 35 degrees.</summary>
    public float scarpSlope = 0.7f;

    /// <summary>Bare rock steeper than this is a sheer cliff (impassable, blocks sight); 1.5 is about 56 degrees.</summary>
    public float sheerSlope = 1.5f;

    /// <summary>Depth into a rock mass (metres from its edge) where a thin roof starts, and where it becomes overhead mountain.</summary>
    public float thinRoofDepth = 3f;
    public float thickRoofDepth = 7f;
}

/// <summary>
/// Soil depth from landform: soil thins with slope (none stays above the bare-rock slope), collects where flow
/// converges (hollows, valley floors) and is shed from divergent crests. A standard steady-state hillslope
/// approximation; exact soil production models can replace it without changing callers.
/// </summary>
public static class SoilModel
{
    public static float[] SoilDepth(float[] height, int sizeX, int sizeZ, float cellSize, SoilModelExtension p)
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
            float steepness = Mathf.Clamp01(1f - gradient / p.bareRockSlope);
            soil[i] = Mathf.Max(0f, p.baseSoilDepth * landform * steepness);
        }
        return soil;
    }
}
