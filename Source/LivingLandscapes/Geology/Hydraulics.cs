using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>
/// The hydraulic quantities of a routed network, defined once (tools/Docs/HYDRO-UNIFICATION.md, E2): the slope of a reach, the
/// speed the water runs at (P4, Manning) and the momentum it carries (P5). Impact, the sediment budget, Spread, the channel
/// shape and the confluence zones each used to rebuild their own copy; they all call these functions now. Game-free (cells
/// are indices into row-major arrays of width <c>sizeX</c>), so unit tests can run them.
/// </summary>
/// <remarks>
/// Step S2a is a pure extraction: every function here is the old inline code, moved, so results are unchanged. The
/// definitions that still differ between consumers (Impact reads the slope upstream, the budget the slope downstream) are
/// separate functions on purpose, until step S2b picks one.
/// </remarks>
public static class Hydraulics
{
    /// <summary>Gravity (m / s^2), for the critical speed sqrt(g d).</summary>
    public const float Gravity = 9.81f;

    /// <summary>Cells downstream over which the transport slope is read (single D8 steps across filled flats are nearly zero).</summary>
    public const int TransportSteps = 5;

    /// <summary>
    /// A graded bed never rises downstream (invariant 1): deposits raise a reach together with the reach above it, so a deposit
    /// cannot stand higher than the bed upstream and dam it into a lake (#217: the feedback passes raised the lower valley of a
    /// world river above its upper reaches, drowning the river). Caps <paramref name="rises"/> in place: each way's receiver
    /// (<paramref name="receiver"/> and <paramref name="secondary"/>, which may be null) rises at most to its donor's raised
    /// surface. <paramref name="cells"/> must be ordered highest first, so every donor is final before its receiver is reached.
    /// </summary>
    public static void GradeRises(List<int> cells, int[] receiver, int[] secondary, float[] surface, float[] rises)
    {
        foreach (int c in cells)
        {
            float top = surface[c] + rises[c];
            CapRise(receiver[c], top, surface, rises);
            if (secondary != null)
                CapRise(secondary[c], top, surface, rises);
        }
    }

    private static void CapRise(int r, float top, float[] surface, float[] rises)
    {
        if (r >= 0 && surface[r] + rises[r] > top)
            rises[r] = Mathf.Max(0f, top - surface[r]);
    }

    /// <summary>Length of the step from <paramref name="from"/> to its neighbour <paramref name="to"/>, in cells (diagonals 1.414).</summary>
    public static float StepLength(int from, int to, int sizeX) =>
        (to % sizeX != from % sizeX && to / sizeX != from / sizeX) ? 1.414f : 1f;

    /// <summary>The unit vector of the step from <paramref name="from"/> to its neighbour <paramref name="to"/>.</summary>
    public static Vector2 Step(int from, int to, int sizeX) =>
        new Vector2(to % sizeX - from % sizeX, to / sizeX - from / sizeX).normalized;

    /// <summary>
    /// The water's drop per cell over the next <paramref name="steps"/> receivers of <paramref name="c"/> (never negative), or up
    /// to the first cell marked in <paramref name="stopAt"/> (base level): the reach it runs down. With
    /// <see cref="TransportSteps"/> it is the transport slope of the budget and the shape; with every cell and base level it is
    /// the mean slope of the whole reach a river entering at <paramref name="c"/> runs down.
    /// </summary>
    public static float ReachSlope(int c, int[] receiver, float[] filled, int sizeX, int steps = TransportSteps, bool[] stopAt = null)
    {
        int d = c;
        float run = 0f;
        for (int s = 0; s < steps && receiver[d] >= 0 && (stopAt == null || !stopAt[d]); s++)
        {
            int r = receiver[d];
            run += StepLength(d, r, sizeX);
            d = r;
        }
        return run > 0f ? Mathf.Max(0f, filled[c] - filled[d]) / run : 0f;
    }

    /// <summary>
    /// The slope of the reach arriving at <paramref name="c"/>: the drop from the cell up to <paramref name="steps"/> cells upstream
    /// along <paramref name="main"/> (its main donors), over the run between them. <paramref name="up"/> is that upstream cell
    /// (<paramref name="c"/> itself on a source) and <paramref name="run"/> the run in cells.
    /// </summary>
    public static float UpstreamSlope(int c, int[] main, float[] filled, int sizeX, int steps, out int up, out float run)
    {
        up = c;
        run = 0f;
        for (int s = 0; s < steps && main[up] >= 0; s++)
        {
            int dx = main[up] % sizeX - up % sizeX, dz = main[up] / sizeX - up / sizeX;
            run += Mathf.Sqrt(dx * dx + dz * dz);
            up = main[up];
        }
        return Mathf.Max(0f, filled[up] - filled[c]) / Mathf.Max(run, 1f);
    }

    /// <summary>
    /// The speed (m / s) of water of <paramref name="depth"/> down <paramref name="slope"/> (P4): Manning,
    /// u = d^(2/3) S^(1/2) / n, at most the critical speed sqrt(g d) (rivers rarely run supercritical).
    /// </summary>
    public static float Speed(float depth, float slope, float manningN)
    {
        float d = Mathf.Max(depth, 0.01f);
        return Mathf.Min(Mathf.Pow(d, 2f / 3f) * Mathf.Sqrt(Mathf.Max(slope, 1e-6f)) / manningN, Mathf.Sqrt(Gravity * d));
    }

    /// <summary>
    /// The speed a cell runs at where water keeps the speed it arrives with (a jet, P5): at least the donor's speed decayed by
    /// exp(-distance / jetLength), jetLength being jetWidths x the channel width.
    /// </summary>
    public static float WithJet(float speed, float donorSpeed, float distance, float jetLength) =>
        Mathf.Max(speed, Jet(donorSpeed, distance, jetLength));

    /// <summary>
    /// The direction the water carries into each cell (P5): the discharge-weighted sum of the steps it took in, down the
    /// receivers (each step averaged with the momentum arriving at its cell, so it follows the flow's bends). Cells are
    /// ordered highest first, so donors come before their receivers; <c>Vector2.zero</c> where no water arrives.
    /// With <paramref name="secondary"/> and <paramref name="secondaryShare"/> (the routing's splits) a split cell sends its
    /// second way the share of its water that takes it, re-aimed along that way's own step, and the receiver the rest (S2b);
    /// without them every cell sends all its water down its receiver (the form Spread used before S2b).
    /// </summary>
    public static Vector2[] Momentum(List<int> cells, int[] receiver, float[] discharge, int sizeX, int[] secondary = null, float[] secondaryShare = null)
    {
        var momentum = new Vector2[receiver.Length];
        foreach (int c in cells)
        {
            int r = receiver[c];
            if (r < 0)
                continue;
            float share = 0f;
            if (secondary != null && secondaryShare != null && secondary[c] >= 0)
            {
                share = secondaryShare[c];
                Carry(momentum, c, secondary[c], discharge[c] * share, sizeX);
            }
            Carry(momentum, c, r, discharge[c] * (1f - share), sizeX);
        }
        return momentum;
    }

    /// <summary>Adds the momentum the water of <paramref name="from"/> carries into its neighbour <paramref name="to"/>: its step, averaged with what arrived at <paramref name="from"/>, times <paramref name="weight"/>.</summary>
    private static void Carry(Vector2[] momentum, int from, int to, float weight, int sizeX)
    {
        Vector2 step = Step(from, to, sizeX);
        Vector2 carried = momentum[from] == Vector2.zero ? step : momentum[from].normalized;
        momentum[to] += (carried + step).normalized * weight;
    }

    /// <summary>
    /// The speed each channel cell runs at (S2b, one slope): Manning on the <see cref="ReachSlope"/> the shape and the sediment
    /// budget read, or the speed a donor carries in as a jet if that is faster. Every donor counts: a cell hands its speed on to
    /// its receiver and, at a split, to its <paramref name="secondary"/> way too, so below a confluence the jet of the faster
    /// tributary is kept, not only the main donor's. With <paramref name="reachSlope"/> false the slope is the old one upstream along
    /// <paramref name="main"/> and with <paramref name="allDonors"/> false only the main donor's jet is kept (the pre-S2b Impact; the
    /// parts exist to isolate which change drives a difference). Cells are ordered highest first; the jet decays over
    /// <paramref name="jetWidths"/> widths of the receiving cell's channel.
    /// </summary>
    public static float[] Speeds(List<int> cells, int[] receiver, int[] secondary, float[] filled, float[] depth, float[] width, int sizeX,
        float manningN, float jetWidths, int[] main = null, int momentumSteps = 8, bool reachSlope = true, bool allDonors = true)
    {
        var speeds = new float[filled.Length];
        var jet = new float[filled.Length];
        void Hand(int from, int to) =>
            jet[to] = Mathf.Max(jet[to], Jet(speeds[from], StepLength(from, to, sizeX), jetWidths * Mathf.Max(width[to], 1f)));
        foreach (int c in cells)
        {
            float slope = reachSlope ? ReachSlope(c, receiver, filled, sizeX) : UpstreamSlope(c, main, filled, sizeX, momentumSteps, out _, out _);
            float speed = Speed(depth[c], slope, manningN);
            if (allDonors)
                speed = Mathf.Max(speed, jet[c]);
            else if (main[c] >= 0)
                speed = WithJet(speed, speeds[main[c]], (new Vector2(c % sizeX, c / sizeX) - new Vector2(main[c] % sizeX, main[c] / sizeX)).magnitude, jetWidths * Mathf.Max(width[c], 1f));
            speeds[c] = speed;
            if (!allDonors)
                continue;
            if (receiver[c] >= 0)
                Hand(c, receiver[c]);
            if (secondary != null && secondary[c] >= 0)
                Hand(c, secondary[c]);
        }
        return speeds;
    }

    /// <summary>
    /// The speed a jet leaves behind it (P5): <paramref name="speed"/> decayed by exp(-distance / jetLength). A cell runs at
    /// least the strongest of its donors' (see <see cref="WithJet"/>).
    /// </summary>
    public static float Jet(float speed, float distance, float jetLength) => speed * Mathf.Exp(-distance / jetLength);

    /// <summary>Downstream direction of each channel cell, averaged over a few steps up and down the channel.</summary>
    public static Vector2[] Directions(List<int> cells, int[] receiver, int[] main, int sizeX, int steps)
    {
        var direction = new Vector2[receiver.Length];
        Vector2 At(int c) => new Vector2(c % sizeX, c / sizeX);
        foreach (int c in cells)
        {
            int down = c, up = c;
            for (int s = 0; s < steps; s++)
            {
                if (receiver[down] >= 0)
                    down = receiver[down];
                if (main[up] >= 0)
                    up = main[up];
            }
            Vector2 v = At(down) - At(up);
            direction[c] = v == Vector2.zero ? Vector2.zero : v.normalized;
        }
        return direction;
    }
}
