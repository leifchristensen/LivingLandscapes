using System;

namespace LivingLandscapes.Geology;

/// <summary>
/// Game-free rules for the water crossing the map boundary (HYDRO-S8, #186): how much water the map's own channels may
/// carry on a tile the world gives no river. No game types, so it is unit-tested directly.
/// </summary>
public static class BoundaryMath
{
    /// <summary>
    /// The largest discharge the map's own channels may carry, in the map's discharge units (width = 0.04 sqrt(Q)).
    /// The width cap always applies. The tile's world flow (a share of a creek) bounds it further only where the climate has
    /// no surplus: where rain beats evaporation the water must leave the map whatever the coarse world number says, so the
    /// world flow must not shrink the map's channels below the seep floor (#141, a tile whose own rain is 15,000 units but whose
    /// world flow is 6% of a creek). A flow that is NaN, zero or negative is "unknown": the width cap alone.
    /// </summary>
    public static float OwnTarget(float maxWidthTarget, float worldFlow, float creekThreshold, float creekDischarge, bool climateSurplus)
    {
        float target = maxWidthTarget;
        if (!climateSurplus && IsFinite(worldFlow) && worldFlow > 0f && IsFinite(creekThreshold) && creekThreshold > 0f && IsFinite(creekDischarge))
            target = Math.Min(target, creekDischarge * worldFlow / creekThreshold);
        return target;
    }

    /// <summary>The factor (0..1] that brings the largest channel down to the target; 1 when the target is unknown or not positive. Never NaN.</summary>
    public static float ClampScale(float target, float largest)
    {
        if (!IsFinite(target) || target <= 0f)
            return 1f;
        float scale = Math.Min(1f, target / Math.Max(largest, 1f));
        return IsFinite(scale) && scale > 0f ? scale : 1f;
    }

    private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}
