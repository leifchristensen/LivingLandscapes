using LivingLandscapes.Environment;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Gameplay;

/// <summary>
/// A vanilla scatterer validator (the <c>validators</c> list of a map generation step, added by XML patch) that makes a
/// discovery likelier where the ground is interesting (LL_Interest) and at the distance from the settlement anchor its
/// curve prefers (Docs/GAMEPLAY-PLAN.md, G3). Vanilla tries cells at random until one is allowed, so a draw against the
/// probability here shapes where the thing ends up without changing how many there are.
/// </summary>
public class ScattererValidator_Siting : ScattererValidator
{
    /// <summary>Interest (rank 0..1) raised to this: 0 ignores interest, higher gathers discoveries in the most interesting ground.</summary>
    public float interestPower = 1.5f;

    /// <summary>Probability by cells from the settlement anchor, 0..1 (null: no preference).</summary>
    public SimpleCurve distance;

    /// <summary>Lowest probability anywhere, so a map with no good ground still gets its discoveries.</summary>
    public float floor = 0.05f;

    public override bool Allows(IntVec3 c, Map map)
    {
        if (!GameplaySiting.Enabled)
            return true;
        var environment = map.GetComponent<MapComponent_Environment>();
        if (environment == null || !environment.SettlementAnchor.IsValid)
            return true;
        float p = Mathf.Pow(environment.ValueAt(EnvironmentLayerDefOf.LL_Interest, c, 1f), interestPower);
        if (distance != null)
            p *= distance.Evaluate((c - environment.SettlementAnchor).LengthHorizontal);
        return Rand.Value < Mathf.Max(floor, Mathf.Clamp01(p));
    }
}
