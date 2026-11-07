using System;
using System.Collections.Generic;
using System.Linq;

namespace LivingLandscapes.Geology;

/// <summary>
/// The world watershed rule (#219, Docs/WORLD-WATERSHED.md): a tile whose accumulated flow Q reaches the channel threshold T
/// is a river tile, linked to the tile it drains to. Q never falls downstream, so those tiles form a forest that drains to a
/// sink (the sea, a lake, an existing river); a tributary is any branch of it joining a tile with larger Q. No dice, no shape:
/// pure arrays in, links out, so it can be tested without the game.
/// </summary>
public static class WatershedMath
{
    /// <summary>Flow below which a tile drains no channel; infinite when the world's river density is (nearly) zero: no rivers at all.</summary>
    public static float Threshold(float baseThreshold, float riverDensity)
        => riverDensity <= 0.01f ? float.PositiveInfinity : baseThreshold / riverDensity;

    /// <summary>
    /// Discharge that never falls downstream: each tile carries at least what any tile draining into it carries. A routing that
    /// passes water on through a basin outlet or a lake (Realistic Planets 2's tier-2 hydrology) can hand a tile less flow than
    /// its tributary had; water does not vanish there, it is carried on, so the larger value is kept. Tiles on a cycle keep their own.
    /// </summary>
    public static float[] NonDecreasing(int[] downstream, float[] flow)
    {
        int n = downstream.Length;
        var result = (float[])flow.Clone();
        var pending = new int[n];
        for (int i = 0; i < n; i++)
        {
            if (downstream[i] >= 0)
                pending[downstream[i]]++;
        }
        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            if (pending[i] == 0)
                queue.Enqueue(i);
        }
        while (queue.Count > 0)
        {
            int t = queue.Dequeue();
            int d = downstream[t];
            if (d < 0)
                continue;
            if (result[t] > result[d])
                result[d] = result[t];
            if (--pending[d] == 0)
                queue.Enqueue(d);
        }
        return result;
    }

    /// <summary>
    /// Tiles whose link to <c>downstream[tile]</c> the rule adds: land with flow at or above the threshold, whose chain of such
    /// tiles reaches a sink (water, or a tile already on a river) and is not already linked. A chain that ends in a closed basin
    /// (no tile to drain to) or in a tile below the threshold is dropped whole.
    /// </summary>
    /// <param name="downstream">The tile each tile drains to, -1 where it drains nowhere.</param>
    /// <param name="flow">Accumulated flow per tile.</param>
    /// <param name="water">True for sea and lake tiles.</param>
    /// <param name="onRiver">True for tiles that already carry a river link.</param>
    /// <param name="linked">True if the two tiles are already joined by a river.</param>
    /// <param name="endInBasins">Also draw chains that end at a closed basin bottom (a land tile that drains nowhere); the default drops them.</param>
    public static List<int> LinksToAdd(int[] downstream, float[] flow, bool[] water, bool[] onRiver, float threshold, Func<int, int, bool> linked, bool endInBasins = false)
    {
        int n = downstream.Length;
        // 0 unknown, 1 reaches a sink, 2 does not, 3 on the current walk (a cycle is a chain that never reaches a sink).
        var state = new byte[n];
        bool Candidate(int i) => !water[i] && downstream[i] >= 0 && flow[i] >= threshold;
        var walk = new List<int>();
        var result = new List<int>();
        for (int start = 0; start < n; start++)
        {
            if (!Candidate(start) || state[start] != 0)
                continue;
            walk.Clear();
            int t = start;
            byte verdict;
            while (true)
            {
                if (state[t] == 3) { verdict = 2; break; }
                state[t] = 3;
                walk.Add(t);
                int next = downstream[t];
                if (water[next] || onRiver[next] || (endInBasins && downstream[next] < 0)) { verdict = 1; break; }
                if (!Candidate(next)) { verdict = 2; break; }
                if (state[next] == 1 || state[next] == 2) { verdict = state[next]; break; }
                t = next;
            }
            foreach (int w in walk)
                state[w] = verdict;
        }
        for (int i = 0; i < n; i++)
        {
            if (state[i] == 1 && !linked(i, downstream[i]))
                result.Add(i);
        }
        return result;
    }

    /// <summary>
    /// Strahler order of every tile in the forest of tiles that are in <paramref name="inForest"/> (order 1 at a source; two
    /// streams of equal order make the next order). 0 for tiles outside it.
    /// </summary>
    public static int[] StrahlerOrders(int[] downstream, bool[] inForest)
    {
        int n = downstream.Length;
        var order = new int[n];
        var maxChild = new int[n];
        var maxCount = new int[n];
        var pending = new int[n];
        for (int i = 0; i < n; i++)
        {
            if (inForest[i] && downstream[i] >= 0 && inForest[downstream[i]])
                pending[downstream[i]]++;
        }
        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            if (inForest[i] && pending[i] == 0)
                queue.Enqueue(i);
        }
        while (queue.Count > 0)
        {
            int t = queue.Dequeue();
            order[t] = maxChild[t] == 0 ? 1 : (maxCount[t] >= 2 ? maxChild[t] + 1 : maxChild[t]);
            int d = downstream[t];
            if (d < 0 || !inForest[d])
                continue;
            if (order[t] > maxChild[d]) { maxChild[d] = order[t]; maxCount[d] = 1; }
            else if (order[t] == maxChild[d]) maxCount[d]++;
            if (--pending[d] == 0)
                queue.Enqueue(d);
        }
        return order;
    }

    /// <summary>
    /// The tiles of <paramref name="links"/> ordered so that each comes after the tile it drains to (when that is also in the list):
    /// by the number of steps from the tile to the first tile outside the list, nearest first. Ties keep the input order.
    /// </summary>
    public static List<int> DownstreamFirst(List<int> links, int[] downstream)
    {
        var inList = new HashSet<int>(links);
        var depth = new Dictionary<int, int>();
        int Depth(int start)
        {
            var path = new List<int>();
            int t = start;
            while (inList.Contains(t) && !depth.ContainsKey(t) && path.Count <= links.Count)
            {
                path.Add(t);
                t = downstream[t];
            }
            int d = depth.TryGetValue(t, out int known) ? known : 0;
            for (int k = path.Count - 1; k >= 0; k--)
                depth[path[k]] = ++d;
            return depth[start];
        }
        foreach (int i in links)
            Depth(i);
        return links.Select((tile, order) => (tile, order)).OrderBy(x => depth[x.tile]).ThenBy(x => x.order).Select(x => x.tile).ToList();
    }
}
