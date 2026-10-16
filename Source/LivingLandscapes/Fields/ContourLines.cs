using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Fields;

/// <summary>
/// Topographic contour lines for a scalar height field (marching squares over cell centres).
/// Lines are saturation-graded by height (white at the map's lowest level, full colour at the rock threshold),
/// so slope direction reads from colour alone. Grading stops at the rock threshold because everything above it
/// is mountain, where lines are mostly hidden; spending colour there would wash out the visible ground. Segments are built once and cached by the owner;
/// drawing is limited to the camera view.
/// </summary>
public class ContourLines
{
    /// <summary>Elevation step between contour lines.</summary>
    private const float Interval = 0.05f;

    /// <summary>Every n-th line is an index line.</summary>
    private const int IndexEvery = 5;

    /// <summary>Vanilla turns cells above this elevation into rock (GenStep_RocksFromGrid).</summary>
    private const float RockThreshold = 0.7f;

    /// <summary>Hue of fully saturated (highest) lines: violet contrasts with sand, soil and vegetation.</summary>
    private const float Hue = 0.75f;

    private const float MinorWidth = 0.15f;
    private const float IndexWidth = 0.25f;
    private const float RockWidth = 0.35f;

    private struct Segment
    {
        public Vector2 A;
        public Vector2 B;
        public Material Material;
        public float Width;
    }

    private readonly List<Segment> segments = new List<Segment>();

    public int Count => segments.Count;

    public void Build(float[] elevation, int sizeX, int sizeZ)
    {
        segments.Clear();

        float min = float.MaxValue, max = float.MinValue;
        foreach (float v in elevation)
        {
            min = Mathf.Min(min, v);
            max = Mathf.Max(max, v);
        }

        int firstLevel = Mathf.CeilToInt(min / Interval);
        int lastLevel = Mathf.FloorToInt(max / Interval);
        for (int level = firstLevel; level <= lastLevel; level++)
        {
            float threshold = level * Interval;
            float width = MinorWidth;
            if (Mathf.Abs(threshold - RockThreshold) < Interval * 0.5f)
                width = RockWidth;
            else if (level % IndexEvery == 0)
                width = IndexWidth;
            float gradeTop = Mathf.Min(max, RockThreshold);
            TraceLevel(elevation, sizeX, sizeZ, threshold, LineMaterial(Mathf.InverseLerp(min, gradeTop, threshold)), width);
        }
    }

    /// <summary>Line material for a height fraction (0 = map minimum, 1 = rock threshold or map maximum).</summary>
    private static Material LineMaterial(float heightFraction)
    {
        Color color = Color.HSVToRGB(Hue, heightFraction, 1f);
        return MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, color);
    }

    private void TraceLevel(float[] e, int sizeX, int sizeZ, float level, Material material, float width)
    {
        for (int z = 0; z < sizeZ - 1; z++)
        {
            for (int x = 0; x < sizeX - 1; x++)
            {
                float a = e[z * sizeX + x];            // bottom-left
                float b = e[z * sizeX + x + 1];        // bottom-right
                float c = e[(z + 1) * sizeX + x + 1];  // top-right
                float d = e[(z + 1) * sizeX + x];      // top-left

                int mask = (a >= level ? 1 : 0) | (b >= level ? 2 : 0) | (c >= level ? 4 : 0) | (d >= level ? 8 : 0);
                if (mask == 0 || mask == 15)
                    continue;

                // Corner positions are cell centres.
                float x0 = x + 0.5f, x1 = x + 1.5f, z0 = z + 0.5f, z1 = z + 1.5f;
                Vector2 bottom = new Vector2(Mathf.Lerp(x0, x1, Fraction(a, b, level)), z0);
                Vector2 right = new Vector2(x1, Mathf.Lerp(z0, z1, Fraction(b, c, level)));
                Vector2 top = new Vector2(Mathf.Lerp(x0, x1, Fraction(d, c, level)), z1);
                Vector2 left = new Vector2(x0, Mathf.Lerp(z0, z1, Fraction(a, d, level)));

                switch (mask)
                {
                    case 1: case 14: Add(left, bottom, material, width); break;
                    case 2: case 13: Add(bottom, right, material, width); break;
                    case 3: case 12: Add(left, right, material, width); break;
                    case 4: case 11: Add(right, top, material, width); break;
                    case 6: case 9: Add(bottom, top, material, width); break;
                    case 7: case 8: Add(left, top, material, width); break;
                    case 5:
                    case 10:
                        // Saddle: the cell centre decides which diagonal pair stays connected.
                        bool centreHigh = (a + b + c + d) * 0.25f >= level;
                        if (centreHigh == (mask == 5))
                        {
                            Add(bottom, right, material, width);
                            Add(left, top, material, width);
                        }
                        else
                        {
                            Add(left, bottom, material, width);
                            Add(right, top, material, width);
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

    private void Add(Vector2 a, Vector2 b, Material material, float width)
    {
        segments.Add(new Segment { A = a, B = b, Material = material, Width = width });
    }

    public void Draw()
    {
        CellRect view = Find.CameraDriver.CurrentViewRect.ExpandedBy(2);
        float y = AltitudeLayer.MetaOverlays.AltitudeFor();
        foreach (Segment s in segments)
        {
            if (!view.Contains(new IntVec3((int)s.A.x, 0, (int)s.A.y)))
                continue;
            GenDraw.DrawLineBetween(new Vector3(s.A.x, y, s.A.y), new Vector3(s.B.x, y, s.B.y), s.Material, s.Width);
        }
    }
}
