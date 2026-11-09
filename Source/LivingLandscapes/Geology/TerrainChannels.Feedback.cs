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
    public float aggradationShare = 0.6f;
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
    /// <summary>
    /// Raises <paramref name="height"/> and <paramref name="filled"/> by the network's deposits; returns the cells raised. The
    /// deposits are laid with the same settings as the final layers (S4, #186): the bend lean (<c>flow</c>) and the junction
    /// zones are found from the network as it stands, and the thickness is the ledger's (one kappa for the map).
    /// </summary>
    private static int Aggrade(List<int> cells, int[] receiver, int[] secondary, float[] secondaryShare, float[] discharge, float dischargeScale, float threshold,
        float[] height, float[] filled, float[] inflow, float[] rockStrength, bool[] moving, float[] width, float[] depth, bool[] sand, Standing standing,
        System.Func<int, bool> isOutlet, int sizeX, int sizeZ, TerrainChannelSettings p, DepositLedger ledger, RoomProbe room, out float raisedVolume)
    {
        int n = height.Length;
        raisedVolume = 0f;
        // Base level as the water stands now: the sea and the network's outlets.
        // Deferred (HYDRO-UNIFICATION D5, #186): standing lakes and hollows are not base level here yet. Treating them so makes the
        // deposits fill lakes during routing (s7 invariant 2 fails: shallow water outside a depression) before the final pass can
        // re-evaluate Standing on the raised bed. It lands with the accommodation step (ledger, S4).
        var baseLevel = new bool[n];
        for (int i = 0; i < n; i++)
            baseLevel[i] = standing.IsSea(i) || (isOutlet(i) && !Outlets.IsEdge(i, sizeX, sizeZ));
        // The junction zones and the flow direction, from the receivers as they stand (Impact may have changed them this pass).
        int[] main = MainDonors(cells, receiver, discharge, threshold, n);
        Vector2[] direction = Hydraulics.Directions(cells, receiver, main, sizeX, p.flowSmoothingSteps);
        var stillNow = new bool[n];
        for (int i = 0; i < n; i++)
            stillNow[i] = standing.Is(i, height[i], depth[i]);
        JunctionMomentum(cells, receiver, main, discharge, sizeX, secondary, secondaryShare, p, out Vector2[] push, out Vector2[] stem);
        ConfluenceZones zones = Confluences.Find(cells, receiver, main, discharge, direction, width, stillNow, sizeX, p.confluences, push, stem);
        GrainLoads grains = GrainLoads.Create(p.sediment, p.impact.manningN, p.impact.jetWidths, cells, receiver, secondary, filled, depth, width, sizeX);
        Sediment.Deposits(cells, receiver, discharge, dischargeScale, filled, inflow, rockStrength, baseLevel, sizeX, p.sediment,
            out float[] deposit, out float[] depositGrain, out _, out _, out _, out _, sand: sand, zones: zones, secondary: secondary, secondaryShare: secondaryShare,
            grains: grains);
        foreach (int c in cells)
        {
            if (!moving[c])
                deposit[c] = 0f;
        }
        Sediment.Spread(cells, receiver, direction, deposit, depositGrain, width, filled, height, sizeX, sizeZ, p.sediment, ledger, out float[] alluvium,
            out _, discharge, baseLevel, zones, p.oneHydraulics && partSpread ? secondary : null, p.oneHydraulics && partSpread ? secondaryShare : null, grains);
        float[] rises = ledger.Raise(alluvium, p.feedback.aggradationShare, out raisedVolume);
        Hydraulics.GradeRises(cells, receiver, secondary, filled, rises);
        room.Observe(alluvium, rises, stillNow, standing, height, depth);
        int raised = 0;
        for (int i = 0; i < n; i++)
        {
            if (rises[i] <= 0f)
                continue;
            height[i] += rises[i];
            filled[i] += rises[i];
            raised++;
        }
        return raised;
    }

    /// <summary>
    /// Measurement only (HYDRO-UNIFICATION D5, #186 step W1): what the feedback passes' alluvium does to standing water, summed
    /// over the passes. It counts the standing cells the alluvium reaches, how many of them a pass actually raises (after
    /// <see cref="Hydraulics.GradeRises"/>), the total rise on them, and the largest <em>room</em> any of them has: the rise that
    /// would make it stop standing, <see cref="Standing.Room"/> with L the filled (spill)
    /// surface. Nothing here moves the ground; it answers whether the sediment budget lays alluvium on lake cells at all, which is
    /// what the accommodation step (deposit accommodation) has to be built for. Dropped once accommodation itself lands.
    /// </summary>
    private sealed class RoomProbe
    {
        private readonly bool[] gotAlluvium;
        private readonly bool[] wasRaised;
        private readonly bool[] wasSea;
        /// <summary>Standing land-water cells the alluvium reached in some pass (each counted once).</summary>
        public int standingCells;
        /// <summary>Sea cells the alluvium reached in some pass (each counted once; the sea has no room to fill).</summary>
        public int seaCells;
        /// <summary>Of <see cref="standingCells"/>, the ones a pass raised (each counted once).</summary>
        public int raisedCells;
        /// <summary>Metres of rise the passes gave those cells, summed over cells and passes (metres times cells).</summary>
        public float raiseTotal;
        /// <summary>The largest room a standing cell the alluvium reached had, over the passes (metres).</summary>
        public float largestRoom;

        public RoomProbe(int n)
        {
            gotAlluvium = new bool[n];
            wasRaised = new bool[n];
            wasSea = new bool[n];
        }

        /// <summary>One pass's alluvium and the rises it was graded to, before either touches the ground.</summary>
        /// <param name="stillNow">Standing water as the water stands this pass (the sea included), the same test the budget used.</param>
        public void Observe(float[] alluvium, float[] rises, bool[] stillNow, Standing standing, float[] bed, float[] depth)
        {
            for (int i = 0; i < alluvium.Length; i++)
            {
                if (alluvium[i] <= 0f || !stillNow[i])
                    continue;
                if (standing.IsSea(i))
                {
                    if (!wasSea[i])
                    {
                        wasSea[i] = true;
                        seaCells++;
                    }
                    continue;
                }
                if (!gotAlluvium[i])
                {
                    gotAlluvium[i] = true;
                    standingCells++;
                }
                float room = standing.Room(i, bed[i], depth[i]);
                largestRoom = Mathf.Max(largestRoom, room);
                if (rises[i] > 0f)
                {
                    raiseTotal += rises[i];
                    if (!wasRaised[i])
                    {
                        wasRaised[i] = true;
                        raisedCells++;
                    }
                }
            }
        }
    }
}
