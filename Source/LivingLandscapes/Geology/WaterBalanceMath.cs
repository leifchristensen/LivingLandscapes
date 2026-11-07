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
    public static float FedLakeShare(float runoff, float rain, float evaporation, float yield = 1f)
    {
        float netLoss = evaporation - rain;
        return netLoss <= 0f ? float.PositiveInfinity : (yield * runoff * rain + Recharge(runoff, rain, evaporation)) / netLoss;
    }

    /// <summary>
    /// What a full lake takes from the water passing through it (#134, P23): its surface evaporates (evaporation - rain + S) mm
    /// a year, which the catchment's runoff must pay for, so a lake of <paramref name="lakeCells"/> cells keeps
    /// lakeCells x (evaporation - rain + S) / rain of full-rain cells, which is lakeCells x runoff / share in the units a cell
    /// of catchment carries (runoff each). No loss where the share is infinite (rain beats evaporation: the lake takes
    /// nothing net); everything where it is zero (no runoff to pay with).
    /// </summary>
    public static float LakeLoss(float lakeCells, float runoff, float share)
    {
        if (float.IsPositiveInfinity(share))
            return 0f;
        return share <= 0f ? float.PositiveInfinity : lakeCells * runoff / share;
    }

    /// <summary>The water a lake hands on over its rim: what came in less what it keeps, never below nothing (#134).</summary>
    public static float PassedOn(float inflow, float lakeLoss) => Mathf.Max(0f, inflow - lakeLoss);
}
