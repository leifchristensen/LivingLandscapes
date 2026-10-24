using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>Tuning of <see cref="TerrainChannels"/>' feedback passes (P7 deposits change the ground the water is routed over).</summary>
public class FeedbackSettings
{
    /// <summary>Routing passes in which the network's deposits raise the ground before the water is routed again (0: none).</summary>
    public int passes = 3;

    /// <summary>
    /// Share of a pass's alluvium (the sediment budget's deposits spread as it lays them down, in metres) that raises the ground
    /// before the next routing: each pass stands for a span of the river's history, the share for how much of the map's
    /// alluvium builds up in that span.
    /// </summary>
    public float aggradationShare = 0.3f;
}

/// <summary>
/// Stage 3c, feedback (P7 -> routing): between routing passes the sediment budget runs on the network as it stands, its
/// deposits spread over the ground near the water as they settle (bars in the channel, banks and floodplain beside it, the load
/// dropped where the river meets still water), and a share of that alluvium raises the ground. The water is then routed again
/// over the changed ground: a channel that silts up stands higher than the ground beside it and loses its water to a lower way
/// (avulsion), a bar raised in the flow divides it, and a mouth bar that rises above the sea becomes land the river must go
/// around. Nothing here names a delta: it is what the river's own deposits do to its routing.
/// </summary>
public static partial class TerrainChannels
{
    /// <summary>Raises <paramref name="height"/> and <paramref name="filled"/> by the network's deposits; returns the cells raised.</summary>
    private static int Aggrade(List<int> cells, int[] receiver, float[] discharge, float dischargeScale, float[] height, float[] filled,
        float[] inflow, float[] rockStrength, bool[] moving, float[] width, bool[] sand, System.Func<int, bool> isOutlet, int sizeX, int sizeZ,
        TerrainChannelSettings p, out float raisedVolume)
    {
        int n = height.Length;
        raisedVolume = 0f;
        // Base level as the water stands now: the sea and the network's outlets.
        var baseLevel = new bool[n];
        for (int i = 0; i < n; i++)
            baseLevel[i] = isOutlet(i);
        Sediment.Deposits(cells, receiver, discharge, dischargeScale, filled, inflow, rockStrength, baseLevel, sizeX, p.sediment,
            out float[] deposit, out float[] depositGrain, out _, out _, out _, sand: sand);
        foreach (int c in cells)
        {
            if (!moving[c])
                deposit[c] = 0f;
        }
        Sediment.Spread(cells, receiver, null, deposit, depositGrain, width, filled, height, sizeX, sizeZ, p.sediment, out float[] alluvium,
            out _, out _);
        int raised = 0;
        for (int i = 0; i < n; i++)
        {
            float rise = alluvium[i] * p.feedback.aggradationShare;
            if (rise <= 0f)
                continue;
            height[i] += rise;
            filled[i] += rise;
            raisedVolume += rise;
            raised++;
        }
        return raised;
    }
}
