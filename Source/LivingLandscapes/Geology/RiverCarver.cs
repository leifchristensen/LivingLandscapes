using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>River bed settings (part of <see cref="GeologyGenerationExtension"/>).</summary>
public class RiverBedSettings
{
    /// <summary>The bed lies this far (metres) below the valley floor beside it.</summary>
    public float depth = 0.6f;

    /// <summary>Cells beyond the channel over which the banks ease back to the natural ground.</summary>
    public float bankWidth = 4f;

    /// <summary>Extra cells either side of the channel carved to the bed, covering the painted river's wobble.</summary>
    public float margin = 2f;
}

/// <summary>
/// Water flows downhill or level (rule W1). The river's course comes from the erosion model, but the map's terrain
/// is finer (detail and micro-relief), so the bed along the course can rise and fall by more than the river drops
/// across the map. This carves the channel instead: the bed profile along the course is made never to rise
/// (the closest non-increasing profile, so humps are cut and hollows silted up), sunk below the valley floor, and the
/// banks ease back to the natural ground.
/// </summary>
public static class RiverCarver
{
    /// <summary>
    /// Carves the river along <paramref name="course"/> (map cells, upstream to downstream) into
    /// <paramref name="height"/> (metres, indexed z * sizeX + x). <paramref name="width"/> is the river's width in cells.
    /// </summary>
    public static void Carve(float[] height, int sizeX, int sizeZ, List<Vector2> course, float width, RiverBedSettings p)
    {
        if (course == null || course.Count < 2)
            return;
        float halfWidth = width * 0.5f + p.margin;
        var cumulative = new float[course.Count];
        for (int i = 1; i < course.Count; i++)
            cumulative[i] = cumulative[i - 1] + (course[i] - course[i - 1]).magnitude;

        // Every cell's distance to the course and position along it.
        int n = sizeX * sizeZ;
        var distance = new float[n];
        var along = new float[n];
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                var cell = new Vector2(x + 0.5f, z + 0.5f);
                float best = float.MaxValue, bestAlong = 0f;
                for (int s = 0; s < course.Count - 1; s++)
                {
                    Vector2 a = course[s], ab = course[s + 1] - a;
                    float len2 = ab.sqrMagnitude;
                    float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(cell - a, ab) / len2);
                    float d = (cell - (a + t * ab)).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        bestAlong = cumulative[s] + t * Mathf.Sqrt(len2);
                    }
                }
                distance[z * sizeX + x] = Mathf.Sqrt(best);
                along[z * sizeX + x] = bestAlong;
            }
        }

        // Valley floor profile: the mean natural height across the channel, per cell of length along the course.
        int bins = Mathf.CeilToInt(cumulative[course.Count - 1]) + 1;
        var sum = new float[bins];
        var count = new int[bins];
        for (int i = 0; i < n; i++)
        {
            if (distance[i] > halfWidth)
                continue;
            int b = Mathf.Clamp((int)along[i], 0, bins - 1);
            sum[b] += height[i];
            count[b]++;
        }
        var profile = new List<float>();
        var position = new List<int>();
        for (int b = 0; b < bins; b++)
        {
            if (count[b] == 0)
                continue;
            profile.Add(sum[b] / count[b]);
            position.Add(b);
        }
        if (profile.Count < 2)
            return;
        float[] bed = NonIncreasing(profile);

        // Bed height for every bin, interpolated between the bins inside the map.
        var bedAt = new float[bins];
        for (int k = 0, b = 0; b < bins; b++)
        {
            while (k < position.Count - 1 && position[k + 1] <= b)
                k++;
            if (b <= position[0])
                bedAt[b] = bed[0];
            else if (k >= position.Count - 1)
                bedAt[b] = bed[bed.Length - 1];
            else
                bedAt[b] = Mathf.Lerp(bed[k], bed[k + 1], (b - position[k]) / (float)(position[k + 1] - position[k]));
            bedAt[b] -= p.depth;
        }

        for (int i = 0; i < n; i++)
        {
            float d = distance[i];
            if (d > halfWidth + p.bankWidth)
                continue;
            float floor = bedAt[Mathf.Clamp((int)along[i], 0, bins - 1)];
            if (d <= halfWidth)
                height[i] = floor;
            else
                height[i] = Mathf.Min(height[i], Mathf.Lerp(floor, height[i], Smooth((d - halfWidth) / p.bankWidth)));
        }
    }

    /// <summary>The non-increasing sequence closest to <paramref name="values"/> (least squares, pool adjacent violators).</summary>
    private static float[] NonIncreasing(List<float> values)
    {
        var means = new List<float>();
        var weights = new List<int>();
        foreach (float v in values)
        {
            means.Add(v);
            weights.Add(1);
            while (means.Count > 1 && means[means.Count - 1] > means[means.Count - 2])
            {
                int last = means.Count - 1;
                int w = weights[last - 1] + weights[last];
                means[last - 1] = (means[last - 1] * weights[last - 1] + means[last] * weights[last]) / w;
                weights[last - 1] = w;
                means.RemoveAt(last);
                weights.RemoveAt(last);
            }
        }
        var result = new float[values.Count];
        for (int block = 0, i = 0; block < means.Count; block++)
        {
            for (int k = 0; k < weights[block]; k++)
                result[i++] = means[block];
        }
        return result;
    }

    private static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
