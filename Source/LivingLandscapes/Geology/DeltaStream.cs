using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>Tuning of <see cref="DeltaStream"/>. Lengths in cells unless noted; speeds as shares of the river's at its mouth.</summary>
public class DeltaStreamSettings
{
    /// <summary>Rounds of trace, erode and deposit (the delta's life in steps); streamlines across the river's width.</summary>
    public int rounds = 30;
    public int streamlines = 60;

    /// <summary>
    /// Turning: water at the mouth's speed turns at most turnAtRiverSpeed degrees per cell; slower water turns more (inversely with
    /// speed), up to maxTurn. A streamline looks lookAhead cells along each heading it could take.
    /// </summary>
    public float turnAtRiverSpeed = 4f;
    public float maxTurn = 40f;
    public int lookAhead = 4;

    /// <summary>
    /// Choosing a way: depth ahead (to depthPreference) times how much the way leads down the water surface toward the sea
    /// (1 + seaward x alignment); dry ground ahead is refused.
    /// </summary>
    public float depthPreference = 1f;
    public float seaward = 0.5f;

    /// <summary>
    /// Water against a shore (excess-stress erosion, as bank and pier scour): a streamline pushes on each dry cell beside it with the
    /// force of being turned by it, speed^2 x the sine of its angle of attack (1 for the river at its mouth's speed meeting a shore
    /// head-on), plus a grazing shear, shear x speed^2 x the cosine. Where that exceeds the ground's threshold the shore is scoured,
    /// scourRate metres per unit of excess (times the ground's erodibility); where it does not, the water is turned aside and piles
    /// up against the shore (backpressure metres per unit of force), lifting the surface there and upstream (spread over
    /// pressureReach cells, keeping pressureMemory of it each round).
    /// Thresholds by material (on the Hjulstrom curve sand is the easiest to move; cohesive mud and clay need faster water; rock far
    /// more): sandThreshold, mudThreshold, rockThreshold. The lab's erodibility marks the material: 1 sand, 0.3-0.9 mud or soil,
    /// below 0.1 rock.
    /// </summary>
    public float shear = 0.2f;
    public float scourRate = 0.5f;
    public float sandThreshold = 0.25f;
    public float mudThreshold = 2f;
    public float rockThreshold = 20f;

    /// <summary>On a map, ground whose rock strength reaches this is rock for the impact rule (else soil or mud; the sea floor sand).</summary>
    public float rockStrengthForRock = 0.75f;
    public float backpressure = 0.004f;
    public int pressureReach = 6;
    public float pressureMemory = 0.6f;

    /// <summary>
    /// Where a streamline's water stops, what it still carries settles within endSpread cells, up to just below the surface,
    /// less waveRemoval of it that waves and currents carry away (P10).
    /// </summary>
    public float waveRemoval = 0.5f;
    public int endSpread = 6;

    /// <summary>Past the delta front the jet keeps this share of its speed per cell.</summary>
    public float jetDecay = 0.85f;

    /// <summary>A streamline's deposits and scour spread over depositPasses 3x3 averagings (its load settles beside it too).</summary>
    public int depositPasses = 2;

    /// <summary>Streamlines are lines; the water a sheet: their discharge is spread over sheetPasses 3x3 averagings.</summary>
    public int sheetPasses = 3;

    /// <summary>Crowded water stands higher: a way is worth 1 / (1 + crowding x the discharge already there, against the mouth's per cell).</summary>
    public float crowding = 8f;

    /// <summary>Speed across the river at the mouth: 1 at its centre falling to bankSpeed at its banks.</summary>
    public float bankSpeed = 0.2f;

    /// <summary>The water surface rises surfaceSlope metres per cell from open water toward the mouth.</summary>
    public float surfaceSlope = 0.002f;
    public float openDepth = 1.5f;

    /// <summary>
    /// Sediment by capacity (as the map's sediment budget): capacity grows with discharge x speed^2; a share of an excess settles,
    /// a share of a shortfall is lifted (times erodibility). The bed changes at most maxBedChange river depths per round. Sediment
    /// over the delta's life: volumeWidths x width^2 x depth.
    /// </summary>
    public float volumeWidths = 150f;
    public float depositShare = 0.3f;
    public float erodeShare = 0.03f;
    public float maxBedChange = 0.08f;

    /// <summary>
    /// Fixed islands before the delta forms, placed like the pegs of a Galton board (measured on the owner's reference delta: 27
    /// islands within +-25 degrees of the river's axis, rows that widen and gain islands outward, sizes from 6 to 3000 cells):
    /// rows at firstRow river widths from the mouth, each rowGrowth times farther than the last, out to the delta front; row k
    /// holds k + 1 slots staggered across the cone (coneAngle either side), each kept with keepShare, jittered by jitter of the
    /// slot spacing. An island is seedSize river widths across at the middle row (log-normal, sizeSpread; smaller near the mouth
    /// and front), seedElongation times longer along the flow, its outline wandering; rockShare of the inner islands have
    /// resistant cores. The last frontRows rows are small sand bars lying across the flow (the delta front's sand line).
    /// </summary>
    public bool seedIslands = true;

    /// <summary>
    /// The drowned valley the delta fills (rivers' lower valleys drowned by the sea's rise since the last ice age, then filled by
    /// their deltas): from an apex valleyWidths river widths up the river, the valley widens toward the sea at valleyAngle degrees
    /// either side and lies platformDepth river depths below the sea; its banks wander by bankNoise river widths. The islands and streamlines
    /// start at the apex, inside the banks.
    /// </summary>
    public float valleyWidths = 12f;
    public float valleyAngle = 32f;
    public float bankNoise = 1.2f;

    /// <summary>The drowned valley's floor (the delta top the islands stand on) lies platformDepth river depths below the sea.</summary>
    public float platformDepth = 0.6f;
    public float firstRow = 1.5f;
    public float rowGrowth = 1.15f;
    public float coneAngle = 30f;

    /// <summary>Islands in the first row (each row one more); 1 puts the first island in the jet's path, as a Galton board's first peg.</summary>
    public int firstRowSlots = 2;
    public float keepShare = 0.75f;
    public float jitter = 0.35f;
    public float seedSize = 2f;

    /// <summary>Seed islands keep this many times the sum of their radii apart (1: they may touch).</summary>
    public float seedGap = 1.15f;
    public float sizeSpread = 0.6f;
    public float seedElongation = 2f;
    public float seedHeight = 0.4f;
    public float rockShare = 0.35f;
    public int frontRows = 1;
    public float frontBarSize = 0.6f;
}

/// <summary>
/// A delta from fixed islands, momentum-limited streamlines and sediment feedback. Streamlines leave the mouth spread across the
/// river, fastest at its centre; each step a streamline takes the most efficient way on (deep water toward the sea) among the
/// headings its speed lets it turn to: fast water runs nearly straight and strikes what is ahead, slow water follows banks and
/// bends round. Their density and speed make the flow field; sediment settles where the flow carries more than its speed supports
/// and is lifted where less, so islands grow in slack water, erode where fast water strikes them, and deposits make new ones.
/// Repeated for a number of rounds standing in for the delta's life.
/// </summary>
public static class DeltaStream
{
    public static int Grow(float[] height, bool[] sea, float[] erodibility, int start, Vector2 heading, float width, float depth, float seaLevel,
        int sizeX, int sizeZ, DeltaStreamSettings p, System.Random random, bool seedIslands, out float[] flowOut, out float[] speedOut, out string report,
        int apexCell = -1, bool[] drownedOut = null)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int n = sizeX * sizeZ;
        heading = heading.normalized;
        var across = new Vector2(-heading.y, heading.x);
        var mouth = new Vector2(start % sizeX + 0.5f, start / sizeX + 0.5f);
        erodibility ??= Fill(n, 1f);
        int seeded = 0, drowned = 0;
        float reach = 0f;

        // 0. The drowned valley: from the apex (up the river's course where it is known, else straight up the heading) a funnel
        // widening toward the sea lies under shallow water; the ground beside it stays as the delta's banks.
        Vector2 apex = mouth;
        if (p.valleyWidths > 0f)
        {
            float length = p.valleyWidths * width;
            if (apexCell >= 0)
                apex = new Vector2(apexCell % sizeX + 0.5f, apexCell / sizeX + 0.5f);
            else
            {
                apex = mouth - heading * length;
                while ((apex.x < 3f || apex.y < 3f || apex.x >= sizeX - 3 || apex.y >= sizeZ - 3) && (apex - mouth).magnitude > width)
                    apex += heading;
            }
            // No room for a valley inland of the mouth (it lies at the map's edge): the delta starts at the mouth.
            if (apex.x < 0f || apex.y < 0f || apex.x >= sizeX || apex.y >= sizeZ)
                apex = mouth;
            length = (mouth - apex).magnitude;
            if (length > width)
            {
                // The valley's axis runs from the apex to the mouth; the river enters at the apex.
                heading = (mouth - apex).normalized;
                across = new Vector2(-heading.y, heading.x);
                float spread = Mathf.Tan(p.valleyAngle * Mathf.Deg2Rad), floorLevel = seaLevel - p.platformDepth * depth;
                float phaseA = (float)random.NextDouble() * 6.28f, phaseB = (float)random.NextDouble() * 6.28f;
                float Wobble(float along, float side) => p.bankNoise * width * (0.6f * Mathf.Sin(along * 0.06f + phaseA + side) + 0.4f * Mathf.Sin(along * 0.17f + phaseB - side));
                for (int i = 0; i < n; i++)
                {
                    var v = new Vector2(i % sizeX + 0.5f, i / sizeX + 0.5f) - apex;
                    float along = Vector2.Dot(v, heading), side = Vector2.Dot(v, across);
                    if (along < 0f || along > length + width)
                        continue;
                    float half = width * 0.6f + along * spread + Wobble(along, side > 0f ? 1.7f : 0f) * Mathf.Clamp01(along / (2f * width));
                    if (Mathf.Abs(side) > half)
                        continue;
                    if (height[i] > floorLevel)
                        height[i] = floorLevel;
                    if (!sea[i])
                    {
                        sea[i] = true;
                        drowned++;
                        if (drownedOut != null)
                            drownedOut[i] = true;
                    }
                    erodibility[i] = 1f;
                }
            }
        }

        // 1. Fixed islands before the delta forms: Galton rows inside the banks, out to the delta front.
        if (seedIslands && p.seedIslands)
        {
            // How far the islands reach: from the valley's head along its axis to where the water turns open (the delta front builds
            // on from there where the streamlines' water stops and drops its load, P7).
            float frontDistance = 0f;
            for (Vector2 v = apex; v.x >= 0f && v.y >= 0f && v.x < sizeX && v.y < sizeZ; v += heading)
            {
                int vi = Mathf.FloorToInt(v.y) * sizeX + Mathf.FloorToInt(v.x);
                if (sea[vi] && seaLevel - height[vi] > p.openDepth * depth)
                    break;
                frontDistance = (v - apex).magnitude;
            }
            reach = frontDistance;
            var rowsAt = new List<float>();
            for (float d = p.firstRow * width; d < frontDistance; d *= p.rowGrowth)
                rowsAt.Add(d);
            var placed = new List<(Vector2 at, float r)>();
            for (int row = 0; row < rowsAt.Count; row++)
            {
                bool front = row >= rowsAt.Count - p.frontRows;
                int slots = row + p.firstRowSlots;
                float middle = 1f - Mathf.Abs(row / Mathf.Max(1f, rowsAt.Count - 1f) - 0.6f) * 1.2f;
                for (int slot = 0; slot < (front ? slots * 2 : slots); slot++)
                {
                    if (random.NextDouble() > p.keepShare)
                        continue;
                    int count = front ? slots * 2 : slots;
                    // Staggered across the cone: odd rows shift half a slot.
                    float t = (slot + 0.5f + (row % 2 == 1 ? 0.25f : -0.25f)) / count;
                    float spacing = 2f * p.coneAngle / count;
                    float angle = (-p.coneAngle + t * 2f * p.coneAngle + ((float)random.NextDouble() * 2f - 1f) * p.jitter * spacing) * Mathf.Deg2Rad;
                    float distance = rowsAt[row] * (1f + ((float)random.NextDouble() * 2f - 1f) * p.jitter * (p.rowGrowth - 1f));
                    var dir = new Vector2(heading.x * Mathf.Cos(angle) - heading.y * Mathf.Sin(angle), heading.x * Mathf.Sin(angle) + heading.y * Mathf.Cos(angle));
                    Vector2 at = apex + dir * distance;
                    // Log-normal size; largest mid-delta, small near the mouth; front bars small and lying across the flow.
                    float gauss = Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(1e-6f, (float)random.NextDouble()))) * Mathf.Cos(6.2832f * (float)random.NextDouble());
                    float size = (front ? p.frontBarSize : p.seedSize * Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(middle))) * width * Mathf.Exp(p.sizeSpread * gauss);
                    float r = Mathf.Clamp(size * 0.5f, 1.5f, frontDistance * 0.25f);
                    if (at.x < r || at.y < r || at.x >= sizeX - r || at.y >= sizeZ - r)
                        continue;
                    int ci = Mathf.FloorToInt(at.y) * sizeX + Mathf.FloorToInt(at.x);
                    if (!sea[ci] || seaLevel - height[ci] > p.openDepth * depth)
                        continue;
                    if (placed.Exists(o => (o.at - at).magnitude < (o.r + r) * p.seedGap))
                        continue;
                    placed.Add((at, r));
                    Vector2 along = front ? new Vector2(-dir.y, dir.x) : dir;
                    float elongation = front ? 2.5f : p.seedElongation * (0.7f + 0.6f * (float)random.NextDouble());
                    bool rock = !front && random.NextDouble() < p.rockShare;
                    Seed(at, along, r, elongation, rock, front);
                    seeded++;
                }
            }
        }
        void Seed(Vector2 at, Vector2 along, float r, float elongation, bool rock, bool sandBar)
        {
            var side = new Vector2(-along.y, along.x);
            float phase = (float)random.NextDouble() * 6.28f, phase2 = (float)random.NextDouble() * 6.28f;
            int r0 = Mathf.CeilToInt(r * elongation * 1.4f);
            for (int dz = -r0; dz <= r0; dz++)
            {
                for (int dx = -r0; dx <= r0; dx++)
                {
                    int x = Mathf.FloorToInt(at.x) + dx, z = Mathf.FloorToInt(at.y) + dz;
                    if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                        continue;
                    var o = new Vector2(dx, dz);
                    float a = Vector2.Dot(o, along) / (r * elongation), b = Vector2.Dot(o, side) / r;
                    // Teardrop: blunt upstream, tapering downstream; outline wandering at two scales.
                    if (a > 0f && !sandBar)
                        a *= 0.8f;
                    float theta = Mathf.Atan2(b, a);
                    float limit = 1f + 0.18f * Mathf.Sin(3f * theta + phase) + 0.1f * Mathf.Sin(7f * theta + phase2);
                    float e = (a * a + b * b) / (limit * limit);
                    if (e >= 1f)
                        continue;
                    int i = z * sizeX + x;
                    if (!sea[i])
                        continue;
                    height[i] = Mathf.Max(height[i], seaLevel + p.seedHeight * (1f - e) - 0.15f * e);
                    // Rock core (resistant), cohesive mud body, sand rim (the outer fifth); front bars all sand.
                    erodibility[i] = rock && e < 0.3f ? 0.03f : sandBar || e > 0.8f ? 1f : 0.5f;
                }
            }
        }

        float riverQ = width * depth;
        float perRound = p.volumeWidths * width * width * depth / Mathf.Max(1, p.rounds);
        float coefficient = perRound / riverQ;
        float maxChange = p.maxBedChange * depth;
        float floor = seaLevel - 2f * depth;
        var surface = new float[n];
        var flow = flowOut = new float[n];     // discharge per cell (sum over streamlines)
        var speed = speedOut = new float[n];   // as a share of the mouth's
        var load = new float[n];
        var visits = new List<int>[p.streamlines];
        var path = new List<int>();
        float deposited = 0f, eroded = 0f, offshore = 0f, struck = 0f;
        float Threshold(int i) => erodibility[i] < 0.1f ? p.rockThreshold : erodibility[i] < 0.95f ? p.mudThreshold : p.sandThreshold;
        var pressure = new float[n];
        var strike = new float[n];
        int strands = 0;

        for (int round = 0; round < p.rounds; round++)
        {
            // The water surface: the sea's over open water, rising toward the mouth over the shallows.
            var toOpen = new float[n];
            var queue = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                bool open = sea[i] && seaLevel - height[i] > p.openDepth * depth;
                int x = i % sizeX, z = i / sizeX;
                if (!open && sea[i] && (x == 0 || z == 0 || x == sizeX - 1 || z == sizeZ - 1) && height[i] < seaLevel)
                    open = true;
                toOpen[i] = open ? 0f : -1f;
                if (open)
                    queue.Enqueue(i);
            }
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                foreach ((int dx, int dz) in Four)
                {
                    int x = c % sizeX + dx, z = c / sizeX + dz;
                    if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                        continue;
                    int nb = z * sizeX + x;
                    if (toOpen[nb] >= 0f || height[nb] >= seaLevel + 0.5f * depth)
                        continue;
                    toOpen[nb] = toOpen[c] + 1f;
                    queue.Enqueue(nb);
                }
            }
            // Backpressure from last round's strikes lifts the surface round them (and spills it over low ground beside).
            for (int i = 0; i < n; i++)
                surface[i] = (toOpen[i] >= 0f ? seaLevel + p.surfaceSlope * toOpen[i] : seaLevel) + pressure[i];
            float Depth(int i) => surface[i] - height[i];
            System.Array.Clear(strike, 0, n);

            // 2. Streamlines from the mouth, each carrying an equal share of the river.
            var newFlow = new float[n];
            var newSpeedSum = new float[n];
            float perLine = riverQ / p.streamlines;
            for (int order = 0; order < p.streamlines; order++)
            {
                // From the river's centre outward, alternating sides.
                int s = p.streamlines / 2 + ((order % 2 == 0) ? order / 2 : -(order / 2 + 1));
                s = Mathf.Clamp(s, 0, p.streamlines - 1);
                // Each round the streamlines start at shifted places across the river (turbulence), so they do not retrace the same
                // tracks and leave stripes in the bed.
                float offset = (s + 0.5f + ((float)random.NextDouble() - 0.5f)) / p.streamlines - 0.5f;
                Vector2 at = apex + across * (offset * width);
                Vector2 dir = heading;
                float u = Mathf.Lerp(1f, p.bankSpeed, Mathf.Abs(offset) * 2f);
                path.Clear();
                for (int step = 0; step < 3 * (sizeX + sizeZ); step++)
                {
                    // A mouth at the map's edge can start a streamline outside it.
                    if (at.x < 0f || at.y < 0f || at.x >= sizeX || at.y >= sizeZ)
                        break;
                    int c = Mathf.FloorToInt(at.y) * sizeX + Mathf.FloorToInt(at.x);
                    path.Add(c);
                    newFlow[c] += perLine;
                    newSpeedSum[c] += u * perLine;
                    // Into open water the jet runs on, slowing, and drops its load there (the delta front builds out).
                    if (Depth(c) > p.openDepth * depth && toOpen[c] == 0f)
                    {
                        u *= p.jetDecay;
                        if (u < 0.05f)
                            break;
                    }
                    // The speed here: last round's field where there is one (water slows as it spreads), else the line's own.
                    if (round > 0 && speed[c] > 0f)
                        u = Mathf.Lerp(u, speed[c], 0.3f);
                    float turn = Mathf.Min(p.maxTurn, p.turnAtRiverSpeed / Mathf.Max(u, 0.05f)) * Mathf.Deg2Rad;
                    // The most efficient way within the turn: deep water ahead, down the surface toward the sea.
                    float best = float.MinValue;
                    Vector2 bestDir = Vector2.zero;
                    for (int k = -4; k <= 4; k++)
                    {
                        float a = turn * k / 4f;
                        var d = new Vector2(dir.x * Mathf.Cos(a) - dir.y * Mathf.Sin(a), dir.x * Mathf.Sin(a) + dir.y * Mathf.Cos(a));
                        float score = 0f;
                        bool dry = false, off = false;
                        for (int l = 1; l <= p.lookAhead; l++)
                        {
                            Vector2 q = at + d * l;
                            if (q.x < 0f || q.y < 0f || q.x >= sizeX || q.y >= sizeZ)
                            {
                                off = true;
                                break;
                            }
                            int qi = Mathf.FloorToInt(q.y) * sizeX + Mathf.FloorToInt(q.x);
                            float h = Depth(qi);
                            if (h <= 0.02f)
                            {
                                dry = true;
                                break;
                            }
                            float fall = surface[c] - surface[qi];
                            float crowd = 1f + p.crowding * newFlow[qi] / (riverQ / width);
                            score += Mathf.Pow(h, p.depthPreference) * (1f + p.seaward * Mathf.Clamp(fall / (p.surfaceSlope * l), -1f, 1f)) / l / crowd;
                        }
                        if (dry)
                            continue;
                        if (off)
                            score = float.MaxValue / 2f;
                        // Straight on wins ties (momentum).
                        score -= Mathf.Abs(k) * 1e-4f;
                        if (score > best)
                        {
                            best = score;
                            bestDir = d;
                        }
                    }
                    // The shore beside: each dry neighbour takes the force of turning this water, its normal push plus a grazing shear.
                    // Above its material's threshold it is scoured; below, the water is turned and piles up against it.
                    {
                        int cx = c % sizeX, cz = c / sizeX;
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int x = cx + dx, z = cz + dz;
                                if ((dx == 0 && dz == 0) || x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                                    continue;
                                int qi = z * sizeX + x;
                                if (Depth(qi) > 0.02f)
                                    continue;
                                var toShore = new Vector2(dx, dz).normalized;
                                float attack = Mathf.Max(0f, Vector2.Dot(dir, toShore));
                                float force = u * u * attack + p.shear * u * u * Mathf.Sqrt(Mathf.Max(0f, 1f - attack * attack));
                                float excess = force - Threshold(qi);
                                if (excess > 0f)
                                    strike[qi] += p.scourRate * excess * erodibility[qi] / p.streamlines * 10f;
                                else
                                    pressure[c] += p.backpressure * u * u * attack;
                            }
                        }
                    }
                    if (bestDir == Vector2.zero && turn < 1.5f)
                    {
                        // Against an obstacle the water slows and turns hard (up to a right angle) to find a way round.
                        for (int k = -6; k <= 6 && bestDir == Vector2.zero; k++)
                        {
                            float a = 1.5708f * k / 6f;
                            var d = new Vector2(dir.x * Mathf.Cos(a) - dir.y * Mathf.Sin(a), dir.x * Mathf.Sin(a) + dir.y * Mathf.Cos(a));
                            Vector2 q = at + d;
                            if (q.x >= 0f && q.y >= 0f && q.x < sizeX && q.y < sizeZ && Depth(Mathf.FloorToInt(q.y) * sizeX + Mathf.FloorToInt(q.x)) > 0.02f)
                                bestDir = d;
                        }
                        u = Mathf.Min(u, 0.3f);
                    }
                    if (bestDir == Vector2.zero)
                    {
                        strands++;
                        break;
                    }
                    dir = bestDir;
                    at += dir;
                    if (at.x < 0f || at.y < 0f || at.x >= sizeX || at.y >= sizeZ)
                        break;
                }
                visits[s] = new List<int>(path);
            }
            // The flow field: discharge and mean speed per cell, evened over neighbours (streamlines are lines, water a sheet).
            for (int pass = 0; pass < p.sheetPasses; pass++)
                Blur(newFlow, sizeX, sizeZ);
            // Backpressure spreads upstream and round the obstacle, and fades between rounds.
            for (int pass = 0; pass < p.pressureReach; pass++)
                Blur(pressure, sizeX, sizeZ);
            for (int i = 0; i < n; i++)
                pressure[i] *= p.pressureMemory;
            for (int i = 0; i < n; i++)
            {
                flow[i] = newFlow[i];
                float h = Mathf.Max(Depth(i), 0.05f);
                speed[i] = flow[i] > 0f ? Mathf.Min(2f, flow[i] / h / (riverQ / width / depth)) : 0f;
            }

            // 3. Sediment along each streamline: its share of the round's supply, settling or lifting by capacity.
            System.Array.Clear(load, 0, n);
            var change = new float[n];
            for (int s = 0; s < p.streamlines; s++)
            {
                float carried = perRound / p.streamlines;
                foreach (int c in visits[s])
                {
                    float capacity = coefficient * perLine * speed[c] * speed[c];
                    // Within the round's limits per cell: the bed gives at most maxChange (and not below the floor); deposits fill
                    // at most maxChange, up to the water's surface. So what is carried is what the bed gave.
                    if (carried > capacity)
                    {
                        // Settling is limited by the room up to the water's surface (accommodation), not by the erosion's stability cap.
                        float room = Mathf.Max(0f, surface[c] + 0.05f - height[c] - change[c]);
                        float put = Mathf.Clamp(p.depositShare * (carried - capacity), 0f, Mathf.Max(0f, room));
                        change[c] += put;
                        carried -= put;
                    }
                    else
                    {
                        float give = Mathf.Min(maxChange + change[c], height[c] + change[c] - floor);
                        float take = speed[c] * speed[c] > Threshold(c) ? Mathf.Clamp(p.erodeShare * (capacity - carried) * erodibility[c], 0f, Mathf.Max(0f, give)) : 0f;
                        change[c] -= take;
                        carried += take;
                    }
                }
                // Where the water stops, what it still carries settles round it (P7), less the share waves carry away (P10).
                if (visits[s].Count > 0 && carried > 0f)
                {
                    float kept = carried * (1f - p.waveRemoval);
                    offshore += carried - kept;
                    int end = visits[s][visits[s].Count - 1], ex = end % sizeX, ez = end / sizeX;
                    for (int ring = 0; ring <= p.endSpread && kept > 1e-4f; ring++)
                    {
                        for (int dz = -ring; dz <= ring && kept > 1e-4f; dz++)
                        {
                            for (int dx = -ring; dx <= ring && kept > 1e-4f; dx++)
                            {
                                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != ring)
                                    continue;
                                int x = ex + dx, z = ez + dz;
                                if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                                    continue;
                                int i = z * sizeX + x;
                                float room = Mathf.Max(0f, surface[i] - 0.1f * depth - height[i] - change[i]);
                                float put = Mathf.Min(kept, room);
                                change[i] += put;
                                kept -= put;
                            }
                        }
                    }
                    offshore += kept;
                }
                else
                    offshore += carried;
            }
            // Struck ground is scoured (its material goes with the water and settles further on).
            for (int i = 0; i < n; i++)
            {
                if (strike[i] <= 0f)
                    continue;
                change[i] -= strike[i];
                struck += strike[i];
            }
            // Spread each cell's change a little (a streamline's load settles over the cells beside it too), then apply.
            for (int pass = 0; pass < p.depositPasses; pass++)
                Blur(change, sizeX, sizeZ);
            System.Array.Clear(strike, 0, n);
            for (int i = 0; i < n; i++)
            {
                if (change[i] == 0f)
                    continue;
                float c = Mathf.Max(change[i], -maxChange);
                // Deposits build up to the water's surface (islands that reach it are land); scour stops at the floor.
                c = c > 0f ? Mathf.Min(c, Mathf.Max(0f, surface[i] + 0.05f - height[i])) : Mathf.Max(c, floor - height[i]);
                height[i] += c;
                if (c > 0f) deposited += c; else eroded -= c;
            }
        }

        int land = 0;
        for (int i = 0; i < n; i++)
        {
            if (sea[i] && height[i] > seaLevel + 0.02f)
                land++;
        }

        report = $"{p.rounds} rounds x {p.streamlines} streamlines in {watch.ElapsedMilliseconds} ms; drowned valley {(mouth - apex).magnitude:F0} cells long ({drowned} cells of land drowned), islands reach {reach:F0} cells from its head, {seeded} seed islands; " +
                 $"deposited {deposited:F0}, eroded {eroded:F0} ({struck:F0} scoured from shores), carried off {offshore:F0}; {land} cells of land over the sea; {strands} streamlines stranded";
        return land;
    }

    private static readonly (int, int)[] Four = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    private static float[] Fill(int n, float v)
    {
        var a = new float[n];
        for (int i = 0; i < n; i++)
            a[i] = v;
        return a;
    }

    private static void Blur(float[] v, int sizeX, int sizeZ)
    {
        var copy = (float[])v.Clone();
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                float total = 0f;
                int count = 0;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, nz = z + dz;
                        if (nx < 0 || nz < 0 || nx >= sizeX || nz >= sizeZ)
                            continue;
                        total += copy[nz * sizeX + nx];
                        count++;
                    }
                }
                v[z * sizeX + x] = total / count;
            }
        }
    }
}
