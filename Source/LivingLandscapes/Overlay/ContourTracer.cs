using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Overlay;

/// <summary>A piece of one contour line, in map coordinates (x, z).</summary>
public struct ContourSegment
{
    public Vector2 A;
    public Vector2 B;
    public int Level;
}

/// <summary>Traces contour lines through a per-cell scalar field with marching squares over cell centres.</summary>
public static class ContourTracer
{
    /// <summary>Traces every multiple of <paramref name="interval"/> between the field's minimum and <paramref name="maxValue"/>.</summary>
    public static List<ContourSegment> Trace(float[] values, int sizeX, int sizeZ, float interval, float maxValue)
    {
        var segments = new List<ContourSegment>();
        if (interval <= 0f)
            return segments;

        float min = float.MaxValue, max = float.MinValue;
        foreach (float v in values)
        {
            min = Mathf.Min(min, v);
            max = Mathf.Max(max, v);
        }
        max = Mathf.Min(max, maxValue);

        int first = Mathf.CeilToInt(min / interval);
        int last = Mathf.FloorToInt(max / interval);
        for (int level = first; level <= last; level++)
            TraceLevel(values, sizeX, sizeZ, level, level * interval, segments);
        return segments;
    }

    private static void TraceLevel(float[] e, int sizeX, int sizeZ, int level, float threshold, List<ContourSegment> output)
    {
        for (int z = 0; z < sizeZ - 1; z++)
        {
            for (int x = 0; x < sizeX - 1; x++)
            {
                float a = e[z * sizeX + x];            // bottom-left
                float b = e[z * sizeX + x + 1];        // bottom-right
                float c = e[(z + 1) * sizeX + x + 1];  // top-right
                float d = e[(z + 1) * sizeX + x];      // top-left

                int mask = (a >= threshold ? 1 : 0) | (b >= threshold ? 2 : 0) | (c >= threshold ? 4 : 0) | (d >= threshold ? 8 : 0);
                if (mask == 0 || mask == 15)
                    continue;

                // Corner positions are cell centres.
                float x0 = x + 0.5f, x1 = x + 1.5f, z0 = z + 0.5f, z1 = z + 1.5f;
                var bottom = new Vector2(Mathf.Lerp(x0, x1, Fraction(a, b, threshold)), z0);
                var right = new Vector2(x1, Mathf.Lerp(z0, z1, Fraction(b, c, threshold)));
                var top = new Vector2(Mathf.Lerp(x0, x1, Fraction(d, c, threshold)), z1);
                var left = new Vector2(x0, Mathf.Lerp(z0, z1, Fraction(a, d, threshold)));

                switch (mask)
                {
                    case 1: case 14: Add(output, left, bottom, level); break;
                    case 2: case 13: Add(output, bottom, right, level); break;
                    case 3: case 12: Add(output, left, right, level); break;
                    case 4: case 11: Add(output, right, top, level); break;
                    case 6: case 9: Add(output, bottom, top, level); break;
                    case 7: case 8: Add(output, left, top, level); break;
                    case 5:
                    case 10:
                        // Saddle: the cell centre decides which diagonal pair stays connected.
                        bool centreHigh = (a + b + c + d) * 0.25f >= threshold;
                        if (centreHigh == (mask == 5))
                        {
                            Add(output, bottom, right, level);
                            Add(output, left, top, level);
                        }
                        else
                        {
                            Add(output, left, bottom, level);
                            Add(output, right, top, level);
                        }
                        break;
                }
            }
        }
    }

    private static float Fraction(float from, float to, float level)
    {
        float span = to - from;
        return Mathf.Abs(span) < 1e-6f ? 0.5f : Mathf.Clamp01((level - from) / span);
    }

    private static void Add(List<ContourSegment> output, Vector2 a, Vector2 b, int level)
    {
        output.Add(new ContourSegment { A = a, B = b, Level = level });
    }
}
