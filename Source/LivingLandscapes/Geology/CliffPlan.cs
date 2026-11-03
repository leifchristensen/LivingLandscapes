using Verse;

namespace LivingLandscapes.Geology;

public enum CliffKind : byte
{
    None,
    /// <summary>Stage edge with the lower ground to the south: the face is in view.</summary>
    Face,
    /// <summary>Any other stage edge: only its lip shows.</summary>
    Rim,
}

/// <summary>
/// The terrace cliffs of a map: long bands along the edges of height stages (the higher side), each kept or cleared as a
/// whole line (<see cref="KeepScarps"/>), and which sides of each drop to lower ground (each drawn as a thin edge).
/// Planned from the geology during map generation and spawned once vanilla has placed its rock. Not saved: the cliffs
/// themselves are things.
/// </summary>
public class CliffPlan
{
    private static readonly IntVec3[] Cardinals = { IntVec3.North, IntVec3.East, IntVec3.South, IntVec3.West };

    /// <summary>North-east, south-east, south-west, north-west: mask bits 16, 32, 64, 128.</summary>
    private static readonly IntVec3[] Diagonals = { new IntVec3(1, 0, 1), new IntVec3(1, 0, -1), new IntVec3(-1, 0, -1), new IntVec3(-1, 0, 1) };

    public readonly CliffKind[] Kind;
    public readonly Rot4[] Facing;

    /// <summary>
    /// Which of a band cell's 8 neighbours are lower ground: north 1, east 2, south 4, west 8 (bits by
    /// <see cref="Rot4.AsInt"/>), north-east 16, south-east 32, south-west 64, north-west 128. It picks the cliff's
    /// shape (<see cref="Building_Cliff"/>); <see cref="Facing"/> is the main drop.
    /// </summary>
    public readonly byte[] Lower;

    public CliffPlan(int cellCount)
    {
        Kind = new CliffKind[cellCount];
        Facing = new Rot4[cellCount];
        Lower = new byte[cellCount];
    }

    /// <summary>
    /// Marks band cells: a cell on a higher stage than one of its four neighbours. Cells where
    /// <paramref name="excluded"/> is true (mountain rock, water) neither get bands nor count as lower ground.
    /// </summary>
    public void PlanBands(Map map, int[] stage, System.Func<int, bool> excluded)
    {
        CellIndices indices = map.cellIndices;
        for (int i = 0; i < stage.Length; i++)
        {
            if (excluded(i))
                continue;
            IntVec3 c = indices.IndexToCell(i);
            Rot4 facing = Rot4.Invalid;
            byte lower = 0;
            for (int d = 0; d < 4; d++)
            {
                IntVec3 n = c + Cardinals[d];
                if (!n.InBounds(map))
                    continue;
                int ni = indices.CellToIndex(n);
                if (excluded(ni) || stage[ni] >= stage[i])
                    continue;
                lower |= (byte)(1 << d);
                // Prefer showing the face: a lower neighbour to the south wins.
                if (!facing.IsValid || d == 2)
                    facing = new Rot4(d);
            }
            for (int k = 0; k < 4; k++)
            {
                IntVec3 n = c + Diagonals[k];
                if (!n.InBounds(map))
                    continue;
                int ni = indices.CellToIndex(n);
                if (!excluded(ni) && stage[ni] < stage[i])
                    lower |= (byte)(16 << k);
            }
            // A cell lower only across a corner is part of the band too: otherwise a diagonal band leaves gaps a pawn can slip
            // through between two cliff cells without climbing. It faces the side of that corner (south first).
            if (!facing.IsValid && lower != 0)
                facing = (lower & ((16 << 1) | (16 << 2))) != 0 ? Rot4.South : Rot4.North;
            if (!facing.IsValid)
                continue;
            PlanEdge(i, facing, lower);
        }
    }

    /// <summary>Marks one cell as a cliff band with the given lower sides; <paramref name="facing"/> is the main one.</summary>
    public void PlanEdge(int i, Rot4 facing, byte lower)
    {
        Kind[i] = facing == Rot4.South ? CliffKind.Face : CliffKind.Rim;
        Facing[i] = facing;
        Lower[i] = lower;
    }

    /// <summary>
    /// Keeps whole scarps, not cells (P26): band cells on ground stepped at least <paramref name="traceSteepness"/> are
    /// traced into lines (8-connected, as diagonal bands join through their corner cells), so a line runs on while the
    /// ground stays stepped and ends where it eases. A line stands when its steep-equivalent length, the sum of
    /// steepness over its cells, is at least <paramref name="minLength"/>: a long line survives gentler stretches, a short
    /// weak one goes. Everything else is cleared. Returns the number of lines kept.
    /// </summary>
    public int KeepScarps(Map map, float[] steepness, float traceSteepness, float minLength)
    {
        for (int i = 0; i < Kind.Length; i++)
        {
            if (Kind[i] != CliffKind.None && steepness[i] < traceSteepness)
                Kind[i] = CliffKind.None;
        }
        int lines = GridMath.LabelRegions(i => Kind[i] != CliffKind.None, map.Size.x, map.Size.z, out int[] labels, diagonals: true);
        var length = new float[lines];
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] >= 0)
                length[labels[i]] += steepness[i];
        }
        int kept = 0;
        foreach (float l in length)
        {
            if (l >= minLength)
                kept++;
        }
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] >= 0 && length[labels[i]] < minLength)
                Kind[i] = CliffKind.None;
        }
        return kept;
    }
}
