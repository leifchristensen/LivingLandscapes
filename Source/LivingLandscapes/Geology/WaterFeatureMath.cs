using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>
/// Game-free maths of the water regime's features at the river centre (a headwater's basin, a river island's knob). They are
/// inputs of the erosion model (uplift and rock softness, <see cref="Landforms"/>): erosion forms them, nothing edits finished ground.
/// </summary>
public static class WaterFeatureMath
{
    /// <summary>
    /// The disk a regime's feature lays on the model's inputs, in fractions of the map's width: its radius, how soft its edge is, how
    /// irregular its outline is (so none is a circle), the uplift change inside it and its rock softness.
    /// </summary>
    public static (float size, float edge, float warp, float upliftInside, float erodibility) Disk(float radiusShare, float basinUpliftDrop,
        float knobErodibility)
    {
        float size = Mathf.Max(radiusShare, 0f);
        return (size, size * 0.5f, size * 0.4f, -Mathf.Clamp01(basinUpliftDrop), Mathf.Clamp(knobErodibility, 0.01f, 1f));
    }

    /// <summary>
    /// How far the ground at a feature stands below (negative) or above (positive) the ground around it: the cell at
    /// <paramref name="centre"/> against the mean of a ring of cells just outside the feature (<paramref name="ringFrom"/> to
    /// <paramref name="ringTo"/> cells away). Used by the dev report of where a basin or knob formed.
    /// </summary>
    public static float RiseAboveRing(float[] h, int sizeX, int sizeZ, Vector2 centre, float ringFrom, float ringTo)
    {
        float sum = 0f;
        int count = 0;
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                float d = (new Vector2(x, z) - centre).magnitude;
                if (d >= ringFrom && d <= ringTo)
                {
                    sum += h[z * sizeX + x];
                    count++;
                }
            }
        }
        int cx = Mathf.Clamp((int)System.Math.Round(centre.x), 0, sizeX - 1), cz = Mathf.Clamp((int)System.Math.Round(centre.y), 0, sizeZ - 1);
        return count == 0 ? 0f : h[cz * sizeX + cx] - sum / count;
    }

    /// <summary>Where a ray from <paramref name="from"/> along <paramref name="direction"/> leaves the map.</summary>
    public static Vector2 EdgeToward(Vector2 from, Vector2 direction, int sizeX, int sizeZ)
    {
        Vector2 step = direction.normalized * 0.5f, v = from;
        for (int i = 0; i < (sizeX + sizeZ) * 4; i++)
        {
            Vector2 next = v + step;
            if (next.x < 0f || next.y < 0f || next.x >= sizeX || next.y >= sizeZ)
                break;
            v = next;
        }
        return v;
    }
}
