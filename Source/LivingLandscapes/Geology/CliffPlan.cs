using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

public enum CliffKind : byte
{
    None,
    /// <summary>Steep broken rock: passable slowly, gives cover.</summary>
    Scarp,
    /// <summary>Sheer rock face: impassable, blocks sight.</summary>
    Sheer,
}

/// <summary>
/// Where cliffs go on a map and which way each one faces (downhill). Planned from the geology during map generation,
/// spawned once vanilla has placed its rock. Not saved: the cliffs themselves are things on the map.
/// </summary>
public class CliffPlan
{
    public readonly CliffKind[] Kind;
    public readonly Rot4[] Facing;

    public CliffPlan(int cellCount)
    {
        Kind = new CliffKind[cellCount];
        Facing = new Rot4[cellCount];
    }

    /// <summary>Downhill direction of a cell, snapped to the four map directions.</summary>
    public static Rot4 DownhillFacing(float[] height, int sizeX, int sizeZ, int i)
    {
        int x = i % sizeX, z = i / sizeX;
        float dx = height[z * sizeX + Mathf.Min(x + 1, sizeX - 1)] - height[z * sizeX + Mathf.Max(x - 1, 0)];
        float dz = height[Mathf.Min(z + 1, sizeZ - 1) * sizeX + x] - height[Mathf.Max(z - 1, 0) * sizeX + x];
        // Downhill is against the gradient.
        if (Mathf.Abs(dx) > Mathf.Abs(dz))
            return dx > 0f ? Rot4.West : Rot4.East;
        return dz > 0f ? Rot4.South : Rot4.North;
    }

    /// <summary>
    /// Sheer cliffs must not cut off walkable ground. Every walkable area not reachable from the largest one gets a
    /// route through the fewest sheer cells to it, which become scarps (steep but climbable).
    /// </summary>
    /// <param name="walkable">Cells that are open ground or passable; mineable rock counts as walkable here, since
    /// colonists can dig through it as in vanilla.</param>
    public int EnsureConnected(bool[] walkable, int sizeX, int sizeZ)
    {
        bool Open(int i) => walkable[i] && Kind[i] != CliffKind.Sheer;
        int regions = GridMath.LabelRegions(Open, sizeX, sizeZ, out int[] labels);
        if (regions <= 1)
            return 0;

        var size = new int[regions];
        var members = new List<int>[regions];
        for (int r = 0; r < regions; r++)
            members[r] = new List<int>();
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] >= 0)
            {
                size[labels[i]]++;
                members[labels[i]].Add(i);
            }
        }
        int main = 0;
        for (int r = 1; r < regions; r++)
        {
            if (size[r] > size[main])
                main = r;
        }

        // Breadth-first from the main area through sheer cells; each other area is joined along its first route.
        var joined = new bool[regions];
        joined[main] = true;
        var parent = new int[labels.Length];
        var visited = new bool[labels.Length];
        var queue = new Queue<int>();
        for (int i = 0; i < labels.Length; i++)
        {
            parent[i] = -1;
            if (labels[i] == main)
            {
                visited[i] = true;
                queue.Enqueue(i);
            }
        }
        int opened = 0;
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            int cx = c % sizeX, cz = c / sizeX;
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + (d == 0 ? 1 : d == 1 ? -1 : 0), nz = cz + (d == 2 ? 1 : d == 3 ? -1 : 0);
                if (nx < 0 || nz < 0 || nx >= sizeX || nz >= sizeZ)
                    continue;
                int n = nz * sizeX + nx;
                if (visited[n] || !walkable[n])
                    continue;
                visited[n] = true;
                parent[n] = c;
                int label = labels[n];
                if (label >= 0 && !joined[label])
                {
                    // Open the sheer cells along the route back to the joined ground.
                    joined[label] = true;
                    for (int p = c; p >= 0 && Kind[p] == CliffKind.Sheer; p = parent[p])
                    {
                        Kind[p] = CliffKind.Scarp;
                        opened++;
                    }
                    // The newly joined area is now part of the reachable ground.
                    foreach (int i in members[label])
                    {
                        if (!visited[i])
                        {
                            visited[i] = true;
                            queue.Enqueue(i);
                        }
                    }
                }
                queue.Enqueue(n);
            }
        }
        return opened;
    }
}
