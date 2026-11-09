namespace LivingLandscapes.Layers;

/// <summary>
/// The one definition of how the water sources combine (#132, plan decision D4). The sources are independent ways a cell
/// gets wet - the rain the climate keeps in freely drained ground (C, <see cref="MoistureWorker"/>, P25), the water
/// gathering from upslope and the water table (W, <see cref="WetnessWorker"/>) and nearness to open water (P,
/// <see cref="WaterDistanceWorker"/>) - and each wets what the others leave dry, so the ground holds the union of the
/// three rather than their average:
///
///   G = 1 - (1 - C)(1 - W)                 the ground term, known where the elevation is
///   M = 1 - (1 - C)(1 - W)(1 - P)          the full moisture plants read, G plus open water
///
/// This file is the only place that writes that combination; <see cref="MoistureWorker"/> and the terrain readers of the
/// wetness grid all take their numbers from here. Plain System.Math only (no UnityEngine), so the game-free test project
/// links it. The three-argument <see cref="Moisture(float,float,float)"/> keeps the operand order the mod has always
/// used, so merging the two water layers changes no plant's moisture by one bit.
/// </summary>
public static class WaterFieldMath
{
    /// <summary>
    /// The moisture of a cell from its three sources, in the order the mod has always evaluated them: rain baseline
    /// <paramref name="climate"/>, topographic wetness <paramref name="wetness"/> (0 where the wetness grid is missing)
    /// and nearness to open water <paramref name="proximity"/>. Bit for bit the expression
    /// <c>1f - (1f - climate) * (1f - wetness) * (1f - proximity)</c>.
    /// </summary>
    public static float Moisture(float climate, float wetness, float proximity) =>
        1f - (1f - climate) * (1f - wetness) * (1f - proximity);

    /// <summary>
    /// The same law in two steps: the full moisture of a cell whose ground term <see cref="GroundTerm"/> is already
    /// known, raised by nearness to open water. Equal to the three-argument form to the last bits of a float for
    /// in-range inputs (rounding differs only in the order the three factors are multiplied); the tests of
    /// <c>WaterFieldTests</c> hold the two together, so the two-step reading cannot drift from what plants get.
    /// </summary>
    public static float Moisture(float ground, float proximity) =>
        1f - (1f - ground) * (1f - proximity);

    /// <summary>
    /// The ground term G: how wet the ground itself is, from the rain the climate keeps (C) and the water the terrain
    /// gathers (W), before any nearness to open water. Known at the elevation stage, and what terrain readers see when
    /// <c>terrainReadsMoisture</c> is on. Never drier than either source alone, never wetter than 1.
    /// </summary>
    public static float GroundTerm(float wetness, float climate) => 1f - (1f - climate) * (1f - wetness);

    /// <summary>
    /// What a terrain reader (fertility patch, wet ground, settlement siting) reads: the topographic wetness
    /// <paramref name="wetness"/> while <paramref name="terrainReadsMoisture"/> is off - exactly what it read before the
    /// two water layers were merged - and the ground term <see cref="GroundTerm"/> of that wetness and the climate
    /// baseline <paramref name="climate"/> when it is on.
    /// </summary>
    public static float TerrainValue(float wetness, float climate, bool terrainReadsMoisture) =>
        terrainReadsMoisture ? GroundTerm(wetness, climate) : wetness;
}
