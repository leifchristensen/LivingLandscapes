using System;

namespace LivingLandscapes.Geology
{
    /// <summary>
    /// Deposition (Davy and Lague 2009): the load runs down the drainage tree from the divides; each land cell keeps
    /// G x load / drainage area of it (rivers drop their load where they gather little more water and slow) and passes the
    /// rest on. What reaches base level (the sea, the region's edges) leaves the model; what reaches a closed basin's lake
    /// settles there, up to the lake's level. Game-free (issue #186): the arithmetic <see cref="LandscapeGenerator"/>
    /// has always used, moved out of the generator so tests can run it and the shore pass can build on it.
    /// </summary>
    public static class ErosionDeposit
    {
        /// <summary>
        /// One deposition pass over the model grid. Walks the drainage tree from the divides (stack, processed back to
        /// front): each cell with a receiver keeps min(load, deposition x load / max(area, 1)) of what it carries, added
        /// to h and deposited, and passes the rest on; a sink fills to its lake level (baseLevel); what reaches base
        /// level stays in load (it leaves the model). Rivers entering the region bring the sediment of their catchment
        /// beyond it (sediment yield scales with drainage area): their inflow, in model cells of drainage, eroding at
        /// the region's own mean rate (yield). Same arithmetic as the generator's Deposit, with Mathf replaced by
        /// System.Math (identical for Min/Max).
        /// </summary>
        public static void Apply(float[] h, float[] eroded, float[] load, float[] inflow, int[] receiver, float[] area,
            int[] stack, int count, bool[] outlet, bool[] sink, float[] baseLevel, float[] deposited, float deposition)
        {
            ApplyWithLedger(h, eroded, load, inflow, receiver, area, stack, count, outlet, sink, baseLevel, deposited,
                deposition, null, out _, out _);
        }

        /// <summary>
        /// The same pass, keeping the ledger of where the load ends up (issue #186, C3): <c>delivered[c]</c> is the load
        /// each base-level cell (receiver &lt; 0, not a sink) still carries - what the model hands to the sea;
        /// <c>sinkFill</c> is the height the closed basins gained; <c>lostAtSink</c> is the load that reached a sink and
        /// did not fit in its lake. The split is exact: sum(eroded) + yield x sum(inflow) equals sum(deposited) +
        /// sum(delivered) + lostAtSink (the lake fill is part of deposited; reported apart as sinkFill, the four-way
        /// partition sums to the same total). Pass a null <c>delivered</c> to skip the ledger.
        /// </summary>
        public static void ApplyWithLedger(float[] h, float[] eroded, float[] load, float[] inflow, int[] receiver,
            float[] area, int[] stack, int count, bool[] outlet, bool[] sink, float[] baseLevel, float[] deposited,
            float deposition, float[] delivered, out float sinkFill, out float lostAtSink)
        {
            ApplyCore(h, eroded, load, inflow, receiver, area, stack, count, outlet, sink, baseLevel, deposited,
                deposition, delivered, out sinkFill, out lostAtSink, null, 0f, null, null);
        }

        /// <summary>
        /// The same pass with the grain of the sediment tracked (issue #186, C3), so the shore pass knows what grain the
        /// model deposited and delivered. <c>supplyGrain[c]</c> is the grain of the sediment cell c's own erosion
        /// contributes (<see cref="Sediment.SupplyGrain"/>), <c>inflowGrain</c> the grain of the sediment world rivers
        /// bring in. Each load carries a mass-weighted mean grain; where a cell keeps part of its load the coarsest
        /// grains drop out first, so <c>depositGrain[c]</c> is coarser than the load's mean and the load passed on finer;
        /// a lake fills with its coarsest part too (<c>depositGrain</c> of the fill mass); <c>deliveredGrain[c]</c> is
        /// the mean grain of what a base-level cell hands to the sea. The terrain and deposit amounts are exactly
        /// Apply's; grain is bookkeeping only. Pass a null <c>supplyGrain</c> to skip the grain (then
        /// <c>depositGrain</c> is unused); <c>deliveredGrain</c> may be null.
        /// </summary>
        public static void ApplyWithGrain(float[] h, float[] eroded, float[] load, float[] inflow, int[] receiver,
            float[] area, int[] stack, int count, bool[] outlet, bool[] sink, float[] baseLevel, float[] deposited,
            float deposition, float[] delivered, float[] supplyGrain, float inflowGrain, float[] depositGrain,
            float[] deliveredGrain, out float sinkFill, out float lostAtSink)
        {
            ApplyCore(h, eroded, load, inflow, receiver, area, stack, count, outlet, sink, baseLevel, deposited,
                deposition, delivered, out sinkFill, out lostAtSink, supplyGrain, inflowGrain, depositGrain,
                deliveredGrain);
        }

        private static void ApplyCore(float[] h, float[] eroded, float[] load, float[] inflow, int[] receiver,
            float[] area, int[] stack, int count, bool[] outlet, bool[] sink, float[] baseLevel, float[] deposited,
            float deposition, float[] delivered, out float sinkFill, out float lostAtSink,
            float[] supplyGrain, float inflowGrain, float[] depositGrain, float[] deliveredGrain)
        {
            int n = h.Length;
            Array.Clear(load, 0, n);
            if (delivered != null) Array.Clear(delivered, 0, n);
            bool grain = supplyGrain != null;
            float[] g = null; // mass-weighted mean grain of each cell's current load (0 where the load is 0)
            if (grain)
            {
                g = new float[n];
                Array.Clear(depositGrain, 0, n);
                if (deliveredGrain != null) Array.Clear(deliveredGrain, 0, n);
            }
            sinkFill = 0f;
            lostAtSink = 0f;
            float yield = 0f;
            int land = 0;
            for (int i = 0; i < n; i++)
            {
                if (!outlet[i])
                {
                    yield += eroded[i];
                    land++;
                }
            }
            yield /= Math.Max(1, land);
            for (int k = count - 1; k >= 0; k--)
            {
                int c = stack[k];
                float arrived = load[c]; // what upstream already passed down
                load[c] += eroded[c] + inflow[c] * yield;
                if (grain)
                {
                    float ownMass = arrived + eroded[c];
                    g[c] = Mix(Mix(g[c], arrived, supplyGrain[c], eroded[c]), ownMass, inflowGrain, inflow[c] * yield);
                }
                int r = receiver[c];
                if (r >= 0)
                {
                    float keep = Math.Min(load[c], deposition * load[c] / Math.Max(area[c], 1f));
                    h[c] += keep;
                    deposited[c] += keep;
                    float onward = load[c] - keep;
                    if (grain)
                    {
                        float gKeep = load[c] > 0f ? KeptGrain(g[c], keep, load[c]) : 0f;
                        if (keep > 0f) depositGrain[c] = gKeep;
                        float gOut = onward > 0f ? (g[c] * load[c] - gKeep * keep) / onward : g[c];
                        g[r] = Mix(g[r], load[r], gOut, onward); // blend before the mass moves, so the mean is by mass
                    }
                    load[r] += onward;
                }
                else if (sink[c])
                {
                    float before = h[c];
                    h[c] = Math.Min(h[c] + load[c], baseLevel[c]);
                    float fill = Math.Max(0f, h[c] - before);
                    deposited[c] += fill;
                    sinkFill += fill;
                    lostAtSink += load[c] - fill; // the load the lake had no room for
                    if (grain && fill > 0f) depositGrain[c] = KeptGrain(g[c], fill, load[c]); // the lake drops its coarsest first too
                }
                else
                {
                    if (delivered != null) delivered[c] = load[c]; // at base level: this load leaves the model, to the sea
                    if (grain && deliveredGrain != null && load[c] > 0f) deliveredGrain[c] = g[c];
                }
            }
        }

        /// <summary>
        /// The grain of the part a cell keeps when the coarsest grains of a load of mean grain <c>g</c> drop out first:
        /// the smaller the kept share, the nearer grain 1 (the coarsest sediment); keeping the whole load keeps its mean.
        /// </summary>
        private static float KeptGrain(float g, float keep, float load)
        {
            return 1f + (g - 1f) * (keep / load);
        }

        /// <summary>Mass-weighted mean of two sediment grains; 0 when there is no mass (grain is undefined for no sediment).</summary>
        private static float Mix(float grainA, float massA, float grainB, float massB)
        {
            float mass = massA + massB;
            return mass > 0f ? (grainA * massA + grainB * massB) / mass : 0f;
        }
    }

    /// <summary>
    /// What a deposition pass with the keeps on (issue #186, C4) accumulated over the erosion model's whole run: the
    /// mass-weighted mean grain of the deposit standing at each model cell (G1), the load each base-level cell handed
    /// to the sea and its grain (G2), and the sediment budget of the run (kept + delivered + lake fill + lost at sinks
    /// = eroded + inflow yield; the log line's closure). Null fields mean that keep was off.
    /// </summary>
    public class ErosionKeepsOutput
    {
        /// <summary>Grain (0..1, 1 coarsest) of the deposit standing at each model cell; 0 where none stands.</summary>
        public float[] DepositGrain;

        /// <summary>Metres of load each base-level model cell handed to the sea, summed over the run.</summary>
        public float[] SeaSupply;

        /// <summary>Grain (0..1) of the load handed to the sea at each base-level model cell; 0 where none was.</summary>
        public float[] SeaSupplyGrain;

        // The sediment budget of the run, in model height units summed over the iterations (the four-way split of
        // everything eroded or brought in; ApplyCore's per-pass conservation, accumulated).
        public float Kept, Delivered, SinkFill, LostAtSink, Eroded, InflowYield;
    }
}
