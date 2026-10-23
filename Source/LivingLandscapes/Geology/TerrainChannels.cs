using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>
/// The map's channels, from the landform's own drainage (Docs/WATER-AUDIT.md, reset steps 1 and 2). Water runs down
/// the steepest descent of the depression-filled landform; a channel forms wherever the discharge it gathers passes a
/// threshold, so channels follow the valleys the erosion model cut and bend with the terrain's own relief. Water
/// entering from beyond the map (world rivers, the region's channels) adds its discharge where it enters. Width and
/// depth follow discharge (hydraulic geometry), depth also stream power, each roughened by low-frequency noise as
/// vanilla does. The water surface is the filled landform, which never rises downstream (rule W1); where a channel
/// crosses a hollow deeper than itself the lake model fills the hollow instead.
///
/// The work is split by stage into partial files: Drain (where water enters and runs), Route (how much each cell
/// carries), Shape (width, depth, regimes), Deltas (land at base level), Trace (channel plans), Paint (carving, springs,
/// bars) and Report (log measures). <see cref="Build"/> runs them in order, three routing passes, and logs the result.
/// </summary>
public static partial class TerrainChannels
{
    /// <summary>Lowers <paramref name="height"/> into channel beds and returns the channels with their per-cell water.</summary>
    /// <param name="courses">The erosion model's channels: where water enters from beyond the map, and the world river's class.</param>
    /// <param name="riverCentre">Where world rivers meet (map cells); a world river rising on the map (headwater) rises here.</param>
    /// <param name="climate">Runoff per cell, 0..1 (the map's climate wetness).</param>
    public static RiverSystem Build(float[] height, int sizeX, int sizeZ, List<ChannelCourse> courses, Vector2 riverCentre,
        float climate, string biome, float[] rockStrength, WaterRegimeDef water, TerrainChannelSettings p, SimpleCurve worldRiverWidth,
        bool[] sea = null, List<WorldContext.RiverInflow> worldInflows = null, float worldFlow = float.NaN, bool riverMouth = false)
    {
        int n = sizeX * sizeZ;
        var system = new RiverSystem
        {
            Regime = water, WaterLevel = new float[n], Flow = new Vector2[n], ChannelOf = new int[n], Still = new bool[n],
            Seep = new bool[n],
            SeepWater = new float[n],
        };
        for (int i = 0; i < n; i++)
        {
            system.WaterLevel[i] = float.NaN;
            system.ChannelOf[i] = -1;
        }

        // Stage 1, drainage and inflow (TerrainChannels.Drain.cs).
        // Only what the ground cannot absorb runs off (a trickle is kept so drainage still orders the cells).
        float runoff = Mathf.Max(p.RunoffOf(climate, biome), 0.01f);
        Drain(height, sizeX, sizeZ, courses, riverCentre, runoff, water, p, worldRiverWidth, out float[] filled, out int[] receiver,
            out float[] inflow, out bool[] worldSource, out float vanillaWidth, out System.Func<int, bool> isOutlet, worldInflows, sea, riverMouth);

        // Three passes of stages 2 and 3 (Route, Shape). Pass 0 routes along steepest descent, reads each channel cell's
        // regime nudges, silts up basins a world river runs into and carves meanders; pass 1 lets water divide where the
        // nudges spread it, builds land at base level and draws delta distributaries (stage 4, Deltas); pass 2 routes and
        // shapes the final network.
        float threshold = p.channelDischarge * (water?.nudges.discharge ?? 1f);
        float mapSpread = water?.nudges.spread ?? 1f;
        float[] spread = null, discharge = null, width = null, depth = null, bankAmplitude = null;
        int[] secondary = null, main = null, branchFrom = null;
        bool[] world = null;
        List<int> cells = null;
        Vector2[] direction = null;
        Dictionary<string, int> regimes = null;
        int splits = 0;
        float dischargeScale = 1f;
        bool[] moving = null;
        float[] power = null, deposition = null, sinuosity = null, valley = null;
        int carvedMeanders = 0, oxbows = 0, deltaCells = 0, deltaBranches = 0, infilled = 0;
        Dictionary<int, (int second, float share)> forcedSplits = null;
        // On a tile the world gives no river, the map's own discharge is scaled to the tile's world flow (below a creek),
        // keeping every channel's share, so streams still visibly add up where they join.
        string ownWater = "";
        float loss = p.transmissionLoss * p.AbsorptionOf(biome);
        if (vanillaWidth <= 0f)
            dischargeScale = OwnDischargeScale(filled, receiver, inflow, worldSource, runoff, threshold, mapSpread, sizeX, sizeZ, p,
                worldRiverWidth, worldFlow, loss, out ownWater);
        float[] carried = null, seepCapacity = null;
        for (int pass = 0; pass < 3; pass++)
        {
            discharge = Route(filled, receiver, inflow, worldSource, runoff, threshold, spread, mapSpread, sizeX, sizeZ, p, forcedSplits,
                out world, out secondary, out branchFrom, out splits, out carried, loss, dischargeScale);

            // Where channels start; the water regime nudges it (a denser or sparser network).
            cells = new List<int>();
            for (int c = 0; c < n; c++)
            {
                if (discharge[c] >= threshold)
                    cells.Add(c);
            }
            if (cells.Count == 0)
                return system;
            // Highest first: every receiver lies lower, so donors come before receivers (splits break discharge order).
            cells.Sort((x, y) => filled[y].CompareTo(filled[x]));

            // The main donor of each channel cell: the channel cell upstream whose steepest descent brings the most water.
            main = new int[n];
            for (int i = 0; i < n; i++)
                main[i] = -1;
            foreach (int c in cells)
            {
                int r = receiver[c];
                if (r >= 0 && discharge[r] >= threshold && (main[r] < 0 || discharge[c] > discharge[main[r]]))
                    main[r] = c;
            }

            direction = Directions(cells, receiver, main, sizeX, p.flowSmoothingSteps);
            width = new float[n];
            depth = new float[n];
            spread = new float[n];
            power = new float[n];
            deposition = new float[n];
            sinuosity = new float[n];
            valley = new float[n];
            regimes = new Dictionary<string, int>();
            // Width and depth from the water the channel still carries after losses into its bed.
            bankAmplitude = Shape(cells, height, filled, rockStrength, receiver, carried, dischargeScale, direction, sizeX, sizeZ, water, p,
                width, depth, spread, moving = new bool[n], power, deposition, regimes, sinuosity, valley, seepCapacity = new float[n]);
            // Once water runs as a channel it keeps running downstream (cells are ordered highest first).
            foreach (int c in cells)
            {
                if (moving[c] && receiver[c] >= 0)
                    moving[receiver[c]] = true;
            }
            int[] routed = receiver;
            if (pass == 1)
                deltaCells = BuildDeltas(height, filled, cells, receiver, discharge, dischargeScale, width, inflow, rockStrength, depth, sea, spread,
                    isOutlet, sizeX, sizeZ, p, system, ref receiver);
            if (pass == 1)
            {
                deltaBranches = Distributaries.Carve(height, filled, routed, main, discharge, width, depth, valley, sea, spread,
                    p.sediment.deltaSpread, sizeX, sizeZ, p.distributaries, system, out int lobeCells, out var deltaSplits);
                deltaCells += lobeCells;
                if (deltaBranches > 0)
                {
                    receiver = Refill(filled, sizeX, sizeZ, isOutlet, p);
                    forcedSplits = new Dictionary<int, (int second, float share)>();
                    foreach ((int at, int a, int b, float share) in deltaSplits)
                    {
                        if (filled[a] < filled[at] && filled[b] < filled[at])
                        {
                            receiver[at] = a;
                            forcedSplits[at] = (b, share);
                        }
                    }
                }
            }
            if (pass == 0 && p.riverInfillDepths > 0f)
                infilled = InfillRiverHollows(height, filled, cells, world, depth, sizeX, sizeZ, p);
            if (pass == 0 && p.meander.enabled)
            {
                // Bends: draw the sinuosity the regimes ask for as a trench along a meandering line, then route again.
                carvedMeanders = Meanders.Apply(height, filled, cells, receiver, main, width, sinuosity, valley, sizeX, sizeZ, p.meander, out oxbows);
                if (carvedMeanders > 0)
                    receiver = Refill(filled, sizeX, sizeZ, isOutlet, p);
            }
        }

        // Stage 5, channel plans (TerrainChannels.Trace.cs); stage 6, carving, springs and bars (TerrainChannels.Paint.cs),
        // with the sediment budget (Sediment) in between.
        List<RiverPlan> plans = Trace(cells, receiver, main, branchFrom, discharge, inflow, world, width, sizeX, runoff, vanillaWidth, out int[] planOf);
        var natural = (float[])height.Clone();
        int inHollows = Paint(height, filled, cells, width, depth, moving, bankAmplitude, direction, planOf, sizeX, sizeZ, p, system);
        // How close each seep comes to running: its water as a share of what the seep can hold.
        foreach (int c in cells)
        {
            if (system.Seep[c])
                system.SeepWater[c] = Mathf.Clamp01(carried[c] * dischargeScale / Mathf.Max(seepCapacity[c], 1e-6f));
        }
        system.Channels = plans;
        int springLake = SpringLake(natural, filled, inflow, worldSource, sizeX, sizeZ, p, system);
        // Base level: the sea, lakes and hollows the water stands in. Rivers drop their load there.
        var baseLevel = new bool[n];
        for (int i = 0; i < n; i++)
            baseLevel[i] = (sea != null && sea[i]) || system.Still[i] || filled[i] - natural[i] > Mathf.Max(depth[i], p.minDepth) + 0.05f;
        // The sediment budget: in-channel deposits make bars (also below confluences, where a tributary brings more than the
        // joined flow can carry), and the floodplain fields.
        Sediment.Deposits(cells, receiver, discharge, dischargeScale, filled, inflow, rockStrength, baseLevel, sizeX, p.sediment,
            out float[] deposit, out float[] depositGrain, out float deposited, out float atBase, out float eroded);
        // Only running water lays down alluvium: a seep's trickle builds no banks or floodplain (the map's deposits are
        // scaled to a typical thickness, which would otherwise raise trickles into troughs).
        foreach (int c in cells)
        {
            if (!moving[c])
                deposit[c] = 0f;
        }
        int bars = Bars(height, cells, width, depth, moving, deposit, deposition, direction, sizeX, sizeZ, p, system, out int islands);
        string sedimentSummary = Sediment.Layers(cells, receiver, deposit, depositGrain, width, filled, natural, sizeX, sizeZ, p.sediment, system,
            deposited, atBase, eroded);
        // Report (TerrainChannels.Report.cs and the log lines below).
        var worldEnds = new List<string>();
        foreach (int c in cells)
        {
            if (world[c] && discharge[c] >= p.sediment.mainRiverShare * 0.5f * discharge[cells[0]] && receiver[c] < 0)
                worldEnds.Add($"({c % sizeX},{c / sizeX})");
        }
        Log.Message($"[LivingLandscapes] World water leaves the drainage at {string.Join(" ", worldEnds)}; entries at {string.Join(" ", System.Linq.Enumerable.Range(0, n).Where(i => worldSource[i]).Select(i => $"({i % sizeX},{i / sizeX})"))}.");
        var dry = new List<string>();
        for (int i = 0; i < n && dry.Count < 6; i++)
        {
            if (system.ChannelOf[i] >= 0 && !float.IsNaN(system.WaterLevel[i]) && height[i] > system.WaterLevel[i] + 0.02f)
                dry.Add($"({i % sizeX},{i / sizeX}) ground {height[i]:F2} level {system.WaterLevel[i]:F2}");
        }
        if (dry.Count > 0)
            Log.Message($"[LivingLandscapes] Channel cells above their water level at build: {string.Join("; ", dry)}");
        string delta = MainRiver(cells, receiver, secondary, world, discharge, inflow, worldSource, sea, system, sizeX, sizeZ, p);
        int painted = 0;
        for (int i = 0; i < n; i++)
        {
            if (system.ChannelOf[i] >= 0)
                painted++;
        }
        float maxWidth = 0f, maxDepth = 0f;
        foreach (int c in cells)
        {
            maxWidth = Mathf.Max(maxWidth, width[c]);
            maxDepth = Mathf.Max(maxDepth, depth[c]);
        }
        Log.Message($"[LivingLandscapes] Channels: {plans.Count} channels over {cells.Count} centre cells ({inHollows} in hollows the lake model fills, {splits} splits), " +
                    $"{painted} water cells, {bars} bars ({islands} islands), {System.Array.FindAll(system.Seep, v => v).Length} seep cells (runoff {runoff:0.##}, own discharge x{dischargeScale:0.###}{ownWater}, transmission loss {loss:0.#}){(springLake > 0 ? $" ({springLake} in the lake a source rises in)" : "")}, widest {maxWidth:F1} cells, deepest {maxDepth:F2} m, climate {climate:F2}; {infilled} cells of lakes the river silted up, {carvedMeanders} cells carved for meanders, {oxbows} oxbows, {deltaCells} cells of delta land, {deltaBranches} delta branches drawn, {delta}; {sedimentSummary}; " +
                    $"regimes by channel cell [{string.Join(", ", regimes.OrderByDescending(r => r.Value).Select(r => $"{r.Key} {100 * r.Value / cells.Count}%"))}].");
        return system;
    }
}
