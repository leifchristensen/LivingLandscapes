using LivingLandscapes.Environment;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>
/// The water field the terrain is built on (#132, plan decision D4). The law of how the water sources combine is written
/// once, in <see cref="WaterFieldMath"/>; this is the game-side door to it, so that every reader that asks "how wet is
/// this ground" (soil fertility, wet ground, settlement siting) asks the same question the same way and follows the same
/// switch. While <see cref="MoistureExtension.terrainReadsMoisture"/> is off - the default - those readers get the
/// topographic wetness grid (<c>LL_Wetness</c>) unchanged, cell for cell, exactly as before the two water layers were
/// merged. With it on they get the ground term G: that wetness raised by the moisture the rain already keeps in freely
/// drained ground, so a wet climate wets the ground even where the terrain drains it. Plants never read this; they read
/// the full moisture <c>LL_Moisture</c>, which also adds nearness to open water and does not change with the switch.
/// </summary>
public static class WaterField
{
    /// <summary>
    /// The water value for the terrain of a map whose layers are computed: the wetness grid as it is while the switch is
    /// off, else that grid with <see cref="WaterFieldMath.TerrainValue"/> applied to every cell. Null when the map has no
    /// wetness grid, which is how these readers already say "no terrain to go on".
    /// </summary>
    public static float[] TerrainWater(MapComponent_Environment environment, Map map)
    {
        if (environment == null || !environment.TryGetGrid(EnvironmentLayerDefOf.LL_Wetness, out EnvironmentGrid grid))
            return null;
        float[] water = grid.ToArray();

        var p = EnvironmentLayerDefOf.LL_Moisture.GetModExtension<MoistureExtension>() ?? new MoistureExtension();
        if (!p.terrainReadsMoisture)
            return water;

        // The switch is on: the ground term of the same wetness and the same rain baseline the plants' moisture uses.
        float climate = MoistureWorker.ClimateBaseline(MoistureWorker.AridityIndex(map), p);
        for (int i = 0; i < water.Length; i++)
            water[i] = WaterFieldMath.TerrainValue(water[i], climate, true);
        return water;
    }

    /// <summary>The same, for a layer worker that already holds its <see cref="EnvironmentContext"/>.</summary>
    public static float[] TerrainWater(EnvironmentContext context) => TerrainWater(context.Environment, context.Map);
}
