using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>
/// Where a river that enters still water lays its load (principle P27, the jet settling kernel): density exp(-x / settle) along the
/// jet axis times a Gaussian across it whose half-width is w / 2 + spreadRate x. Extracted from <c>Sediment.Jet</c> so the same
/// numbers serve the channel stage and the sea fill (<see cref="SeaFill"/>). Game-free: no RimWorld or Verse types.
/// </summary>
public static class JetKernel
{
    /// <summary>Weights below this are dropped (the kernel's tail is not worth a cell).</summary>
    public const float MinWeight = 1e-4f;

    /// <summary>
    /// Fills <paramref name="weights"/> (cleared first) with (cell, weight, distance along the axis) for every cell of the box
    /// around <paramref name="c"/> that lies from half a cell behind the mouth to <paramref name="reach"/> cells along the axis,
    /// whose <paramref name="natural"/> ground is no higher than <paramref name="level"/> (pass +infinity for no such filter) and
    /// whose weight reaches <see cref="MinWeight"/>. Returns the sum of the weights. The kernel is not normalised.
    /// </summary>
    public static float Weights(int c, float axisX, float axisZ, float w, float level, float[] natural, int sizeX, int sizeZ,
        float settle, float reach, float spreadRate, List<(int cell, float weight, float d)> weights)
    {
        // The across direction is the axis turned a quarter turn: (-axisZ, axisX).
        float acrossX = -axisZ, acrossZ = axisX;
        int cx = c % sizeX, cz = c / sizeX, r = Mathf.CeilToInt(reach);
        weights.Clear();
        float sum = 0f;
        for (int dz = -r; dz <= r; dz++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                    continue;
                float along = dx * axisX + dz * axisZ, side = dx * acrossX + dz * acrossZ;
                if (along < -0.5f || along > reach)
                    continue;
                int i = z * sizeX + x;
                if (natural[i] > level)
                    continue;
                float half = 0.5f * w + spreadRate * Mathf.Max(along, 0f);
                float weight = Mathf.Exp(-Mathf.Max(along, 0f) / settle) * Mathf.Exp(-side * side / (2f * half * half));
                if (weight < MinWeight)
                    continue;
                weights.Add((i, weight, Mathf.Max(along, 0f)));
                sum += weight;
            }
        }
        return sum;
    }
}
