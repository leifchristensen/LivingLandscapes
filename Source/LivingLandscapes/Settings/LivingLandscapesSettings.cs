using Verse;

namespace LivingLandscapes.Settings;

/// <summary>Player options, saved by RimWorld in the mod's config file.</summary>
public class LivingLandscapesSettings : ModSettings
{
    /// <summary>
    /// Reshape newly generated maps from geological rules (drainage, basins, valleys). Off: the mod only reads
    /// the terrain other generators made, for players who run unsupported terrain-generation mods.
    /// </summary>
    public bool geologicalTerrain = true;

    /// <summary>
    /// Vanilla's scattered ruins and ancient debris (<c>LL_ScatteredRuins</c>). Off for now: they look out of place
    /// until terrain generation is polished.
    /// </summary>
    public bool scatteredRuins;

    /// <summary>
    /// Plant ecology: wild plants follow the ground (wetness, soil, slope, sun), cluster by dispersal and shade each other
    /// (Docs/FLORA-REVIEW.md). Off: vanilla's wild plant placement.
    /// </summary>
    public bool plantEcology = true;

    /// <summary>How strongly the ecology applies, 0 (vanilla) to 1 (full).</summary>
    public float habitatStrength = 1f;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref geologicalTerrain, "geologicalTerrain", true);
        Scribe_Values.Look(ref scatteredRuins, "scatteredRuins", false);
        Scribe_Values.Look(ref plantEcology, "plantEcology", true);
        Scribe_Values.Look(ref habitatStrength, "habitatStrength", 1f);
    }
}
