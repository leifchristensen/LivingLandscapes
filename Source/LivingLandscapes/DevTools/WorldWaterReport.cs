using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LivingLandscapes.Geology;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LivingLandscapes.DevTools;

/// <summary>
/// "-ll-world-water": counts what the world generation made of water, to see whether rivers or lakes are rare (#216).
/// Land and sea tiles, lake bodies (vanilla turns a sea body of 15 tiles or fewer into a lake), the world rivers (links,
/// river tiles, mouths, sources, confluences), the flow vanilla's river step works out against its spawn thresholds, and
/// which land the coast-flood reaches at all. Written to the log and to world-water.txt in the export folder.
/// </summary>
public static class WorldWaterReport
{
    public static void Run()
    {
        PlanetLayer layer = Find.WorldGrid.Surface;
        int n = layer.TilesCount;
        var neighbours = new int[n][];
        var list = new List<PlanetTile>();
        for (int i = 0; i < n; i++)
        {
            Find.WorldGrid.GetTileNeighbors(new PlanetTile(i, layer), list);
            neighbours[i] = list.Select(t => t.tileId).ToArray();
        }
        bool Sea(int i) => Find.WorldGrid[i].PrimaryBiome == BiomeDefOf.Ocean;
        bool Lake(int i) => Find.WorldGrid[i].PrimaryBiome == BiomeDefOf.Lake;
        bool Water(int i) => Find.WorldGrid[i].WaterCovered;

        var text = new StringBuilder();
        int land = Enumerable.Range(0, n).Count(i => !Water(i));
        text.AppendLine($"World water report: seed {Find.World.info.seedString}, coverage {Find.World.PlanetCoverage}, rainfall {Find.World.info.overallRainfall}, temperature {Find.World.info.overallTemperature}, {n} tiles: land {land}, sea {Enumerable.Range(0, n).Count(Sea)}, lake {Enumerable.Range(0, n).Count(Lake)}.");

        // Water bodies by connected component of water-covered tiles.
        var body = new int[n];
        Array.Fill(body, -1);
        var sizes = new List<int>();
        var isLake = new List<bool>();
        for (int i = 0; i < n; i++)
        {
            if (!Water(i) || body[i] >= 0)
                continue;
            int id = sizes.Count, size = 0;
            bool lake = Lake(i);
            var stack = new Stack<int>();
            stack.Push(i);
            body[i] = id;
            while (stack.Count > 0)
            {
                int t = stack.Pop();
                size++;
                foreach (int m in neighbours[t])
                {
                    if (Water(m) && body[m] < 0 && Lake(m) == lake)
                    {
                        body[m] = id;
                        stack.Push(m);
                    }
                }
            }
            sizes.Add(size);
            isLake.Add(lake);
        }
        var lakeSizes = Enumerable.Range(0, sizes.Count).Where(b => isLake[b]).Select(b => sizes[b]).OrderBy(s => s).ToList();
        var seaSizes = Enumerable.Range(0, sizes.Count).Where(b => !isLake[b]).Select(b => sizes[b]).OrderByDescending(s => s).ToList();
        text.AppendLine($"  lake bodies: {lakeSizes.Count} (tiles {lakeSizes.Sum()}; sizes {string.Join(",", lakeSizes.Take(30))}); sea bodies: {seaSizes.Count} (largest {string.Join(",", seaSizes.Take(8))}).");

        // Land tiles touching a lake / the sea.
        int landByLake = 0, landBySea = 0;
        for (int i = 0; i < n; i++)
        {
            if (Water(i))
                continue;
            if (neighbours[i].Any(Lake))
                landByLake++;
            if (neighbours[i].Any(Sea))
                landBySea++;
        }
        text.AppendLine($"  land tiles next to a lake {landByLake}, next to the sea {landBySea}.");

        // World rivers.
        var upstreamCount = new int[n];
        var linkTotal = 0;
        var rivers = new Dictionary<string, int>();
        var riverTile = new bool[n];
        var degree = new int[n];
        for (int i = 0; i < n; i++)
        {
            if (Find.WorldGrid[i] is not SurfaceTile s || s.Rivers.NullOrEmpty())
                continue;
            riverTile[i] = true;
            degree[i] = s.Rivers.Count;
            foreach (SurfaceTile.RiverLink link in s.Rivers)
            {
                if (link.neighbor.tileId < i)
                    continue;
                linkTotal++;
                rivers[link.river.defName] = rivers.TryGetValue(link.river.defName, out int c) ? c + 1 : 1;
            }
        }
        int riverTiles = riverTile.Count(b => b);
        int sources = 0, confluences = 0;
        for (int i = 0; i < n; i++)
        {
            if (!riverTile[i])
                continue;
            if (Water(i))
                continue;
            if (degree[i] == 1)
                sources++;
            else if (degree[i] >= 3)
                confluences++;
        }
        // Mouths: a link from a land tile to a water tile.
        int mouthLinks = 0;
        for (int i = 0; i < n; i++)
        {
            if (Find.WorldGrid[i] is not SurfaceTile s || s.Rivers.NullOrEmpty() || Water(i))
                continue;
            mouthLinks += s.Rivers.Count(l => Water(l.neighbor.tileId));
        }
        text.AppendLine($"  world rivers: {linkTotal} links over {riverTiles} tiles ({riverTiles * 100f / Math.Max(land, 1):F1}% of land), by def {string.Join(", ", rivers.Select(p => $"{p.Key} {p.Value}"))}; river mouths into water {mouthLinks}, sources (end tiles) {sources}, confluence tiles (3+ links) {confluences}.");

        // Flow against the spawn thresholds, and what the coast flood reaches.
        float[] flow = WorldFlow.Compute(layer, out List<PlanetTile> coastal, out List<PlanetTile>[] tree);
        if (flow == null)
        {
            text.AppendLine("  no coastal sea tiles: vanilla lays no rivers.");
        }
        else
        {
            var reached = new bool[n];
            var stack = new Stack<int>();
            var catchment = new Dictionary<int, int>();
            foreach (PlanetTile root in coastal)
            {
                int count = 0;
                stack.Push(root.tileId);
                reached[root.tileId] = true;
                while (stack.Count > 0)
                {
                    int t = stack.Pop();
                    count++;
                    if (tree[t] == null)
                        continue;
                    foreach (PlanetTile child in tree[t])
                    {
                        if (!reached[child.tileId])
                        {
                            reached[child.tileId] = true;
                            stack.Push(child.tileId);
                        }
                    }
                }
                catchment[root.tileId] = count;
            }
            int unreached = Enumerable.Range(0, n).Count(i => !Water(i) && !reached[i]);
            text.AppendLine($"  coast flood: {coastal.Count} coastal sea tiles; land never reached by the flood {unreached} of {land}.");
            var landFlow = Enumerable.Range(0, n).Where(i => !Water(i) && reached[i]).Select(i => flow[i]).OrderByDescending(f => f).ToList();
            text.AppendLine($"  land flow: max {(landFlow.Count > 0 ? landFlow[0] : 0f):F0}; tiles at or above 20000 {landFlow.Count(f => f >= 20000)}, 50000 {landFlow.Count(f => f >= 50000)}, 100000 (river spawn) {landFlow.Count(f => f >= 100000)}, 500000 {landFlow.Count(f => f >= 500000)}.");
            // Catchment per coast tile: how many coasts could ever make a river.
            var big = catchment.OrderByDescending(p => p.Value).Take(10).Select(p => $"{p.Value}").ToList();
            text.AppendLine($"  catchment tiles per coastal sea tile: largest {string.Join(",", big)}.");
            // Evaporation: rain gathered against flow kept at the mouth.
            float rain = 0f, kept = 0f;
            foreach (PlanetTile root in coastal)
            {
                var neighbourList = new List<PlanetTile>();
                Find.WorldGrid.GetTileNeighbors(root, neighbourList);
                foreach (PlanetTile nb in neighbourList)
                {
                    if (!Water(nb.tileId))
                        kept += flow[nb.tileId];
                }
            }
            for (int i = 0; i < n; i++)
            {
                if (!Water(i) && reached[i])
                    rain += Find.WorldGrid[i].rainfall;
            }
            text.AppendLine($"  rainfall summed over reached land {rain:F0}; flow arriving at the coast {kept:F0} ({(rain > 0 ? kept * 100f / rain : 0f):F0}% survives evaporation).");
        }

        Directory.CreateDirectory(LayerImageExporter.Folder);
        File.WriteAllText(Path.Combine(LayerImageExporter.Folder, "world-water.txt"), text.ToString());
        foreach (string line in text.ToString().Split('\n'))
        {
            if (line.Trim().Length > 0)
                Log.Message("[LivingLandscapes] " + line.TrimEnd());
        }
    }
}
