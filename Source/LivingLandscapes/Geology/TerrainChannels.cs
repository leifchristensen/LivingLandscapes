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
/// carries), Shape (width, depth, regimes), Impact (deflect or erode), Trace (channel plans), Paint (carving, springs,
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
        bool[] sea = null, List<WorldContext.RiverInflow> worldInflows = null, float worldFlow = float.NaN, float seaLevel = float.NaN, bool riverMouth = false,
        bool[] sand = null, bool[] edgeOpen = null, float lakeShare = float.PositiveInfinity)
    {
        // A closed basin keeps its water (#99), but world-defined water is never lost into one (invariant 11): the closing is
        // decided from the first drainage, and the passes (splits, bends, deposits) can carry the river's water to a basin the
        // first drainage did not run into. When they do, that basin is opened (it fills and spills like any hollow on a river's
        // way) and the channels are built again from the unchanged ground.
        var original = (float[])height.Clone();
        var open = new bool[sizeX * sizeZ];
        for (int attempt = 0; ; attempt++)
        {
            RiverSystem system = BuildOnce(height, sizeX, sizeZ, courses, riverCentre, climate, biome, rockStrength, water, p, worldRiverWidth, sea,
                worldInflows, worldFlow, seaLevel, riverMouth, sand, edgeOpen, lakeShare, open);
            if (system.SwallowedWorldWater.Count == 0 || attempt >= 3)
                return system;
            Log.Message($"[LivingLandscapes] World water ended in {system.SwallowedWorldWater.Count} closed-basin cell(s); opening those basins and building the channels again (attempt {attempt + 1}).");
            foreach (int c in system.SwallowedWorldWater)
                open[c] = true;
            System.Array.Copy(original, height, height.Length);
        }
    }

    private static RiverSystem BuildOnce(float[] height, int sizeX, int sizeZ, List<ChannelCourse> courses, Vector2 riverCentre,
        float climate, string biome, float[] rockStrength, WaterRegimeDef water, TerrainChannelSettings p, SimpleCurve worldRiverWidth,
        bool[] sea, List<WorldContext.RiverInflow> worldInflows, float worldFlow, float seaLevel, bool riverMouth,
        bool[] sand, bool[] edgeOpen, float lakeShare, bool[] openHollows)
    {
        int n = sizeX * sizeZ;
        // The beach is land (loose sand): the sea, for the water, is the water.
        if (sea != null && sand != null)
        {
            sea = (bool[])sea.Clone();
            for (int i = 0; i < n; i++)
                sea[i] &= !sand[i];
        }
        var system = new RiverSystem
        {
            Regime = water, WaterLevel = new float[n], Flow = new Vector2[n], ChannelOf = new int[n], Still = new bool[n],
            Seep = new bool[n],
            Sink = new bool[n],
            HandedToLake = new bool[n],
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
        float threshold = p.channelDischarge * (water?.nudges.discharge ?? 1f);
        float mapSpread = water?.nudges.spread ?? 1f;
        float loss = p.transmissionLoss * p.AbsorptionOf(biome);
        // Where the rain runs off: the climate's mean, distributed by the ground (rock, slope; GroundRunoff, #216).
        float[] runoffAt = GroundRunoff.Distribute(runoff, climate, p.AbsorptionOf(biome), rockStrength, GridMath.Slope(height, sizeX, sizeZ),
            p.rockAbsorptionLoss, p.slopeAbsorptionHalf);
        Drain(height, sizeX, sizeZ, courses, riverCentre, runoff, water, p, worldRiverWidth, out float[] filled, out int[] receiver,
            out float[] inflow, out bool[] worldSource, out float vanillaWidth, out float[] outletLevel, out float yield, out float[] lakeLoss,
            worldInflows, sea, riverMouth, seaLevel, edgeOpen, lakeShare, system.Sink,
            yieldScale: (f, r, inf, ws) => OwnDischargeScale(f, r, inf, ws, runoff, threshold, mapSpread, sizeX, sizeZ, p, worldRiverWidth, worldFlow, loss, runoffAt, out _), openHollows: openHollows);
        system.BaseLevel = outletLevel;
        system.YieldScale = yield;
        System.Func<int, bool> isOutlet = i => Outlets.IsOutlet(outletLevel, i);
        // The one test for standing water (Standing.cs, #156): the filled surface is the level here (the spill estimate).
        var standing = new Standing(filled, sea, p.minDepth, p.standingTolerance, p.springLakeDepth);

        // Passes of stages 2 and 3 (Route, Shape). Pass 0 routes along steepest descent, reads each channel cell's regime
        // nudges, silts up basins a world river runs into and carves meanders; the passes after it let water divide where the
        // nudges spread it, strike the ground it turns against (Impact) and raise the ground with its deposits (Feedback);
        // the last routes and shapes the final network.
        float[] spread = null, discharge = null, width = null, depth = null, bankAmplitude = null;
        int[] secondary = null, main = null, branchFrom = null;
        float[] secondaryShare = null;
        bool[] world = null;
        List<int> cells = null;
        Vector2[] direction = null;
        Dictionary<string, int> regimes = null;
        int splits = 0;
        float dischargeScale = 1f;
        bool[] moving = null;
        float[] power = null, deposition = null, sinuosity = null, valley = null;
        int carvedMeanders = 0, oxbows = 0, infilled = 0, impactCuts = 0, impactCells = 0;
        var gouged = new float[n];
        // On a tile the world gives no river, the map's own discharge is scaled to the tile's world flow (below a creek),
        // keeping every channel's share, so streams still visibly add up where they join.
        string ownWater = "";
        if (vanillaWidth <= 0f)
            dischargeScale = OwnDischargeScale(filled, receiver, inflow, worldSource, runoff, threshold, mapSpread, sizeX, sizeZ, p,
                worldRiverWidth, worldFlow, loss, runoffAt, out ownWater);
        float[] carried = null, seepCapacity = null;
        // Dev: -ll-feedback=share,passes overrides the feedback tuning for sweeps.
        if (GenCommandLine.TryGetCommandLineArg("ll-feedback", out string feedbackArg))
        {
            string[] parts = feedbackArg.Split(',');
            p.feedback.aggradationShare = float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            if (parts.Length > 1)
                p.feedback.passes = int.Parse(parts[1]);
        }
        // Dev: -ll-one-hydraulics=false runs the old Impact and Spread momentum (the A/B of HYDRO-UNIFICATION S2b).
        if (GenCommandLine.TryGetCommandLineArg("ll-one-hydraulics", out string oneArg) && bool.TryParse(oneArg, out bool one))
            p.oneHydraulics = one;
        // Dev: -ll-accommodation=true caps a standing-water bed's rise at its room (the A/B of D5, #186).
        if (GenCommandLine.TryGetCommandLineArg("ll-accommodation", out string accomArg) && bool.TryParse(accomArg, out bool accom))
            p.accommodation = accom;
        // Dev: -ll-grains=none|classes|settling|full switches the grain loads of HYDRO-UNIFICATION S3 (classes = S3a, settling adds S3b, full adds S3c).
        if (GenCommandLine.TryGetCommandLineArg("ll-grains", out string grainsArg))
        {
            p.sediment.grainClasses = grainsArg != "none";
            p.sediment.settlingLength = grainsArg == "settling" || grainsArg == "full";
            p.sediment.jetClasses = grainsArg == "full";
        }
        // Dev: -ll-boundary=on|off switches the boundary-state fixes of HYDRO-S8 (phase B); nothing reads p.boundaryState yet.
        if (GenCommandLine.TryGetCommandLineArg("ll-boundary", out string boundaryArg))
            p.boundaryState = boundaryArg == "on";
        partSlope = partJets = partMomentum = partJoin = partJunction = true;
        partSpread = false;
        joinScale = GenCommandLine.TryGetCommandLineArg("ll-join", out string joinArg) && float.TryParse(joinArg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float js) ? js : 1f;
        stemChord = GenCommandLine.TryGetCommandLineArg("ll-stem", out string stemArg) && stemArg == "chord";
        if (GenCommandLine.TryGetCommandLineArg("ll-hyd", out string hydParts))
        {
            p.oneHydraulics = true;
            partJoin = hydParts.Contains("join");
            partSpread = hydParts.Contains("spread");
            partSlope = hydParts.Contains("slope");
            partJets = hydParts.Contains("jets");
            partMomentum = hydParts.Contains("momentum");
            partJunction = hydParts.Contains("junction");
        }
        int passes = Mathf.Max(3, p.feedback.passes + 2), aggraded = 0;
        float aggradedVolume = 0f;
        // The one deposit ledger of the map (S4): its kappa is fixed by the first pass that deposits and held, so the feedback
        // passes and the final layers lay the same deposit as the same thickness.
        var ledger = new DepositLedger();
        // D5 measurement (#186 step W1): what the passes' alluvium does to standing water. Measures only, logged once below.
        var room = new RoomProbe(n);
        // D5 accommodation report (#186 step W4): what the passes did to standing-water beds with the setting on; reported in the
        // export summary (DevTools.Invariants.Accommodation).
        var accomTally = new AccommodationTally(n);
        for (int pass = 0; pass < passes; pass++)
        {
            discharge = Route(filled, receiver, inflow, worldSource, runoff, threshold, spread, mapSpread, sizeX, sizeZ, p,
                out world, out secondary, out secondaryShare, out branchFrom, out splits, out carried, out _, loss, dischargeScale, lakeLoss, runoffAt);

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
            main = MainDonors(cells, receiver, discharge, threshold, n);

            direction = Hydraulics.Directions(cells, receiver, main, sizeX, p.flowSmoothingSteps);
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
            // Below a junction the channel carries both arms' water: it is not narrower than its widest arm (#220).
            if (p.oneHydraulics && partJunction)
                Confluences.WidthContinuity(cells, receiver, width, moving, p.confluences.widthRelax);
            // Water the ground turns strikes it: it cuts through to lower ground within reach, or is deflected (P6).
            if (pass < passes - 1 && p.impact.enabled)
            {
                int cuts = Impact(cells, height, filled, receiver, main, secondary, secondaryShare, discharge, width, depth, moving, rockStrength, sand,
                    isOutlet, sizeX, sizeZ, p.impact, p.oneHydraulics, p.bendRadiusWidths, p.momentumCells, gouged, out int cutCells);
                if (cuts > 0)
                {
                    receiver = Refill(filled, sizeX, sizeZ, outletLevel, p);
                    impactCuts += cuts;
                    impactCells += cutCells;
                }
            }
            // The network's deposits raise the ground it will be routed over next (P7 feedback).
            if (pass >= 1 && pass < passes - 1 && p.feedback.passes > 0)
            {
                int raised = Aggrade(cells, receiver, secondary, secondaryShare, discharge, dischargeScale, threshold, height, filled, inflow, rockStrength,
                    moving, width, depth, sand, standing, isOutlet, sizeX, sizeZ, p, ledger, room, accomTally, out float volume);
                if (raised > 0)
                {
                    receiver = Refill(filled, sizeX, sizeZ, outletLevel, p);
                    aggraded += raised;
                    aggradedVolume += volume;
                }
            }
            if (pass == 0 && p.riverInfillDepths > 0f)
                infilled = InfillRiverHollows(height, filled, cells, world, depth, sizeX, sizeZ, p);
            if (pass == 0 && p.meander.enabled)
            {
                // Bends: draw the sinuosity the regimes ask for as a trench along a meandering line, then route again.
                carvedMeanders = Meanders.Apply(height, filled, cells, receiver, main, width, sinuosity, valley, sizeX, sizeZ, p.meander, out oxbows);
                if (carvedMeanders > 0)
                    receiver = Refill(filled, sizeX, sizeZ, outletLevel, p);
            }
        }

        // D5 accommodation report (#186 step W4): what the passes did to standing-water beds, for the export summary. depth here
        // is the depth the passes used (before junction scour); Standing holds the spill surface by reference and Refill updated
        // it in place, so the still-standing count reads the final water level.
        system.AccommodationRaisedCells = accomTally.cells;
        system.AccommodationRaisedVolume = accomTally.volume;
        system.AccommodationStillStanding = accomTally.StillStanding(standing, height, depth);

        if (GenCommandLine.TryGetCommandLineArg("ll-probe-cell", out string probe))
        {
            string[] xz = probe.Split(',');
            int pc = int.Parse(xz[1]) * sizeX + int.Parse(xz[0]);
            int pr = receiver[pc];
            Log.Message($"[LivingLandscapes] PROBE ({probe}): height {height[pc] - seaLevel:F2} above the sea, filled {filled[pc] - seaLevel:F2}, sea {sea?[pc]}, outlet {isOutlet(pc)}, discharge {discharge[pc]:F0}, width {width[pc]:F1}; receiver {(pr < 0 ? "none" : $"({pr % sizeX},{pr / sizeX}) height {height[pr] - seaLevel:F2} sea {sea?[pr]} outlet {isOutlet(pr)} width {width[pr]:F1}")}");
        }
        // Stage 5, channel plans (TerrainChannels.Trace.cs); stage 6, carving, springs and bars (TerrainChannels.Paint.cs),
        // with the sediment budget (Sediment) in between.
        // Junctions (Confluences, #20): where a tributary joins, the flow slows on its side and the scour hole deepens the bed.
        // Junctions in still water (the sea, lakes) have none.
        // Standing water uses the depth BEFORE this scour, here and everywhere after (a bed effect never moves the decision).
        var depthBeforeScour = (float[])depth.Clone();
        var stillBefore = new bool[n];
        for (int i = 0; i < n; i++)
            stillBefore[i] = standing.Is(i, height[i], depthBeforeScour[i]);
        JunctionMomentum(cells, receiver, main, discharge, sizeX, secondary, secondaryShare, p, out Vector2[] push, out Vector2[] stem);
        ConfluenceZones zones = Confluences.Find(cells, receiver, main, discharge, direction, width, stillBefore, sizeX, p.confluences, push, stem);
        foreach (int c in cells)
            depth[c] *= zones.Scour[c];
        List<RiverPlan> plans = Trace(cells, receiver, main, branchFrom, discharge, inflow, world, width, sizeX, runoff, vanillaWidth, out int[] planOf);
        var natural = (float[])height.Clone();
        int inHollows = Paint(height, filled, cells, width, depth, depthBeforeScour, standing, moving, bankAmplitude, direction, planOf, sizeX, sizeZ, p, system);
        // How close each seep comes to running: its water as a share of what the seep can hold.
        foreach (int c in cells)
        {
            if (system.Seep[c])
                system.SeepWater[c] = Mathf.Clamp01(carried[c] * dischargeScale / Mathf.Max(seepCapacity[c], 1e-6f));
        }
        system.Channels = plans;
        int springLake = SpringLake(natural, standing, inflow, worldSource, sizeX, sizeZ, p, system);
        // Base level: the sea, lakes and hollows the water stands in. Rivers drop their load there.
        var baseLevel = new bool[n];
        for (int i = 0; i < n; i++)
            baseLevel[i] = system.Still[i] || standing.Is(i, natural[i], depthBeforeScour[i]);
        system.CountedStanding = baseLevel;
        // The sediment budget: in-channel deposits make bars (also below confluences, where a tributary brings more than the
        // joined flow can carry), and the floodplain fields.
        GrainLoads grains = GrainLoads.Create(p.sediment, p.impact.manningN, p.impact.jetWidths, cells, receiver, secondary, filled, depthBeforeScour, width, sizeX);
        Sediment.Deposits(cells, receiver, discharge, dischargeScale, filled, inflow, rockStrength, baseLevel, sizeX, p.sediment,
            out float[] deposit, out float[] depositGrain, out float deposited, out float atBase, out float eroded, out _, sand: sand, zones: zones,
            secondary: secondary, secondaryShare: secondaryShare, grains: grains);
        // Ground the water's impact cut away joins its load. It is a volume in metres; the budget's units are metres divided by
        // the ledger's kappa. Kappa is fixed by the feedback passes; without them (or without deposits there) a first Spread fixes it.
        float gougedTotal = gouged.Sum();
        if (gougedTotal > 0f)
        {
            if (!ledger.IsFixed)
                Sediment.Spread(cells, receiver, system.Flow, deposit, depositGrain, width, filled, natural, sizeX, sizeZ, p.sediment, ledger, out _, out _,
                    discharge, baseLevel, zones, p.oneHydraulics && partSpread ? secondary : null, p.oneHydraulics && partSpread ? secondaryShare : null, grains);
            if (ledger.IsFixed)
            {
                var gougedUnits = new float[n];
                for (int i = 0; i < n; i++)
                    gougedUnits[i] = gouged[i] / ledger.Kappa;
                Sediment.Deposits(cells, receiver, discharge, dischargeScale, filled, inflow, rockStrength, baseLevel, sizeX, p.sediment,
                    out deposit, out depositGrain, out deposited, out atBase, out eroded, out _, gougedUnits, sand, zones, secondary, secondaryShare, grains);
            }
        }
        // Only running water lays down alluvium: a seep's trickle builds no banks or floodplain (the map's deposits are
        // scaled to a typical thickness, which would otherwise raise trickles into troughs).
        foreach (int c in cells)
        {
            if (!moving[c])
                deposit[c] = 0f;
        }
        // The final layers are rebuilt from the final network at the ledger's kappa; the bars' height then reads that deposit.
        string sedimentSummary = Sediment.Layers(cells, receiver, deposit, depositGrain, width, filled, natural, sizeX, sizeZ, p.sediment, ledger, system,
            deposited, atBase, eroded, discharge, baseLevel, zones, p.oneHydraulics && partSpread ? secondary : null, p.oneHydraulics && partSpread ? secondaryShare : null, grains);
        system.Junctions = Confluences.Audit(zones, cells, receiver, width, moving, deposit, sizeX);
        system.Junctions.WidthEnforced = p.oneHydraulics && partJunction;
        int bars = Bars(height, cells, width, depth, moving, deposit, deposition, system.Alluvium, direction, sizeX, sizeZ, p, system, out int islands, zones);
        // Report (TerrainChannels.Report.cs and the log lines below).
        var worldEnds = new List<string>();
        foreach (int c in cells)
        {
            if (world[c] && discharge[c] >= p.sediment.mainRiverShare * 0.5f * discharge[cells[0]] && receiver[c] < 0)
                if (system.Sink[c])
                    system.SwallowedWorldWater.Add(c);
                worldEnds.Add($"({c % sizeX},{c / sizeX}){(sea != null && sea[c] ? " sea" : " land")}");
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
        // D5 measurement (#186 step W1) on its own line, so the "Channels:" line stays byte-identical to main for the flag-off proof.
        Log.Message($"[LivingLandscapes] Deposit room (D5 probe): {room.standingCells} standing cells got alluvium ({room.seaCells} sea cells), " +
                    $"{room.raisedCells} of them raised, {room.raiseTotal:0.###} m of rise on them, largest room {room.largestRoom:0.###} m");
        Log.Message($"[LivingLandscapes] Channels: {plans.Count} channels over {cells.Count} centre cells ({inHollows} in hollows the lake model fills, {splits} splits), " +
                    $"{painted} water cells, {bars} bars ({islands} islands), {System.Array.FindAll(system.Seep, v => v).Length} seep cells (runoff {runoff:0.##}, own discharge x{dischargeScale:0.###}{ownWater}, transmission loss {loss:0.#}){(springLake > 0 ? $" ({springLake} in the lake a source rises in)" : "")}, widest {maxWidth:F1} cells, deepest {maxDepth:F2} m, climate {climate:F2}; {infilled} cells of lakes the river silted up, {carvedMeanders} cells carved for meanders, {oxbows} oxbows, {impactCuts} impact cuts ({impactCells} cells, {gougedTotal:0} m3 into the load), {aggradedVolume:0} m3 aggraded over {p.feedback.passes} feedback passes, {delta}; {zones.Junctions} junctions (strongest {zones.Strongest:F2}); {sedimentSummary}; " +
                    $"regimes by channel cell [{string.Join(", ", regimes.OrderByDescending(r => r.Value).Select(r => $"{r.Key} {100 * r.Value / cells.Count}%"))}].");
        return system;
    }

    /// <summary>The main donor of each channel cell: the channel cell upstream whose steepest descent brings the most water (-1 if none).</summary>
    private static int[] MainDonors(List<int> cells, int[] receiver, float[] discharge, float threshold, int n)
    {
        var main = new int[n];
        for (int i = 0; i < n; i++)
            main[i] = -1;
        foreach (int c in cells)
        {
            int r = receiver[c];
            if (r >= 0 && discharge[r] >= threshold && (main[r] < 0 || discharge[c] > discharge[main[r]]))
                main[r] = c;
        }
        return main;
    }
}
