using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>
/// Game-free maths of the water regime's features at the river centre (a headwater's basin, a river island's knob). They are
/// inputs of the erosion model (uplift and rock softness, <see cref="FeaturePlan"/>): erosion forms them, nothing edits finished ground.
/// </summary>
public static class WaterFeatureMath
{
    /// <summary>
    /// The disk a regime's feature lays on the model's inputs, in fractions of the map's width: its radius, how soft its edge is, how
    /// irregular its outline is (so none is a circle), the uplift change inside it and its rock softness.
    /// </summary>
    /// <param name="valleyHalfWidth">Half-width of the valley the river runs in, as a share of the map's width. A knob is at most half of it
    /// (so a knob always leaves room for water to pass on both sides; a wider knob dams the valley and a lake forms behind it).</param>
    public static (float size, float edge, float warp, float upliftInside, float erodibility) Disk(float radiusShare, float basinUpliftDrop,
        float knobErodibility, float valleyHalfWidth = float.PositiveInfinity)
    {
        float size = Mathf.Max(radiusShare, 0f);
        if (knobErodibility < 1f)
            size = Mathf.Min(size, valleyHalfWidth * 0.5f);
        return (size, size * 0.5f, size * 0.4f, -Mathf.Clamp01(basinUpliftDrop), Mathf.Clamp(knobErodibility, 0.01f, 1f));
    }

    /// <summary>
    /// Whether a regime's feature can exist on a tile's river. A knob (<paramref name="knobErodibility"/> below 1) is rock the river
    /// divides round, so a river must come in from beyond the map; a river that rises on the map (no inflow, the game's own headwater
    /// tile: it adds a river island only where the tile has other than one river link) has nothing to divide, and the knob would stand on its
    /// spring, a hill with the spring's pool perched on it. Used to refuse a forced regime the world map cannot give.
    /// </summary>
    public static bool RegimeFitsRiver(float knobErodibility, int riverInflows) => knobErodibility >= 1f || riverInflows > 0;

    /// <summary>The smallest distance from <paramref name="point"/> to any of the course's points, or infinity for an empty course.</summary>
    public static float DistanceToCourse(System.Collections.Generic.IEnumerable<System.Collections.Generic.IList<Vector2>> courses, Vector2 point)
    {
        float best = float.PositiveInfinity;
        foreach (var course in courses)
            foreach (Vector2 v in course)
                best = Mathf.Min(best, (v - point).magnitude);
        return best;
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
