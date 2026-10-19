using UnityEngine;

namespace LivingLandscapes.Flora;

/// <summary>
/// Shade cast by placed plants, 0..1 per cell. Strata are placed canopy first; each plant adds a soft round shadow
/// shifted towards the pole, and the strata after it read the field.
/// </summary>
public class ShadeField
{
    private readonly float[] values;
    private readonly int sizeX, sizeZ;

    public ShadeField(int sizeX, int sizeZ)
    {
        this.sizeX = sizeX;
        this.sizeZ = sizeZ;
        values = new float[sizeX * sizeZ];
    }

    public float[] Values => values;

    /// <summary>Adds one plant's shadow: a Gaussian of the given radius (cells) centred <paramref name="shift"/> cells from the plant.</summary>
    public void Cast(int x, int z, float strength, float radius, Vector2 shift)
    {
        if (strength <= 0f || radius <= 0f)
            return;
        int cx = Mathf.RoundToInt(x + shift.x), cz = Mathf.RoundToInt(z + shift.y);
        int reach = Mathf.CeilToInt(radius * 2f);
        for (int zz = Mathf.Max(cz - reach, 0); zz <= Mathf.Min(cz + reach, sizeZ - 1); zz++)
        {
            for (int xx = Mathf.Max(cx - reach, 0); xx <= Mathf.Min(cx + reach, sizeX - 1); xx++)
            {
                float d2 = (xx - cx) * (xx - cx) + (zz - cz) * (zz - cz);
                float add = Mathf.Min(strength * Mathf.Exp(-d2 / (2f * radius * radius)), 1f);
                int i = zz * sizeX + xx;
                // Shadows overlap like layered glass: two half-shades give three quarters, never more than 1.
                values[i] = 1f - (1f - values[i]) * (1f - add);
            }
        }
    }
}
