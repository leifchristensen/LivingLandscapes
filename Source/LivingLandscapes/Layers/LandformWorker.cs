using System.Linq;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using UnityEngine;
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

    /// <summary>Compass point of a map direction (north is +z).</summary>
    private static string Compass(UnityEngine.Vector2 v)
    {
        string[] points = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };
        int sector = Mathf.RoundToInt(Mathf.Atan2(v.y, v.x) / (Mathf.PI / 4f));
        return points[(sector % 8 + 8) % 8];
    }

    public override float[] Compute(EnvironmentContext context)
    {
        Map map = context.Map;
        var generation = def.GetModExtension<GeologyGenerationExtension>() ?? new GeologyGenerationExtension();

        if (StageFor(map) == GenerationStage.Elevation)
        {
            float[] generated = LandscapeGenerator.Generate(map, generation, out WorldContext world, out WaterRegimeDef water, out var channels,
                out RockStrata strata, out float[] rockStrength, out ThingDef[] rock);
            // Channels from the landform's own drainage, sized by discharge and carved to a never-rising water surface
            // (Docs/WATER-AUDIT.md). The river system isn't on the map yet, so its regime's climate offset is added here.
            float climate = Mathf.Clamp01((EnvironmentLayerDefOf.LL_Wetness.GetModExtension<WetnessExtension>() ?? new WetnessExtension()).ClimateOf(map)
                                          + (water?.climateOffset ?? 0f));
            var riverCentre = new Vector2(map.Size.x * (0.5f + world.RiverCentre.x), map.Size.z * 0.5f + world.RiverCentre.y * map.Size.x);
            // Mutator structures nudge the ground first (headwater hollow, island knob, delta flats); water follows.
            Vector2? exit = world.HasRiver ? WaterTerrainNudges.EdgeToward(riverCentre, world.RiverExit, map.Size.x, map.Size.z) : (Vector2?)null;
            Vector2 knobCentre = water != null && water.centreKnob.max > 0f
                ? TerrainChannels.NearestOnWorldRiver(generated, map.Size.x, map.Size.z, channels, riverCentre, water, generation.terrainChannels,
                    generation.network.worldRiverWidth)
                : riverCentre;
            string nudged = WaterTerrainNudges.Apply(generated, rockStrength, map.Size.x, map.Size.z, riverCentre, knobCentre, exit, water,
                generation.terrainChannels);
            if (nudged.Length > 0)
                Log.Message($"[LivingLandscapes] Water regime {water.defName} nudged the terrain: {nudged}.");
            RiverSystem rivers = TerrainChannels.Build(generated, map.Size.x, map.Size.z, channels, riverCentre, climate, map.Biome?.defName, rockStrength, water,
                generation.terrainChannels, generation.network.worldRiverWidth, Coastline.SeaCells(map), world.RiverInflows,
                world.HasRiver ? float.NaN : world.Flow);
            var environment = map.GetComponent<MapComponent_Environment>();
            if (environment != null)
            {
                environment.PlannedRivers = rivers.Channels.Count > 0 ? rivers : null;
                environment.PlannedRockStrength = rockStrength;
                environment.PlannedRock = rock;
            }
            Log.Message($"[LivingLandscapes] Geological terrain for {map}: tile {world.Elevation:F0} m, {world.Hilliness}, " +
                        $"relief {generation.ReliefFor(world.Hilliness):F0} m, downhill ({world.Downhill.x:F2}, {world.Downhill.y:F2}), " +
                        $"river {(world.HasRiver ? $"in from [{string.Join(" ", world.RiverInflows.Select(r => $"{Compass(r.From)} {r.Width:F0}"))}] out toward {Compass(world.RiverExit)}, meeting at ({world.RiverCentre.x:F2}, {world.RiverCentre.y:F2})" : "none")}, " +
                        $"water regime {water?.defName ?? "none"}, {rivers.Channels.Count} channels (widths {string.Join(" ", rivers.Channels.Select(c => c.NominalWidth.ToString("F0")))}; " +
                        $"), " +
                        $"sea {(world.SeaDirection == UnityEngine.Vector2.zero ? "none" : $"({world.SeaDirection.x:F2}, {world.SeaDirection.y:F2})")}, " +
                        $"mutators [{string.Join(", ", map.TileInfo.Mutators.Select(m => m.defName))}], " +
                        $"{strata.BedCount} rock beds {strata.BedThickness.min:F0}-{strata.BedThickness.max:F0} m thick dipping {strata.DipDegrees:F1} deg toward ({strata.DipDirection.x:F2}, {strata.DipDirection.y:F2}).");
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
