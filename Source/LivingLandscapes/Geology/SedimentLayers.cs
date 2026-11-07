using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

public static partial class Sediment
{
    /// <summary>
    /// Spreads the deposits over the floodplain into the alluvium and grain size fields of <paramref name="system"/>, at the
    /// ledger's kappa (the one scale of the map); returns a summary for the log. The layers are built from the final network
    /// alone: the aggradation passes' rise is kept apart in the ledger (<see cref="DepositLedger.Aggraded"/>).
    /// </summary>
    public static string Layers(List<int> cells, int[] receiver, float[] deposit, float[] depositGrain, float[] width, float[] filled,
        float[] natural, int sizeX, int sizeZ, SedimentSettings p, DepositLedger ledger, RiverSystem system, float total, float atBase, float eroded,
        float[] discharge = null, bool[] still = null, ConfluenceZones zones = null)
    {
        LayTotals laid = Spread(cells, receiver, system.Flow, deposit, depositGrain, width, filled, natural, sizeX, sizeZ, p, ledger,
            out float[] alluvium, out float[] grainSize, discharge, still, zones);
        system.Alluvium = alluvium;
        system.GrainSize = grainSize;
        system.Aggraded = ledger.Aggraded;
        int covered = 0;
        float thickest = 0f;
        foreach (float a in alluvium)
        {
            if (a > 0.05f)
                covered++;
            thickest = Mathf.Max(thickest, a);
        }
        return $"sediment: {eroded:0} supplied, {total:0} deposited ({(total > 0f ? 100f * atBase / total : 0f):0}% at base level), " +
               $"alluvium over {100f * covered / filled.Length:0.0}% of the map, thickest {thickest:0.00} m " +
               $"(kappa {ledger.Kappa:0.####}{(ledger.KappaClamped ? " clamped" : "")}, laid {laid.Laid:0} m3, clipped {laid.Clipped:0}, unplaced {laid.Unplaced:0})";
    }
}
