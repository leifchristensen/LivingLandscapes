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

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref geologicalTerrain, "geologicalTerrain", true);
    }
}
