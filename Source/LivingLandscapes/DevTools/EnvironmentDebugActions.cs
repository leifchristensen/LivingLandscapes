using System.Collections.Generic;
using LivingLandscapes.Environment;
using LudeonTK;
using RimWorld;
using Verse;

namespace LivingLandscapes.DevTools;

/// <summary>Dev-mode actions under "Living Landscapes" in the debug actions menu.</summary>
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

    [DebugAction("Living Landscapes", "Show environment overlay...", allowedGameStates = AllowedGameStates.IsCurrentlyOnMap)]
    private static void ShowOverlay()
    {
        MapComponent_Environment environment = CurrentEnvironment();
        if (environment == null)
            return;
        var options = new List<DebugMenuOption> { new DebugMenuOption("None", DebugMenuOptionMode.Action, () => environment.Overlay.Select(null)) };
        foreach (EnvironmentLayerDef layer in environment.Overlay.Available)
            options.Add(new DebugMenuOption(layer.LabelCap, DebugMenuOptionMode.Action, () => environment.Overlay.Select(layer)));
        Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
    }

    [DebugAction("Living Landscapes", "Cycle environment overlay", allowedGameStates = AllowedGameStates.IsCurrentlyOnMap)]
    private static void CycleOverlay()
    {
        CurrentEnvironment()?.Overlay.Cycle();
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
