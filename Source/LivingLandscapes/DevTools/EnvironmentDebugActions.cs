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

    [DebugAction("Living Landscapes", "Plant fitness: choose species", allowedGameStates = AllowedGameStates.IsCurrentlyOnMap)]
    private static void ChooseFitnessSpecies()
    {
        var options = new System.Collections.Generic.List<DebugMenuOption>
        {
            new DebugMenuOption("Best of biome", DebugMenuOptionMode.Action, () => ShowFitnessOf(null)),
        };
        foreach (ThingDef plant in Find.CurrentMap.Biome.AllWildPlants)
        {
            ThingDef chosen = plant;
            options.Add(new DebugMenuOption(plant.defName, DebugMenuOptionMode.Action, () => ShowFitnessOf(chosen)));
        }
        Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
    }

    private static void ShowFitnessOf(ThingDef plant)
    {
        Layers.FitnessWorker.Selected = plant;
        Find.CurrentMap?.GetComponent<MapComponent_Environment>()?.ComputeLayers(GenerationStage.Plants);
    }
}
