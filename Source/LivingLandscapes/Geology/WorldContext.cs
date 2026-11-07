using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// What the world says about the slice of terrain a map shows. A map (~1 m per cell) is a small window into a world
/// tile tens of kilometres across, so the world sets the absolute elevation, how rugged the ground is, which way the
/// region slopes, and where a river enters and leaves; the map itself supplies the local landforms.
/// Directions are unit vectors in map space (x east, z north).
/// </summary>
public class WorldContext
{
    /// <summary>Tile elevation above sea level, metres.</summary>
    public float Elevation;

    public Hilliness Hilliness;

    /// <summary>
    /// Direction the region slopes down towards (lower neighbours of a different ruggedness, the sea, the river), or zero
    /// where the tile is surrounded by ground like its own (<see cref="RegionalSlopeMath"/>).
    /// </summary>
    public Vector2 Downhill;

    /// <summary>Regional fall per metre towards <see cref="Downhill"/>, from neighbouring tile elevations weighted by relief contrast.</summary>
    public float RegionalGradient;

    /// <summary>Direction the sea lies in, or zero if no neighbouring tile is sea.</summary>
    public Vector2 SeaDirection;

    public bool HasRiver;

    /// <summary>Vanilla's world river flow through the tile (<see cref="WorldFlow"/>), in the river defs' units.</summary>
    public float Flow;

    /// <summary>A world river entering the map: the side it comes from and the width vanilla gives it.</summary>
    public struct RiverInflow
    {
        public Vector2 From;
        public float Width;

        /// <summary>Vanilla's world flow through the tile the river comes from (<see cref="WorldFlow"/>): the water it brings.</summary>
        public float Flow;
    }

    /// <summary>World rivers entering the map, the main one first. Empty at a headwater, where the river rises on the map.</summary>
    public List<RiverInflow> RiverInflows = new List<RiverInflow>();

    /// <summary>Direction in which the world river leaves the map (toward the sea at a river mouth), and its width there.</summary>
    public Vector2 RiverExit;
    public float RiverExitWidth;

    /// <summary>
    /// Where the world rivers meet, as a fraction of the map width from its centre. As in vanilla, each river runs
    /// from its own side of the map to this point (vanilla picks it between 0.3 and 0.7 of the map), and the river
    /// leaves from here toward its exit. Fixed per tile, so every part of generation agrees.
    /// </summary>
    public Vector2 RiverCentre;

    /// <summary>Direction the river flows (towards its downstream neighbour).</summary>
    public Vector2 RiverFlow;

    /// <summary>Least fall per metre along a world river's map (the real fall can be a metre in tens of kilometres).</summary>
    private const float MinRiverGradient = 0.0005f;

    /// <param name="p">The generation settings: each neighbour's fall counts by the relief contrast of the two tiles'
    /// hilliness. Null counts every neighbour fully (callers that only need the river).</param>
    public static WorldContext For(Map map, float tileSpacingMetres, GeologyGenerationExtension p = null)
    {
        Tile tile = map.TileInfo;
        var context = new WorldContext
        {
            Elevation = WorldAltitude.At(map.Tile),
            Hilliness = HillinessFor(tile),
            Flow = WorldFlow.At(map.Tile),
        };

        PlanetTile here = map.Tile;
        var neighbours = new List<PlanetTile>();
        Find.WorldGrid.GetTileNeighbors(here, neighbours);

        // The altitude our code reads is the smooth field of WorldAltitude (vanilla's tile elevations jump by hundreds of
        // metres between neighbours, #201), so the fall to a neighbour is the regional slope and not noise.
        float hereHeight = context.Elevation;
        Vector2 downhill = Vector2.zero, sea = Vector2.zero;
        foreach (PlanetTile n in neighbours)
        {
            Tile other = Find.WorldGrid[n];
            Vector2 dir = Heading(Find.WorldGrid.GetHeadingFromTo(here, n));
            // Weight each neighbour by how much lower it is; higher neighbours push the other way. The fall counts by the
            // relief contrast of the two tiles: a foot slope between plain and hills is a landform the map shows, the tilt
            // between two plain tiles' coarse means is not. A coast is always a boundary.
            float weight = p == null || other.WaterCovered ? 1f
                : RegionalSlopeMath.NeighbourWeight(p.ReliefFor(context.Hilliness), p.ReliefFor(other.hilliness), p.uniformSlopeShare);
            downhill += dir * (hereHeight - Mathf.Max(WorldAltitude.At(n), 0f)) * weight;
            if (other.WaterCovered)
                sea += dir;
        }
        if (sea.sqrMagnitude > 1e-6f)
        {
            context.SeaDirection = sea.normalized;
            downhill += context.SeaDirection * Mathf.Max(hereHeight, 1f) * neighbours.Count;
        }
        if (downhill.sqrMagnitude > 1e-6f)
        {
            context.Downhill = downhill.normalized;
            context.RegionalGradient = downhill.magnitude / Mathf.Max(1, neighbours.Count) / tileSpacingMetres;
        }

        if (tile is SurfaceTile surface && !surface.Rivers.NullOrEmpty()
            && tile.Mutators.Any(m => m.Worker is TileMutatorWorker_River))
        {
            // Same ordering vanilla uses to orient the river on the map: upstream links first, the downstream one last.
            // A single link is the river's upstream at a river mouth (by the sea) and its downstream at a headwater.
            // As in vanilla's confluence, a link to a water-covered tile (sea or lake) is the river's mouth, and every
            // link over land flows in, the widest being the main river.
            List<SurfaceTile.RiverLink> links = surface.Rivers.OrderBy(rl => -((SurfaceTile)Find.WorldGrid[rl.neighbor]).riverDist).ToList();
            List<SurfaceTile.RiverLink> toWater = links.Where(rl => Find.WorldGrid[rl.neighbor].WaterCovered).ToList();
            bool mouth = toWater.Count > 0 || (links.Count == 1 && context.SeaDirection != Vector2.zero);
            SurfaceTile.RiverLink downstream = toWater.Count > 0 ? toWater.First() : links.Last();
            List<SurfaceTile.RiverLink> upstream = toWater.Count > 0
                ? links.Where(rl => !toWater.Contains(rl)).OrderByDescending(rl => rl.river.widthOnMap).ToList()
                : mouth ? links : links.Take(links.Count - 1).ToList();
            context.HasRiver = true;
            context.RiverExit = toWater.Count == 0 && mouth ? context.SeaDirection : Heading(Find.WorldGrid.GetHeadingFromTo(here, downstream.neighbor));
            context.RiverExitWidth = links.Max(rl => rl.river.widthOnMap);
            foreach (SurfaceTile.RiverLink link in upstream)
            {
                context.RiverInflows.Add(new RiverInflow
                {
                    From = Heading(Find.WorldGrid.GetHeadingFromTo(here, link.neighbor)),
                    Width = link.river.widthOnMap,
                    Flow = WorldFlow.At(link.neighbor),
                });
            }
            context.RiverFlow = context.RiverInflows.Count > 0
                ? (context.RiverExit - context.RiverInflows[0].From).normalized
                : context.RiverExit;
            // A river's course is the world's to give: it enters where its upstream links lie and leaves toward its downstream
            // one. The map therefore slopes the way the river flows (not the other way round, with the river following
            // whatever slope the neighbours' elevations happen to give), and the fall is the river's own: from where it
            // comes (the mean of its upstream tiles, or this tile at a headwater) to where it goes (the downstream tile,
            // or the sea), over the tile centres between them. Water entering never has to climb to its exit.
            if (context.RiverFlow.sqrMagnitude > 1e-6f)
            {
                context.Downhill = context.RiverFlow.normalized;
                float upHeight = upstream.Count > 0 ? upstream.Average(rl => WorldAltitude.At(rl.neighbor)) : hereHeight;
                bool toSea = toWater.Count > 0 ? Find.WorldGrid[downstream.neighbor].PrimaryBiome == BiomeDefOf.Ocean : mouth;
                float downHeight = toSea ? 0f : Mathf.Max(WorldAltitude.At(downstream.neighbor), 0f);
                float tiles = upstream.Count > 0 ? 2f : 1f;
                context.RegionalGradient = Mathf.Max(MinRiverGradient, (upHeight - downHeight) / (tiles * tileSpacingMetres));
            }
            Rand.PushState(Gen.HashCombineInt(Find.World.info.Seed, here.tileId));
            context.RiverCentre = new Vector2(Rand.Range(-0.2f, 0.2f), Rand.Range(-0.2f, 0.2f));
            Rand.PopState();
        }
        return context;
    }

    /// <summary>
    /// The tile's hilliness for terrain generation. Some mutators (plateau, cliffs) force vanilla's noise flat so it
    /// can't disturb their shape; that doesn't apply to landforms the geology models, so those keep the world's value.
    /// </summary>
    private static Hilliness HillinessFor(Tile tile)
    {
        foreach (TileMutatorDef mutator in tile.Mutators)
        {
            if (mutator.hillinessForElevationGen != Hilliness.Undefined && !mutator.HasModExtension<LandformExtension>())
                return mutator.hillinessForElevationGen;
        }
        return tile.hilliness;
    }

    /// <summary>World heading in degrees (0 = north, 90 = east) as a map-space unit vector.</summary>
    public static Vector2 Heading(float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
    }
}
