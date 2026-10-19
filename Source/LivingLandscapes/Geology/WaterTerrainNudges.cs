using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>
/// Mutator structures as nudges on the terrain, not drawn shapes (Docs/WATER-AUDIT.md, step 5). The water regime
/// shapes the ground where the world river runs, and the channels and lakes then follow from topology:
/// - a depression at the river centre (headwater): the lake model fills it and the river leaves from it;
/// - a knob of resistant rock at the river centre (river island): the river divides around it;
/// - relief eased toward base level near the river's exit (delta): flat ground where the water spreads.
/// Outlines are warped by noise, so no two are alike and none is a circle.
/// </summary>
public static class WaterTerrainNudges
{
    /// <summary>Share of the radius by which noise warps an outline.</summary>
    private const float OutlineWarp = 0.4f;

    /// <param name="exit">Where the world river leaves the map (map cells); null if it has none.</param>
    /// <returns>A short description for the log, empty if nothing was nudged.</returns>
    public static string Apply(float[] height, float[] rockStrength, int sizeX, int sizeZ, Vector2 riverCentre, Vector2? exit, WaterRegimeDef water)
    {
        if (water == null)
            return "";
        int n = sizeX * sizeZ;
        float size = Mathf.Max(sizeX, sizeZ);
        float depression = water.centreDepression.RandomInRange, knob = water.centreKnob.RandomInRange;
        float radius = water.centreRadiusShare * size;
        string done = "";

        if ((depression > 0f || knob > 0f) && radius > 0f)
        {
            var warp = new Perlin(1.0 / Mathf.Max(radius * 0.8f, 1f), 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);
            for (int i = 0; i < n; i++)
            {
                int x = i % sizeX, z = i / sizeX;
                float d = (new Vector2(x, z) - riverCentre).magnitude / (radius * (1f + OutlineWarp * (float)warp.GetValue(x, 0.0, z)));
                if (d >= 1f)
                    continue;
                float f = 1f - Mathf.SmoothStep(0f, 1f, d);
                height[i] += (knob - depression) * f;
                if (knob > 0f && rockStrength != null)
                    rockStrength[i] = Mathf.Lerp(rockStrength[i], Mathf.Max(rockStrength[i], water.knobRockStrength), f);
            }
            done = depression > 0f ? $"centre depression {depression:F1} m" : $"centre knob {knob:F1} m";
        }

        if (water.exitFlatten > 0f && exit.HasValue)
        {
            float lowest = float.MaxValue;
            for (int i = 0; i < n; i++)
                lowest = Mathf.Min(lowest, height[i]);
            float reach = water.exitReachShare * size;
            for (int i = 0; i < n; i++)
            {
                float t = 1f - (new Vector2(i % sizeX, i / sizeX) - exit.Value).magnitude / reach;
                if (t <= 0f)
                    continue;
                height[i] = Mathf.Lerp(height[i], lowest, water.exitFlatten * Mathf.SmoothStep(0f, 1f, t));
            }
            done += (done.Length > 0 ? ", " : "") + $"relief eased {water.exitFlatten:P0} toward the exit";
        }
        return done;
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
