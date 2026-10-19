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

    /// <param name="knobCentre">Where an island's knob rises: on the world river's course nearest the river centre.</param>
    /// <param name="exit">Where the world river leaves the map (map cells); null if it has none.</param>
    /// <returns>A short description for the log, empty if nothing was nudged.</returns>
    public static string Apply(float[] height, float[] rockStrength, int sizeX, int sizeZ, Vector2 riverCentre, Vector2 knobCentre,
        Vector2? exit, WaterRegimeDef water, TerrainChannelSettings p)
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
            // The hollow's floor keeps low swells (as a delta's flats): bars and islands where water spreads over it.
            var swells = new Perlin(1.0 / p.deltaApronWavelength, 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);
            for (int i = 0; i < n; i++)
            {
                int x = i % sizeX, z = i / sizeX;
                Vector2 centre = knob > 0f ? knobCentre : riverCentre;
                float d = (new Vector2(x, z) - centre).magnitude / (radius * (1f + OutlineWarp * (float)warp.GetValue(x, 0.0, z)));
                if (d >= 1f)
                    continue;
                // A knob stands in the middle half of the hollow: the river widens around it rather than being dammed.
                float f = 1f - Mathf.SmoothStep(0f, 1f, d);
                // The knob is flat-topped over the inner quarter and falls away by six tenths of the radius.
                float inner = depression > 0f ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.6f, d)) : f;
                height[i] += knob * inner - depression * f;
                if (depression > 0f)
                    height[i] += p.deltaApronRelief * (0.5f + 0.5f * (float)swells.GetValue(x, 0.0, z)) * f;
                // Resistant enough to stand in the river, but soil still holds on its top.
                if (knob > 0f && rockStrength != null)
                    rockStrength[i] = Mathf.Lerp(rockStrength[i], Mathf.Max(rockStrength[i], water.knobRockStrength), inner * 0.5f);
            }
            done = (depression > 0f ? $"centre depression {depression:F1} m" : "") + (knob > 0f ? $" centre knob {knob:F1} m" : "");
        }

        if (water.exitFlatten > 0f && exit.HasValue)
        {
            float lowest = float.MaxValue;
            for (int i = 0; i < n; i++)
                lowest = Mathf.Min(lowest, height[i]);
            float reach = water.exitReachShare * size;
            // The flats keep low swells and swales, so the water divides around them into distributaries.
            var swells = new Perlin(1.0 / p.deltaApronWavelength, 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);
            for (int i = 0; i < n; i++)
            {
                int x = i % sizeX, z = i / sizeX;
                float t = 1f - (new Vector2(x, z) - exit.Value).magnitude / reach;
                if (t <= 0f)
                    continue;
                float apron = lowest + p.deltaApronRelief * (0.5f + 0.5f * (float)swells.GetValue(x, 0.0, z));
                height[i] = Mathf.Lerp(height[i], apron, water.exitFlatten * Mathf.SmoothStep(0f, 1f, t));
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
