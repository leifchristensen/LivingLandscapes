using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// A landform that gives low, gentle country its rock (#51): remnant hills of a hard bed (buttes, inselbergs), a single
/// escarpment where the land steps up, or bluffs where a river cuts into the plain. Erosion leaves these on plains
/// worldwide; which one a map gets follows the tile's data (hilliness, rock types, a world river), so flat maps differ
/// and each has rock to mine and defend. One feature is drawn per map, by weight, from those whose conditions hold.
/// The shape is a <see cref="LandformExtension"/> (uplift and hardness the erosion model shapes like any other landform),
/// laid <see cref="count"/> times with the ranges below drawn per instance.
/// </summary>
public class PlainsFeatureDef : Def
{
    /// <summary>Tiles of these hilliness levels can get the feature.</summary>
    public List<Hilliness> hilliness = new List<Hilliness>();

    /// <summary>The tile must have one of these rock types (defName); empty: any.</summary>
    public List<string> rockTypes = new List<string>();

    /// <summary>The tile must have a world river (bluffs along its valley).</summary>
    public bool needsRiver;

    /// <summary>Relative chance among the features whose conditions hold.</summary>
    public float weight = 1f;

    /// <summary>How many of the shape the map gets.</summary>
    public IntRange count = new IntRange(1, 1);

    /// <summary>Metres the feature adds to the map's relief: how far it stands above (or cuts below) the plain.</summary>
    public FloatRange extraRelief = new FloatRange(10f, 20f);

    /// <summary>The shape; orientation Random turns each instance its own way. Ignored with <see cref="alongRivers"/> for direction.</summary>
    public LandformExtension landform = new LandformExtension();

    /// <summary>Per-instance ranges for the shape's size and its offset from the map centre (fractions of the map width).</summary>
    public FloatRange size = FloatRange.Zero;
    public FloatRange offset = FloatRange.Zero;

    /// <summary>Laid as rays from where the world rivers meet toward each river's side, like the river valley (bluffs).</summary>
    public bool alongRivers;

    /// <summary>Whether this feature fits the tile.</summary>
    public bool Fits(Tile tile, WorldContext world, IEnumerable<ThingDef> rocks)
    {
        if (!hilliness.Contains(world.Hilliness))
            return false;
        if (needsRiver && !world.HasRiver)
            return false;
        return rockTypes.Count == 0 || rocks.Any(r => rockTypes.Contains(r.defName));
    }

    /// <summary>The feature for a map, drawn by weight among those that fit; null if none does.</summary>
    public static PlainsFeatureDef For(Map map, WorldContext world)
    {
        List<ThingDef> rocks = Find.World.NaturalRockTypesIn(map.Tile).ToList();
        List<PlainsFeatureDef> fitting = DefDatabase<PlainsFeatureDef>.AllDefsListForReading.Where(d => d.Fits(map.TileInfo, world, rocks)).ToList();
        return fitting.Count == 0 ? null : fitting.RandomElementByWeight(d => d.weight);
    }

    /// <summary>One instance of the shape, its ranges drawn.</summary>
    public LandformExtension Draw()
    {
        LandformExtension f = landform;
        return new LandformExtension
        {
            shape = f.shape, orientation = f.orientation, offset = offset.max > 0f ? offset.RandomInRange : f.offset,
            size = size.max > 0f ? size.RandomInRange : f.size, edge = f.edge, upliftInside = f.upliftInside,
            upliftOutside = f.upliftOutside, erodibilityInside = f.erodibilityInside, warp = f.warp, replacesVanillaElevation = false,
        };
    }
}
