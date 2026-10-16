using Verse;

namespace LivingLandscapes.Geology;

/// <summary>Cliff piece defs by kind and length: Small, Medium, Large = 1, 2, 3 cells along the cliff line.</summary>
public static class CliffDefs
{
    public const int MaxLength = 3;

    public static ThingDef For(CliffKind kind, int length)
    {
        string prefix = kind == CliffKind.Sheer ? "LL_Cliff" : "LL_Scarp";
        string size = length <= 1 ? "Small" : length == 2 ? "Medium" : "Large";
        return DefDatabase<ThingDef>.GetNamed(prefix + size);
    }
}
