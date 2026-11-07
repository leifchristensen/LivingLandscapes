using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>
/// Where the rain runs off (#216): the climate sets how much of the rain a tile sheds, the ground sets where. Rain soaks in
/// until the ground's infiltration capacity is used up, so hard rock and steep slopes (little time on the surface to soak
/// in) shed far more than deep soil on gentle ground.
///
///   a_i = a_biome x w_i,   w_i = (1 - L x strength_i) / (1 + slope_i / S_half)
///   r_i = clamp01((climate - a_i) / (1 - a_i))
///
/// w_i is the cell's infiltration capacity as a share of deep soil on flat ground (1), L the share the hardest rock loses,
/// S_half the slope (height per cell) at which infiltration halves. The routing and the lakes read the tile's mean, which the
/// climate decides (the same mean as before the ground was asked); the ground only moves runoff from the soft, gentle cells to
/// the hard, steep ones, so rocky headwaters carry the torrents and deep soil the seeps. Pure arithmetic, no game types.
/// </summary>
public static class GroundRunoff
{
    /// <summary>Infiltration capacity of one cell as a share of deep soil on flat ground.</summary>
    public static float Infiltration(float rockStrength, float slope, float rockAbsorptionLoss, float slopeAbsorptionHalf) =>
        Mathf.Clamp01(1f - rockAbsorptionLoss * Mathf.Clamp01(rockStrength)) / (1f + Mathf.Max(0f, slope) / Mathf.Max(slopeAbsorptionHalf, 1e-4f));

    /// <summary>Runoff share of one cell: what rain is left once its absorption (the biome's, scaled by the cell's infiltration) is used up.</summary>
    public static float CellRunoff(float climate, float absorption, float infiltration)
    {
        float a = absorption * infiltration;
        return Mathf.Clamp01((climate - a) / Mathf.Max(1f - a, 0.05f));
    }

    /// <summary>
    /// Each cell's runoff, from the ground, with the tile's mean held at <paramref name="meanRunoff"/>: the ground moves the
    /// water about, the climate decides how much there is. Null where there is nothing to move it by (no rock layer).
    /// </summary>
    public static float[] Distribute(float meanRunoff, float climate, float absorption, float[] rockStrength, float[] slope,
        float rockAbsorptionLoss, float slopeAbsorptionHalf, float minimum = 0.01f)
    {
        if (rockStrength == null || slope == null || rockStrength.Length != slope.Length || rockStrength.Length == 0)
            return null;
        int n = slope.Length;
        var runoff = new float[n];
        for (int i = 0; i < n; i++)
            runoff[i] = CellRunoff(climate, absorption, Infiltration(rockStrength[i], slope[i], rockAbsorptionLoss, slopeAbsorptionHalf));
        // Rescale to the climate's mean; clamping at 1 and the floor loses a little, so settle it in a few rounds.
        for (int round = 0; round < 4; round++)
        {
            double sum = 0;
            for (int i = 0; i < n; i++)
                sum += runoff[i];
            float mean = (float)(sum / n);
            if (mean <= 1e-6f || Mathf.Abs(mean - meanRunoff) < 1e-4f)
                break;
            float k = meanRunoff / mean;
            for (int i = 0; i < n; i++)
                runoff[i] = Mathf.Clamp(runoff[i] * k, minimum, 1f);
        }
        return runoff;
    }
}
