using System.Linq;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>Decides whether a map gets geologically driven terrain ("On" mode) or inferred height ("Off" mode).</summary>
public static class GeologicalTerrain
{
    /// <summary>
    /// On when the setting is on and vanilla would generate natural surface terrain here: not underground, not a
    /// sea tile, and no tile mutator that replaces natural elevation (e.g. space or cavern maps).
    /// </summary>
    public static bool AppliesTo(Map map)
    {
        if (!LivingLandscapesMod.Settings.geologicalTerrain || map?.TileInfo == null)
            return false;
        if (map.generatorDef != null && map.generatorDef.isUnderground)
            return false;
        if (map.TileInfo.WaterCovered)
            return false;
        return !map.TileInfo.Mutators.Any(m => m.preventNaturalElevation);
    }
}
