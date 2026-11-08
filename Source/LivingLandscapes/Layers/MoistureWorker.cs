using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>Tuning for <see cref="MoistureWorker"/>, set on the layer Def in XML.</summary>
public class MoistureExtension : DefModExtension
{
    /// <summary>Distance (cells) over which water proximity falls to 1/e.</summary>
    public float waterReachCells = 10f;

    /// <summary>
    /// Site water balance (P25): the moisture rain alone keeps in the root zone of freely drained ground, approached as the
    /// aridity index (rainfall over potential evaporation) grows; half of it where the aridity index is
    /// <see cref="aridityHalf"/>.
    /// </summary>
    public float climateMoistureMax = 0.5f;
    public float aridityHalf = 0.5f;
}

/// <summary>
/// 0..1 moisture: the water rain keeps in freely drained ground (P25, site water balance), raised by topographic wetness
/// (<see cref="WetnessWorker"/>: water gathering from upslope, the water table) and toward 1 near surface water. Each
/// source wets what the others leave dry: moisture = 1 - (1 - climate)(1 - wetness)(1 - proximity).
/// </summary>
public class MoistureWorker : EnvironmentLayerWorker
{
    public override float[] Compute(EnvironmentContext context)
    {
        float[] distance = context.Get(EnvironmentLayerDefOf.LL_WaterDistance);
        float[] wetness = context.Get(EnvironmentLayerDefOf.LL_Wetness);
        if (distance == null)
            return null;

        var p = def.GetModExtension<MoistureExtension>() ?? new MoistureExtension();
        float aridity = AridityIndex(context.Map);
        float climate = p.climateMoistureMax * aridity / (aridity + p.aridityHalf);
        var moisture = new float[distance.Length];
        for (int i = 0; i < moisture.Length; i++)
        {
            float proximity = Mathf.Exp(-distance[i] / p.waterReachCells);
            // The one water formula (#132): rain, terrain and open water, each wetting what the others leave dry.
            moisture[i] = WaterFieldMath.Moisture(climate, wetness?[i] ?? 0f, proximity);
        }
        Log.Message($"[LivingLandscapes] Moisture on {context.Map}: rainfall {context.Map.TileInfo?.rainfall ?? 0f:F0} mm, " +
                    $"aridity index {aridity:F2}, moisture from rain alone {climate:F2} (P25).");
        return moisture;
    }

    /// <summary>
    /// Aridity index P / PET: the tile's yearly rainfall over the evaporation its mean temperature drives, the same
    /// evaporation the lake model balances (<see cref="SurfaceWaterExtension.Evaporation"/>).
    /// </summary>
    public static float AridityIndex(Map map)
    {
        var surface = EnvironmentLayerDefOf.LL_SurfaceWater.GetModExtension<SurfaceWaterExtension>() ?? new SurfaceWaterExtension();
        return (map.TileInfo?.rainfall ?? 0f) / surface.Evaporation(WorldAltitude.TemperatureAt(map.Tile, map.TileInfo));
    }
}
