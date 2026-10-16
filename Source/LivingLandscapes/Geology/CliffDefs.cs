using System.Collections.Generic;
using RimWorld;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// The cliff pieces the geology places: our own climbable bands with Decorative Cliffs' art (linking with its pieces),
/// tinted with the local rock, and our steps. Which piece faces which way: lower ground south shows the face (bottom
/// left), west and east show the face from the side (bottom left / bottom right), north shows only the top edge.
/// </summary>
public static class CliffDefs
{
    public static ThingDef FacingSouth => DefDatabase<ThingDef>.GetNamedSilentFail("LL_Cliff_Left");
    public static ThingDef FacingWest => DefDatabase<ThingDef>.GetNamedSilentFail("LL_Cliff_Left");
    public static ThingDef FacingEast => DefDatabase<ThingDef>.GetNamedSilentFail("LL_Cliff_Right");
    public static ThingDef FacingNorth => DefDatabase<ThingDef>.GetNamedSilentFail("LL_CliffTop_Left");
    public static ThingDef Steps => DefDatabase<ThingDef>.GetNamedSilentFail("LL_CliffSteps");

    private static HashSet<ThingDef> all;

    /// <summary>The band piece for a cliff whose lower ground lies in the given direction.</summary>
    public static ThingDef BandFacing(Rot4 facing)
    {
        if (facing == Rot4.North) return FacingNorth;
        if (facing == Rot4.East) return FacingEast;
        if (facing == Rot4.West) return FacingWest;
        return FacingSouth;
    }

    /// <summary>True for any cliff band or steps the geology places.</summary>
    public static bool IsCliff(ThingDef def)
    {
        if (all == null)
        {
            all = new HashSet<ThingDef>();
            foreach (ThingDef d in new[] { FacingSouth, FacingWest, FacingEast, FacingNorth, Steps })
            {
                if (d != null)
                    all.Add(d);
            }
        }
        return def != null && all.Contains(def);
    }

    /// <summary>The stone blocks a rock type yields (the stuff Decorative Cliffs' stone cliffs are made of).</summary>
    public static ThingDef BlocksOf(ThingDef rock)
    {
        List<ThingDefCountClass> products = rock?.building?.mineableThing?.butcherProducts;
        if (products != null)
        {
            foreach (ThingDefCountClass product in products)
            {
                if (product.thingDef?.stuffProps?.categories?.Contains(StuffCategoryDefOf.Stony) == true)
                    return product.thingDef;
            }
        }
        return null;
    }
}
