using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

public enum CliffKind : byte
{
    None,
    /// <summary>Steep broken rock: slow to cross, gives cover.</summary>
    Scarp,
    /// <summary>Steep rock face: very slow to climb, blocks sight.</summary>
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
}
