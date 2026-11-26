using System.Linq;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>Decides whether a map gets geologically driven terrain ("On" mode) or inferred height ("Off" mode).</summary>
public static class GeologicalTerrain
{
    /// <summary>
    /// On when the map is natural surface terrain: not underground, not a sea tile, and any tile mutator that replaces
    /// natural elevation is one the geology models (a <see cref="FeatureTypeDef"/> is anchored on it) or one whose
    /// shaping it leaves out (<see cref="StandIns"/>). Not a player setting (#130).
    /// </summary>
    public static bool AppliesTo(Map map)
    {
        // "-ll-geology=off" gives vanilla terrain for A/B runs.
        if (map?.TileInfo == null || (GenCommandLine.TryGetCommandLineArg("ll-geology", out string mode) && mode == "off"))
            return false;
        if (map.generatorDef != null && map.generatorDef.isUnderground)
            return false;
        if (map.TileInfo.WaterCovered)
            return false;
        return !map.TileInfo.Mutators.Any(m => m.preventNaturalElevation && !FeatureTypeDef.Models(m) && !StandIns.Replaces(m));
    }
}
