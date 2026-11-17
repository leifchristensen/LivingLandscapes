using System;

namespace LivingLandscapes.Geology;

/// <summary>
/// The starting bed of a world river's antecedent course (issue #186, river-boundary plan W2). Pure, no game types, so unit tests link this file.
/// A river never flows uphill: the bed along the course only falls, however the ground rises and dips beside it.
/// </summary>
public static class RiverBed
{
    /// <summary>
    /// The bed along a course, in course order from upstream, ending at the course's outlet at <paramref name="baseLevel"/>.
    /// Never above the ground less <paramref name="depth"/> (it is entrenched), never rising, never below the base level the water
    /// leaves at (a bed below its own outlet is a pit), and falling evenly in the share of the way left to the outlet:
    /// bed(k) = base + (U(k) - base) x (left / total), U(k) = the running minimum of ground - depth, held at or above the base level.
    /// </summary>
    public static float[] Grade(float[] ground, float depth, float baseLevel)
    {
        int m = ground.Length;
        var bed = new float[m];
        float upper = float.PositiveInfinity;
        for (int k = 0; k < m; k++)
        {
            upper = Math.Min(upper, ground[k] - depth);
            float u = Math.Max(upper, baseLevel);
            float left = m > 1 ? (m - 1 - k) / (float)(m - 1) : 0f;
            bed[k] = baseLevel + (u - baseLevel) * left;
        }
        return bed;
    }
}

/// <summary>
/// The world rivers' courses as a prescribed flow path (issue #186, river-boundary plan W2b): a boundary condition of the erosion
/// model, not a starting guess. Pure, no game types.
/// </summary>
public static class PrescribedCourse
{
    /// <summary>The cells with a cell equal to its predecessor dropped.</summary>
    public static System.Collections.Generic.List<int> Dedupe(System.Collections.Generic.IEnumerable<int> cells)
    {
        var result = new System.Collections.Generic.List<int>();
        foreach (int c in cells)
        {
            if (result.Count == 0 || result[result.Count - 1] != c)
                result.Add(c);
        }
        return result;
    }

    /// <summary>
    /// Cuts the ground along a course's drainage (<paramref name="receivers"/>: each course cell's bound receiver, -1 elsewhere, as
    /// <see cref="PrescribedReceivers"/> makes it) so every course cell lies at least <paramref name="fall"/> above the cell it drains
    /// to: a course must never step uphill, and with a fall &gt; 0 the drainage's highest-first order keeps every donor before its
    /// receiver. The cut follows the one drainage tree the cells are bound to (donors first), so two legs that pass the same cells
    /// in different orders cannot contradict each other. Heights only go down. Returns how many cells were lowered.
    /// </summary>
    public static int CutDownhill(float[] ground, int[] receivers, float fall = 0f)
    {
        var target = new bool[receivers.Length];
        for (int c = 0; c < receivers.Length; c++)
        {
            if (receivers[c] >= 0)
                target[receivers[c]] = true;
        }
        int lowered = 0;
        foreach (int c in RiverEntryRules.DonorsFirst(receivers, i => receivers[i] >= 0 || target[i]))
        {
            int r = receivers[c];
            if (r >= 0 && ground[r] > ground[c] - fall)
            {
                ground[r] = ground[c] - fall;
                lowered++;
            }
        }
        return lowered;
    }

    /// <summary>
    /// The receiver each course cell is bound to: the next cell along its leg (-1 elsewhere). <paramref name="legs"/> are cell paths
    /// in flow order, the exit leg first: where legs share a cell, the leg listed first decides (the way toward the exit), so
    /// tributaries join the exit river by construction. A step that would close a loop is skipped.
    /// </summary>
    public static int[] PrescribedReceivers(int cellCount, System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<int>> legs)
    {
        var receiver = new int[cellCount];
        for (int i = 0; i < cellCount; i++)
            receiver[i] = -1;
        foreach (var leg in legs)
        {
            for (int k = 0; k + 1 < leg.Count; k++)
            {
                int a = leg[k], b = leg[k + 1];
                if (a == b || receiver[a] >= 0 || Reaches(receiver, b, a))
                    continue;
                receiver[a] = b;
            }
        }
        return receiver;
    }

    /// <summary>
    /// The point on any of the <paramref name="paths"/> (polylines) nearest to (<paramref name="x"/>, <paramref name="z"/>): the foot of
    /// the perpendicular on the nearest segment, or the nearest end of it. False when there is no path point at all.
    /// </summary>
    public static bool NearestOnPaths(System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<(float x, float z)>> paths,
        float x, float z, out float nearestX, out float nearestZ)
    {
        nearestX = x;
        nearestZ = z;
        float best = float.MaxValue;
        bool found = false;
        foreach (var path in paths)
        {
            for (int k = 0; k < path.Count; k++)
            {
                (float ax, float az) = path[k];
                (float bx, float bz) = k + 1 < path.Count ? path[k + 1] : path[k];
                float dx = bx - ax, dz = bz - az;
                float length2 = dx * dx + dz * dz;
                float t = length2 > 0f ? Math.Max(0f, Math.Min(1f, ((x - ax) * dx + (z - az) * dz) / length2)) : 0f;
                float px = ax + t * dx, pz = az + t * dz;
                float d = (px - x) * (px - x) + (pz - z) * (pz - z);
                if (d < best)
                {
                    best = d;
                    nearestX = px;
                    nearestZ = pz;
                    found = true;
                }
            }
        }
        return found;
    }

    private static bool Reaches(int[] receiver, int from, int target)
    {
        for (int c = from, step = 0; c >= 0 && step <= receiver.Length; c = receiver[c], step++)
        {
            if (c == target)
                return true;
        }
        return false;
    }
}
