using LivingLandscapes.Environment;
using LudeonTK;
using RimWorld;
using Verse;

namespace LivingLandscapes.DevTools;

/// <summary>Dev-mode actions under "Living Landscapes" in the debug actions menu. Overlays are chosen with the
/// map toggle in the lower right corner.</summary>
public static class EnvironmentDebugActions
{
    private static MapComponent_Environment CurrentEnvironment()
    {
        var environment = Find.CurrentMap?.GetComponent<MapComponent_Environment>();
        if (environment == null || !environment.Overlay.Available.GetEnumerator().MoveNext())
        {
            Messages.Message("[LivingLandscapes] This map has no environment layers (generated without the mod, or plants step skipped).",
                MessageTypeDefOf.RejectInput, historical: false);
            return null;
        }
        return environment;
    }

    [DebugAction("Living Landscapes", "Export environment layers (PNG)", allowedGameStates = AllowedGameStates.IsCurrentlyOnMap)]
    private static void Export()
    {
        if (CurrentEnvironment() != null)
            LayerImageExporter.ExportAll(Find.CurrentMap);
    }

    [DebugAction("Living Landscapes", "Recompute environment layers", allowedGameStates = AllowedGameStates.IsCurrentlyOnMap)]
    private static void Recompute()
    {
        Find.CurrentMap?.GetComponent<MapComponent_Environment>()?.ComputeLayers(GenerationStage.Plants);
    }
}
