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

    [DebugAction("Living Landscapes", "Feature regions: show one region", allowedGameStates = AllowedGameStates.IsCurrentlyOnMap)]
    private static void ChooseFeatureRegion()
    {
        Geology.PlannedRegions regions = Find.CurrentMap?.GetComponent<MapComponent_Environment>()?.PlannedRegions;
        if (regions == null)
        {
            Messages.Message("[LivingLandscapes] Feature regions: regions are only known on a freshly generated map.",
                MessageTypeDefOf.RejectInput, historical: false);
            return;
        }
        var options = new System.Collections.Generic.List<DebugMenuOption>
        {
            new DebugMenuOption("All regions", DebugMenuOptionMode.Action, () => ShowRegion(null)),
        };
        foreach (Geology.PlannedRegion row in regions.Table)
        {
            Geology.PlannedRegion chosen = row;
            string label = $"{row.number}: {row.region.type.label} ({row.region.kind}, toward ({row.region.direction.x:F1}, {row.region.direction.y:F1}))";
            options.Add(new DebugMenuOption(label, DebugMenuOptionMode.Action, () => ShowRegion(chosen)));
        }
        Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
    }

    private static void ShowRegion(Geology.PlannedRegion row)
    {
        var environment = Find.CurrentMap?.GetComponent<MapComponent_Environment>();
        if (environment?.PlannedRegions == null)
            return;
        environment.PlannedRegions.Shown = row;
        // Switched off and on again, so the picture is computed for the choice.
        if (environment.Overlay.IsActive(EnvironmentLayerDefOf.LL_FeatureRegion))
            environment.Overlay.Toggle(EnvironmentLayerDefOf.LL_FeatureRegion);
        environment.Overlay.Toggle(EnvironmentLayerDefOf.LL_FeatureRegion);
    }

    private static void ShowFitnessOf(ThingDef plant)
    {
        Layers.FitnessWorker.Selected = plant;
        var environment = Find.CurrentMap?.GetComponent<MapComponent_Environment>();
        if (environment == null || !environment.ComputeOnDemand(EnvironmentLayerDefOf.LL_Fitness))
            return;
        environment.Overlay.Notify_LayersChanged();
        if (!environment.Overlay.IsActive(EnvironmentLayerDefOf.LL_Fitness))
            environment.Overlay.Toggle(EnvironmentLayerDefOf.LL_Fitness);
    }
}
