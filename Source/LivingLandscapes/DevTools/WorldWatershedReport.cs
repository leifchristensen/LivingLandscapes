using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LivingLandscapes.DevTools;

/// <summary>
/// "-ll-world-watershed" (#219): reads Realistic Planets 2's flow field (rain-weighted flow accumulation and flow
/// direction, baked in its ClimateData) and counts what a supplementary rule "every land tile whose flow reaches the
/// threshold T is a river tile, joined to the tile it drains to" would add to the world's rivers, for several T.
/// Nothing is changed; the numbers go to the log and to world-watershed.txt in the export folder.
/// </summary>
public static class WorldWatershedReport
{
    public static void Run()
    {
        PlanetLayer layer = Find.WorldGrid.Surface;
        int n = layer.TilesCount;
        var text = new StringBuilder();
        var neighbours = new int[n][];
        var list = new List<PlanetTile>();
        for (int i = 0; i < n; i++)
        {
            Find.WorldGrid.GetTileNeighbors(new PlanetTile(i, layer), list);
            neighbours[i] = list.Select(t => t.tileId).ToArray();
        }
        bool Water(int i) => Find.WorldGrid[i].WaterCovered;
        bool Lake(int i) => Find.WorldGrid[i].PrimaryBiome == BiomeDefOf.Lake;
        int land = Enumerable.Range(0, n).Count(i => !Water(i));
        text.AppendLine($"World watershed report: seed {Find.World.info.seedString}, coverage {Find.World.PlanetCoverage}, {n} tiles, land {land}. Mods: {string.Join(",", ModsConfig.ActiveModsInLoadOrder.Select(m => m.PackageIdPlayerFacing))}");

        Type climateType = AccessTools.TypeByName("Planets.WorldGen.Climate.ClimateData");
        object climate = climateType == null ? null : Find.World.components.FirstOrDefault(c => c.GetType() == climateType);
        if (climate == null)
        {
            text.AppendLine("  Realistic Planets 2 climate data not found: nothing to measure.");
            Write(text);
            return;
        }
        T Get<T>(string name) where T : class => AccessTools.Field(climateType, name)?.GetValue(climate) as T;
        float[] flow = Get<float[]>("FlowRain");
        int[] dirTopo = Get<int[]>("FlowDirectionTopology"), spillTopo = Get<int[]>("SpillDirectionTopology");
        int[] dirHydro = Get<int[]>("FlowDirectionHydrology"), spillHydro = Get<int[]>("SpillDirectionHydrology");
        bool[] trueOcean = Get<bool[]>("TrueOceanMask");
        float density = (float)(AccessTools.Field(AccessTools.TypeByName("Planets.Core.Planets_GameComponent"), "riverDensity")?.GetValue(null) ?? 1f);
        object settings = AccessTools.Property(AccessTools.TypeByName("Planets.Core.Controller"), "Settings")?.GetValue(null)
                          ?? AccessTools.Field(AccessTools.TypeByName("Planets.Core.Controller"), "Settings")?.GetValue(null);
        object feature = settings == null ? null : (AccessTools.Field(settings.GetType(), "featureConfig")?.GetValue(settings) ?? AccessTools.Property(settings.GetType(), "featureConfig")?.GetValue(settings));
        object t2 = feature == null ? null : (AccessTools.Property(feature.GetType(), "IsTier2HydrologyEnabled")?.GetValue(feature) ?? AccessTools.Field(feature.GetType(), "IsTier2HydrologyEnabled")?.GetValue(feature));
        bool tier2 = t2 is bool b && b && dirHydro != null && spillHydro != null;
        int[] dir = tier2 ? dirHydro : dirTopo, spill = tier2 ? spillHydro : spillTopo;
        text.AppendLine($"  riverDensity {density}, tier-2 hydrology {tier2}, FlowRain {(flow == null ? "missing" : flow.Length.ToString())}, true-ocean mask {(trueOcean != null)}.");
        if (flow == null || dir == null)
            return;

        // The tile each tile drains to, as Patch_DrainageRivers.Next works it out.
        int Next(int i)
        {
            if (Find.WorldGrid[i].PrimaryBiome == BiomeDefOf.Ocean)
                return -1;
            int k = -1;
            if (i < dir.Length && dir[i] >= 0)
                k = dir[i];
            else if (spill != null && i < spill.Length && spill[i] >= 0)
                k = spill[i];
            return k < 0 || k >= neighbours[i].Length ? -1 : neighbours[i][k];
        }
        var next = new int[n];
        for (int i = 0; i < n; i++)
            next[i] = i < flow.Length ? Next(i) : -1;

        // The rivers the world has (RP2's own), and whether their links agree with the drainage direction.
        var linkSet = new HashSet<(int, int)>();
        for (int i = 0; i < n; i++)
        {
            if (Find.WorldGrid[i] is not SurfaceTile s || s.Rivers.NullOrEmpty())
                continue;
            foreach (SurfaceTile.RiverLink l in s.Rivers)
                linkSet.Add((Math.Min(i, l.neighbor.tileId), Math.Max(i, l.neighbor.tileId)));
        }
        // The world as it is now (after the watershed rule, if it ran): river tiles, confluences (3+ links), sources, Strahler orders.
        {
            var degree = new Dictionary<int, int>();
            foreach ((int a, int c) in linkSet)
            {
                degree[a] = degree.GetValueOrDefault(a) + 1;
                degree[c] = degree.GetValueOrDefault(c) + 1;
            }
            var inForest = new bool[n];
            foreach (int t in degree.Keys)
                inForest[t] = !Water(t);
            int[] orders = WatershedMathOrders(next, inForest);
            var hist = new int[8];
            for (int i = 0; i < n; i++)
                if (orders[i] > 0)
                    hist[Math.Min(7, orders[i])]++;
            int tiles = degree.Keys.Count(t => !Water(t));
            text.AppendLine($"  ACTUAL world rivers: {linkSet.Count} links, {tiles} river tiles ({tiles * 100f / land:F1}% of land), confluences {degree.Count(p => !Water(p.Key) && p.Value >= 3)}, sources {degree.Count(p => !Water(p.Key) && p.Value == 1)}, Strahler order histogram 1..7+ (by drainage direction): {string.Join(",", hist.Skip(1))}.");
        }
        int agree = linkSet.Count(p => next[p.Item1] == p.Item2 || next[p.Item2] == p.Item1);
        text.AppendLine($"  RP2 world rivers: {linkSet.Count} links; {agree} follow the drainage direction ({agree * 100f / Math.Max(1, linkSet.Count):F0}%).");

        var landFlow = Enumerable.Range(0, n).Where(i => !Water(i) && i < flow.Length).Select(i => flow[i]).OrderBy(f => f).ToArray();
        string Pct(double q) => landFlow.Length == 0 ? "-" : landFlow[Math.Min(landFlow.Length - 1, (int)(q * landFlow.Length))].ToString("F0");
        text.AppendLine($"  land FlowRain percentiles: 50% {Pct(0.5)}, 75% {Pct(0.75)}, 90% {Pct(0.9)}, 95% {Pct(0.95)}, 99% {Pct(0.99)}, max {Pct(1.0)}.");
        int noOutlet = Enumerable.Range(0, n).Count(i => !Water(i) && next[i] < 0);
        text.AppendLine($"  land tiles with no drainage direction (closed basin bottoms) {noOutlet}; land tiles whose chain ends on land: counted below per threshold.");

        // Where do the chains of tiles at or above the threshold end? (candidates by terminal: sea/lake, river, closed basin bottom, below T, cycle)
        {
            float[] env = LivingLandscapes.Geology.WatershedMath.NonDecreasing(next, flow);
            float T = 50000f / Math.Max(density, 0.01f);
            var ends = new Dictionary<string, int>();
            for (int i = 0; i < n; i++)
            {
                if (Water(i) || i >= flow.Length || env[i] < T || next[i] < 0 && false)
                    continue;
                int t = i, steps = 0;
                string end;
                while (true)
                {
                    if (steps++ > n) { end = "cycle"; break; }
                    int d = next[t];
                    if (d < 0) { end = "closed basin bottom"; break; }
                    if (Water(d)) { end = "sea or lake"; break; }
                    if (env[d] < T) { end = "below threshold"; break; }
                    t = d;
                }
                ends[end] = ends.GetValueOrDefault(end) + 1;
            }
            // The terminal tiles themselves: what are the closed basin bottoms?
            var terminals = Enumerable.Range(0, n).Where(i => !Water(i) && i < flow.Length && env[i] >= T && next[i] < 0).ToList();
            int nearWater = terminals.Count(i => neighbours[i].Any(Water)), nearLake = terminals.Count(i => neighbours[i].Any(Lake));
            int hasDir = terminals.Count(i => i < dir.Length && dir[i] >= 0), hasSpill = terminals.Count(i => spill != null && i < spill.Length && spill[i] >= 0);
            var upstreamOfTerminal = terminals.OrderByDescending(i => env[i]).Take(8).Select(i => $"{i}:{Find.WorldGrid[i].PrimaryBiome.defName}/{Find.WorldGrid[i].rainfall:F0}mm/{Find.WorldGrid[i].elevation:F0}m/F{env[i]:F0}");
            text.AppendLine($"  terminal tiles {terminals.Count}: next to water {nearWater}, next to a lake {nearLake}, with a flow direction {hasDir}, with a spill direction {hasSpill}; biggest: {string.Join(" ", upstreamOfTerminal)}");
            text.AppendLine($"  tiles with flow >= {T:F0} by where their chain ends: {string.Join(", ", ends.Select(p => $"{p.Key} {p.Value}"))}.");
        }
        // Supplementary rule at several thresholds: tile with flow >= T joins the tile it drains to.
        foreach (float absT in new[] { 5000f, 10000f, 20000f, 50000f, 100000f })
        {
            float T = absT / Math.Max(density, 0.01f);
            var links = new HashSet<(int, int)>(linkSet);
            int added = 0;
            for (int i = 0; i < n; i++)
            {
                if (Water(i) || i >= flow.Length || flow[i] < T || next[i] < 0)
                    continue;
                if (links.Add((Math.Min(i, next[i]), Math.Max(i, next[i]))))
                    added++;
            }
            var deg = new Dictionary<int, int>();
            foreach ((int a, int c) in links)
            {
                deg[a] = deg.GetValueOrDefault(a) + 1;
                deg[c] = deg.GetValueOrDefault(c) + 1;
            }
            int tiles = deg.Keys.Count(t => !Water(t));
            int conf = deg.Count(p => !Water(p.Key) && p.Value >= 3);
            int src = deg.Count(p => !Water(p.Key) && p.Value == 1);
            // Chains of the new rule that end on land: a tile that drains nowhere and is not water.
            int deadEnds = Enumerable.Range(0, n).Count(i => !Water(i) && i < flow.Length && flow[i] >= T && next[i] < 0);
            // Strahler order over the drainage forest of tiles with flow >= T (sources order 1).
            var order = new int[n];
            var upstream = new List<int>[n];
            for (int i = 0; i < n; i++)
                if (!Water(i) && i < flow.Length && flow[i] >= T && next[i] >= 0 && !Water(next[i]) && flow[next[i]] >= T)
                    (upstream[next[i]] ??= new List<int>()).Add(i);
            int Order(int t)
            {
                if (order[t] > 0) return order[t];
                if (upstream[t] == null) return order[t] = 1;
                var os = upstream[t].Select(Order).OrderByDescending(o => o).ToList();
                return order[t] = os.Count >= 2 && os[0] == os[1] ? os[0] + 1 : os[0];
            }
            var hist = new int[8];
            for (int i = 0; i < n; i++)
                if (!Water(i) && i < flow.Length && flow[i] >= T && next[i] >= 0 && !Water(next[i]) && flow[next[i]] >= T || upstream[i] != null)
                    hist[Math.Min(7, Order(i))]++;
            text.AppendLine($"  T = {absT:F0}/density = {T:F0}: +{added} links (total {links.Count}), river tiles {tiles} ({tiles * 100f / land:F1}% of land), confluences {conf}, sources {src}, chains ending on land {deadEnds}, Strahler order histogram 1..7+: {string.Join(",", hist.Skip(1))}.");
        }
        Write(text);
    }

    private static int[] WatershedMathOrders(int[] next, bool[] inForest) => LivingLandscapes.Geology.WatershedMath.StrahlerOrders(next, inForest);

    private static void Write(StringBuilder text)
    {
        Directory.CreateDirectory(LayerImageExporter.Folder);
        File.WriteAllText(Path.Combine(LayerImageExporter.Folder, "world-watershed.txt"), text.ToString());
        foreach (string line in text.ToString().Split('\n'))
            if (line.Trim().Length > 0)
                Log.Message("[LivingLandscapes] " + line.TrimEnd());
    }
}
