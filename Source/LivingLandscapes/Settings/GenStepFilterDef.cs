using System.Collections.Generic;
using RimWorld;
using Verse;

namespace LivingLandscapes.Settings;

/// <summary>
/// A named list of vanilla or mod map generation parts the mod leaves out: generation steps (scattered ruins, a mod's
/// cenote buildings) and the shaping of tile mutators (cave tunnels, dune shaping). When a list applies is decided by
/// the code that reads it. Kept in XML (<c>1.7/Defs/MapGeneration/GenStepFilters.xml</c>) so DLC and modded entries can
/// be added with MayRequire.
/// </summary>
public class GenStepFilterDef : Def
{
    public List<GenStepDef> genSteps = new List<GenStepDef>();

    /// <summary>
    /// Tile mutators whose own elevation and terrain shaping is not run. The mutator stays on the tile, with its other
    /// effects (weather, plants, structures, extra generation steps).
    /// </summary>
    public List<TileMutatorDef> mutators = new List<TileMutatorDef>();
}
