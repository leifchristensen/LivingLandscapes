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
/// "-ll-world-report": measures how smooth the world's tile elevations are, vanilla's against the altitude model
/// (<see cref="WorldAltitude"/>, #201): the elevation step between neighbouring land tiles by hilliness pair, the
/// spread of each hilliness, coast against inland plains, and how many world river links run uphill. Written to the
/// log and to world-altitude.txt in the export folder.
/// </summary>
public static class WorldAltitudeReport
{
    public static void Run()
    {
        PlanetLayer layer = Find.WorldGrid.Surface;
        WorldAltitudeDef def = WorldAltitudeDef.Current;
        int n = layer.TilesCount;
        float[] vanilla = new float[n];
        var neighbours = new int[n][];
        var sea = new bool[n];
        var list = new List<PlanetTile>();
        for (int i = 0; i < n; i++)
        {
            var tile = new PlanetTile(i, layer);
            Tile info = Find.WorldGrid[tile];
            vanilla[i] = Math.Max(info.elevation, 0f);
            Find.WorldGrid.GetTileNeighbors(tile, list);
            neighbours[i] = list.Select(t => t.tileId).ToArray();
            sea[i] = info.PrimaryBiome == BiomeDefOf.Ocean;
        }
        float[] model = WorldAltitude.Compute(layer, def);
        int[] seaDistance = WorldAltitudeMath.DistanceToSea(neighbours, sea);

        var text = new StringBuilder();
        text.AppendLine($"World altitude report: seed {Find.World.info.seedString}, {n} tiles, {sea.Count(s => s)} sea, coverage {Find.World.PlanetCoverage}, model {(def.enabled ? "on" : "off")}, RP2 {(ModsConfig.IsActive("koth.realisticplanets2") ? "active" : "absent")}.");
        foreach (var (name, field) in new[] { ("vanilla", vanilla), ("model", model) })
        {
            text.AppendLine($"[{name}]");
            // Step between neighbouring land tiles, by the pair's hilliness.
            var steps = new Dictionary<string, List<float>>();
            var all = new List<float>();
            for (int i = 0; i < n; i++)
            {
                if (sea[i])
                    continue;
                foreach (int j in neighbours[i])
                {
                    if (j < i || sea[j])
                        continue;
                    Hilliness a = Find.WorldGrid[i].hilliness, b = Find.WorldGrid[j].hilliness;
                    string key = a <= b ? $"{a}-{b}" : $"{b}-{a}";
                    float step = Math.Abs(field[i] - field[j]);
                    all.Add(step);
                    if (!steps.TryGetValue(key, out var s))
                        steps[key] = s = new List<float>();
                    s.Add(step);
                }
            }
            text.AppendLine($"  neighbour step, all land pairs: {Describe(all)}");
            foreach (var pair in steps.OrderByDescending(p => p.Value.Count).Take(8))
                text.AppendLine($"  neighbour step {pair.Key}: {Describe(pair.Value)}");
            // Elevation of each hilliness.
            foreach (Hilliness h in new[] { Hilliness.Flat, Hilliness.SmallHills, Hilliness.LargeHills, Hilliness.Mountainous, Hilliness.Impassable })
            {
                var values = Enumerable.Range(0, n).Where(i => !sea[i] && Find.WorldGrid[i].hilliness == h).Select(i => field[i]).ToList();
                if (values.Count > 0)
                    text.AppendLine($"  elevation of {h}: {Describe(values)}");
            }
            var coastFlat = Enumerable.Range(0, n).Where(i => !sea[i] && seaDistance[i] == 1 && Find.WorldGrid[i].hilliness == Hilliness.Flat).Select(i => field[i]).ToList();
            var inlandFlat = Enumerable.Range(0, n).Where(i => !sea[i] && seaDistance[i] >= 4 && Find.WorldGrid[i].hilliness == Hilliness.Flat).Select(i => field[i]).ToList();
            text.AppendLine($"  flat at the coast: {Describe(coastFlat)}; flat 4+ tiles inland: {Describe(inlandFlat)}");
            // World river links: the river flows to the neighbour it links to, which should not be higher.
            int links = 0, uphill = 0, uphillFar = 0;
            float climb = 0f;
            for (int i = 0; i < n; i++)
            {
                if (Find.WorldGrid[i] is not SurfaceTile surface || surface.Rivers.NullOrEmpty())
                    continue;
                foreach (SurfaceTile.RiverLink link in surface.Rivers)
                {
                    int j = link.neighbor.tileId;
                    if (j < i || sea[i] || sea[j])
                        continue;
                    // The downstream end of a link is the neighbour with the smaller river distance to the sea.
                    bool iIsDownstream = ((SurfaceTile)Find.WorldGrid[i]).riverDist < ((SurfaceTile)Find.WorldGrid[j]).riverDist;
                    int down = iIsDownstream ? i : j, up = iIsDownstream ? j : i;
                    links++;
                    float rise = field[down] - field[up];
                    if (rise > 0f)
                    {
                        uphill++;
                        climb += rise;
                        if (rise > 100f)
                            uphillFar++;
                    }
                }
            }
            text.AppendLine($"  world river links over land: {links}, running uphill {uphill} ({(links > 0 ? uphill * 100f / links : 0f):F0}%), by more than 100 m {uphillFar}, mean climb {(uphill > 0 ? climb / uphill : 0f):F0} m");
        }

        Directory.CreateDirectory(LayerImageExporter.Folder);
        File.WriteAllText(Path.Combine(LayerImageExporter.Folder, "world-altitude.txt"), text.ToString());
        foreach (string line in text.ToString().Split('\n'))
        {
            if (line.Trim().Length > 0)
                Log.Message("[LivingLandscapes] " + line.TrimEnd());
        }
    }

    private static string Describe(List<float> values)
    {
        if (values.Count == 0)
            return "none";
        values.Sort();
        float At(float q) => values[Math.Min(values.Count - 1, (int)(q * values.Count))];
        return $"n {values.Count}, median {At(0.5f):F0} m, p10 {At(0.1f):F0}, p90 {At(0.9f):F0}, max {values[values.Count - 1]:F0}";
    }
}
