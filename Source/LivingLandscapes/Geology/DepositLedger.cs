using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>
/// Where the volume of one <see cref="Sediment.Spread"/> went, in metres times cells (thickness times one cell's area), so it
/// can be added up: <c>Offered = Laid + Clipped + Unplaced</c> (HYDRO-UNIFICATION P28, section 10 point 4).
/// </summary>
public struct LayTotals
{
    /// <summary>Everything the budget's deposits offered, converted at the ledger's kappa.</summary>
    public float Offered;

    /// <summary>Volume that is now alluvium (the thickness that was kept).</summary>
    public float Laid;

    /// <summary>Volume cut off by <c>maxThickness</c> (a thicker deposit) or <c>minThickness</c> (a thinner one counts as none).</summary>
    public float Clipped;

    /// <summary>Volume of deposits that had no ground to settle on (all of it above its own level); booked here, not lost silently.</summary>
    public float Unplaced;
}

/// <summary>
/// The deposit ledger of one map (HYDRO-UNIFICATION E3 and D2, issue #186 step S4): the one conversion from the sediment
/// budget's relative units to metres of alluvium (<see cref="Kappa"/>, R24 "one deposit map scale per map"), and the one place
/// the aggradation passes' rise is kept. It is an instance made in <c>TerrainChannels.Build</c> and passed down, not a static.
///
/// Kappa is fixed by the first pass that has deposits, so that the 90th percentile of the ground it covers is
/// <c>typicalThickness</c> (owner 2026-10-20: every map with deposition shows it), and is then held: later passes and the final
/// layers convert at the same factor, so the same deposit is the same thickness in every call and the passes accumulate.
/// </summary>
public sealed class DepositLedger
{
    /// <summary>Metres of alluvium per unit of the sediment budget; 1 until a pass with deposits fixes it.</summary>
    public float Kappa { get; private set; } = 1f;

    /// <summary>True once a pass with deposits has set <see cref="Kappa"/>.</summary>
    public bool IsFixed { get; private set; }

    /// <summary>True when the first pass wanted a kappa outside [kappaMin, kappaMax] and it was clamped.</summary>
    public bool KappaClamped { get; private set; }

    /// <summary>
    /// The rise the aggradation passes gave the ground, per cell (metres), summed over the passes. It is kept for the height only:
    /// the final alluvium is rebuilt from the final network, and soil depth counts alluvium beyond this rise (it is in the landform).
    /// </summary>
    public float[] Aggraded { get; private set; }

    /// <summary>Total of <see cref="Aggraded"/> (metres times cells).</summary>
    public float AggradedVolume { get; private set; }

    /// <summary>
    /// Fixes kappa from <paramref name="raw"/> (the budget's units laid on the ground) if it is not fixed yet: the 90th percentile
    /// of the covered ground becomes <c>typicalThickness</c>, within [kappaMin, kappaMax]. Nothing covered leaves kappa at 1 and
    /// unfixed (nothing is shown, no NaN).
    /// </summary>
    public void FixFrom(float[] raw, SedimentSettings p)
    {
        if (IsFixed)
            return;
        var covered = new List<float>();
        foreach (float a in raw)
        {
            if (a > 0f)
                covered.Add(a);
        }
        if (covered.Count == 0)
            return;
        covered.Sort();
        float wanted = p.typicalThickness / Mathf.Max(covered[(int)(covered.Count * 0.9f)], 1e-6f);
        Kappa = Mathf.Clamp(wanted, p.kappaMin, p.kappaMax);
        KappaClamped = Kappa != wanted;
        IsFixed = true;
    }

    /// <summary>The thickness (metres) a deposit of <paramref name="units"/> of the budget makes before clipping.</summary>
    public float Metres(float units) => units * Kappa;

    /// <summary>
    /// Turns the budget's units laid on the ground (<paramref name="raw"/>, with their grain sums) into alluvium thickness in
    /// metres and grain size, at kappa (fixing it first if no pass has). A thickness over <c>maxThickness</c> is cut to it, one
    /// under <c>minThickness</c> counts as none; the volume cut is returned in <paramref name="totals"/>.
    /// </summary>
    public void Lay(float[] raw, float[] grainSum, SedimentSettings p, float unplacedUnits, float offeredUnits, out float[] alluvium, out float[] grainSize,
        out LayTotals totals)
    {
        FixFrom(raw, p);
        int n = raw.Length;
        alluvium = new float[n];
        grainSize = new float[n];
        totals = new LayTotals { Offered = offeredUnits * Kappa, Unplaced = unplacedUnits * Kappa };
        for (int i = 0; i < n; i++)
        {
            if (raw[i] <= 0f)
                continue;
            float wanted = raw[i] * Kappa;
            float thickness = Mathf.Min(wanted, p.maxThickness);
            if (thickness >= p.minThickness)
            {
                grainSize[i] = Mathf.Clamp01(grainSum[i] / raw[i]);
                alluvium[i] = thickness;
                totals.Laid += thickness;
                totals.Clipped += wanted - thickness;
            }
            else
                totals.Clipped += wanted;
        }
    }

    /// <summary>
    /// One aggradation pass: the ground rises by <paramref name="share"/> of this pass's alluvium. Adds the rise to
    /// <see cref="Aggraded"/> and returns it per cell, so the pass's rise sum is exactly share times the layer's sum.
    /// </summary>
    public float[] Raise(float[] alluvium, float share, out float volume)
    {
        Aggraded ??= new float[alluvium.Length];
        var rise = new float[alluvium.Length];
        volume = 0f;
        for (int i = 0; i < alluvium.Length; i++)
        {
            if (alluvium[i] <= 0f)
                continue;
            rise[i] = alluvium[i] * share;
            Aggraded[i] += rise[i];
            volume += rise[i];
        }
        AggradedVolume += volume;
        return rise;
    }
}
