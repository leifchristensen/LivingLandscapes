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
}
