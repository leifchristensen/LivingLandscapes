using System;
using System.Collections.Generic;

namespace LivingLandscapes.Geology;

/// <summary>
/// Pure rules for where a river enters the map and whether a tributary joins the exit river (invariant 7, #69, #165).
/// No game types, so unit tests link this file.
/// </summary>
public static class RiverEntryRules
{
    /// <summary>
    /// A cell on the map's border: the only place water from beyond the map can come in. Water that appears inside (the
    /// springs a headwater river rises from) is a source, not an entry.
    /// </summary>
    public static bool IsEdgeCell(int cell, int sizeX, int sizeZ)
    {
        int x = cell % sizeX;
        int z = cell / sizeX;
        return x == 0 || z == 0 || x == sizeX - 1 || z == sizeZ - 1;
    }

    /// <summary>
    /// Following Parent links from channel <paramref name="start"/> reaches the world river's channel, every hop ending
    /// inside the map. A channel that has no parent, or whose last point lies outside the map, does not join.
    /// </summary>
    public static bool Joins(int start, int[] parent, bool[] worldRiver, bool[] endsInside)
    {
        int index = start;
        // The step limit guards against a Parent loop.
        for (int step = 0; step <= parent.Length; step++)
        {
            if (worldRiver[index])
                return true;
            if (parent[index] < 0 || !endsInside[index])
                return false;
            index = parent[index];
        }
        return false;
    }

    /// <summary>
    /// Where water from beyond the map comes in: the point where a course crosses the map's edge, on the segment from its last
    /// point outside (<paramref name="outsideX"/>, <paramref name="outsideZ"/>) to its first point inside. The erosion model's
    /// cells are coarser than the map's, so its first point inside can lie a cell or two in from the edge; the boundary
    /// condition belongs on the edge cell itself. Map coordinates, the map spanning 0 to size.
    /// </summary>
    public static void EdgeCrossing(float outsideX, float outsideZ, float insideX, float insideZ, int sizeX, int sizeZ, out float x, out float z)
    {
        float dx = insideX - outsideX, dz = insideZ - outsideZ, t = 0f;
        if (dx != 0f)
            t = Math.Max(t, (Math.Min(Math.Max(outsideX, 0f), sizeX - 0.01f) - outsideX) / dx);
        if (dz != 0f)
            t = Math.Max(t, (Math.Min(Math.Max(outsideZ, 0f), sizeZ - 0.01f) - outsideZ) / dz);
        t = Math.Min(Math.Max(t, 0f), 1f);
        x = outsideX + dx * t;
        z = outsideZ + dz * t;
    }

    /// <summary>
    /// The cells of a drainage tree with every donor before its receiver (a topological order), for the cells where
    /// <paramref name="isMember"/> holds. Ordering by drainage area is not enough: a world river's entry brings an area so large
    /// (millions of model cells) that a float cannot tell a cell from the next one downstream, so equal areas would come in any order.
    /// </summary>
    public static List<int> DonorsFirst(int[] receiver, Func<int, bool> isMember)
    {
        var pending = new int[receiver.Length];
        var members = new List<int>();
        for (int c = 0; c < receiver.Length; c++)
        {
            if (!isMember(c))
                continue;
            members.Add(c);
            int r = receiver[c];
            if (r >= 0 && isMember(r))
                pending[r]++;
        }
        var order = new List<int>(members.Count);
        var ready = new Queue<int>();
        foreach (int c in members)
        {
            if (pending[c] == 0)
                ready.Enqueue(c);
        }
        while (ready.Count > 0)
        {
            int c = ready.Dequeue();
            order.Add(c);
            int r = receiver[c];
            if (r >= 0 && isMember(r) && --pending[r] == 0)
                ready.Enqueue(r);
        }
        return order;
    }
}
