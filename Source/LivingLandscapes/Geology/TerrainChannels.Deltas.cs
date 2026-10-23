using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>
/// Stage 4, land at base level: load a river drops where it meets the sea or a lake builds the floor up; ground built above the
/// sea becomes delta land the coast mutator leaves alone. (Distributaries are drawn by <see cref="Distributaries"/>.)
/// </summary>
public static partial class TerrainChannels
{
    /// <summary>
    /// Deltas from the sediment budget (Docs/SEDIMENT-PLAN.md, step 3): where rivers drop their load into the sea or a lake the
    /// floor rises by the alluvium laid on it; ground built above the water is delta land, flow over it spreads (distributaries),
    /// and the water is routed again over the new ground. Returns the number of delta land cells.
    /// </summary>
    private static int BuildDeltas(float[] height, float[] filled, List<int> cells, int[] receiver, float[] discharge, float dischargeScale, float[] width,
        float[] inflow, float[] rockStrength, float[] depth, bool[] sea, float[] spread, System.Func<int, bool> isOutlet, int sizeX, int sizeZ,
        TerrainChannelSettings p, RiverSystem system, ref int[] receiverOut)
    {
        int n = height.Length;
        var baseLevel = new bool[n];
        float seaLevel = float.MinValue;
        bool anyBase = false;
        for (int i = 0; i < n; i++)
        {
            bool inSea = sea != null && sea[i];
            baseLevel[i] = inSea || filled[i] - height[i] > Mathf.Max(depth[i], p.minDepth) + 0.05f;
            anyBase |= baseLevel[i];
            if (inSea)
                seaLevel = Mathf.Max(seaLevel, height[i]);
        }
        if (!anyBase)
            return 0;
        Sediment.Deposits(cells, receiver, discharge, dischargeScale, filled, inflow, rockStrength, baseLevel, sizeX, p.sediment,
            out float[] deposit, out float[] grain, out _, out float atBase, out _);
        if (atBase <= 0f)
            return 0;
        // Only what settled at base level builds land: rivers drop everything there; floodplains upstream keep their shape.
        for (int i = 0; i < n; i++)
        {
            if (!baseLevel[i])
                deposit[i] = 0f;
        }
        Sediment.Spread(cells, receiver, null, deposit, grain, width, filled, height, sizeX, sizeZ, p.sediment, out float[] built, out _);
        system.DeltaLand = new bool[n];
        int land = 0;
        for (int i = 0; i < n; i++)
        {
            if (!baseLevel[i] || built[i] <= 0f)
                continue;
            float raise = built[i] * p.sediment.landBuilding;
            height[i] += raise;
            filled[i] = Mathf.Max(filled[i], height[i]);
            spread[i] = Mathf.Max(spread[i], 1f) * p.sediment.deltaSpread;
            if (sea != null && sea[i] && height[i] > seaLevel + 0.05f)
            {
                system.DeltaLand[i] = true;
                land++;
            }
        }
        receiverOut = Refill(filled, sizeX, sizeZ, isOutlet, p);
        var raised = new bool[n];
        for (int i = 0; i < n; i++)
            raised[i] = baseLevel[i] && built[i] > 0f;
        Log.Message($"[LivingLandscapes] Channel deltas raised {System.Array.FindAll(raised, r => r).Length} cells at base level ({GridMath.Strips(raised, sizeX, sizeZ)} in strips), {land} above the sea ({GridMath.Strips(system.DeltaLand, sizeX, sizeZ)} in strips).");
        return land;
    }
}
