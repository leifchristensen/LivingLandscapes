using RimWorld;
using Verse;

namespace LivingLandscapes.Geology;

[DefOf]
public static class CliffDefOf
{
    public static ThingDef LL_CliffSheer;
    public static ThingDef LL_CliffScarp;

    static CliffDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(CliffDefOf));
    }
}
