using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Fields;

/// <summary>
/// Topographic contour lines for a scalar height field (marching squares over cell centres).
/// Segments are built once and cached by the owner; drawing is limited to the camera view.
/// </summary>
public class ContourLines
{
    /// <summary>Elevation step between contour lines.</summary>
    private const float Interval = 0.05f;

    /// <summary>Every n-th line is an index line.</summary>
    private const int IndexEvery = 5;

    /// <summary>Vanilla turns cells above this elevation into rock (GenStep_RocksFromGrid).</summary>
    private const float RockThreshold = 0.7f;

    private const float MinorWidth = 0.12f;
    private const float IndexWidth = 0.25f;

    /// <summary>A cell is a candidate extremum if it is the highest/lowest within this many cells.</summary>
    private const int ExtremumWindow = 6;

    /// <summary>Extrema closer than this (cells) to a stronger one of the same kind are dropped.</summary>
    private const float ExtremumSeparation = 15f;

    private const int MaxExtremaPerKind = 10;

    private const float MarkerRadius = 1.5f;

    private struct Segment
    {
        public Vector2 A;
        public Vector2 B;
        public SimpleColor Color;
        public float Width;
    }

    private struct Extremum
    {
        public Vector2 Pos;
        public float Value;
        public bool IsMax;
    }

    private readonly List<Segment> segments = new List<Segment>();
    private readonly List<Extremum> extrema = new List<Extremum>();

    public int Count => segments.Count;

    public int ExtremaCount => extrema.Count;

    public void Build(float[] elevation, int sizeX, int sizeZ)
    {
        segments.Clear();
        FindExtrema(elevation, sizeX, sizeZ);

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
            SimpleColor color = SimpleColor.White;
            float width = MinorWidth;
            if (Mathf.Abs(threshold - RockThreshold) < Interval * 0.5f)
            {
                color = SimpleColor.Red;
                width = IndexWidth;
            }
            else if (level % IndexEvery == 0)
            {
                color = SimpleColor.Yellow;
                width = IndexWidth;
            }
            TraceLevel(elevation, sizeX, sizeZ, threshold, color, width);
        }
    }

    private void TraceLevel(float[] e, int sizeX, int sizeZ, float level, SimpleColor color, float width)
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
                    case 1: case 14: Add(left, bottom, color, width); break;
                    case 2: case 13: Add(bottom, right, color, width); break;
                    case 3: case 12: Add(left, right, color, width); break;
                    case 4: case 11: Add(right, top, color, width); break;
                    case 6: case 9: Add(bottom, top, color, width); break;
                    case 7: case 8: Add(left, top, color, width); break;
                    case 5:
                    case 10:
                        // Saddle: the cell centre decides which diagonal pair stays connected.
                        bool centreHigh = (a + b + c + d) * 0.25f >= level;
                        if (centreHigh == (mask == 5))
                        {
                            Add(bottom, right, color, width);
                            Add(left, top, color, width);
                        }
                        else
                        {
                            Add(left, bottom, color, width);
                            Add(right, top, color, width);
                        }
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Window extrema, thinned so plateaus (e.g. lakes flattened to 0) and noise don't produce a marker per cell:
    /// strongest first, anything within ExtremumSeparation of an accepted one of the same kind is dropped.
    /// </summary>
    private void FindExtrema(float[] e, int sizeX, int sizeZ)
    {
        extrema.Clear();
        var maxima = new List<Extremum>();
        var minima = new List<Extremum>();
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                float v = e[z * sizeX + x];
                bool isMax = true, isMin = true;
                for (int dz = -ExtremumWindow; dz <= ExtremumWindow && (isMax || isMin); dz++)
                {
                    int nz = z + dz;
                    if (nz < 0 || nz >= sizeZ)
                        continue;
                    for (int dx = -ExtremumWindow; dx <= ExtremumWindow; dx++)
                    {
                        int nx = x + dx;
                        if (nx < 0 || nx >= sizeX)
                            continue;
                        float n = e[nz * sizeX + nx];
                        if (n > v) isMax = false;
                        if (n < v) isMin = false;
                    }
                }
                var pos = new Vector2(x + 0.5f, z + 0.5f);
                if (isMax) maxima.Add(new Extremum { Pos = pos, Value = v, IsMax = true });
                if (isMin) minima.Add(new Extremum { Pos = pos, Value = v, IsMax = false });
            }
        }
        maxima.Sort((p, q) => q.Value.CompareTo(p.Value));
        minima.Sort((p, q) => p.Value.CompareTo(q.Value));
        Thin(maxima);
        Thin(minima);
    }

    private void Thin(List<Extremum> sorted)
    {
        int accepted = 0;
        int start = extrema.Count;
        foreach (Extremum candidate in sorted)
        {
            bool tooClose = false;
            for (int i = start; i < extrema.Count; i++)
            {
                if ((extrema[i].Pos - candidate.Pos).sqrMagnitude < ExtremumSeparation * ExtremumSeparation)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose)
                continue;
            extrema.Add(candidate);
            if (++accepted >= MaxExtremaPerKind)
                break;
        }
    }

    private static float Fraction(float from, float to, float level)
    {
        float span = to - from;
        return Mathf.Abs(span) < 1e-6f ? 0.5f : Mathf.Clamp01((level - from) / span);
    }

    private void Add(Vector2 a, Vector2 b, SimpleColor color, float width)
    {
        segments.Add(new Segment { A = a, B = b, Color = color, Width = width });
    }

    public void Draw()
    {
        CellRect view = Find.CameraDriver.CurrentViewRect.ExpandedBy(2);
        float y = AltitudeLayer.MetaOverlays.AltitudeFor();
        foreach (Segment s in segments)
        {
            if (!view.Contains(new IntVec3((int)s.A.x, 0, (int)s.A.y)))
                continue;
            GenDraw.DrawLineBetween(new Vector3(s.A.x, y, s.A.y), new Vector3(s.B.x, y, s.B.y), s.Color, s.Width);
        }
        foreach (Extremum x in extrema)
            GenDraw.DrawCircleOutline(new Vector3(x.Pos.x, y, x.Pos.y), MarkerRadius, x.IsMax ? SimpleColor.Orange : SimpleColor.Cyan);
    }

    /// <summary>Labels the extrema with their elevation (H = local high, L = local low). Call from OnGUI.</summary>
    public void DrawLabels()
    {
        CellRect view = Find.CameraDriver.CurrentViewRect.ExpandedBy(2);
        foreach (Extremum x in extrema)
        {
            if (!view.Contains(new IntVec3((int)x.Pos.x, 0, (int)x.Pos.y)))
                continue;
            string text = (x.IsMax ? "H " : "L ") + x.Value.ToString("F2");
            GenMapUI.DrawText(new Vector2(x.Pos.x, x.Pos.y - MarkerRadius), text, x.IsMax ? Color.yellow : Color.cyan);
        }
    }
}
