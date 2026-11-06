using System;

namespace LivingLandscapes.Geology;

/// <summary>
/// Enrichment of soil by water moving through it (#187). Pure math, no game types, so it is unit-tested.
///
/// Principle (Darcy, with hillslope hydrology): lateral flow in saturated subsoil is q = K i d, with i the hydraulic gradient
/// (follows the slope) and d the saturated thickness. Water gathered from the upslope area A (<c>LL_DrainageArea</c>, log10
/// cells) supplies it, so a drainage line carries throughflow before any water shows on the surface. Moving, oxygenated
/// water delivers nutrients and organic matter (leaching down the line, colluvial and alluvial enrichment) and keeps the
/// soil from gleying: dark, rich soil. Stagnant saturation (level ground, no gradient) is a different state: it gleys and
/// builds peat, which the marsh and mud ground rules place from wetness.
///
///   T = C(A) * M(slope) * R(h) * P
///   C = clamp01((log10 A - a0) / (a1 - a0))      convergence: how much water the cell carries
///   M = clamp01(gradient / gMoving)               moving water: no gradient, no throughflow (that is stagnation)
///   P = profile of the channel (Profile): the axis of a line, not its flanks, so lines stay thin and branch to small tributaries
///   R = exp(-h (1 - C) / H)                       reach: height above the drainage line (LL_HeightAboveDrainage) the flow keeps up; a cell that carries the flow is on a line
/// </summary>
public static class Throughflow
{
    public static float Of(float drainageAreaLog10, float gradient, float heightAboveDrainage,
        float areaMin, float areaFull, float movingGradient, float reachHeight)
    {
        float convergence = Clamp01((drainageAreaLog10 - areaMin) / Math.Max(areaFull - areaMin, 1e-4f));
        float moving = Moving(gradient, movingGradient);
        // A cell carrying the flow is itself on a drainage line (its own height above it is zero), even where the layer's
        // nearest mapped line is farther: height counts only for the share of the flow it does not carry.
        float reach = (float)Math.Exp(-Math.Max(0f, heightAboveDrainage) * (1f - convergence) / Math.Max(reachHeight, 1e-4f));
        return convergence * moving * reach;
    }

    /// <summary>M: 0 on level ground, 1 once the gradient reaches <paramref name="movingGradient"/>.</summary>
    public static float Moving(float gradient, float movingGradient) => Clamp01(gradient / Math.Max(movingGradient, 1e-4f));

    /// <summary>
    /// Stagnant saturation = wetness x (1 - M): wet ground the water does not move through (gleying, peat, marsh and mud).
    /// Wet ground on a grade is flowing water, which enriches instead (<see cref="Of"/>).
    /// </summary>
    public static float Stagnation(float wetness, float gradient, float movingGradient) => wetness * (1f - Moving(gradient, movingGradient));

    /// <summary>
    /// Channel profile: how far a cell's drainage area (log10) stands above the area around it (blurred), 0..1 over
    /// <paramref name="range"/>. Flow converges into the axis of a line and not onto its flanks, at every scale (log area), so
    /// weighting throughflow by it keeps a line thin and lets small tributaries show like large ones. <paramref name="thinning"/>
    /// 0 ignores the profile, 1 follows it fully.
    /// </summary>
    public static float Profile(float drainageAreaLog10, float surroundingLog10, float range, float thinning)
    {
        float axis = Clamp01((drainageAreaLog10 - surroundingLog10) / Math.Max(range, 1e-4f));
        return 1f - thinning + thinning * axis;
    }

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
}
