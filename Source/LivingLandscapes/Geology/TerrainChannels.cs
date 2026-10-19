using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>How much of the rain a biome's ground absorbs before water runs off (0..1 of the climate wetness).</summary>
public class BiomeAbsorption
{
    public string biome;
    public float absorption;
}

/// <summary>Channel settings (part of <see cref="GeologyGenerationExtension"/>). Discharge in map cells of runoff.</summary>
public class TerrainChannelSettings
{
    /// <summary>
    /// The ground absorbs water first: runoff per cell is the climate wetness (from rainfall) above what the ground
    /// absorbs, as a share of what is left: (climate - absorption) / (1 - absorption). Absorption by biome (defName);
    /// <see cref="absorption"/> for biomes not listed. Only runoff gathers into channels.
    /// </summary>
    public float absorption = 0.3f;
    public List<BiomeAbsorption> biomeAbsorption = new List<BiomeAbsorption>();

    /// <summary>
    /// Moving water is at least this wide (cells). Where a drainage line's runoff makes a narrower channel, the water
    /// seeps: saturated ground (marsh, mud) instead of a stream, so drainage visibly gathers before it flows.
    /// </summary>
    public float minMovingWidth = 2f;

    public float RunoffOf(float climate, string biome)
    {
        float absorbed = absorption;
        foreach (BiomeAbsorption b in biomeAbsorption)
        {
            if (b.biome == biome)
                absorbed = b.absorption;
        }
        return Mathf.Clamp01((climate - absorbed) / Mathf.Max(1f - absorbed, 0.05f));
    }

    /// <summary>A channel starts where the discharge (drainage area times climate, plus inflow) reaches this.</summary>
    public float channelDischarge = 1500f;

    /// <summary>
    /// Hydraulic geometry: width = this × √discharge (cells), depth = <see cref="depthPerDischarge"/> × discharge^<see cref="depthExponent"/>
    /// (metres). A world river's inflow is the discharge that gives vanilla's width for its class (compressed to map
    /// scale by <see cref="RiverNetworkSettings.worldRiverWidth"/>), so vanilla's class scales the river, nothing else.
    /// </summary>
    public float widthPerSqrtDischarge = 0.04f;
    public float minWidth = 1f;

    /// <summary>
    /// On a tile without a world river the map's own channels stay below a creek: their discharge is scaled so the
    /// largest is at most this wide (vanilla's creek is 4 cells, 3 at map scale).
    /// </summary>
    public float maxWidthWithoutWorldRiver = 2.5f;
    public float depthPerDischarge = 0.045f;
    public float depthExponent = 0.3f;
    public float minDepth = 0.15f;

    /// <summary>
    /// Stream power scour: depth is multiplied by (slope / the map's median channel slope)^this, within the range, so
    /// steep reaches scour deep threads and flat reaches shoal.
    /// </summary>
    public float scourExponent = 0.25f;
    public FloatRange scourRange = new FloatRange(0.7f, 1.4f);

    /// <summary>
    /// Noise, as vanilla's river: width varies along the channel, depth in patches (vanilla's "shallowizer"), and the
    /// bank line cell by cell. Amplitudes are shares; wavelengths in cells.
    /// </summary>
    public float widthNoise = 0.15f;
    public float widthNoiseWavelength = 40f;
    public float depthNoise = 0.35f;

    /// <summary>
    /// Shoals (vanilla's "shallowizer"): where a second, slower noise passes the threshold the bed rises toward the
    /// surface, leaving this share of the depth, so wide deep rivers break into deep pools and shallow fords. Only in
    /// channels at least <see cref="minShoalWidth"/> cells wide; wavelength in cells.
    /// </summary>
    public float shoalShare = 0.2f;
    public float shoalThreshold = 0.05f;
    public float shoalWavelength = 26f;
    public float minShoalWidth = 5f;
    public float depthNoiseWavelength = 18f;
    public float bankNoise = 0.2f;
    public float bankNoiseWavelength = 6f;

    /// <summary>Steps up and down a channel over which its flow direction is averaged (D8 steps are 45 degrees apart).</summary>
    public int flowSmoothingSteps = 3;

    /// <summary>
    /// A world river rising on the map (headwater) is fed by springs over this share of the water regime's centre
    /// hollow (<see cref="WaterRegimeDef.centreRadiusShare"/>), so its source is a lake rather than a point.
    /// </summary>
    public float sourceRadiusShare = 0.5f;

    /// <summary>
    /// Relief (metres) left on ground a delta flattens: low swells and swales the distributaries divide around.
    /// Wavelength in cells.
    /// </summary>
    public float deltaApronRelief = 1.5f;
    public float deltaApronWavelength = 24f;

    /// <summary>
    /// Bars (deposition): where a channel's stream power per unit width falls below its average over the reach
    /// upstream (the channel widens or flattens), sediment settles. Share of the drop, times the regimes' deposition
    /// nudge and <see cref="deposition"/>, from <see cref="barThreshold"/> up builds a bar in mid-channel: elongated
    /// along the flow (<see cref="barLengthWidths"/> and <see cref="barWidthShare"/> of the channel), outline warped
    /// by noise, crest from the bed (at the threshold) to <see cref="barRise"/> above the water (at the full drop):
    /// high bars are islands, low ones fords. Only in channels at least <see cref="minBarChannelWidth"/> wide, with
    /// bars at least one bar length apart. The upstream average runs over <see cref="powerReachCells"/> cells.
    /// </summary>
    public float deposition = 1f;
    public float barThreshold = 0.35f;

    /// <summary>The in-channel deposit per width at this quantile of the map's channel cells counts as a typical bar-forming deposit.</summary>
    public float barReferenceShare = 0.75f;
    public float barLengthWidths = 2f;
    public float barWidthShare = 0.5f;
    public float barRise = 1.2f;
    public float minBarChannelWidth = 6f;
    public float powerReachCells = 12f;

    /// <summary>A headwater's springs pool where the filled surface lies more than this (metres) above the ground.</summary>
    public float springLakeDepth = 0.1f;

    /// <summary>Ground more than this (metres) above a channel's water surface is not cut into the channel's width.</summary>
    public float maxBankCut = 0.3f;

    /// <summary>
    /// Flow spreading (step 3): how readily water divides where the ground falls away two ways. Higher is more
    /// single-thread; regimes divide it by their spread nudge (braided reaches, deltas and wetlands spread more).
    /// </summary>
    public float spreadExponent = 6f;

    /// <summary>
    /// Confinement field for the channel regimes: the valley floor is ground no higher than this (metres) above the
    /// water, measured across the channel up to this many channel widths each side.
    /// </summary>
    public float valleyFloorHeight = 1.5f;
    public float maxConfinementWidths = 8f;

    /// <summary>
    /// A world river takes the course the erosion model gave it: routing sees a trench this deep (metres) and wide (cells, each side)
    /// along it. On steep ground the valley is already there and this changes nothing; on flat ground, where micro-relief would
    /// steer the river along the map edge or into a hollow, it keeps the river on the way the model drained it.
    /// </summary>
    public float worldCourseTrench = 0.5f;
    public float worldCourseTrenchWidth = 3f;

    /// <summary>On a coast tile the map edge drains only within this many cells of the sea.</summary>
    public float coastEdgeDrain = 6f;

    /// <summary>The map edge within this many cells of where water enters does not drain (the land beyond slopes in).</summary>
    public float entryGuardCells = 15f;

    /// <summary>
    /// Micro-relief (metres, wavelength in cells) added to the ground before routing water: the same on every call, so
    /// the drainage a map is planned with is the drainage it is built with.
    /// </summary>
    public float flatNoise = 0.08f;
    public float flatNoiseWavelength = 9f;
    public int flatNoiseSeed = 4711;

    /// <summary>Sediment budget and floodplain deposits (<see cref="Sediment"/>).</summary>
    public SedimentSettings sediment = new SedimentSettings();

    /// <summary>Meander planform and oxbows (<see cref="Meanders"/>).</summary>
    public MeanderSettings meander = new MeanderSettings();

    /// <summary>Gradient (metres per cell) across filled hollows, so water crosses them toward the spill point.</summary>
    public float fillGradient = 1e-3f;
}

/// <summary>
/// The map's channels, from the landform's own drainage (Docs/WATER-AUDIT.md, reset steps 1 and 2). Water runs down
/// the steepest descent of the depression-filled landform; a channel forms wherever the discharge it gathers passes a
/// threshold, so channels follow the valleys the erosion model cut and bend with the terrain's own relief. Water
/// entering from beyond the map (world rivers, the region's channels) adds its discharge where it enters. Width and
/// depth follow discharge (hydraulic geometry), depth also stream power, each roughened by low-frequency noise as
/// vanilla does. The water surface is the filled landform, which never rises downstream (rule W1); where a channel
/// crosses a hollow deeper than itself the lake model fills the hollow instead.
/// </summary>
public static class TerrainChannels
{
    /// <summary>Lowers <paramref name="height"/> into channel beds and returns the channels with their per-cell water.</summary>
    /// <param name="courses">The erosion model's channels: where water enters from beyond the map, and the world river's class.</param>
    /// <param name="riverCentre">Where world rivers meet (map cells); a world river rising on the map (headwater) rises here.</param>
    /// <param name="climate">Runoff per cell, 0..1 (the map's climate wetness).</param>
    public static RiverSystem Build(float[] height, int sizeX, int sizeZ, List<ChannelCourse> courses, Vector2 riverCentre,
        float climate, string biome, float[] rockStrength, WaterRegimeDef water, TerrainChannelSettings p, SimpleCurve worldRiverWidth,
        bool[] sea = null, List<WorldContext.RiverInflow> worldInflows = null)
    {
        int n = sizeX * sizeZ;
        var system = new RiverSystem
        {
            Regime = water, WaterLevel = new float[n], Flow = new Vector2[n], ChannelOf = new int[n], Still = new bool[n],
            Seep = new bool[n],
        };
        for (int i = 0; i < n; i++)
        {
            system.WaterLevel[i] = float.NaN;
            system.ChannelOf[i] = -1;
        }

        // Only what the ground cannot absorb runs off (a trickle is kept so drainage still orders the cells).
        float runoff = Mathf.Max(p.RunoffOf(climate, biome), 0.01f);
        Drain(height, sizeX, sizeZ, courses, riverCentre, runoff, water, p, worldRiverWidth, out float[] filled, out int[] receiver,
            out float[] inflow, out bool[] worldSource, out float vanillaWidth, out System.Func<int, bool> isOutlet, worldInflows, sea);

        // Two passes: the first routes along steepest descent only and reads each channel cell's regime nudges; the
        // second lets water divide where those nudges spread it (splits emerge, step 3), then shapes the result.
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
        int carvedMeanders = 0, oxbows = 0, deltaCells = 0;
        for (int pass = 0; pass < 3; pass++)
        {
            discharge = Route(filled, receiver, inflow, worldSource, runoff, threshold, spread, mapSpread, sizeX, sizeZ, p,
                out world, out secondary, out branchFrom, out splits);

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
            // On a tile the world gives no river, the world says nothing here carries more than a creek: the map's own
            // discharge is scaled so its largest channel stays below a creek, keeping every channel's share, so
            // streams still visibly add up where they join.
            float scale = 1f;
            if (vanillaWidth <= 0f)
            {
                float largest = 0f;
                foreach (int c in cells)
                    largest = Mathf.Max(largest, discharge[c]);
                scale = Mathf.Min(1f, Mathf.Pow(p.maxWidthWithoutWorldRiver / p.widthPerSqrtDischarge, 2f) / Mathf.Max(largest, 1f));
            }
            dischargeScale = scale;
            bankAmplitude = Shape(cells, height, filled, rockStrength, receiver, discharge, scale, direction, sizeX, sizeZ, water, p,
                width, depth, spread, moving = new bool[n], power, deposition, regimes, sinuosity, valley);
            // Once water runs as a channel it keeps running downstream (cells are ordered highest first).
            foreach (int c in cells)
            {
                if (moving[c] && receiver[c] >= 0)
                    moving[receiver[c]] = true;
            }
            if (pass == 1)
                deltaCells = BuildDeltas(height, filled, cells, receiver, discharge, dischargeScale, width, inflow, rockStrength, depth, sea, spread,
                    isOutlet, sizeX, sizeZ, p, system, ref receiver);
            if (pass == 0 && p.meander.enabled)
            {
                // Bends: draw the sinuosity the regimes ask for as a trench along a meandering line, then route again.
                carvedMeanders = Meanders.Apply(height, filled, cells, receiver, main, width, sinuosity, valley, sizeX, sizeZ, p.meander, out oxbows);
                if (carvedMeanders > 0)
                {
                    GridMath.FillDepressions(filled, sizeX, sizeZ, isOutlet, p.fillGradient, includeEdges: false);
                    receiver = GridMath.Receivers(filled, sizeX, sizeZ);
                    for (int i = 0; i < n; i++)
                    {
                        if (isOutlet(i))
                            receiver[i] = -1;
                    }
                }
            }
        }

        List<RiverPlan> plans = Trace(cells, receiver, main, branchFrom, discharge, inflow, world, width, sizeX, sizeZ, runoff, vanillaWidth, out int[] planOf);
        var natural = (float[])height.Clone();
        int inHollows = Paint(height, filled, cells, width, depth, moving, bankAmplitude, direction, planOf, sizeX, sizeZ, p, system);
        system.Channels = plans;
        int springLake = SpringLake(natural, filled, inflow, worldSource, planOf, sizeX, sizeZ, p, system);
        // Base level: the sea, lakes and hollows the water stands in. Rivers drop their load there.
        var baseLevel = new bool[n];
        for (int i = 0; i < n; i++)
            baseLevel[i] = (sea != null && sea[i]) || system.Still[i] || filled[i] - natural[i] > Mathf.Max(depth[i], p.minDepth) + 0.05f;
        // The sediment budget: in-channel deposits make bars (also below confluences, where a tributary brings more than the
        // joined flow can carry), and the floodplain fields.
        Sediment.Deposits(cells, receiver, discharge, dischargeScale, filled, width, inflow, rockStrength, baseLevel, sizeX, p.sediment,
            out float[] deposit, out float[] depositGrain, out float deposited, out float atBase, out float eroded);
        int bars = Bars(height, cells, main, width, depth, moving, deposit, deposition, direction, sizeX, sizeZ, p, system, out int islands);
        string sedimentSummary = Sediment.Layers(cells, receiver, deposit, depositGrain, width, filled, natural, sizeX, sizeZ, p.sediment, system,
            deposited, atBase, eroded);
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
                    $"{painted} water cells, {bars} bars ({islands} islands), {System.Array.FindAll(system.Seep, v => v).Length} seep cells (runoff {runoff:0.##}, own discharge x{dischargeScale:0.###}){(springLake > 0 ? $" ({springLake} in the lake a source rises in)" : "")}, widest {maxWidth:F1} cells, deepest {maxDepth:F2} m, climate {climate:F2}; {carvedMeanders} cells carved for meanders, {oxbows} oxbows, {deltaCells} cells of delta land, {delta}; {sedimentSummary}; " +
                    $"regimes by channel cell [{string.Join(", ", regimes.OrderByDescending(r => r.Value).Select(r => $"{r.Key} {100 * r.Value / cells.Count}%"))}].");
        return system;
    }

    /// <summary>
    /// The world river's own network (Docs/SEDIMENT-PLAN.md): channels carrying a real share of its water. Counts how often it
    /// branches and rejoins itself and how many of its branches reach the sea, the measure of a delta or a braided reach (runoff
    /// creeks reaching the sea beside it do not count). Branch and rejoin points closer than a few cells count once.
    /// </summary>
    private static string MainRiver(List<int> cells, int[] receiver, int[] secondary, bool[] world, float[] discharge, float[] inflow, bool[] worldSource,
        bool[] sea, RiverSystem system, int sizeX, int sizeZ, TerrainChannelSettings p)
    {
        float q = 0f;
        for (int i = 0; i < inflow.Length; i++)
        {
            if (worldSource[i])
                q += inflow[i];
        }
        if (q <= 0f)
            return "no world river";
        int n = receiver.Length;
        var main = new bool[n];
        foreach (int c in cells)
            main[c] = world[c] && discharge[c] >= p.sediment.mainRiverShare * q;
        var donors = new int[n];
        var branches = new List<Vector2>();
        foreach (int c in cells)
        {
            if (!main[c])
                continue;
            int r = receiver[c], s = secondary[c];
            if (r >= 0 && main[r])
                donors[r]++;
            if (s >= 0 && main[s])
            {
                donors[s]++;
                if (r >= 0 && main[r])
                    AddPoint(branches, c, sizeX);
            }
        }
        var rejoins = new List<Vector2>();
        foreach (int c in cells)
        {
            if (main[c] && donors[c] >= 2)
                AddPoint(rejoins, c, sizeX);
        }
        int mouths = sea == null ? 0 : Mouths(sea, system, sizeX, sizeZ, i => main[i] || (system.ChannelOf[i] >= 0 && Near(main, i, sizeX, sizeZ)));
        int mainCells = 0;
        foreach (bool m in main)
            if (m) mainCells++;
        int worldCells = 0;
        float biggest = 0f;
        foreach (int c in cells)
        {
            if (world[c]) { worldCells++; biggest = Mathf.Max(biggest, discharge[c]); }
        }
        return $"main river ({mainCells} cells; {worldCells} carry world water, largest {biggest / q:P0} of the {q:0} entering) branches {branches.Count} times, rejoins {rejoins.Count} times, {mouths} of its branches reach the sea";
    }

    private static void AddPoint(List<Vector2> points, int c, int sizeX)
    {
        var v = new Vector2(c % sizeX, c / sizeX);
        if (!points.Exists(o => (o - v).sqrMagnitude < 36f))
            points.Add(v);
    }

    private static bool Near(bool[] mask, int i, int sizeX, int sizeZ)
    {
        int x = i % sizeX, z = i / sizeX;
        for (int dz = -3; dz <= 3; dz++)
        {
            for (int dx = -3; dx <= 3; dx++)
            {
                int nx = x + dx, nz = z + dz;
                if (nx >= 0 && nz >= 0 && nx < sizeX && nz < sizeZ && mask[nz * sizeX + nx])
                    return true;
            }
        }
        return false;
    }

    /// <summary>Distinct channels reaching the sea: groups of moving water cells (of those <paramref name="counted"/>), apart from each other, that touch it.</summary>
    private static int Mouths(bool[] sea, RiverSystem system, int sizeX, int sizeZ, System.Func<int, bool> counted)
    {
        int n = sea.Length;
        bool Mouth(int i)
        {
            if (sea[i] || system.ChannelOf[i] < 0 || system.Still[i] || !counted(i))
                return false;
            int x = i % sizeX, z = i / sizeX;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx >= 0 && nz >= 0 && nx < sizeX && nz < sizeZ && sea[nz * sizeX + nx])
                        return true;
                }
            }
            return false;
        }
        return GridMath.LabelRegions(Mouth, sizeX, sizeZ, out _);
    }

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
        Sediment.Deposits(cells, receiver, discharge, dischargeScale, filled, width, inflow, rockStrength, baseLevel, sizeX, p.sediment,
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
        GridMath.FillDepressions(filled, sizeX, sizeZ, isOutlet, p.fillGradient, includeEdges: false);
        receiverOut = GridMath.Receivers(filled, sizeX, sizeZ);
        for (int i = 0; i < n; i++)
        {
            if (isOutlet(i))
                receiverOut[i] = -1;
        }
        return land;
    }

    /// <summary>
    /// The drainage the channels follow: water entering the map, the depression-filled landform and each cell's
    /// steepest-descent receiver. The map edge drains, except where water enters: there the land beyond slopes in.
    /// Water reaching a draining edge leaves the map there rather than running along the edge.
    /// </summary>
    private static void Drain(float[] height, int sizeX, int sizeZ, List<ChannelCourse> courses, Vector2 riverCentre, float runoff,
        WaterRegimeDef water, TerrainChannelSettings p, SimpleCurve worldRiverWidth, out float[] filled, out int[] receiver,
        out float[] inflow, out bool[] worldSource, out float vanillaWidth, out System.Func<int, bool> outlet,
        List<WorldContext.RiverInflow> worldInflows = null, bool[] sea = null)
    {
        int n = sizeX * sizeZ;
        float sourceRadius = (water?.centreRadiusShare ?? 0f) * Mathf.Max(sizeX, sizeZ) * p.sourceRadiusShare;
        inflow = Inflow(courses, sizeX, sizeZ, riverCentre, sourceRadius, runoff, water?.riverInflow ?? true, p, worldRiverWidth,
            out worldSource, out vanillaWidth, worldInflows);
        var entries = new List<Vector2>();
        for (int i = 0; i < n; i++)
        {
            int x = i % sizeX, z = i / sizeX;
            // A world river's entry cell lies a cell or two inside the edge (its course is cut to the map), so near the edge counts.
            if (inflow[i] > 0f && (x <= 2 || z <= 2 || x >= sizeX - 3 || z >= sizeZ - 3))
                entries.Add(new Vector2(x, z));
        }
        // On a coast the sea is the base level: the map edge drains only near it (the land beyond the other edges rises inland),
        // so rivers run on into the sea rather than leaving through a land edge beside it.
        float[] toSea = sea != null ? GridMath.DistanceTo(i => sea[i], sizeX, sizeZ) : null;
        bool IsOutlet(int i)
        {
            int x = i % sizeX, z = i / sizeX;
            if (x != 0 && z != 0 && x != sizeX - 1 && z != sizeZ - 1)
                return false;
            if (toSea != null && toSea[i] > p.coastEdgeDrain)
                return false;
            var v = new Vector2(x, z);
            return entries.TrueForAll(e => (e - v).sqrMagnitude > p.entryGuardCells * p.entryGuardCells);
        }
        // Flats keep a little micro-relief, so water wanders across them instead of running straight or at 45 degrees;
        // on slopes it is far below the fall of the ground and changes nothing.
        filled = (float[])height.Clone();
        var microRelief = new Perlin(1.0 / p.flatNoiseWavelength, 2.0, 0.5, 3, p.flatNoiseSeed, QualityMode.Medium);
        for (int i = 0; i < n; i++)
            filled[i] += p.flatNoise * (float)microRelief.GetValue(i % sizeX, 0.0, i / sizeX);
        foreach (ChannelCourse course in courses ?? new List<ChannelCourse>())
        {
            if (course.VanillaWidth > 0f && (water?.riverInflow ?? true))
                Trench(filled, sizeX, sizeZ, course, p);
        }
        outlet = IsOutlet;
        GridMath.FillDepressions(filled, sizeX, sizeZ, IsOutlet, p.fillGradient, includeEdges: false);
        receiver = GridMath.Receivers(filled, sizeX, sizeZ);
        for (int i = 0; i < n; i++)
        {
            if (IsOutlet(i))
                receiver[i] = -1;
        }
    }

    /// <summary>Lowers the ground along a course (see <see cref="TerrainChannelSettings.worldCourseTrench"/>), deepest on the line.</summary>
    private static void Trench(float[] filled, int sizeX, int sizeZ, ChannelCourse course, TerrainChannelSettings p)
    {
        int reach = Mathf.CeilToInt(p.worldCourseTrenchWidth);
        var deepest = new float[filled.Length];
        foreach (Vector2 v in course.Points)
        {
            int cx = Mathf.RoundToInt(v.x - 0.5f), cz = Mathf.RoundToInt(v.y - 0.5f);
            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                        continue;
                    float share = 1f - Mathf.Sqrt(dx * dx + dz * dz) / (p.worldCourseTrenchWidth + 0.01f);
                    if (share > 0f)
                        deepest[z * sizeX + x] = Mathf.Max(deepest[z * sizeX + x], share * share);
                }
            }
        }
        for (int i = 0; i < filled.Length; i++)
            filled[i] -= p.worldCourseTrench * deepest[i];
    }

    /// <summary>
    /// The cell of the world river's course (steepest descent from where it enters) nearest to <paramref name="point"/>,
    /// or <paramref name="point"/> itself if no world river enters. Mutator structures that sit in the river (an
    /// island's knob) are placed there.
    /// </summary>
    public static Vector2 NearestOnWorldRiver(float[] height, int sizeX, int sizeZ, List<ChannelCourse> courses, Vector2 point,
        WaterRegimeDef water, TerrainChannelSettings p, SimpleCurve worldRiverWidth)
    {
        Drain(height, sizeX, sizeZ, courses, point, 1f, water, p, worldRiverWidth, out _, out int[] receiver, out _, out bool[] worldSource, out _, out _);
        Vector2 best = point;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < worldSource.Length; i++)
        {
            if (!worldSource[i])
                continue;
            for (int c = i, steps = 0; c >= 0 && steps < worldSource.Length; c = receiver[c], steps++)
            {
                var v = new Vector2(c % sizeX + 0.5f, c / sizeX + 0.5f);
                float d = (v - point).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = v;
                }
            }
        }
        return best;
    }

    /// <summary>
    /// Discharge: each cell's own runoff plus inflow, gathered downstream over the filled landform, highest cells
    /// first (both receivers of a cell lie lower, so its donors are done). Water follows steepest descent, but where
    /// the ground also falls away steeply a second way (at least 90 degrees apart) it divides as multiple-flow routing
    /// does: the second way takes slope2^e / (slope1^e + slope2^e), with e the spreading exponent divided by the
    /// cell's spread nudge. A division is kept only where the second share is itself a channel; otherwise all water
    /// stays on the main way, so splits form only where a lot of water meets ground falling away two ways.
    /// </summary>
    private static float[] Route(float[] filled, int[] receiver, float[] inflow, bool[] worldSource, float runoff, float threshold,
        float[] spread, float mapSpread, int sizeX, int sizeZ, TerrainChannelSettings p,
        out bool[] world, out int[] secondary, out int[] branchFrom, out int splits)
    {
        int n = filled.Length;
        var order = new int[n];
        for (int i = 0; i < n; i++)
            order[i] = i;
        System.Array.Sort(order, (a, b) => filled[b].CompareTo(filled[a]));
        var discharge = new float[n];
        world = new bool[n];
        secondary = new int[n];
        branchFrom = new int[n];
        for (int i = 0; i < n; i++)
        {
            discharge[i] = runoff + inflow[i];
            world[i] = worldSource[i];
            secondary[i] = branchFrom[i] = -1;
        }
        splits = 0;
        foreach (int c in order)
        {
            int r = receiver[c];
            if (r < 0)
                continue;
            float share = 0f;
            int second = -1;
            if (discharge[c] * 0.5f >= threshold)
            {
                float factor = spread != null && spread[c] > 0f ? spread[c] : mapSpread;
                second = SecondWay(filled, c, r, sizeX, sizeZ, out float main, out float other);
                if (second >= 0)
                {
                    float e = p.spreadExponent / Mathf.Max(factor, 0.1f);
                    float a = Mathf.Pow(main, e), b = Mathf.Pow(other, e);
                    share = b / (a + b);
                    if (discharge[c] * share < threshold)
                        second = -1;
                }
            }
            if (second >= 0)
            {
                discharge[second] += discharge[c] * share;
                world[second] |= world[c];
                secondary[c] = second;
                if (branchFrom[second] < 0 || discharge[c] > discharge[branchFrom[second]])
                    branchFrom[second] = c;
                splits++;
            }
            discharge[r] += discharge[c] * (1f - share);
            world[r] |= world[c];
        }
        return discharge;
    }

    /// <summary>The steepest downhill neighbour at least 90 degrees from the main way, with both slopes; -1 if none.</summary>
    private static int SecondWay(float[] h, int c, int primary, int sizeX, int sizeZ, out float mainSlope, out float slope)
    {
        int cx = c % sizeX, cz = c / sizeX;
        var toPrimary = new Vector2(primary % sizeX - cx, primary / sizeX - cz);
        mainSlope = (h[c] - h[primary]) / toPrimary.magnitude;
        slope = 0f;
        int best = -1;
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if ((dx == 0 && dz == 0) || x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                    continue;
                if (Vector2.Dot(new Vector2(dx, dz), toPrimary) > 0f)
                    continue;
                int nb = z * sizeX + x;
                float s = (h[c] - h[nb]) / Mathf.Sqrt(dx * dx + dz * dz);
                if (s > slope)
                {
                    slope = s;
                    best = nb;
                }
            }
        }
        return best;
    }

    /// <summary>
    /// Discharge entering the map: world rivers with the discharge that gives their vanilla width, the region's other
    /// channels with the runoff of the area they drain. On a tile the world gives no river, nothing entering is larger
    /// than a creek, as in vanilla. A world river that does not enter (headwater) rises at the river centre.
    /// </summary>
    private static float[] Inflow(List<ChannelCourse> courses, int sizeX, int sizeZ, Vector2 riverCentre, float sourceRadius, float runoff,
        bool riverEnters, TerrainChannelSettings p, SimpleCurve worldRiverWidth, out bool[] worldSource, out float vanillaWidth,
        List<WorldContext.RiverInflow> worldInflows = null)
    {
        int n = sizeX * sizeZ;
        var inflow = new float[n];
        worldSource = new bool[n];
        vanillaWidth = 0f;
        float DischargeFor(float width) => Mathf.Pow(worldRiverWidth.Evaluate(width) / p.widthPerSqrtDischarge, 2f);
        float creek = DischargeFor(4f);
        bool anyWorldRiver = courses != null && courses.Exists(c => c.VanillaWidth > 0f);
        bool worldEnters = false;
        foreach (ChannelCourse course in courses ?? new List<ChannelCourse>())
        {
            vanillaWidth = Mathf.Max(vanillaWidth, course.VanillaWidth);
            if (course.EntryArea <= 0f)
                continue;
            int first = course.Points.FindIndex(v => v.x >= 0f && v.y >= 0f && v.x < sizeX && v.y < sizeZ);
            if (first < 0)
                continue;
            int cell = RiverChannels.Index(course.Points[first], sizeX, sizeZ);
            if (course.VanillaWidth > 0f)
            {
                // At a headwater the world river does not come from beyond the map: it rises here.
                if (!riverEnters)
                    continue;
                inflow[cell] += DischargeFor(course.VanillaWidth);
                worldSource[cell] = worldEnters = true;
            }
            else
                inflow[cell] += anyWorldRiver && riverEnters ? course.EntryArea * runoff : Mathf.Min(course.EntryArea * runoff, creek);
        }
        if (anyWorldRiver && !worldEnters && riverEnters && worldInflows != null && worldInflows.Count > 0)
        {
            // The erosion model's river does not cross the map edge (its course starts inside): the world says the river comes in,
            // so it enters at the edge facing the neighbour it comes from, as vanilla lays it, and finds its own way from there.
            foreach (WorldContext.RiverInflow from in worldInflows)
            {
                Vector2 edge = WaterTerrainNudges.EdgeToward(riverCentre, from.From, sizeX, sizeZ);
                int cell = RiverChannels.Index(edge, sizeX, sizeZ);
                inflow[cell] += DischargeFor(from.Width);
                worldSource[cell] = worldEnters = true;
            }
        }
        if (anyWorldRiver && !worldEnters)
        {
            // Springs feeding the river's source: its discharge rises over the hollow at the river centre (the lake
            // model fills it and the river leaves at its spill point), or at the centre cell if there is no hollow.
            var springs = new List<int>();
            for (int i = 0; i < n && sourceRadius > 0f; i++)
            {
                if ((new Vector2(i % sizeX + 0.5f, i / sizeX + 0.5f) - riverCentre).sqrMagnitude <= sourceRadius * sourceRadius)
                    springs.Add(i);
            }
            if (springs.Count == 0)
                springs.Add(RiverChannels.Index(riverCentre, sizeX, sizeZ));
            float each = DischargeFor(vanillaWidth) / springs.Count;
            foreach (int cell in springs)
            {
                inflow[cell] += each;
                worldSource[cell] = true;
            }
        }
        return inflow;
    }

    /// <summary>
    /// Width and depth of each channel cell from its discharge, stream power and noise, nudged by the channel regimes
    /// its data matches (Docs/WATER-AUDIT.md, step 4). Fields: Discharge (log10), Slope (water drop per cell over a few
    /// cells), BankStrength (rock strength 0..1), Confinement (valley floor width over channel width). Returns the bank
    /// noise amplitude per cell and counts the dominant regime per cell for the log.
    /// </summary>
    private static float[] Shape(List<int> cells, float[] natural, float[] filled, float[] rockStrength, int[] receiver, float[] discharge,
        float dischargeScale,
        Vector2[] direction, int sizeX, int sizeZ, WaterRegimeDef water, TerrainChannelSettings p, float[] width, float[] depth,
        float[] spread, bool[] moving, float[] power, float[] deposition, Dictionary<string, int> regimes, float[] sinuosity, float[] valley)
    {
        // Slope over a few cells downstream (single D8 steps across filled flats are nearly zero).
        var slope = new float[cells.Count];
        for (int k = 0; k < cells.Count; k++)
        {
            int c = cells[k], d = c;
            float run = 0f;
            for (int s = 0; s < 5 && receiver[d] >= 0; s++)
            {
                int r = receiver[d];
                run += (r % sizeX != d % sizeX && r / sizeX != d / sizeX) ? 1.414f : 1f;
                d = r;
            }
            slope[k] = run > 0f ? (filled[c] - filled[d]) / run : 0f;
        }
        var sorted = (float[])slope.Clone();
        System.Array.Sort(sorted);
        float reference = Mathf.Max(sorted[sorted.Length / 2], 1e-4f);

        var bankAmplitude = new float[natural.Length];
        var widthNoise = new Perlin(1.0 / p.widthNoiseWavelength, 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);
        var depthNoise = new Perlin(1.0 / p.depthNoiseWavelength, 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);
        var fields = new Dictionary<string, float>();
        for (int k = 0; k < cells.Count; k++)
        {
            int c = cells[k], x = c % sizeX, z = c / sizeX;
            float q = discharge[c] * dischargeScale;
            float baseWidth = Mathf.Max(p.minWidth, p.widthPerSqrtDischarge * Mathf.Sqrt(q));
            fields["Discharge"] = Mathf.Log10(q);
            fields["Slope"] = slope[k];
            fields["BankStrength"] = rockStrength != null ? rockStrength[c] : 0.5f;
            float floor = ValleyFloor(natural, filled[c], c, direction[c], baseWidth, sizeX, sizeZ, p);
            fields["Confinement"] = floor / baseWidth;
            valley[c] = floor;
            ChannelNudges nudge = WaterRegimes.Nudges(fields, water, out ChannelRegimeDef dominant);
            string key = dominant?.defName ?? "none";
            regimes[key] = regimes.TryGetValue(key, out int count) ? count + 1 : 1;

            // Moving water by the width without noise, so a channel near the limit does not flicker between the two.
            moving[c] = baseWidth * nudge.width >= p.minMovingWidth;
            width[c] = Mathf.Max(p.minWidth, baseWidth * nudge.width
                                             * (1f + p.widthNoise * nudge.widthNoise * (float)widthNoise.GetValue(x, 0.0, z)));
            float scour = p.scourRange.ClampToRange(Mathf.Pow(Mathf.Max(slope[k], 1e-6f) / reference, p.scourExponent));
            depth[c] = Mathf.Max(p.minDepth, p.depthPerDischarge * Mathf.Pow(q, p.depthExponent) * scour * nudge.depth
                                             * (1f + Mathf.Clamp(p.depthNoise * nudge.depthNoise, 0f, 0.9f) * (float)depthNoise.GetValue(x, 0.0, z)));
            bankAmplitude[c] = Mathf.Clamp(p.bankNoise * nudge.bankNoise, 0f, 0.8f);
            spread[c] = nudge.spread;
            sinuosity[c] = nudge.sinuosity;
            deposition[c] = nudge.deposition;
            // Stream power per unit width: the flow's capacity to carry sediment.
            power[c] = q * Mathf.Max(slope[k], 0f) / Mathf.Max(width[c], 1f);
        }
        return bankAmplitude;
    }

    /// <summary>
    /// Width (cells) of the valley floor across a channel cell: ground no higher than the water surface plus
    /// <see cref="TerrainChannelSettings.valleyFloorHeight"/>, measured both ways across the flow, up to
    /// <see cref="TerrainChannelSettings.maxConfinementWidths"/> channel widths each side.
    /// </summary>
    private static float ValleyFloor(float[] natural, float level, int c, Vector2 direction, float width, int sizeX, int sizeZ, TerrainChannelSettings p)
    {
        var across = new Vector2(-direction.y, direction.x);
        if (across == Vector2.zero)
            return width;
        float limit = Mathf.Max(width, 2f) * p.maxConfinementWidths, floor = 1f;
        var start = new Vector2(c % sizeX + 0.5f, c / sizeX + 0.5f);
        foreach (float sign in new[] { -1f, 1f })
        {
            for (float t = 1f; t <= limit; t += 1f)
            {
                Vector2 v = start + across * (sign * t);
                int x = (int)v.x, z = (int)v.y;
                if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ || natural[z * sizeX + x] > level + p.valleyFloorHeight)
                    break;
                floor += 1f;
            }
        }
        return floor;
    }

    /// <summary>Downstream direction of each channel cell, averaged over a few steps up and down the channel.</summary>
    private static Vector2[] Directions(List<int> cells, int[] receiver, int[] main, int sizeX, int steps)
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

    /// <summary>
    /// Channels as courses: each runs from its head (or where it enters the map) down through every junction where it
    /// brings the most water, and ends where it joins a larger one or leaves the map. Channels carrying a world river's
    /// water take its vanilla width; the one leaving the map is the world river.
    /// </summary>
    private static List<RiverPlan> Trace(List<int> cells, int[] receiver, int[] main, int[] branchFrom, float[] discharge, float[] inflow, bool[] world, float[] width,
        int sizeX, int sizeZ, float runoff, float vanillaWidth, out int[] planOf)
    {
        int n = receiver.Length;
        planOf = new int[n];
        for (int i = 0; i < n; i++)
            planOf[i] = -1;
        var plans = new List<RiverPlan>();
        var ends = new List<int>();
        var parents = new List<int>();
        Vector2 Centre(int c) => new Vector2(c % sizeX + 0.5f, c / sizeX + 0.5f);
        foreach (int c in cells)
        {
            if (planOf[c] < 0)
            {
                // A head: no channel flows into this cell as its main donor.
                planOf[c] = plans.Count;
                var plan = new RiverPlan();
                // A branch starts where it divided from its channel.
                if (branchFrom[c] >= 0)
                {
                    plan.Main.Add(Centre(branchFrom[c]));
                    plan.MainWidth.Add(Mathf.Max(1f, Mathf.Round(width[c])));
                }
                plans.Add(plan);
                ends.Add(c);
                parents.Add(-1);
            }
            int k = planOf[c];
            // Water entering from beyond the map, as the drainage area that would bring it (for drainage and wetness).
            plans[k].EntryArea += inflow[c] / runoff;
            plans[k].Main.Add(Centre(c));
            plans[k].MainWidth.Add(Mathf.Max(1f, Mathf.Round(width[c])));
            plans[k].NominalWidth = width[c];
            ends[k] = c;
            if (world[c] && vanillaWidth > 0f)
                plans[k].VanillaWidth = vanillaWidth;
            int r = receiver[c];
            if (r >= 0 && main[r] == c)
                planOf[r] = k;
        }
        // Each channel that ends where a larger one passes joins it there.
        for (int k = 0; k < plans.Count; k++)
        {
            int r = receiver[ends[k]];
            if (r >= 0 && planOf[r] >= 0 && planOf[r] != k)
            {
                plans[k].Main.Add(Centre(r));
                plans[k].MainWidth.Add(plans[k].MainWidth[plans[k].MainWidth.Count - 1]);
                parents[k] = planOf[r];
            }
        }

        // The world river: the largest channel carrying world water that leaves the map.
        int worldRiver = -1;
        for (int k = 0; k < plans.Count; k++)
        {
            if (parents[k] < 0 && plans[k].VanillaWidth > 0f && (worldRiver < 0 || discharge[ends[k]] > discharge[ends[worldRiver]]))
                worldRiver = k;
        }
        if (worldRiver >= 0)
            plans[worldRiver].WorldRiver = true;

        // Larger channels first (the world river leads), parents re-indexed.
        var sorted = new List<int>();
        for (int k = 0; k < plans.Count; k++)
            sorted.Add(k);
        sorted.Sort((a, b) => a == worldRiver ? -1 : b == worldRiver ? 1 : discharge[ends[b]].CompareTo(discharge[ends[a]]));
        var index = new int[plans.Count];
        for (int i = 0; i < sorted.Count; i++)
            index[sorted[i]] = i;
        var result = new List<RiverPlan>();
        foreach (int k in sorted)
        {
            plans[k].Parent = parents[k] >= 0 ? index[parents[k]] : -1;
            result.Add(plans[k]);
        }
        for (int i = 0; i < n; i++)
        {
            if (planOf[i] >= 0)
                planOf[i] = index[planOf[i]];
        }
        return result;
    }

    /// <summary>
    /// Bars where the sediment budget deposits in the channel (<see cref="Sediment"/>): where the flow loses the power to carry
    /// its load (an easing slope, a widening, below a confluence whose tributary brings more than the joined flow carries) the bed
    /// rises into mid-channel bars; cells raised above the water become dry (islands). The channel regimes' deposition nudge
    /// scales how readily a deposit forms a bar. Returns the number of bars.
    /// </summary>
    private static int Bars(float[] height, List<int> cells, int[] main, float[] width, float[] depth, bool[] moving, float[] deposit,
        float[] deposition, Vector2[] direction, int sizeX, int sizeZ, TerrainChannelSettings p, RiverSystem system, out int islands)
    {
        int n = height.Length;
        islands = 0;
        // Deposit per channel width, against the map's typical in-channel deposit (the budget's units are relative).
        var perWidth = new List<float>();
        foreach (int c in cells)
        {
            if (moving[c] && deposit[c] > 0f && width[c] >= p.minBarChannelWidth)
                perWidth.Add(deposit[c] / width[c]);
        }
        if (perWidth.Count == 0)
            return 0;
        perWidth.Sort();
        float reference = Mathf.Max(perWidth[(int)(perWidth.Count * p.barReferenceShare)], 1e-6f);
        var candidates = new List<(int cell, float strength)>();
        foreach (int c in cells)
        {
            if (!moving[c] || width[c] < p.minBarChannelWidth || deposit[c] <= 0f || float.IsNaN(system.WaterLevel[c]))
                continue;
            float drop = Mathf.Clamp01(deposit[c] / width[c] / reference * 0.5f) * deposition[c] * p.deposition;
            if (drop >= p.barThreshold)
                candidates.Add((c, Mathf.Min(drop, 1f)));
        }
        candidates.Sort((a, b) => b.strength.CompareTo(a.strength));

        var placed = new List<(Vector2 at, float length)>();
        var warp = new Perlin(0.15, 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);
        foreach ((int c, float strength) in candidates)
        {
            var at = new Vector2(c % sizeX + 0.5f, c / sizeX + 0.5f);
            float length = p.barLengthWidths * width[c], half = p.barWidthShare * width[c] * 0.5f;
            if (placed.Exists(b => (b.at - at).magnitude < Mathf.Max(b.length, length)))
                continue;
            placed.Add((at, length));
            float level = system.WaterLevel[c];
            float bed = level - depth[c];
            float crest = bed + (depth[c] + p.barRise) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(p.barThreshold, 1f, strength));
            Vector2 along = direction[c] == Vector2.zero ? Vector2.right : direction[c], across = new Vector2(-along.y, along.x);
            int reach = Mathf.CeilToInt(length * 0.5f + 1f);
            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = c % sizeX + dx, z = c / sizeX + dz;
                    if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                        continue;
                    int i = z * sizeX + x;
                    if (float.IsNaN(system.WaterLevel[i]))
                        continue;
                    var v = new Vector2(dx, dz);
                    float a = Vector2.Dot(v, along) / (length * 0.5f);
                    // Teardrop: pointed upstream (narrowing toward a < 0), round downstream, as flow-shaped islands are.
                    float taper = a < 0f ? 1f + 1.2f * -a : 1f;
                    float b = Vector2.Dot(v, across) * taper / Mathf.Max(half, 0.5f);
                    float e = Mathf.Sqrt(a * a + b * b) * (1f + 0.3f * (float)warp.GetValue(x, 0.0, z));
                    if (e >= 1f)
                        continue;
                    float surface = Mathf.Lerp(crest, bed, e * e);
                    height[i] = Mathf.Max(height[i], surface);
                    if (height[i] >= level)
                    {
                        // Above the water: an island.
                        system.WaterLevel[i] = float.NaN;
                        system.ChannelOf[i] = -1;
                        system.Flow[i] = Vector2.zero;
                        islands++;
                    }
                }
            }
        }
        return placed.Count;
    }

    /// <summary>
    /// A river rising on the map (headwater) rises from springs; where they rise in a hollow, their water pools to the
    /// hollow's spill level before it flows on: every cell connected to a spring and lying below the filled surface
    /// becomes still water at that level, part of the river that leaves it. Returns the number of lake cells.
    /// </summary>
    private static int SpringLake(float[] natural, float[] filled, float[] inflow, bool[] worldSource, int[] planOf, int sizeX, int sizeZ,
        TerrainChannelSettings p, RiverSystem system)
    {
        int n = natural.Length;
        bool InHollow(int i) => filled[i] - natural[i] > p.springLakeDepth;
        var queue = new Queue<int>();
        var seen = new bool[n];
        int plan = -1;
        for (int i = 0; i < n; i++)
        {
            int x = i % sizeX, z = i / sizeX;
            bool edge = x == 0 || z == 0 || x == sizeX - 1 || z == sizeZ - 1;
            if (worldSource[i] && inflow[i] > 0f && !edge && InHollow(i))
            {
                seen[i] = true;
                queue.Enqueue(i);
            }
        }
        for (int i = 0; i < n && plan < 0; i++)
        {
            if (seen[i] && system.ChannelOf[i] >= 0)
                plan = system.ChannelOf[i];
        }
        if (plan < 0)
            plan = system.Channels.FindIndex(c => c.WorldRiver);
        int count = 0;
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            system.WaterLevel[c] = filled[c];
            system.Still[c] = true;
            system.Flow[c] = Vector2.zero;
            system.ChannelOf[c] = plan;
            count++;
            GridMath.ForEachNeighbour(c, sizeX, sizeZ, nb =>
            {
                if (!seen[nb] && InHollow(nb))
                {
                    seen[nb] = true;
                    queue.Enqueue(nb);
                }
            });
        }
        return count;
    }

    /// <summary>
    /// Lays the water around each channel's centre line: a disc of the channel's width, its edge roughened by bank
    /// noise, each cell taking the channel cell that gives it the deepest water. Depth falls from the centre to the
    /// bank; the bed is lowered to the water surface less that depth (ground is only ever lowered). Where the filled
    /// surface lies deeper above the ground than the channel's own depth the channel crosses a hollow: the lake model
    /// fills it.
    /// </summary>
    private static int Paint(float[] height, float[] filled, List<int> cells, float[] width, float[] depth, bool[] moving, float[] bankAmplitude, Vector2[] direction,
        int[] planOf, int sizeX, int sizeZ, TerrainChannelSettings p, RiverSystem system)
    {
        int n = height.Length;
        var best = new float[n];
        var owner = new int[n];
        var ownerT = new float[n];
        for (int i = 0; i < n; i++)
        {
            best[i] = 0f;
            owner[i] = -1;
            ownerT[i] = float.MaxValue;
        }
        int inHollows = 0;
        var bank = new Perlin(1.0 / p.bankNoiseWavelength, 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);
        var shoalNoise = new Perlin(1.0 / p.shoalWavelength, 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);
        foreach (int c in cells)
        {
            if (filled[c] - height[c] > depth[c])
            {
                inHollows++;
                continue;
            }
            if (!moving[c])
            {
                system.Seep[c] = true;
                continue;
            }
            int cx = c % sizeX, cz = c / sizeX;
            float half = Mathf.Max(width[c], p.minMovingWidth) * 0.5f;
            int reach = Mathf.CeilToInt(half * (1f + bankAmplitude[c]) + 0.5f);
            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                        continue;
                    float edge = half * (1f + bankAmplitude[c] * (float)bank.GetValue(x, 0.0, z)) + 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dz * dz);
                    if (r >= edge)
                        continue;
                    // Water spreads over ground near its surface, not through high ground: banks, knobs and valley
                    // sides higher than this stay dry, so the channel narrows there and splits around islands.
                    if (r > 0f && height[z * sizeX + x] > filled[c] + p.maxBankCut)
                        continue;
                    float t = r / edge;
                    float d = depth[c] * (1f - t * t);
                    // Shoals are sampled per cell, so their edges follow the noise, not the channel's cells.
                    if (width[c] >= p.minShoalWidth)
                        d *= Mathf.Lerp(1f, p.shoalShare, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(p.shoalThreshold, p.shoalThreshold + 0.25f, (float)shoalNoise.GetValue(x, 0.0, z))));
                    d = Mathf.Max(p.minDepth, d);
                    int i = z * sizeX + x;
                    // A cell takes the water of the channel cell it lies nearest to across the channel (distance over
                    // the channel's own edge): a centre-line cell always keeps its own, never-rising level, and a
                    // river keeps the cells within its width against a creek joining it.
                    if (t < ownerT[i] || (t == ownerT[i] && filled[c] < filled[owner[i]]))
                    {
                        best[i] = d;
                        owner[i] = c;
                        ownerT[i] = t;
                    }
                }
            }
        }
        for (int i = 0; i < n; i++)
        {
            int c = owner[i];
            if (c < 0)
                continue;
            float level = filled[c];
            system.WaterLevel[i] = level;
            system.Flow[i] = direction[c];
            system.ChannelOf[i] = planOf[c];
            height[i] = Mathf.Min(height[i], level - best[i]);
        }
        return inHollows;
    }
}
