using Verse;

namespace LivingLandscapes.Climate;

/// <summary>
/// Tuning for the local climate (P23, #90): how much colder or warmer an outdoor cell is than the map's outdoor
/// temperature, from its height, its slope to the sun and cold air pooling in hollows. Literature values by default.
/// </summary>
public class LocalClimateDef : Def
{
    /// <summary>Environmental lapse rate, K per metre above the map's mean ground height (6.5 K per 1000 m).</summary>
    public float lapseRate = 0.0065f;

    /// <summary>
    /// Daytime warming, K per unit of sun exposure above level ground's (LL_Sun): sun-facing slopes run 1-3 K warmer
    /// than level ground by day, slopes facing away as much cooler (Geiger, Climate Near the Ground).
    /// </summary>
    public float sunWarming = 5f;

    /// <summary>Night cooling of a hollow at full depth on a clear, calm night, K (frost hollows: 2-10 K, Geiger).</summary>
    public float poolCooling = 4f;

    /// <summary>Depth below the hollow's spill level (m) at which the pooled cold air is full.</summary>
    public float poolDepthFull = 4f;

    /// <summary>Smoothing of the ground (cells) before finding hollows: cold air pools in hollows tens of metres across, not in hummocks.</summary>
    public float poolSmoothing = 5f;

    /// <summary>Wind speed (vanilla's units) at which night air is fully mixed and no cold pool forms.</summary>
    public float mixingWind = 1f;

    /// <summary>Share of the pool left under cloud or rain (cloud cover stops the ground radiating its heat away).</summary>
    public float pooledUnderCloud = 0.3f;

    private static LocalClimateDef current;

    public static LocalClimateDef Current => current ??= DefDatabase<LocalClimateDef>.GetNamedSilentFail("LL_LocalClimate") ?? new LocalClimateDef();
}
