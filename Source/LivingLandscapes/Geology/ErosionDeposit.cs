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
            int n = h.Length;
            Array.Clear(load, 0, n);
            if (delivered != null) Array.Clear(delivered, 0, n);
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
                load[c] += eroded[c] + inflow[c] * yield;
                int r = receiver[c];
                if (r >= 0)
                {
                    float keep = Math.Min(load[c], deposition * load[c] / Math.Max(area[c], 1f));
                    h[c] += keep;
                    deposited[c] += keep;
                    load[r] += load[c] - keep;
                }
                else if (sink[c])
                {
                    float before = h[c];
                    h[c] = Math.Min(h[c] + load[c], baseLevel[c]);
                    float fill = Math.Max(0f, h[c] - before);
                    deposited[c] += fill;
                    sinkFill += fill;
                    lostAtSink += load[c] - fill; // the load the lake had no room for
                }
                else if (delivered != null)
                {
                    delivered[c] = load[c]; // at base level: this load leaves the model, to the sea
                }
            }
        }
    }
}
