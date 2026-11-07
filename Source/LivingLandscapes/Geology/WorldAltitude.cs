using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// The altitude our code reads for a world tile (#201): the smooth steady-state field of <see cref="WorldAltitudeMath"/>,
/// worked out once per world from the tiles' hilliness and the sea. Vanilla's <see cref="Tile.elevation"/> is left
/// alone (it drives vanilla's climate, biomes and rivers), so this works for existing saves and worlds. A world made by
/// Realistic Planets 2, whose elevation is already smooth and real, keeps its own values.
/// </summary>
public static class WorldAltitude
{
    private const string RealisticPlanets2 = "koth.realisticplanets2";

    private static readonly object computeLock = new object();
    private static World cachedWorld;
    private static float[] cached;

    /// <summary>True while our model replaces vanilla's elevation: switched on in the def and no world-generation mod brings its own.</summary>
    public static bool Active => WorldAltitudeDef.Current.enabled && !ModsConfig.IsActive(RealisticPlanets2);

    /// <summary>Height of a tile above sea level, metres: the model's, or the tile's own elevation where the model is off.</summary>
    public static float At(PlanetTile tile)
    {
        Tile info = Find.WorldGrid[tile];
        if (!Active || tile.Layer != Find.WorldGrid.Surface)
            return info.elevation;
        float[] field = Field();
        return field != null && tile.tileId < field.Length ? field[tile.tileId] : info.elevation;
    }

    /// <summary>The altitude to show for a tile (the world tile tab): ours, or the tile's own elevation where the model is off.</summary>
    public static float ShownElevation(Tile info)
        => info != null && info.tile.Valid ? At(info.tile) : info?.elevation ?? 0f;

    /// <summary>
    /// Degrees to add to the tile's temperature so it follows our altitude: vanilla baked the cooling of its own elevation
    /// in, so take that out and put the cooling of ours in (same curve, <see cref="WorldAltitudeDef.CoolingAt"/>).
    /// Kept within the biome's temperature range (<see cref="WorldAltitudeDef.biomeTemperatures"/>). Zero where the model is off.
    /// </summary>
    public static float TemperatureOffset(PlanetTile tile)
    {
        WorldAltitudeDef def = WorldAltitudeDef.Current;
        if (!Active || !def.temperatureFollowsAltitude || tile.Layer != Find.WorldGrid.Surface)
            return 0f;
        Tile info = Find.WorldGrid[tile];
        if (info == null)
            return 0f;
        float offset = def.CoolingAt(info.elevation) - def.CoolingAt(At(tile));
        WorldAltitudeDef.BiomeTemperature range = def.biomeTemperatures.Find(b => b.biome == info.PrimaryBiome);
        if (range == null)
            return offset;
        // Toward the biome's range, no further than needed and by no more than maxBiomeAdjustment; vanilla's own value, even outside the range, is the limit.
        float stored = info.temperature;
        float low = System.Math.Min(range.min, stored), high = System.Math.Max(range.max, stored);
        float free = stored + offset;
        float adjustment = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.Clamp(free, low, high) - free, -def.maxBiomeAdjustment, def.maxBiomeAdjustment);
        return offset + adjustment;
    }

    /// <summary>
    /// The tile's annual mean temperature for code that reads it directly (the stored value is vanilla's, with the cooling
    /// of vanilla's elevation baked in): <see cref="Tile.temperature"/> plus <see cref="TemperatureOffset"/>. Vanilla's own
    /// readers get the offset through <c>Patch_GenTemperature_OffsetFromSeasonCycle</c>. Under Realistic Planets 2 the offset is 0.
    /// </summary>
    public static float TemperatureAt(PlanetTile tile, Tile info)
        => info == null ? 10f : info.temperature + (tile.Valid ? TemperatureOffset(tile) : 0f);

    /// <summary>The model's height for every surface tile, worked out once per world (null if it cannot be).</summary>
    public static float[] Field()
    {
        // Map Preview reads this from its worker thread (#204): one computation at a time.
        lock (computeLock)
        {
            if (cached != null && cachedWorld == Find.World)
                return cached;
            cachedWorld = Find.World;
            cached = null;
            try
            {
                cached = Compute(Find.WorldGrid.Surface, WorldAltitudeDef.Current);
            }
            catch (System.Exception e)
            {
                Log.Warning($"[LivingLandscapes] Could not work out the world's altitude ({e.Message}); tiles use vanilla's elevation.");
            }
            return cached;
        }
    }

    /// <summary>
    /// The world's river links over land as (upstream, downstream), those nearest the sea first. A link's downstream end is
    /// the tile with the smaller river distance to the sea (vanilla's own ordering, as in <see cref="WorldContext"/>).
    /// </summary>
    public static List<(int up, int down)> RiverLinks(PlanetLayer layer, bool[] sea)
    {
        var links = new List<(int up, int down, int dist)>();
        for (int i = 0; i < layer.TilesCount; i++)
        {
            if (sea[i] || Find.WorldGrid[i] is not SurfaceTile surface || surface.Rivers.NullOrEmpty())
                continue;
            foreach (SurfaceTile.RiverLink link in surface.Rivers)
            {
                int j = link.neighbor.tileId;
                if (j < i || sea[j])
                    continue;
                float di = surface.riverDist, dj = ((SurfaceTile)Find.WorldGrid[j]).riverDist;
                if (di == dj)
                    continue;
                links.Add(di < dj ? (j, i, (int)di) : (i, j, (int)dj));
            }
        }
        links.Sort((a, b) => a.dist.CompareTo(b.dist));
        return links.ConvertAll(l => (l.up, l.down));
    }

    /// <summary>Tile graph, sea and targets of a layer, as <see cref="WorldAltitudeMath"/> takes them.</summary>
    public static float[] Compute(PlanetLayer layer, WorldAltitudeDef def)
    {
        int n = layer.TilesCount;
        var neighbours = new int[n][];
        var sea = new bool[n];
        var target = new float[n];
        var stiffness = new float[n];
        var list = new List<PlanetTile>();
        for (int i = 0; i < n; i++)
        {
            var tile = new PlanetTile(i, layer);
            Tile info = Find.WorldGrid[tile];
            Find.WorldGrid.GetTileNeighbors(tile, list);
            neighbours[i] = new int[list.Count];
            for (int j = 0; j < list.Count; j++)
                neighbours[i][j] = list[j].tileId;
            sea[i] = info.PrimaryBiome == BiomeDefOf.Ocean;
            WorldAltitudeDef.HillinessAltitude h = def.For(info.hilliness);
            target[i] = h?.height ?? 0f;
            stiffness[i] = h?.stiffness ?? 0f;
        }
        float[] height = WorldAltitudeMath.Solve(neighbours, sea, target, stiffness, def.continentalUplift);
        WorldAltitudeMath.EnforceDescent(height, RiverLinks(layer, sea), def.riverFall);
        return height;
    }
}
