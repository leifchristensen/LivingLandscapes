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
    /// Stand-in R19 (owner 2026-11-02): the lapse rate times this. A map's relief stands for a compressed landscape, and
    /// the true rate gives under 1 K across a mountainous map. Replace with a map scale from the world tile's relief.
    /// </summary>
    public float lapseScale = 10f;

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

    /// <summary>Share of the day the sun part acts on average, for a cell's mean offset (species placement).</summary>
    public float meanDayShare = 0.5f;

    /// <summary>Share of the time a full cold pool stands on average (nights that are clear and calm), for the mean offset.</summary>
    public float meanPoolShare = 0.25f;

    private static LocalClimateDef current;

    public static LocalClimateDef Current => current ??= DefDatabase<LocalClimateDef>.GetNamedSilentFail("LL_LocalClimate") ?? new LocalClimateDef();
}
