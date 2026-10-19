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

    /// <summary>Direction the region slopes down towards (lower neighbours, the sea), or zero on level ground.</summary>
    public Vector2 Downhill;

    /// <summary>Regional fall per metre towards <see cref="Downhill"/>, from neighbouring tile elevations.</summary>
    public float RegionalGradient;

    /// <summary>Direction the sea lies in, or zero if no neighbouring tile is sea.</summary>
    public Vector2 SeaDirection;

    public bool HasRiver;

    /// <summary>A world river entering the map: the side it comes from and the width vanilla gives it.</summary>
    public struct RiverInflow
    {
        public Vector2 From;
        public float Width;
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

    public static WorldContext For(Map map, float tileSpacingMetres)
    {
        Tile tile = map.TileInfo;
        var context = new WorldContext
        {
            Elevation = tile.elevation,
            Hilliness = HillinessFor(tile),
        };

        PlanetTile here = map.Tile;
        var neighbours = new List<PlanetTile>();
        Find.WorldGrid.GetTileNeighbors(here, neighbours);

        Vector2 downhill = Vector2.zero, sea = Vector2.zero;
        foreach (PlanetTile n in neighbours)
        {
            Tile other = Find.WorldGrid[n];
            Vector2 dir = Heading(Find.WorldGrid.GetHeadingFromTo(here, n));
            // Weight each neighbour by how much lower it is; higher neighbours push the other way.
            downhill += dir * (tile.elevation - Mathf.Max(other.elevation, 0f));
            if (other.WaterCovered)
                sea += dir;
        }
        if (sea.sqrMagnitude > 1e-6f)
        {
            context.SeaDirection = sea.normalized;
            downhill += context.SeaDirection * Mathf.Max(tile.elevation, 1f) * neighbours.Count;
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
                });
            }
            context.RiverFlow = context.RiverInflows.Count > 0
                ? (context.RiverExit - context.RiverInflows[0].From).normalized
                : context.RiverExit;
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
