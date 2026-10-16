using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>Tuning for <see cref="HeightInference"/>, set on the height layer Def in XML. Heights are in vanilla units (rock forms above 0.7).</summary>
public class HeightInferenceExtension : DefModExtension
{
    /// <summary>Gaussian smoothing (cells) that removes vanilla's high-frequency detail noise.</summary>
    public float smoothingSigma = 2.5f;

    /// <summary>How far below its surroundings ground near water is pulled (water collects in lows).</summary>
    public float basinDepth = 0.12f;

    /// <summary>Distance (cells) over which the basin pull fades to 1/e.</summary>
    public float basinReach = 18f;

    /// <summary>Basin pull of waterlogged ground (marsh, mud) relative to open water.</summary>
    public float wetBasinFactor = 0.5f;

    /// <summary>Lake surface below its lowest shore cell.</summary>
    public float shoreDrop = 0.01f;

    /// <summary>River bed below the surrounding valley floor.</summary>
    public float riverIncision = 0.04f;

    /// <summary>Extra drop per cell along the river's flow direction, so it drains the way vanilla says it flows.</summary>
    public float riverGradientPerCell = 0.0006f;

    /// <summary>Minimum height of natural rock cells (vanilla's rock threshold).</summary>
    public float rockThreshold = 0.7f;

    /// <summary>Softening (cells) of the step where ground meets the rock threshold at mountain edges.</summary>
    public float rockEdgeSigma = 1f;

    /// <summary>Deepest notch carved through a basin rim; deeper basins are partly filled first.</summary>
    public float maxBreachDepth = 0.06f;

    /// <summary>Minimum fall per cell after breaching and filling, so every cell drains somewhere.</summary>
    public float drainageEpsilon = 0.00005f;
}

/// <summary>
/// "Off" mode height: infers a hydrologically consistent surface from the terrain vanilla placed, without changing
/// that terrain. Principles: detail noise is not landform; water collects in lows; lakes are flat and below their
/// shores; rivers are incised and descend along their flow; mountains are high; every cell drains to an outlet.
/// </summary>
public static class HeightInference
{
    public static float[] Infer(Map map, float[] vanillaElevation, HeightInferenceExtension p)
    {
        int sizeX = map.Size.x, sizeZ = map.Size.z, n = vanillaElevation.Length;
        TerrainFeature[] features = TerrainFeatures.Classify(map);

        // 1. Land surface without vanilla's detail noise. Water cells carry artificial values (lakes flattened to 0,
        //    rivers -1), so they get zero weight and are filled in from the surrounding land.
        var weights = new float[n];
        for (int i = 0; i < n; i++)
            weights[i] = TerrainFeatures.IsWater(features[i]) ? 0f : 1f;
        float[] h = GridMath.MaskedBlur(vanillaElevation, weights, sizeX, sizeZ, p.smoothingSigma);

        // 2. Water collects in lows: pull the ground down around water and waterlogged ground.
        float[] toWater = GridMath.DistanceTo(i => TerrainFeatures.IsWater(features[i]), sizeX, sizeZ);
        float[] toWet = GridMath.DistanceTo(i => features[i] == TerrainFeature.Wet, sizeX, sizeZ);
        for (int i = 0; i < n; i++)
        {
            float pull = Mathf.Max(Mathf.Exp(-toWater[i] / p.basinReach), p.wetBasinFactor * Mathf.Exp(-toWet[i] / p.basinReach));
            h[i] -= p.basinDepth * pull;
        }

        // 3. Mountains are high: natural rock never sits below the rock threshold. A light blur afterwards
        //    softens the cell-sized step at mountain edges; rock is clamped again so it stays above the threshold.
        ClampRock(h, features, p.rockThreshold);
        var ones = new float[n];
        for (int i = 0; i < n; i++)
            ones[i] = 1f;
        h = GridMath.MaskedBlur(h, ones, sizeX, sizeZ, p.rockEdgeSigma);
        ClampRock(h, features, p.rockThreshold);

        // 4. Still water is flat and below its lowest shore.
        FlattenStillWater(h, features, sizeX, sizeZ, p);

        // 5. Rivers are incised and descend along vanilla's flow direction.
        Vector2 flow = TerrainFeatures.MeanRiverFlow(map);
        var centre = new Vector2(sizeX * 0.5f, sizeZ * 0.5f);
        for (int i = 0; i < n; i++)
        {
            if (features[i] != TerrainFeature.River)
                continue;
            float along = Vector2.Dot(new Vector2(i % sizeX, i / sizeX) - centre, flow);
            h[i] -= p.riverIncision + along * p.riverGradientPerCell;
        }

        // 6. Everything drains: closed depressions are breached through their rim (deep ones partly filled first).
        //    The sea is an outlet.
        GridMath.BreachDepressions(h, sizeX, sizeZ, i => features[i] == TerrainFeature.Ocean, p.drainageEpsilon, p.maxBreachDepth);
        return h;
    }

    private static void ClampRock(float[] h, TerrainFeature[] features, float threshold)
    {
        for (int i = 0; i < h.Length; i++)
        {
            if (features[i] == TerrainFeature.Rock)
                h[i] = Mathf.Max(h[i], threshold);
        }
    }

    private static void FlattenStillWater(float[] h, TerrainFeature[] features, int sizeX, int sizeZ, HeightInferenceExtension p)
    {
        int bodies = GridMath.LabelRegions(i => features[i] == TerrainFeature.StillWater || features[i] == TerrainFeature.DeepStillWater,
            sizeX, sizeZ, out int[] labels);
        if (bodies == 0)
            return;

        // Lowest land cell touching each body.
        var lowestShore = new float[bodies];
        for (int b = 0; b < bodies; b++)
            lowestShore[b] = float.MaxValue;
        for (int i = 0; i < h.Length; i++)
        {
            if (labels[i] != -1 || TerrainFeatures.IsWater(features[i]))
                continue;
            float height = h[i];
            GridMath.ForEachNeighbour(i, sizeX, sizeZ, nb =>
            {
                int body = labels[nb];
                if (body != -1 && height < lowestShore[body])
                    lowestShore[body] = height;
            });
        }

        for (int i = 0; i < h.Length; i++)
        {
            int body = labels[i];
            if (body == -1)
                continue;
            float surface = lowestShore[body] == float.MaxValue ? h[i] : lowestShore[body] - p.shoreDrop;
            h[i] = surface;
        }
    }
}
