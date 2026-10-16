using System.Linq;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>
/// The natural landform, metres above sea level: the surface erosion shaped, before it is stepped into terraces.
/// Soil and mountain rock follow it; the walkable terraced height (<see cref="HeightWorker"/>) is derived from it.
/// On (geological terrain): generated from geological principles right after vanilla's elevation step, and then
/// drives where rock, scree and soil are placed (<see cref="LandscapeGenerator"/>).
/// Off: inferred from the terrain vanilla placed, just before plants (<see cref="HeightInference"/>).
/// </summary>
public class LandformWorker : EnvironmentLayerWorker
{
    /// <summary>Typical spread of vanilla's elevation grid, used to turn inferred heights into metres.</summary>
    private const float VanillaElevationSpread = 0.6f;

    public override GenerationStage StageFor(Map map) =>
        GeologicalTerrain.AppliesTo(map) ? GenerationStage.Elevation : GenerationStage.Plants;

    public override float[] Compute(EnvironmentContext context)
    {
        Map map = context.Map;
        var generation = def.GetModExtension<GeologyGenerationExtension>() ?? new GeologyGenerationExtension();

        if (StageFor(map) == GenerationStage.Elevation)
        {
            float[] generated = LandscapeGenerator.Generate(map, generation, out WorldContext world, out var riverCourse);
            var environment = map.GetComponent<MapComponent_Environment>();
            if (environment != null)
                environment.PlannedRiverCourse = riverCourse;
            Log.Message($"[LivingLandscapes] Geological terrain for {map}: tile {world.Elevation:F0} m, {world.Hilliness}, " +
                        $"relief {generation.ReliefFor(world.Hilliness):F0} m, downhill ({world.Downhill.x:F2}, {world.Downhill.y:F2}), " +
                        $"river {(world.HasRiver ? $"flowing ({world.RiverFlow.x:F2}, {world.RiverFlow.y:F2})" : "none")}, " +
                        $"sea {(world.SeaDirection == UnityEngine.Vector2.zero ? "none" : $"({world.SeaDirection.x:F2}, {world.SeaDirection.y:F2})")}, " +
                        $"mutators [{string.Join(", ", map.TileInfo.Mutators.Select(m => m.defName))}].");
            return generated;
        }

        float[] vanilla = context.Get(EnvironmentLayerDefOf.LL_Elevation);
        if (vanilla == null)
            return null;
        var inference = def.GetModExtension<HeightInferenceExtension>() ?? new HeightInferenceExtension();
        float[] inferred = HeightInference.Infer(map, vanilla, inference);

        // Inference works in vanilla's units; express it in metres around the tile's elevation so both modes agree.
        float tileElevation = map.TileInfo.elevation;
        float metresPerUnit = generation.ReliefFor(map.TileInfo.HillinessForElevationGen) / VanillaElevationSpread;
        for (int i = 0; i < inferred.Length; i++)
            inferred[i] = tileElevation + (inferred[i] - inference.rockThreshold * 0.5f) * metresPerUnit;
        return inferred;
    }
}
