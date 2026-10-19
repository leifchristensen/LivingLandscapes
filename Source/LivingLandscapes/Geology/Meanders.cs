using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// Meander planform and oxbows (Docs/RIVER-PLANFORM.md, issue #42). Tuning in XML with the channel settings. The sinuosity the
/// river wants comes from the channel regimes (the <c>sinuosity</c> nudge: slope, discharge, bank strength and confinement
/// decide where it is high), not from here; this only draws it.
/// </summary>
public class MeanderSettings
{
    public bool enabled = true;

    /// <summary>Channels narrower than this (cells) keep the course the terrain routed them along.</summary>
    public float minWidth = 2.5f;

    /// <summary>Meander wavelength in channel widths (Leopold and Wolman: 10 to 14).</summary>
    public float wavelengthPerWidth = 11f;

    /// <summary>Wavelength wobble, as a share: bends are not a metronome.</summary>
    public float wavelengthJitter = 0.2f;

    /// <summary>The sinuosity asked for is capped here (a sine curve of this sinuosity already has tight bends).</summary>
    public float maxSinuosity = 2f;

    /// <summary>
    /// Largest bend amplitude, in channel widths: a bend's radius of curvature stays above about 1.5 widths (real bends 2 to 3), so a
    /// bend never closes into a spiral however high the sinuosity asked for.
    /// </summary>
    public float maxAmplitudeWidths = 2.6f;

    /// <summary>Amplitude tapers to nothing over this many wavelengths at a channel's source and where it joins a larger one.</summary>
    public float taperWavelengths = 0.8f;

    /// <summary>The meander trench lies this far (metres) below the water profile of the old course, plus <see cref="trenchPerSlope"/> times the slope per cell.</summary>
    public float trenchDepth = 0.35f;
    public float trenchPerSlope = 8f;

    /// <summary>Oxbows: the chance a bend of enough sinuosity and room has one, and its depth (m) and width (share of the channel's).</summary>
    public float oxbowChance = 0.6f;
    public float oxbowMinSinuosity = 1.4f;
    public float oxbowDepth = 0.7f;
    public float oxbowWidthShare = 0.8f;
}

public static class Meanders
{
    private struct Oxbow
    {
        public List<Vector2> line;
        public float radius;
        public float depth;
    }

    /// <summary>
    /// Replaces the course of each wide enough channel stem by a meandering one with the sinuosity the regimes ask for, carved into
    /// <paramref name="height"/> and <paramref name="filled"/> (a trench along the new line, below the old course's water
    /// profile, so the next routing follows it and the water still never climbs). Returns the number of cells carved;
    /// <paramref name="oxbows"/> the number of oxbow hollows left beside bends.
    /// </summary>
    public static int Apply(float[] height, float[] filled, List<int> cells, int[] receiver, int[] main, float[] width, float[] sinuosity,
        float[] valley, int sizeX, int sizeZ, MeanderSettings p, out int oxbows)
    {
        oxbows = 0;
        int n = height.Length;
        var inSet = new bool[n];
        var hasDonor = new bool[n];
        foreach (int c in cells)
            inSet[c] = true;
        foreach (int c in cells)
        {
            int r = receiver[c];
            if (r >= 0 && inSet[r])
                hasDonor[r] = true;
        }
        var trench = new float[n];
        for (int i = 0; i < n; i++)
            trench[i] = float.MaxValue;
        int carved = 0;
        var oxbowList = new List<Oxbow>();
        foreach (int head in cells)
        {
            if (hasDonor[head])
                continue;
            var stem = new List<int> { head };
            int c = head;
            // A stem goes on while it is the main donor of the next cell; it ends beside the larger flow that takes over.
            while (receiver[c] >= 0 && inSet[receiver[c]] && main[receiver[c]] == c)
            {
                c = receiver[c];
                stem.Add(c);
            }
            if (receiver[c] >= 0 && inSet[receiver[c]])
                stem.Add(receiver[c]);
            if (stem.Count < 14)
                continue;
            float meanWidth = 0f;
            foreach (int s in stem)
                meanWidth += width[s];
            if (meanWidth / stem.Count < p.minWidth)
                continue;
            carved += CarveStem(stem, filled, width, sinuosity, valley, sizeX, sizeZ, p, trench, oxbowList);
        }
        for (int i = 0; i < n; i++)
        {
            if (trench[i] < float.MaxValue)
            {
                filled[i] = Mathf.Min(filled[i], trench[i]);
                height[i] = Mathf.Min(height[i], trench[i]);
            }
        }
        // Oxbows are hollows beside the new course, never in it: the lake model decides whether they hold water.
        var dug = new HashSet<int>();
        foreach (Oxbow ox in oxbowList)
        {
            bool any = false;
            dug.Clear();
            for (int k = 0; k + 1 < ox.line.Count; k++)
            {
                for (float t = 0f; t < 1f; t += 0.25f)
                {
                    Vector2 v = Vector2.Lerp(ox.line[k], ox.line[k + 1], t);
                    int reach = Mathf.CeilToInt(ox.radius);
                    for (int dz = -reach; dz <= reach; dz++)
                    {
                        for (int dx = -reach; dx <= reach; dx++)
                        {
                            int x = Mathf.FloorToInt(v.x) + dx, z = Mathf.FloorToInt(v.y) + dz;
                            if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                                continue;
                            float d = new Vector2(x + 0.5f - v.x, z + 0.5f - v.y).magnitude;
                            int i = z * sizeX + x;
                            if (d >= ox.radius || trench[i] < float.MaxValue || dug.Contains(i))
                                continue;
                            dug.Add(i);
                            height[i] -= ox.depth * (1f - (d / ox.radius) * (d / ox.radius));
                            any = true;
                        }
                    }
                }
            }
            if (any)
                oxbows++;
        }
        return carved;
    }

    private static int CarveStem(List<int> stem, float[] filled, float[] width, float[] sinuosity, float[] valley, int sizeX, int sizeZ,
        MeanderSettings p, float[] trench, List<Oxbow> oxbows)
    {
        int count = stem.Count;
        // The valley axis: the routed cells' centres, smoothed over a few cells.
        var q = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 sum = Vector2.zero;
            int found = 0;
            for (int k = Mathf.Max(0, i - 3); k <= Mathf.Min(count - 1, i + 3); k++)
            {
                sum += new Vector2(stem[k] % sizeX + 0.5f, stem[k] / sizeX + 0.5f);
                found++;
            }
            q[i] = sum / found;
        }
        var arc = new float[count];
        for (int i = 1; i < count; i++)
            arc[i] = arc[i - 1] + (q[i] - q[i - 1]).magnitude;
        float length = arc[count - 1];
        var normal = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 t = q[Mathf.Min(count - 1, i + 2)] - q[Mathf.Max(0, i - 2)];
            t = t.sqrMagnitude > 1e-6f ? t.normalized : Vector2.right;
            normal[i] = new Vector2(-t.y, t.x);
        }

        var w = new float[count];
        var amplitude = new float[count];
        var phase = new float[count];
        var wavelength = new float[count];
        float startPhase = Rand.Range(0f, Mathf.PI * 2f), jitterPhase = Rand.Range(0f, Mathf.PI * 2f);
        for (int i = 0; i < count; i++)
        {
            int c = stem[i];
            w[i] = Mathf.Max(width[c], 1f);
            wavelength[i] = p.wavelengthPerWidth * w[i] * (1f + p.wavelengthJitter * Mathf.Sin(jitterPhase + arc[i] * 0.043f));
            float sigma = Mathf.Clamp(sinuosity[c], 1f, p.maxSinuosity);
            float want = wavelength[i] / Mathf.PI * Mathf.Sqrt(Mathf.Max(sigma - 1f, 0f));
            float room = (valley[c] - w[i]) * 0.5f - 1f;
            float toEnd = Mathf.Min(arc[i], length - arc[i]);
            float taper = Mathf.SmoothStep(0f, 1f, toEnd / Mathf.Max(p.taperWavelengths * wavelength[i], 1f));
            amplitude[i] = Mathf.Max(0f, Mathf.Min(Mathf.Min(want, room), p.maxAmplitudeWidths * w[i])) * taper;
        }
        // Amplitude and wavelength change gradually along the stem.
        amplitude = Smooth(amplitude, 5);
        wavelength = Smooth(wavelength, 5);
        phase[0] = startPhase;
        for (int i = 1; i < count; i++)
            phase[i] = phase[i - 1] + Mathf.PI * 2f * (arc[i] - arc[i - 1]) / Mathf.Max(wavelength[i], 4f);

        var offset = new float[count];
        float biggest = 0f;
        for (int i = 0; i < count; i++)
        {
            offset[i] = amplitude[i] * Mathf.Sin(phase[i]);
            biggest = Mathf.Max(biggest, Mathf.Abs(offset[i]));
        }
        if (biggest < 0.8f)
            return 0;
        var line = new Vector2[count];
        for (int i = 0; i < count; i++)
            line[i] = q[i] + normal[i] * offset[i];

        // The water profile of the old course never rises downstream; the trench follows it down.
        var profile = new float[count];
        float lowest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            lowest = Mathf.Min(lowest, filled[stem[i]]);
            profile[i] = lowest;
        }
        int carved = 0;
        for (int i = 0; i + 1 < count; i++)
        {
            int ahead = Mathf.Min(count - 1, i + 6);
            float run = Mathf.Max(arc[ahead] - arc[i], 1f);
            float slope = Mathf.Max(0f, profile[i] - profile[ahead]) / run;
            float dig = p.trenchDepth + p.trenchPerSlope * slope;
            float radius = w[i] * 0.5f + 1f;
            float segment = (line[i + 1] - line[i]).magnitude;
            int steps = Mathf.Max(1, Mathf.CeilToInt(segment / 0.5f));
            for (int s = 0; s < steps; s++)
            {
                float t = s / (float)steps;
                Vector2 v = Vector2.Lerp(line[i], line[i + 1], t);
                float bottom = Mathf.Lerp(profile[i], profile[i + 1], t) - dig;
                int reach = Mathf.CeilToInt(radius);
                for (int dz = -reach; dz <= reach; dz++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        int x = Mathf.FloorToInt(v.x) + dx, z = Mathf.FloorToInt(v.y) + dz;
                        if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                            continue;
                        float d = new Vector2(x + 0.5f - v.x, z + 0.5f - v.y).magnitude;
                        if (d >= radius)
                            continue;
                        int cell = z * sizeX + x;
                        float level = bottom + 0.15f * (d / radius) * (d / radius);
                        if (level < trench[cell])
                        {
                            if (trench[cell] == float.MaxValue)
                                carved++;
                            trench[cell] = level;
                        }
                    }
                }
            }
        }

        // Oxbows: the old loop beyond a bend, plugged at both ends (the middle of each half wave only).
        int begin = 0;
        for (int i = 1; i <= count; i++)
        {
            bool crossing = i == count || (Mathf.Sin(phase[i]) * Mathf.Sin(phase[begin]) <= 0f && Mathf.Abs(Mathf.Sin(phase[i - 1])) < 0.5f);
            if (!crossing)
                continue;
            int end = i - 1;
            if (end - begin >= 8)
                AddOxbow(stem, begin, end, q, normal, offset, w, sinuosity, valley, p, oxbows);
            begin = i;
        }
        return carved;
    }

    private static void AddOxbow(List<int> stem, int begin, int end, Vector2[] q, Vector2[] normal, float[] offset, float[] w,
        float[] sinuosity, float[] valley, MeanderSettings p, List<Oxbow> oxbows)
    {
        int apex = begin;
        for (int i = begin; i <= end; i++)
        {
            if (Mathf.Abs(offset[i]) > Mathf.Abs(offset[apex]))
                apex = i;
        }
        int c = stem[apex];
        if (sinuosity[c] < p.oxbowMinSinuosity || Rand.Value > p.oxbowChance || Mathf.Abs(offset[apex]) < w[apex])
            return;
        float side = Mathf.Sign(offset[apex]);
        float beyond = w[apex] * 1.1f + 0.35f * Mathf.Abs(offset[apex]);
        // The loop must stay on the valley floor.
        if (Mathf.Abs(offset[apex]) + beyond + w[apex] * 0.5f > valley[c] * 0.5f)
            return;
        int span = end - begin;
        var line = new List<Vector2>();
        for (int i = begin + span / 5; i <= end - span / 5; i++)
        {
            float u = (i - begin) / (float)span;
            float arch = Mathf.Sin(u * Mathf.PI);
            line.Add(q[i] + normal[i] * (offset[i] + side * beyond * arch));
        }
        if (line.Count >= 4)
            oxbows.Add(new Oxbow { line = line, radius = w[apex] * 0.5f * p.oxbowWidthShare + 0.7f, depth = p.oxbowDepth });
    }

    private static float[] Smooth(float[] values, int radius)
    {
        var result = new float[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            float sum = 0f;
            int found = 0;
            for (int k = Mathf.Max(0, i - radius); k <= Mathf.Min(values.Length - 1, i + radius); k++)
            {
                sum += values[k];
                found++;
            }
            result[i] = sum / found;
        }
        return result;
    }
}
