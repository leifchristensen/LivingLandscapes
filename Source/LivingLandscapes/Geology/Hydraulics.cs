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
        Mathf.Max(speed, donorSpeed * Mathf.Exp(-distance / jetLength));

    /// <summary>
    /// The direction the water carries into each cell (P5): the discharge-weighted sum of the steps it took in, down the
    /// receivers (each step averaged with the momentum arriving at its cell, so it follows the flow's bends). Cells are
    /// ordered highest first, so donors come before their receivers; <c>Vector2.zero</c> where no water arrives.
    /// </summary>
    public static Vector2[] Momentum(List<int> cells, int[] receiver, float[] discharge, int sizeX)
    {
        var momentum = new Vector2[receiver.Length];
        foreach (int c in cells)
        {
            int r = receiver[c];
            if (r < 0)
                continue;
            var step = new Vector2(r % sizeX - c % sizeX, r / sizeX - c / sizeX).normalized;
            Vector2 carried = momentum[c] == Vector2.zero ? step : momentum[c].normalized;
            momentum[r] += (carried + step).normalized * discharge[c];
        }
        return momentum;
    }

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
