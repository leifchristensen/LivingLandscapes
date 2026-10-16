using System.Collections.Generic;
using Verse;

namespace LivingLandscapes.Settings;

/// <summary>
/// A named list of vanilla map generation steps that a setting can switch off (e.g. scattered ruins). Kept in XML
/// (<c>1.7/Defs/MapGeneration/GenStepFilters.xml</c>) so DLC and modded steps can be added with MayRequire.
/// </summary>
public class GenStepFilterDef : Def
{
    public List<GenStepDef> genSteps = new List<GenStepDef>();
}
