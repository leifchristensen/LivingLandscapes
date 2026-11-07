using System;
using System.Collections.Generic;

namespace LivingLandscapes.Geology;

/// <summary>
/// A smooth, hilliness-coherent altitude for the world's tiles (game-free, unit-tested). Vanilla draws a tile's
/// elevation from one noise field and its hilliness from another, so a mountain tile can stand at 100 m beside a flat
/// tile at 3000 m. Real land is the steady state of uplift against erosion: where the ground rises (uplift U) and
/// hillslopes creep downhill (diffusivity K), the height settles where K * laplacian(h) + U = 0, with the sea as the
/// base level (h = 0). On the tile graph, with every neighbour one step away:
///
///   sum over neighbours j of (h_j - h_i)  +  k_i * (T_i - h_i)  +  c  =  0
///
/// T_i is the height the tile's hilliness stands for (a mountain tile means a range at about 3000 m) and k_i how firmly
/// the tile holds it: the uplift of a range against the creep that wears it into its surroundings (zero for flat land,
/// which has no uplift of its own and takes whatever its neighbours give). c is the continent's slow uplift, in metres
/// per tile squared; it is what lets a plain rise away from the coast. Properties of the solution: heights stay between
/// the sea (0) and the highest target or continental rise (maximum principle); a range falls away from its crest as
/// neighbouring tiles average (diffusion); a field that already satisfies the equation is its own solution.
/// </summary>
public static class WorldAltitudeMath
{
    private const float Overrelaxation = 1.8f;

    /// <summary>Solves the equation above by successive overrelaxation.</summary>
    /// <param name="neighbours">For each tile, the indices of its neighbours.</param>
    /// <param name="sea">Tiles held at height 0 (the base level).</param>
    /// <param name="target">Height each tile's hilliness stands for, metres.</param>
    /// <param name="stiffness">How firmly each tile holds its target (k_i, 0 for none).</param>
    /// <param name="uplift">Continental uplift c, metres per tile squared.</param>
    /// <param name="start">Starting heights (null: sea level). A start that is already the solution is returned unchanged.</param>
    /// <param name="tolerance">Largest change in a sweep, metres, at which the solution counts as settled.</param>
    public static float[] Solve(int[][] neighbours, bool[] sea, float[] target, float[] stiffness, float uplift,
        float[] start = null, float tolerance = 0.01f, int maxSweeps = 20000)
    {
        int n = neighbours.Length;
        float[] h = new float[n];
        for (int i = 0; i < n; i++)
            h[i] = sea[i] || start == null ? 0f : start[i];

        for (int sweep = 0; sweep < maxSweeps; sweep++)
        {
            float largest = 0f;
            for (int i = 0; i < n; i++)
            {
                if (sea[i])
                    continue;
                float sum = 0f;
                int[] around = neighbours[i];
                for (int j = 0; j < around.Length; j++)
                    sum += h[around[j]];
                float k = stiffness[i];
                float solved = (sum + k * target[i] + uplift) / (around.Length + k);
                float next = Math.Max(0f, h[i] + Overrelaxation * (solved - h[i]));
                largest = Math.Max(largest, Math.Abs(next - h[i]));
                h[i] = next;
            }
            if (largest < tolerance)
                break;
        }
        return h;
    }

    /// <summary>
    /// Makes water run downhill along the world's rivers: a river is the drainage the land's fall belongs to, and vanilla
    /// laid the world's rivers on its own elevations, which this field replaces. Walking the links from the sea upstream,
    /// each tile is lifted to at least <paramref name="minFall"/> above the tile it drains to (never lowered), so the
    /// field is monotone along every river and no link climbs.
    /// </summary>
    /// <param name="links">(upstream tile, downstream tile) of each river link over land, downstream links first.</param>
    public static void EnforceDescent(float[] height, IEnumerable<(int up, int down)> links, float minFall)
    {
        foreach ((int up, int down) in links)
            height[up] = Math.Max(height[up], height[down] + minFall);
    }

    /// <summary>Tiles' graph distance to the nearest sea tile (0 for the sea); int.MaxValue where there is none.</summary>
    public static int[] DistanceToSea(int[][] neighbours, bool[] sea)
    {
        int n = neighbours.Length;
        int[] distance = new int[n];
        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            distance[i] = sea[i] ? 0 : int.MaxValue;
            if (sea[i])
                queue.Enqueue(i);
        }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            foreach (int j in neighbours[i])
            {
                if (distance[j] != int.MaxValue)
                    continue;
                distance[j] = distance[i] + 1;
                queue.Enqueue(j);
            }
        }
        return distance;
    }
}
