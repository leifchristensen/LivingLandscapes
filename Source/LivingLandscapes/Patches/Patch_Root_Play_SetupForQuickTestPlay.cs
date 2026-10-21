using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Development aids for dev quicktest, inactive without their launch arguments:
/// "-ll-seed=&lt;text&gt;" fixes the world seed and start tile, so the same test maps can be regenerated before and after
/// a change; "-ll-world-seed" and the other world settings (<see cref="Patch_WorldGenerator_GenerateWorld"/>) with
/// "-ll-tile=7825" (or "7825,0": tile, layer) and "-ll-map-size=250" regenerate a player's world and start on their tile;
/// "-ll-mutators=A+B" adds tile mutators to the start tile; "-ll-hilliness=Mountainous" sets its hilliness;
/// "-ll-biome", "-ll-rainfall", "-ll-temperature", "-ll-elevation", "-ll-coast" and "-ll-rivers" set the rest of the
/// tile's conditions (most tiles have no river or coast, so tests force them).
/// </summary>
[HarmonyPatch(typeof(Root_Play), nameof(Root_Play.SetupForQuickTestPlay))]
public static class Patch_Root_Play_SetupForQuickTestPlay
{
    /// <summary>
    /// True while quicktest sets up with a fixed seed; read by <see cref="Patch_GenText_RandomSeedString"/> and
    /// <see cref="Patch_WorldGenerator_GenerateWorld"/>.
    /// </summary>
    internal static bool Active { get; private set; }

    public static void Prefix()
    {
        Active = (GenCommandLine.TryGetCommandLineArg("ll-world-seed", out string seed) && !seed.NullOrEmpty())
                 || (GenCommandLine.TryGetCommandLineArg("ll-seed", out seed) && !seed.NullOrEmpty());
        // The start tile is picked with the global random state, so seed that too.
        if (Active)
            Rand.PushState(GenText.StableStringHash(seed));
    }

    public static void Postfix()
    {
        if (Active)
            Rand.PopState();
        Active = false;
        ChooseStartTile();
        ForceStartTile();
    }

    /// <summary>"-ll-tile=7825" or "-ll-tile=7825,0" (tile, planet layer) and "-ll-map-size=250".</summary>
    private static void ChooseStartTile()
    {
        if (GenCommandLine.TryGetCommandLineArg("ll-tile", out string text))
        {
            string[] parts = text.Split(',');
            int layer = parts.Length > 1 && int.TryParse(parts[1], out int l) ? l : Find.WorldGrid.Surface.LayerID;
            var reason = new System.Text.StringBuilder();
            if (int.TryParse(parts[0], out int id) && id >= 0 && id < Find.WorldGrid.Surface.TilesCount
                && TileFinder.IsValidTileForNewSettlement(new PlanetTile(id, layer), reason))
            {
                Find.GameInitData.startingTile = new PlanetTile(id, layer);
                Tile tile = Find.WorldGrid[Find.GameInitData.startingTile];
                Log.Message($"[LivingLandscapes] Quicktest start tile {text}: {tile.PrimaryBiome?.defName}, {tile.hilliness}, " +
                            $"{tile.elevation:F0} m, mutators [{string.Join(", ", tile.Mutators.Select(m => m.defName))}].");
            }
            else
                Log.Warning($"[LivingLandscapes] -ll-tile: {text} is not a settleable tile of this world ({(reason.Length > 0 ? reason.ToString() : "no such tile")}); " +
                            $"keeping the random start tile {Find.GameInitData.startingTile}. Check the world seed and settings match the world the tile came from.");
        }
        if (GenCommandLine.TryGetCommandLineArg("ll-map-size", out string size) && int.TryParse(size, out int mapSize) && mapSize >= 50)
            Find.GameInitData.mapSize = mapSize;
    }

    private static void ForceStartTile()
    {
        PlanetTile start = Find.GameInitData.startingTile;
        if (!start.Valid)
            return;
        Tile tile = Find.WorldGrid[start];
        if (GenCommandLine.TryGetCommandLineArg("ll-mutators", out string mutators))
        {
            foreach (string name in mutators.Split('+'))
            {
                TileMutatorDef mutator = DefDatabase<TileMutatorDef>.GetNamedSilentFail(name);
                if (mutator == null)
                    Log.Warning($"[LivingLandscapes] -ll-mutators: no tile mutator named {name}.");
                else
                {
                    // A forced mutator replaces the tile's mutators it conflicts with (a shared category), as world
                    // generation would have chosen one of them.
                    foreach (TileMutatorDef existing in tile.Mutators.Where(m => m.categories.Any(mutator.categories.Contains)).ToList())
                        tile.RemoveMutator(existing);
                    tile.AddMutator(mutator);
                }
            }
        }
        if (GenCommandLine.TryGetCommandLineArg("ll-hilliness", out string hilliness) && System.Enum.TryParse(hilliness, out Hilliness value))
            tile.hilliness = value;
        ForceClimate(tile);
        ForceNeighbours(start, tile);
    }

    /// <summary>"-ll-biome=Desert", "-ll-rainfall=300" (mm), "-ll-temperature=25" (C), "-ll-elevation=500" (m).</summary>
    private static void ForceClimate(Tile tile)
    {
        if (GenCommandLine.TryGetCommandLineArg("ll-biome", out string biome))
        {
            BiomeDef def = DefDatabase<BiomeDef>.GetNamedSilentFail(biome);
            if (def == null)
                Log.Warning($"[LivingLandscapes] -ll-biome: no biome named {biome}.");
            else
                tile.PrimaryBiome = def;
        }
        if (TryFloat("ll-rainfall", out float rainfall))
            tile.rainfall = rainfall;
        if (TryFloat("ll-temperature", out float temperature))
            tile.temperature = temperature;
        if (TryFloat("ll-elevation", out float elevation))
            tile.elevation = elevation;
    }

    private static bool TryFloat(string name, out float value)
    {
        value = 0f;
        return GenCommandLine.TryGetCommandLineArg(name, out string text)
               && float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// "-ll-coast" (or "-ll-coast=North|East|South|West": the sea lies that way) makes a neighbour ocean. "-ll-rivers=
    /// Creek,River,~HugeRiver" ("-" skips a neighbour) replaces the tile's river links, one per land neighbour in order; a name prefixed "~"
    /// links to the ocean (a river mouth; needs -ll-coast). The tile's Coast and river mutators then follow from this as
    /// world generation assigns them (<c>WorldGenStep_Mutators</c>): Coast for a coastal tile; RiverDelta for a coastal
    /// tile with more than two links, Headwater for one link, RiverConfluence for more than two, else River. This mirrors
    /// that step only for test tiles (it cannot be called for one tile); the mod itself reads a tile's real mutators and
    /// links. Neighbours are the same for a given seed.
    /// </summary>
    private static void ForceNeighbours(PlanetTile start, Tile tile)
    {
        // "-ll-neighbour-biome=Desert": the first neighbour gets that biome (a mixed-biome tile needs a neighbour of another biome).
        if (GenCommandLine.TryGetCommandLineArg("ll-neighbour-biome", out string other) && DefDatabase<BiomeDef>.GetNamedSilentFail(other) is { } otherBiome)
        {
            var near = new System.Collections.Generic.List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(start, near);
            foreach (PlanetTile n in near)
                Find.WorldGrid[n].PrimaryBiome = otherBiome;
        }
        // "-ll-roads=DirtRoad,DirtRoad": one road link per neighbour, in order.
        if (GenCommandLine.TryGetCommandLineArg("ll-roads", out string roadList) && tile is SurfaceTile roadTile)
        {
            var near = new System.Collections.Generic.List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(start, near);
            roadTile.potentialRoads = new System.Collections.Generic.List<SurfaceTile.RoadLink>();
            string[] names = roadList.Split(',');
            for (int i = 0; i < names.Length && i < near.Count; i++)
            {
                RoadDef road = DefDatabase<RoadDef>.GetNamedSilentFail(names[i]);
                if (road != null)
                    roadTile.potentialRoads.Add(new SurfaceTile.RoadLink { neighbor = near[i], road = road });
            }
        }
        // Either the bare flag or "-ll-coast=North".
        bool coast = GenCommandLine.TryGetCommandLineArg("ll-coast", out string coastSide) || GenCommandLine.CommandLineArgPassed("ll-coast");
        bool rivers = GenCommandLine.TryGetCommandLineArg("ll-rivers", out string riverList) && !riverList.NullOrEmpty();
        if (!coast && !rivers)
            return;
        var neighbours = new System.Collections.Generic.List<PlanetTile>();
        Find.WorldGrid.GetTileNeighbors(start, neighbours);
        PlanetTile ocean = PlanetTile.Invalid;
        if (coast && neighbours.Count > 0)
        {
            Rot4 side = coastSide switch { "North" => Rot4.North, "East" => Rot4.East, "South" => Rot4.South, "West" => Rot4.West, _ => Rot4.Invalid };
            int pick = side.IsValid ? neighbours.FindIndex(n => Find.WorldGrid.GetRotFromTo(start, n) == side) : 0;
            ocean = neighbours[pick < 0 ? 0 : pick];
            Tile sea = Find.WorldGrid[ocean];
            sea.PrimaryBiome = BiomeDefOf.Ocean;
            sea.elevation = -100f;
            neighbours.Remove(ocean);
        }
        if (rivers && tile is SurfaceTile surface)
        {
            surface.potentialRivers = new System.Collections.Generic.List<SurfaceTile.RiverLink>();
            int land = 0;
            foreach (string entry in riverList.Split(','))
            {
                if (entry == "-")
                {
                    land++; // a neighbour without a river, so links can be set on opposite sides
                    continue;
                }
                bool mouth = entry.StartsWith("~");
                RiverDef river = DefDatabase<RiverDef>.GetNamedSilentFail(mouth ? entry.Substring(1) : entry);
                PlanetTile to = mouth ? ocean : (land < neighbours.Count ? neighbours[land++] : PlanetTile.Invalid);
                if (river == null || !to.Valid)
                {
                    Log.Warning($"[LivingLandscapes] -ll-rivers: cannot place {entry} (unknown river, or no neighbour: a mouth needs -ll-coast).");
                    continue;
                }
                surface.potentialRivers.Add(new SurfaceTile.RiverLink { neighbor = to, river = river });
            }
        }
        AssignMutators(tile, coast, (tile as SurfaceTile)?.Rivers?.Count ?? 0);
        Log.Message($"[LivingLandscapes] Forced tile: {tile.PrimaryBiome.defName}, rain {tile.rainfall:F0} mm, {tile.temperature:F0} C, " +
                    $"{(tile as SurfaceTile)?.Rivers?.Count ?? 0} river links, coast {(ocean.Valid ? Find.World.CoastDirectionAt(start).ToStringHuman() : "none")}, " +
                    $"mutators [{string.Join(", ", tile.Mutators.Select(m => m.defName))}].");
    }

    /// <summary>The Coast and river mutators world generation would give a tile with this coast and these river links.</summary>
    private static void AssignMutators(Tile tile, bool coast, int riverLinks)
    {
        void Set(string name)
        {
            TileMutatorDef def = DefDatabase<TileMutatorDef>.GetNamedSilentFail(name);
            if (def == null)
                return;
            foreach (TileMutatorDef existing in tile.Mutators.Where(m => m.categories.Any(def.categories.Contains)).ToList())
                tile.RemoveMutator(existing);
            tile.AddMutator(def);
        }
        if (coast)
            Set("Coast");
        if (riverLinks == 0)
            return;
        Set(coast && riverLinks > 2 && DefDatabase<TileMutatorDef>.GetNamedSilentFail("RiverDelta") != null ? "RiverDelta"
            : riverLinks == 1 && DefDatabase<TileMutatorDef>.GetNamedSilentFail("Headwater") != null ? "Headwater"
            : riverLinks > 2 && DefDatabase<TileMutatorDef>.GetNamedSilentFail("RiverConfluence") != null ? "RiverConfluence" : "River");
    }
}
