using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>Channel network settings (part of <see cref="GeologyGenerationExtension"/>). Widths in map cells.</summary>
public class RiverNetworkSettings
{
    /// <summary>A channel starts where the erosion model's drainage area reaches this share of the map's area.</summary>
    public float sourceShare = 0.02f;

    /// <summary>Channels shorter than this inside the map (cells) are left out, unless a kept channel joins them.</summary>
    public float minLengthInside = 20f;

    /// <summary>
    /// Width of a world river by the width vanilla gives its class (creek 4, river 6, large river 14, huge river 30),
    /// compressed to map scale (rule V3). Vanilla's order of classes is kept; the width fits the map.
    /// </summary>
    public SimpleCurve worldRiverWidth = new SimpleCurve
    {
        new CurvePoint(4f, 3f), new CurvePoint(6f, 6f), new CurvePoint(14f, 12f), new CurvePoint(30f, 24f),
    };

    /// <summary>Width of the map's own channels by stream order (1 = a source channel, +1 where two equal ones join).</summary>
    public SimpleCurve widthByOrder = new SimpleCurve
    {
        new CurvePoint(1f, 2.5f), new CurvePoint(2f, 3.5f), new CurvePoint(3f, 5f), new CurvePoint(4f, 7f), new CurvePoint(6f, 10f),
    };

    /// <summary>A tributary is at most this share of the width of the channel it joins.</summary>
    public float tributaryShare = 0.7f;
}

/// <summary>One channel of the network as the erosion model drew it: map cells, upstream to downstream.</summary>
public class ChannelCourse
{
    public List<Vector2> Points = new List<Vector2>();

    /// <summary>Stream order (Strahler): 1 for a source channel, one more where two of equal order join.</summary>
    public int Order;

    /// <summary>Index of the channel this one joins at its last point (-1: it leaves the map or the region).</summary>
    public int Parent = -1;

    /// <summary>Drainage area (map cells) already gathered where the channel enters the map; 0 if it starts inside.</summary>
    public float EntryArea;

    /// <summary>The world river's channel.</summary>
    public bool WorldRiver;
}

/// <summary>
/// The channel network (rules V1, V2): channels come first and the valleys follow them. The erosion model's drainage
/// tree is cut where drainage area passes a threshold; each channel runs from its source (or where it enters from
/// beyond the map) down to where it joins a larger one, keeping the larger donor's identity through each junction.
/// Stream order sets width, so a map gets a readable tree of creeks feeding rivers.
/// </summary>
public static class RiverNetwork
{
    /// <param name="area">Drainage area per model cell (model cells, inflow included).</param>
    /// <param name="toMap">Model cell index to map coordinates.</param>
    /// <param name="mapCellsPerModelCell">Map cells along one model cell.</param>
    public static List<ChannelCourse> Extract(int[] receiver, float[] area, int gx, int gz, System.Func<int, Vector2> toMap,
        float mapCellsPerModelCell, int sizeX, int sizeZ, int riverEntry, RiverNetworkSettings p)
    {
        int n = gx * gz;
        float cellArea = mapCellsPerModelCell * mapCellsPerModelCell;
        float threshold = p.sourceShare * sizeX * sizeZ / cellArea;
        bool IsChannel(int c) => area[c] >= threshold;

        // Channel donors of each channel cell, then cells from small area to large (donors before receivers).
        var donors = new List<int>[n];
        var cells = new List<int>();
        for (int c = 0; c < n; c++)
        {
            if (!IsChannel(c))
                continue;
            cells.Add(c);
            int r = receiver[c];
            if (r >= 0 && IsChannel(r))
                (donors[r] ??= new List<int>()).Add(c);
        }
        cells.Sort((a, b) => area[a].CompareTo(area[b]));

        var channelOf = new int[n];
        var order = new int[n];
        var courses = new List<List<int>>();
        var orders = new List<int>();
        var parents = new List<int>();
        foreach (int c in cells)
        {
            List<int> from = donors[c];
            int main = -1, top = 0, topCount = 0;
            if (from != null)
            {
                foreach (int d in from)
                {
                    if (main < 0 || area[d] > area[main])
                        main = d;
                    if (order[d] > top)
                    {
                        top = order[d];
                        topCount = 1;
                    }
                    else if (order[d] == top)
                        topCount++;
                }
            }
            order[c] = main < 0 ? 1 : top + (topCount >= 2 ? 1 : 0);
            if (main < 0)
            {
                channelOf[c] = courses.Count;
                courses.Add(new List<int>());
                orders.Add(0);
                parents.Add(-1);
            }
            else
                channelOf[c] = channelOf[main];
            courses[channelOf[c]].Add(c);
            orders[channelOf[c]] = Mathf.Max(orders[channelOf[c]], order[c]);
            // The smaller donors end here, joining this channel.
            if (from == null)
                continue;
            foreach (int d in from)
            {
                if (d == main)
                    continue;
                courses[channelOf[d]].Add(c);
                parents[channelOf[d]] = channelOf[c];
            }
        }

        // Cut each channel to the map (one point beyond each edge) and keep those long enough, or joined by a kept one.
        bool Inside(Vector2 v) => v.x >= 0f && v.y >= 0f && v.x < sizeX && v.y < sizeZ;
        int count = courses.Count;
        var cut = new ChannelCourse[count];
        var keep = new bool[count];
        for (int k = 0; k < count; k++)
        {
            List<int> course = courses[k];
            int first = course.FindIndex(c => Inside(toMap(c))), last = course.FindLastIndex(c => Inside(toMap(c)));
            if (first < 0)
                continue;
            int a = Mathf.Max(0, first - 1), b = Mathf.Min(course.Count - 1, last + 1);
            var channel = new ChannelCourse { Order = orders[k], WorldRiver = riverEntry >= 0 && course.Contains(riverEntry) };
            for (int i = a; i <= b; i++)
                channel.Points.Add(toMap(course[i]));
            if (a < first)
                channel.EntryArea = area[course[first]] * cellArea;
            float inside = 0f;
            for (int i = first; i < last; i++)
                inside += (toMap(course[i + 1]) - toMap(course[i])).magnitude;
            cut[k] = channel;
            keep[k] = inside >= p.minLengthInside || channel.WorldRiver;
        }
        for (bool changed = true; changed;)
        {
            changed = false;
            for (int k = 0; k < count; k++)
            {
                int parent = parents[k];
                if (keep[k] && parent >= 0 && cut[parent] != null && !keep[parent])
                {
                    keep[parent] = true;
                    changed = true;
                }
            }
        }

        // Larger channels first, so each channel's parent is planned before it.
        var kept = new List<int>();
        for (int k = 0; k < count; k++)
        {
            if (keep[k])
                kept.Add(k);
        }
        kept.Sort((a, b) => cut[b].WorldRiver != cut[a].WorldRiver ? (cut[b].WorldRiver ? 1 : -1)
            : cut[b].Order != cut[a].Order ? cut[b].Order.CompareTo(cut[a].Order)
            : area[courses[b][courses[b].Count - 1]].CompareTo(area[courses[a][courses[a].Count - 1]]));
        var index = new Dictionary<int, int>();
        for (int i = 0; i < kept.Count; i++)
            index[kept[i]] = i;
        var result = new List<ChannelCourse>();
        foreach (int k in kept)
        {
            ChannelCourse channel = cut[k];
            channel.Parent = parents[k] >= 0 && index.TryGetValue(parents[k], out int parent) && parent < result.Count ? parent : -1;
            result.Add(channel);
        }
        return result;
    }
}
