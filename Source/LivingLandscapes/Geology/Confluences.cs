using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>Tuning of <see cref="Confluences"/>, set with the channel settings in XML (tools/Docs/CONFLUENCE-ZONES.md).</summary>
public class ConfluenceSettings
{
    public bool enabled = true;

    /// <summary>
    /// A junction's strength s = smoothstep(minShare, fullShare, qr) x |sin theta|, with qr = Q2 / (Q1 + Q2) the tributary's share
    /// of the joined flow: a trickle joining a river changes nothing, a tributary of a third or more shapes the junction fully.
    /// </summary>
    public float minShare = 0.05f;
    public float fullShare = 0.3f;

    /// <summary>The junction angle theta (degrees, between the two incoming flows) is clamped to this range: near 0 the channels merge side by side, near 180 they meet head on.</summary>
    public FloatRange angleRange = new FloatRange(15f, 165f);

    /// <summary>
    /// Flow separation (Best and Reid 1984): below the junction the flow leaves the tributary-side bank, and a slow zone this wide
    /// (share of the joined width W3) and this long (in W3) forms there at full strength. Stand-in R-number in PRINCIPLES.md
    /// until calibrated; the joined flow keeps at least one cell of width beside it.
    /// </summary>
    public float separationWidth = 0.4f;
    public float separationLength = 4f;

    /// <summary>Flow speed in the stagnation and separation zones, as a share of the reach's mean (a floor, so the excess settles over a few cells).</summary>
    public float slowSpeed = 0.2f;

    /// <summary>Scour hole depth over the mean depth (Ashmore and Parker 1983): ds / d = scourBase + scourPerDegree x theta, at full strength; at most maxScour.</summary>
    public float scourBase = 2.24f;
    public float scourPerDegree = 0.031f;
    public float maxScour = 4f;
}

public static class Confluences
{
    /// <summary>
    /// Finds each junction (a channel cell whose receiver's main donor is another cell) and lays its zones down the joined
    /// channel. <paramref name="excluded"/> marks cells at base level (the sea, lakes): junctions there have no zones, and the
    /// zones stop where they reach it or the map's edge.
    /// </summary>
    public static ConfluenceZones Find(List<int> cells, int[] receiver, int[] main, float[] discharge, Vector2[] direction, float[] width,
        bool[] excluded, int sizeX, ConfluenceSettings p)
    {
        int n = receiver.Length;
        var zones = new ConfluenceZones
        {
            SlowShare = new float[n],
            SlowSide = new Vector2[n],
            Scour = new float[n],
            SlowCapacity = p.slowSpeed * p.slowSpeed,
        };
        for (int i = 0; i < n; i++)
            zones.Scour[i] = 1f;
        if (!p.enabled)
            return zones;
        Vector2 Centre(int c) => new Vector2(c % sizeX, c / sizeX);
        foreach (int c in cells)
        {
            int r = receiver[c];
            if (r < 0 || main[r] < 0 || main[r] == c || (excluded != null && excluded[r]))
                continue;
            int m = main[r];
            float q1 = discharge[m], q2 = discharge[c];
            if (q1 + q2 <= 0f || direction[c] == Vector2.zero || direction[m] == Vector2.zero)
                continue;
            float share = q2 / (q1 + q2);
            float theta = p.angleRange.ClampToRange(Vector2.Angle(direction[c], direction[m]));
            float strength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(p.minShare, p.fullShare, share)) * Mathf.Sin(theta * Mathf.Deg2Rad);
            if (strength <= 0.01f)
                continue;
            // A junction within one joined width of the map's edge has no reach below it on the map to shape (the zones would
            // be cut off at once, leaving only the scour hole).
            int rx = r % sizeX, rz = r / sizeX, sizeZ = n / sizeX;
            if (Mathf.Min(Mathf.Min(rx, sizeX - 1 - rx), Mathf.Min(rz, sizeZ - 1 - rz)) < Mathf.Max(width[r], 1f))
                continue;
            zones.Junctions++;
            zones.Strongest = Mathf.Max(zones.Strongest, strength);

            // The tributary's side of the joined channel: across its flow, toward where the tributary comes from.
            Vector2 along = direction[r] != Vector2.zero ? direction[r] : direction[m];
            var across = new Vector2(-along.y, along.x);
            Vector2 side = Vector2.Dot(across, Centre(c) - Centre(r)) >= 0f ? across : -across;
            float w3 = Mathf.Max(width[r], 1f);
            // The slow zone tapers from the junction down its length; the joined flow keeps at least one cell beside it.
            float slow = Mathf.Min(p.separationWidth * strength, Mathf.Max(0f, 1f - 1f / w3));
            float length = Mathf.Max(1f, p.separationLength * w3 * strength);
            float scour = Mathf.Min(p.maxScour, 1f + strength * (p.scourBase + p.scourPerDegree * theta - 1f));
            float d = 0f;
            for (int k = r; k >= 0 && d <= length && (excluded == null || !excluded[k]); k = receiver[k])
            {
                float a = slow * (1f - d / length);
                if (a > zones.SlowShare[k])
                {
                    zones.SlowShare[k] = a;
                    zones.SlowSide[k] = side;
                }
                // The scour hole: deepest at the junction, easing back to the mean depth over one joined width.
                zones.Scour[k] = Mathf.Max(zones.Scour[k], 1f + (scour - 1f) * Mathf.Exp(-d / w3));
                int next = receiver[k];
                if (next < 0)
                    break;
                d += Hydraulics.StepLength(k, next, sizeX);
            }
        }
        return zones;
    }
}
