using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>Tuning of <see cref="TerrainChannels"/>' impact stage (P6, deflect or erode).</summary>
public class ImpactSettings
{
    public bool enabled = true;

    /// <summary>
    /// Manning's roughness n (s / m^(1/3)) for the flow speed u = d^(2/3) S^(1/2) / n, at most the critical speed sqrt(g d)
    /// (rivers rarely run supercritical).
    /// </summary>
    public float manningN = 0.035f;

    /// <summary>
    /// Shear on the ground a turned flow strikes, as a share of rho u^2 at full attack (an impinging jet's wall shear, Beltaos and
    /// Rajaratnam: a few hundredths).
    /// </summary>
    public float impactShear = 0.03f;

    /// <summary>
    /// Critical shear stress (Pa) of the ground: soilResistance on soft ground, rockResistance on the strongest rock, geometric
    /// between them by rock strength.
    /// </summary>
    public float soilResistance = 10f;
    public float rockResistance = 1000f;

    /// <summary>The water turns less than this (degrees) where it only follows a gentle bend: no impact.</summary>
    public float minTurn = 45f;

    /// <summary>
    /// What the impact can remove ahead (Partheniades: erosion grows with the excess stress, stress / resistance - 1): cutBudget
    /// metre-cells of ground above the cut's bed per channel width and unit of excess, along the flow's momentum, at most
    /// maxGougeWidths widths far. Lower ground within that is cut through to; otherwise the water turns aside.
    /// </summary>
    public float cutBudget = 1f;
    public float maxGougeWidths = 8f;

    /// <summary>Cells upstream over which the incoming flow's momentum (direction and speed) is read.</summary>
    public int momentumSteps = 8;
}

/// <summary>
/// Stage 3b, impact (P6, deflect or erode): wherever moving water is turned by the ground, it strikes the ground ahead along
/// its momentum. The impact shear is tau = C rho u^2 sin(phi / 2) for a turn of phi (0 running straight on, 1 turned right
/// back), with u from Manning on the reach upstream. Above the ground's critical shear (by rock strength) the impact can remove
/// ground ahead in proportion to the excess stress: if lower ground (another basin, the sea) lies within what it can remove,
/// the water cuts through to it; otherwise it is deflected and keeps the way the ground gives. The
/// same rule for every channel, from creeks to the world river.
/// </summary>
public static partial class TerrainChannels
{
    private const float WaterDensity = 1000f;

    /// <summary>Returns the number of cuts; lowers <paramref name="height"/> and <paramref name="filled"/> along them.</summary>
    /// <param name="gouged">Ground (cubic metres) each striking cell's water cut away, added to: it joins that water's load.</param>
    private static int Impact(List<int> cells, float[] height, float[] filled, int[] receiver, int[] main, float[] width, float[] depth,
        bool[] moving, float[] rockStrength, System.Func<int, bool> isOutlet, int sizeX, int sizeZ, ImpactSettings p, float[] gouged,
        out int cutCells)
    {
        cutCells = 0;
        int cuts = 0;
        float cosMinTurn = Mathf.Cos(p.minTurn * Mathf.Deg2Rad);
        Vector2 At(int c) => new Vector2(c % sizeX + 0.5f, c / sizeX + 0.5f);
        var path = new List<int>();
        int turned = 0, over = 0, deflected = 0;
        float strongest = 0f, fastest = 0f;
        // Highest first: a cut upstream changes what the water downstream meets only through the next routing pass.
        foreach (int c in cells)
        {
            int r = receiver[c];
            if (!moving[c] || r < 0)
                continue;
            int up = c;
            float run = 0f;
            for (int s = 0; s < p.momentumSteps && main[up] >= 0; s++)
            {
                run += (At(main[up]) - At(up)).magnitude;
                up = main[up];
            }
            if (up == c)
                continue;
            Vector2 momentum = (At(c) - At(up)).normalized, onward = (At(r) - At(c)).normalized;
            float turn = Vector2.Dot(momentum, onward);
            if (turn > cosMinTurn)
                continue;
            // Speed of the water arriving (Manning on the reach upstream), and the normal part of its pressure on the ground ahead.
            float slope = Mathf.Max(0f, filled[up] - filled[c]) / Mathf.Max(run, 1f);
            float d = Mathf.Max(depth[c], 0.01f);
            float speed = Mathf.Min(Mathf.Pow(d, 2f / 3f) * Mathf.Sqrt(Mathf.Max(slope, 1e-6f)) / p.manningN, Mathf.Sqrt(9.81f * d));
            float phi = Mathf.Acos(Mathf.Clamp(turn, -1f, 1f));
            float stress = p.impactShear * WaterDensity * speed * speed * Mathf.Sin(phi * 0.5f);
            Vector2 ahead = At(c) + momentum;
            int ax = Mathf.Clamp((int)ahead.x, 0, sizeX - 1), az = Mathf.Clamp((int)ahead.y, 0, sizeZ - 1);
            float strength = rockStrength != null ? rockStrength[az * sizeX + ax] : 0.5f;
            float resistance = p.soilResistance * Mathf.Pow(p.rockResistance / p.soilResistance, Mathf.Clamp01(strength));
            turned++;
            strongest = Mathf.Max(strongest, stress / resistance);
            fastest = Mathf.Max(fastest, speed);
            if (stress <= resistance)
                continue;
            over++;
            // Erode ahead along the momentum; cut through only if lower ground lies within reach.
            float reach = p.maxGougeWidths * Mathf.Max(width[c], 1f);
            float budget = p.cutBudget * Mathf.Max(width[c], 1f) * (stress / resistance - 1f), bed = filled[c] - depth[c], cost = 0f;
            path.Clear();
            bool through = false;
            for (float t = 1f; t <= reach; t += 1f)
            {
                Vector2 v = At(c) + momentum * t;
                if (v.x < 0f || v.y < 0f || v.x >= sizeX || v.y >= sizeZ)
                    break;
                int i = (int)v.y * sizeX + (int)v.x;
                if (i == c || (path.Count > 0 && path[path.Count - 1] == i))
                    continue;
                if (filled[i] < filled[c] || isOutlet(i))
                {
                    through = true;
                    break;
                }
                cost += Mathf.Max(0f, height[i] - bed);
                if (cost > budget)
                    break;
                path.Add(i);
            }
            if (!through || path.Count == 0)
            {
                deflected++;
                continue;
            }
            // The cut falls gently from the water's surface here to the lower ground, its bed a channel depth below.
            for (int k = 0; k < path.Count; k++)
            {
                int i = path[k];
                float level = filled[c] - 1e-4f * (k + 1);
                filled[i] = Mathf.Min(filled[i], level);
                float bedHere = level - depth[c];
                if (height[i] > bedHere)
                {
                    gouged[c] += height[i] - bedHere;
                    height[i] = bedHere;
                }
            }
            cutCells += path.Count;
            cuts++;
        }
        Verse.Log.Message($"[LivingLandscapes] Impact (P6): {turned} channel cells turned over {p.minTurn:F0} deg (fastest {fastest:F2} m/s, " +
                          $"strongest {strongest:F1} x the ground's resistance); {over} erode ahead: {cuts} cut through to lower ground, {deflected} deflected.");
        return cuts;
    }
}
