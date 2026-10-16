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
/// intervals along each band, and which way each faces (towards the lower stage). Planned from the geology during
/// map generation and spawned once vanilla has placed its rock. Not saved: the cliffs themselves are things.
/// </summary>
public class CliffPlan
{
    private static readonly IntVec3[] Cardinals = { IntVec3.North, IntVec3.East, IntVec3.South, IntVec3.West };

    public readonly CliffKind[] Kind;
    public readonly Rot4[] Facing;

    public CliffPlan(int cellCount)
    {
        Kind = new CliffKind[cellCount];
        Facing = new Rot4[cellCount];
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
            for (int d = 0; d < 4; d++)
            {
                IntVec3 n = c + Cardinals[d];
                if (!n.InBounds(map))
                    continue;
                int ni = indices.CellToIndex(n);
                if (excluded(ni) || stage[ni] >= stage[i])
                    continue;
                // Prefer showing the face: a lower neighbour to the south wins.
                if (!facing.IsValid || d == 2)
                    facing = new Rot4(d);
            }
            if (!facing.IsValid)
                continue;
            Kind[i] = facing == Rot4.South ? CliffKind.Face : CliffKind.Rim;
            Facing[i] = facing;
        }
    }

    /// <summary>Marks one cell as a cliff band facing the given way (towards the lower ground).</summary>
    public void PlanEdge(int i, Rot4 facing)
    {
        Kind[i] = facing == Rot4.South ? CliffKind.Face : CliffKind.Rim;
        Facing[i] = facing;
    }

    /// <summary>
    /// Makes every band 4-connected: linked cliff art only joins orthogonal neighbours, so two band cells touching only
    /// at a corner would draw as separate pieces. A connector goes into one of the two cells between them (the higher
    /// one, not rock, not already a cliff), facing like its neighbour, so diagonal bands become staircases.
    /// Returns the number added.
    /// </summary>
    public int ConnectDiagonals(Map map, System.Func<int, bool> blocked, float[] height)
    {
        CellIndices indices = map.cellIndices;
        int added = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < Kind.Length; i++)
            {
                if (!IsBandKind(Kind[i]))
                    continue;
                IntVec3 c = indices.IndexToCell(i);
                foreach (IntVec3 diagonal in Diagonals)
                {
                    IntVec3 d = c + diagonal;
                    if (!d.InBounds(map) || !IsBandKind(Kind[indices.CellToIndex(d)]))
                        continue;
                    IntVec3 a = new IntVec3(d.x, 0, c.z), b = new IntVec3(c.x, 0, d.z);
                    int ai = indices.CellToIndex(a), bi = indices.CellToIndex(b);
                    if (Kind[ai] != CliffKind.None || Kind[bi] != CliffKind.None)
                        continue;
                    bool aOk = !blocked(ai), bOk = !blocked(bi);
                    if (!aOk && !bOk)
                        continue;
                    int pick = aOk && (!bOk || height[ai] >= height[bi]) ? ai : bi;
                    Kind[pick] = Kind[i] == CliffKind.Steps ? CliffKind.Rim : Kind[i];
                    Facing[pick] = Facing[i];
                    added++;
                }
            }
        }
        return added;
    }

    private static readonly IntVec3[] Diagonals = { new IntVec3(1, 0, 1), new IntVec3(1, 0, -1), new IntVec3(-1, 0, 1), new IntVec3(-1, 0, -1) };

    private static bool IsBandKind(CliffKind kind) => kind == CliffKind.Face || kind == CliffKind.Rim || kind == CliffKind.Steps;

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
