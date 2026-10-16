using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// How a natural rock type behaves in the geology, set on its ThingDef in XML (vanilla rocks are patched in
/// <c>1.7/Patches/RockProperties.xml</c>). Rocks from other mods without it get middle values.
/// </summary>
public class RockProperties : DefModExtension
{
    /// <summary>Resistance to erosion, 0..1: hard beds stand as ridges, narrows and spires; soft beds wear into valleys.</summary>
    public float strength = 0.6f;

    /// <summary>How easily water dissolves it, 0..1; reserved for cave routing.</summary>
    public float solubility;

    private static readonly RockProperties Default = new RockProperties();

    public static RockProperties Of(ThingDef rock) => rock?.GetModExtension<RockProperties>() ?? Default;
}
