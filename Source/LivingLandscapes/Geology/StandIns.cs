using LivingLandscapes.Settings;
using RimWorld;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// Vanilla and mod stand-ins that draw a feature straight onto the map (cave tunnels, dune shaping, a cenote building).
/// With geological terrain on they are not run: caves and dunes are to come from processes of their own
/// (FEATURE-REGIONS.md phase 7), and until then those tiles are ordinary geological land. Which tile mutators and
/// generation steps count is listed in XML (<c>LL_GeologyStandIns</c>, <c>1.7/Defs/MapGeneration/GenStepFilters.xml</c>).
/// </summary>
public static class StandIns
{
    /// <summary>The list itself; null if its XML failed to load (vanilla then shapes as before).</summary>
    public static GenStepFilterDef List => DefDatabase<GenStepFilterDef>.GetNamedSilentFail("LL_GeologyStandIns");

    /// <summary>Whether the mutator's own shaping is left out where geological terrain applies.</summary>
    public static bool Replaces(TileMutatorDef mutator) => mutator != null && List?.mutators.Contains(mutator) == true;
}
