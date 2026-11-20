using System;

namespace LivingLandscapes.Geology;

/// <summary>
/// The small formulas of the delta model (Docs DELTA-MODEL-PLAN.md, sections 2 and 6), game-free so they can be tested alone.
/// D1 only measures with them (the "Delta report" log line); later phases let them limit and drive the sea fill. Lengths are in
/// map cells, heights and depths in metres.
/// </summary>
public static class DeltaMath
{
    /// <summary>
    /// Least slope the backwater length divides by, metres per map cell. Below it a river is as good as flat and the length would
    /// run to infinity; it sits well under the coastal plain's own least gradient (CoastlineExtension.plainGradient, 0.006).
    /// A stand-in tuning value for the measurement, to be replaced by a registered value when D4 uses the length.
    /// </summary>
    public const float MinSlope = 0.001f;

    /// <summary>How the wave and river fluxes compare at a mouth: the regime classes of the plan's E4.</summary>
    public enum Regime
    {
        /// <summary>R below 1: the waves carry away everything the river brings; no delta (beach, estuary).</summary>
        NoDelta,
        /// <summary>R from 1 to below 3: smooth, cuspate coast.</summary>
        WaveInfluenced,
        /// <summary>R of 3 or more: lobate, with distributaries.</summary>
        RiverDominated,
    }

    /// <summary>The regime ratio R = river flux / wave flux and the class it falls in.</summary>
    public readonly struct WaveResult
    {
        public readonly double Ratio;
        public readonly Regime Class;

        public WaveResult(double ratio, Regime regime)
        {
            Ratio = ratio;
            Class = regime;
        }
    }

    /// <summary>
    /// E2, backwater length: L_b = depth / max(slope, sMin), at most <paramref name="cap"/> (the coastal plain's width). A slope that
    /// is zero, negative or NaN counts as <paramref name="sMin"/>. Never negative. <paramref name="capped"/> says the cap decided,
    /// so a caller can ignore the length where the cap, not the river, set it. The slope is metres per map cell.
    /// </summary>
    public static float BackwaterLength(float depth, float slope, float cap, float sMin, out bool capped)
    {
        float s = float.IsNaN(slope) || slope <= 0f ? sMin : Math.Max(slope, sMin);
        float length = depth <= 0f || float.IsNaN(depth) ? 0f : depth / s;
        float limit = float.IsNaN(cap) ? float.PositiveInfinity : Math.Max(cap, 0f);
        capped = length > limit;
        return capped ? limit : length;
    }

    /// <summary>
    /// E8, superelevation: sigma = (bed + depth - lateralGround) / depth, how far the water surface stands above the lowest ground
    /// beside the channel, in channel depths. Near 1 or more the river is ready to leave its bed (avulsion). 0 for a depth of 0 or less.
    /// </summary>
    public static float Superelevation(float bed, float depth, float lateralGround)
    {
        return depth <= 0f || float.IsNaN(depth) ? 0f : (bed + depth - lateralGround) / depth;
    }

    /// <summary>E7, mouth bar: true when the basin is shallower than <paramref name="rho"/> channel depths, so the jet's slowing raises a bar.</summary>
    public static bool BarForms(float basinDepth, float channelDepth, float rho)
    {
        return basinDepth < rho * channelDepth;
    }

    /// <summary>
    /// E4, regime: R = riverFlux / waveFlux (+infinity where there is no wave flux to carry anything off), class NoDelta for R below 1,
    /// WaveInfluenced for 1 up to (not including) 3, RiverDominated from 3.
    /// </summary>
    public static WaveResult WaveRegime(double riverFlux, double waveFlux)
    {
        double r = waveFlux <= 0.0 ? double.PositiveInfinity : riverFlux / waveFlux;
        Regime regime = r < 1.0 ? Regime.NoDelta : r < 3.0 ? Regime.WaveInfluenced : Regime.RiverDominated;
        return new WaveResult(r, regime);
    }

    /// <summary>E6, room: how far a bed can still rise before it reaches the water level, max(0, level - bed).</summary>
    public static float Room(float level, float bed)
    {
        return Math.Max(0f, level - bed);
    }
}
