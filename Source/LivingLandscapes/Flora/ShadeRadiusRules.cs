namespace LivingLandscapes.Flora;

/// <summary>
/// The radius over which a plant's shade spreads. It never reads the plant's drawn size: visual mods (Better Trees
/// scales every tree's visualSizeRange, default x2) change how big a crown is drawn, not how big the plant is, so shade
/// would otherwise differ with the mod list (#166). A plant either states its radius in its habitat or takes its
/// stratum's typical crown.
/// </summary>
public static class ShadeRadiusRules
{
    /// <summary>Typical canopy crown radius (cells): the value every described vanilla tree uses (about 1.1 x vanilla's tallest drawn size).</summary>
    public const float CanopyDefault = 2.2f;

    /// <summary>Radius for plants below the canopy (they cast no shade unless their habitat says so).</summary>
    public const float UnderstoryDefault = 1f;

    /// <summary>
    /// Fewest days a tree takes to mature for it to be canopy. Time to maturity follows stature (a bigger plant has more
    /// to build), and growDays is a vanilla field no visual mod rescales: vanilla trees take 12 to 35 days, a dwarf palm
    /// 6 (#193). A "tree" faster than this is understory and casts no canopy shade.
    /// </summary>
    public const float CanopyMinGrowDays = 10f;

    /// <summary>Whether a plant no habitat describes is canopy: a tree that takes long enough to grow to a real crown.</summary>
    public static bool IsCanopy(bool isTree, float growDays) => isTree && growDays >= CanopyMinGrowDays;

    /// <summary>The habitat's radius when it states one, else the stratum's typical crown.</summary>
    public static float Radius(float habitatRadius, bool canopy)
        => habitatRadius > 0f ? habitatRadius : canopy ? CanopyDefault : UnderstoryDefault;
}
