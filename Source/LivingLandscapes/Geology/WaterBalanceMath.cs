using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>
/// The arithmetic of the water balance of a closed hollow (P23, W6), extended with seepage through its wetted floor
/// (#149):
///
///   A_lake = runoff x rain x catchment / (evaporation - rain + S)
///
/// <paramref name="rain"/> and <paramref name="evaporation"/> are mm/yr of the climate, <paramref name="runoff"/> is the
/// share of the rain that reaches the hollow's catchment, and <paramref name="seepage"/> (S) is the effective seepage
/// through the wetted floor in mm/yr - what the floor under the hollow lets through, capped by how far the water table
/// lies below the floor (FloorSeepage, #149). The hollow stands below its spill level whenever
/// evaporation - rain + S &gt; 0: the water it collects is taken by evaporation and seepage before it reaches the rim.
/// With S = 0 this is exactly the balance without seepage, so a floor nothing seeps through behaves as before.
/// </summary>
public static class WaterBalanceMath
{
    /// <summary>
    /// Lake area per unit of catchment (cells of lake per cell of catchment): runoff x rain / (evaporation - rain + S),
    /// infinite where the losses cannot take the rain (evaporation - rain + S &lt;= 0) and every hollow fills to its rim.
    /// </summary>
    public static float LakeShare(float runoff, float rain, float evaporation, float seepage)
    {
        float netLoss = evaporation - rain + seepage;
        return netLoss <= 0f ? float.PositiveInfinity : runoff * rain / netLoss;
    }

    /// <summary>
    /// Recharge (mm/yr): the rain that soaks into the ground and reaches the water table, the share that does not run off
    /// of what evaporation leaves, (1 - runoff) x max(0, rain - evaporation). The groundwater a catchment holds comes from
    /// nothing else.
    /// </summary>
    public static float Recharge(float runoff, float rain, float evaporation) =>
        (1f - runoff) * Mathf.Max(0f, rain - evaporation);

    /// <summary>
    /// Lake share of a hollow whose floor lies below the water table (#149): groundwater flows in, but only what the
    /// catchment's recharge supplies, so water is conserved; the groundwater the lake takes is that recharge, which then
    /// reaches nothing else. A_lake = (runoff x rain + recharge) x catchment / (evaporation - rain), no floor seepage
    /// (the floor is fed, not drained). Where rain does not beat evaporation there is no recharge and the share is the
    /// runoff balance alone: groundwater cannot keep a lake in a dry climate.
    /// </summary>
    public static float FedLakeShare(float runoff, float rain, float evaporation)
    {
        float netLoss = evaporation - rain;
        return netLoss <= 0f ? float.PositiveInfinity : (runoff * rain + Recharge(runoff, rain, evaporation)) / netLoss;
    }
}
