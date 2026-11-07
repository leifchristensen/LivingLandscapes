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

    /// <summary>
    /// The same bounds when the strength is read from the join push (f = share x sin theta, below the share itself): a push of
    /// this share of the joined flow starts to shape the junction, one of fullPush shapes it fully. Stand-in R-number.
    /// </summary>
    public float minPush = 0.03f;
    public float fullPush = 0.15f;

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

    /// <summary>Width continuity below a junction (#220): the joined channel keeps its widest arm's width, easing back to its own over 1 / widthRelax cells. Stand-in R-number.</summary>
    public float widthRelax = 0.04f;
}

public static class Confluences
{
    /// <summary>
    /// Finds each junction (a channel cell whose receiver's main donor is another cell) and lays its zones down the joined
    /// channel. <paramref name="excluded"/> marks cells at base level (the sea, lakes): junctions there have no zones, and the
    /// zones stop where they reach it or the map's edge. With <paramref name="push"/> and <paramref name="stem"/> (S5, from
    /// <see cref="Hydraulics.JoinPush"/>) a junction's strength is f = |push| / Q, the tributaries' share times sin(theta): the
    /// cross momentum the stem cannot absorb. It tilts the stem (Impact), slows the water in the zone to u (1 - f) so the load
    /// settles, and its direction is the zone's side. Without them the old field-angle strength is used.
    /// </summary>
    public static ConfluenceZones Find(List<int> cells, int[] receiver, int[] main, float[] discharge, Vector2[] direction, float[] width,
        bool[] excluded, int sizeX, ConfluenceSettings p, Vector2[] push = null, Vector2[] stem = null)
    {
        int n = receiver.Length;
        var zones = new ConfluenceZones
        {
            SlowShare = new float[n],
            SlowSide = new Vector2[n],
            Scour = new float[n],
            SlowCapacity = p.slowSpeed * p.slowSpeed,
            SlowCapacityAt = push != null ? new float[n] : null,
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
            if (q1 + q2 <= 0f)
                continue;
            float share = q2 / (q1 + q2);
            float theta, strength, f = 0f;
            if (push != null)
            {
                // The lost cross momentum as a share of the joined flow.
                if (push[r] == Vector2.zero || discharge[r] <= 0f)
                    continue;
                f = Mathf.Min(push[r].magnitude / discharge[r], 1f);
                theta = p.angleRange.ClampToRange(Mathf.Asin(Mathf.Clamp01(f / Mathf.Max(share, 1e-6f))) * Mathf.Rad2Deg);
                strength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(p.minPush, p.fullPush, f));
            }
            else
            {
                if (direction[c] == Vector2.zero || direction[m] == Vector2.zero)
                    continue;
                theta = p.angleRange.ClampToRange(Vector2.Angle(direction[c], direction[m]));
                strength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(p.minShare, p.fullShare, share)) * Mathf.Sin(theta * Mathf.Deg2Rad);
            }
            if (strength <= 0.01f)
                continue;
            // A junction within one joined width of the map's edge has no reach below it on the map to shape (the zones would
            // be cut off at once, leaving only the scour hole).
            int rx = r % sizeX, rz = r / sizeX, sizeZ = n / sizeX;
            if (Mathf.Min(Mathf.Min(rx, sizeX - 1 - rx), Mathf.Min(rz, sizeZ - 1 - rz)) < Mathf.Max(width[r], 1f))
                continue;
            zones.Junctions++;
            zones.JunctionCells.Add(r);
            zones.JunctionWidths.Add(Mathf.Max(width[r], 1f));
            zones.Strongest = Mathf.Max(zones.Strongest, strength);

            // The tributary's side of the joined channel: across its flow, toward where the tributary comes from.
            Vector2 side;
            if (push != null)
                side = -push[r].normalized; // the push is where the stem is shoved to (away from the tributary); the slow zone is on the tributary's bank
            else
            {
                Vector2 along = direction[r] != Vector2.zero ? direction[r] : direction[m];
                var across = new Vector2(-along.y, along.x);
                side = Vector2.Dot(across, Centre(c) - Centre(r)) >= 0f ? across : -across;
            }
            // Speed in the slow water: the lost cross momentum slows it (u (1 - f)), never below the floor.
            float slowCapacity = push != null ? Mathf.Pow(Mathf.Max(p.slowSpeed, 1f - f), 2f) : 0f;
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
                    if (push != null)
                        zones.SlowCapacityAt[k] = slowCapacity;
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

    /// <summary>
    /// Width continuity through a junction (S5, #220). Hydraulic geometry gives a channel width that grows with its discharge
    /// (P3), and a joined channel carries both arms' water, so it is not narrower than its widest arm; where per-cell regimes,
    /// slope or noise would have thinned it (a thread right below two wide rivers), the width is held at the widest arm and
    /// relaxes back to the local width over 1 / <paramref name="relax"/> cells (stand-in R-number until calibrated). Only moving
    /// water widens; returns the number of cells widened.
    /// </summary>
    public static int WidthContinuity(List<int> cells, int[] receiver, float[] width, bool[] moving, float relax)
    {
        int n = receiver.Length;
        var donors = new int[n];
        foreach (int c in cells)
        {
            if (receiver[c] >= 0)
                donors[receiver[c]]++;
        }
        // Highest first: every donor of a cell has its final width (hold applied) before the cell is looked at.
        var widest = new float[n];
        var sumSquares = new float[n];
        var hold = new float[n];
        var drop = new float[n];
        int widened = 0;
        foreach (int c in cells)
        {
            // Hydraulic geometry: width grows as the square root of the discharge, and the joined discharge is the arms' sum,
            // so the joined width is the root of the sum of the arms' squares.
            float joined = Mathf.Sqrt(sumSquares[c]);
            if (donors[c] >= 2 && moving[c] && joined > hold[c])
            {
                hold[c] = joined;
                drop[c] = joined * relax;
            }
            if (hold[c] > width[c] && moving[c])
            {
                width[c] = hold[c];
                widened++;
            }
            int r = receiver[c];
            if (r < 0)
                continue;
            widest[r] = Mathf.Max(widest[r], width[c]);
            sumSquares[r] += width[c] * width[c];
            if (hold[c] - drop[c] > hold[r])
            {
                hold[r] = hold[c] - drop[c];
                drop[r] = drop[c];
            }
        }
        return widened;
    }

    /// <summary>
    /// Junction width continuity (S5, #220): below a junction the joined channel carries both arms' water, so over the next
    /// <paramref name="reach"/> cells it is not narrower than its widest arm (by more than <paramref name="ratio"/>). Counts the
    /// junctions of moving water, those where the joined channel's mean width falls below ratio x the widest arm, and the worst.
    /// </summary>
    public static int NarrowedJunctions(List<int> cells, int[] receiver, float[] width, bool[] moving, int reach, float ratio,
        out int junctions, out float worstRatio, out int worstCell)
    {
        int n = receiver.Length;
        var donors = new int[n];
        var widest = new float[n];
        foreach (int c in cells)
        {
            int r = receiver[c];
            if (r < 0)
                continue;
            donors[r]++;
            widest[r] = Mathf.Max(widest[r], width[c]);
        }
        junctions = 0;
        worstRatio = float.MaxValue;
        worstCell = -1;
        int narrowed = 0;
        foreach (int r in cells)
        {
            if (donors[r] < 2 || !moving[r] || widest[r] <= 0f)
                continue;
            float sum = 0f;
            int count = 0;
            for (int k = r; k >= 0 && count <= reach; k = receiver[k], count++)
                sum += width[k];
            junctions++;
            float below = sum / count / widest[r];
            if (below < worstRatio)
            {
                worstRatio = below;
                worstCell = r;
            }
            if (below < ratio)
                narrowed++;
        }
        if (junctions == 0)
            worstRatio = 1f;
        return narrowed;
    }

    /// <summary>
    /// The junction invariant (10): width continuity below every junction of moving water (#220), the scour deepest at the junction
    /// that made it, and deposit laid in the zone of every junction that shaped one (the sediment budget's slow water).
    /// </summary>
    public static JunctionAudit Audit(ConfluenceZones zones, List<int> cells, int[] receiver, float[] width, bool[] moving, float[] deposit, int sizeX)
    {
        var audit = new JunctionAudit();
        audit.Narrowed = NarrowedJunctions(cells, receiver, width, moving, 4, 0.8f, out audit.Joins, out audit.WorstWidthRatio, out audit.WorstWidthCell);
        var junction = new HashSet<int>(zones.JunctionCells);
        var counted = new HashSet<int>();
        foreach (int c in cells)
            audit.DepositTotal += deposit[c];
        // A junction with another within two joined widths either way shares its zone: its scour is not checked.
        bool Crowded(int r, float w3)
        {
            foreach (int o in zones.JunctionCells)
            {
                if (o != r && Mathf.Max(Mathf.Abs(o % sizeX - r % sizeX), Mathf.Abs(o / sizeX - r / sizeX)) <= 2f * w3 + 1f)
                    return true;
            }
            return false;
        }
        for (int j = 0; j < zones.JunctionCells.Count; j++)
        {
            int r = zones.JunctionCells[j];
            float w3 = zones.JunctionWidths[j];
            audit.Zoned++;
            if (zones.Scour[r] >= 1.5f)
                audit.Holes++;
            bool crowded = Crowded(r, w3), deposited = false;
            float d = 0f;
            for (int k = r; k >= 0 && d <= 2f * w3; k = receiver[k])
            {
                if (k != r && junction.Contains(k))
                    crowded = true;
                if (counted.Add(k))
                    audit.DepositAtJoins += deposit[k];
                if (!crowded && d <= w3 && zones.Scour[k] > zones.Scour[r] + 1e-3f)
                    audit.ScourOffCentre++;
                if (deposit[k] > 0f)
                    deposited = true;
                int next = receiver[k];
                if (next < 0)
                    break;
                d += Hydraulics.StepLength(k, next, sizeX);
            }
            if (!deposited)
                audit.NoDeposit++;
        }
        return audit;
    }
}
