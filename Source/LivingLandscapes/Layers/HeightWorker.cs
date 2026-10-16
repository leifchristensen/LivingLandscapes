using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>
/// The geological height every downstream layer (drainage, slope, moisture, plants) reads.
/// Off: inferred from the terrain vanilla placed (<see cref="HeightInference"/>).
/// On: generated from geological principles before terrain placement; not built yet, so it falls back to inference.
/// </summary>
public class HeightWorker : EnvironmentLayerWorker
{
    private static bool loggedFallback;

    public override float[] Compute(EnvironmentContext context)
    {
        float[] vanilla = context.Get(EnvironmentLayerDefOf.LL_Elevation);
        if (vanilla == null)
            return null;

        if (LivingLandscapesMod.Settings.geologicalTerrain && !loggedFallback)
        {
            loggedFallback = true;
            Log.Message("[LivingLandscapes] Geological terrain generation is not built yet; height is inferred from vanilla terrain.");
        }

        var p = def.GetModExtension<HeightInferenceExtension>() ?? new HeightInferenceExtension();
        return HeightInference.Infer(context.Map, vanilla, p);
    }
}
