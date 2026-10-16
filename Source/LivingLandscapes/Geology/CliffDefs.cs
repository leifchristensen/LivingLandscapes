using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// The cliff pieces the geology places: <c>LL_Cliff</c> bands and <c>LL_CliffSteps</c>, both <see cref="Building_Cliff"/>
/// (thin edges on the sides that drop to lower ground, tinted with the local rock).
/// </summary>
public static class CliffDefs
{
    public static ThingDef Band => DefDatabase<ThingDef>.GetNamedSilentFail("LL_Cliff");
    public static ThingDef Steps => DefDatabase<ThingDef>.GetNamedSilentFail("LL_CliffSteps");

    /// <summary>True for any cliff band or steps the geology places.</summary>
    public static bool IsCliff(ThingDef def) => def != null && (def == Band || def == Steps);
}
