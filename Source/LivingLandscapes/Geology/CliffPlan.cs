using System.Collections.Generic;
using Verse;

namespace LivingLandscapes.Geology;

public enum CliffKind : byte
{
    None,
    /// <summary>Stage edge with the lower ground to the south: the face is in view.</summary>
    Face,
    /// <summary>Any other stage edge: only its lip shows.</summary>
    Rim,
    /// <summary>Steps between stages.</summary>
    Steps,
}

/// <summary>
/// The terrace cliffs of a map: a band on every cell at the edge of a height stage (the higher side), steps at
/// intervals along each band, and which sides of each drop to lower ground (each drawn as a thin edge). Planned from the geology during
/// map generation and spawned once vanilla has placed its rock. Not saved: the cliffs themselves are things.
/// </summary>
public class CliffPlan
{
    private static readonly IntVec3[] Cardinals = { IntVec3.North, IntVec3.East, IntVec3.South, IntVec3.West };

    public readonly CliffKind[] Kind;
    public readonly Rot4[] Facing;

    /// <summary>
    /// Every side of a band cell that drops to lower ground, as bits by <see cref="Rot4.AsInt"/> (north 1, east 2,
    /// south 4, west 8). The cliff draws a thin edge on each of them; <see cref="Facing"/> is the main one.
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
    /// <paramref name="excluded"/> is true (mountain rock, gentle ground) neither get bands nor count as lower ground.
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
    /// Puts steps along every band, about every <paramref name="spacing"/> cells and at least once per band, on
    /// straight stretches where the drop is a single stage. Bands shorter than half the spacing (small knolls and
    /// hollows) get none: they are easier to walk around. Returns the number placed.
    /// </summary>
    public int PlanSteps(Map map, int[] stage, int spacing)
    {
        int bands = GridMath.LabelRegions(i => Kind[i] == CliffKind.Face || Kind[i] == CliffKind.Rim,
            map.Size.x, map.Size.z, out int[] labels);
        var members = new List<int>[bands];
        for (int b = 0; b < bands; b++)
            members[b] = new List<int>();
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] >= 0)
                members[labels[i]].Add(i);
        }

        int placed = 0;
        foreach (List<int> band in members)
        {
            if (band.Count < spacing / 2)
                continue;
            int sinceLast = spacing / 2;
            int fallback = -1;
            bool any = false;
            foreach (int i in OrderAlong(band, labels, map))
            {
                sinceLast++;
                if (!IsStepCandidate(map, stage, i))
                    continue;
                if (fallback < 0)
                    fallback = i;
                if (sinceLast < spacing)
                    continue;
                Kind[i] = CliffKind.Steps;
                sinceLast = 0;
                any = true;
                placed++;
            }
            if (!any && fallback >= 0)
            {
                Kind[fallback] = CliffKind.Steps;
                placed++;
            }
        }
        return placed;
    }

    /// <summary>A straight stretch of band (band cells either side along it) dropping exactly one stage.</summary>
    private bool IsStepCandidate(Map map, int[] stage, int i)
    {
        CellIndices indices = map.cellIndices;
        IntVec3 c = indices.IndexToCell(i);
        IntVec3 down = c + Facing[i].FacingCell;
        if (!down.InBounds(map) || stage[i] - stage[indices.CellToIndex(down)] != 1)
            return false;
        IntVec3 along = Facing[i].IsHorizontal ? IntVec3.North : IntVec3.East;
        return IsBand(map, c + along) && IsBand(map, c - along);
    }

    private bool IsBand(Map map, IntVec3 c)
    {
        if (!c.InBounds(map))
            return false;
        CliffKind k = Kind[map.cellIndices.CellToIndex(c)];
        return k == CliffKind.Face || k == CliffKind.Rim;
    }

    /// <summary>Band cells in walking order (breadth-first from one end), so step spacing follows the band.</summary>
    private static IEnumerable<int> OrderAlong(List<int> band, int[] labels, Map map)
    {
        int label = labels[band[0]];
        var seen = new HashSet<int> { band[0] };
        var queue = new Queue<int>();
        queue.Enqueue(band[0]);
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            yield return i;
            IntVec3 c = map.cellIndices.IndexToCell(i);
            foreach (IntVec3 d in Cardinals)
            {
                IntVec3 n = c + d;
                if (!n.InBounds(map))
                    continue;
                int ni = map.cellIndices.CellToIndex(n);
                if (labels[ni] == label && seen.Add(ni))
                    queue.Enqueue(ni);
            }
        }
    }
}
