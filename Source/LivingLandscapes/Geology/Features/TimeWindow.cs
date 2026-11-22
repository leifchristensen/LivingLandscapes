using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>
/// When a contribution acts, as a share of the erosion run (0 = its start, 1 = its end; the run has a number of steps, a
/// tuning value, and no years). It starts and ends smoothly, as regions fade in space, so no step shows in the land
/// (FEATURE-REGIONS.md 2.1, "Time"). The default is the whole run. For rock arrangement the start is the contribution's age:
/// contributions apply oldest first.
/// </summary>
public class TimeWindow
{
    /// <summary>Share of the run at which the contribution starts to act.</summary>
    public float start;

    /// <summary>Share of the run at which it has stopped acting.</summary>
    public float end = 1f;

    /// <summary>Share of the run over which it fades in after <see cref="start"/> and out before <see cref="end"/>.</summary>
    public float ramp = 0.1f;

    public static readonly TimeWindow WholeRun = new TimeWindow();

    /// <summary>True when the contribution acts over the whole run, at full strength throughout.</summary>
    public bool IsWholeRun => start <= 0f && end >= 1f;

    /// <summary>
    /// How strongly the contribution acts at a share <paramref name="t"/> of the run, 0..1: exactly 1 throughout for the whole
    /// run; otherwise 0 before the start, rising smoothly over the ramp, holding, and falling smoothly to 0 at the end.
    /// </summary>
    public float Weight(float t)
    {
        if (IsWholeRun)
            return 1f;
        float r = Mathf.Min(ramp, (end - start) * 0.5f);
        if (r <= 0f)
            return t >= start && t < end ? 1f : 0f; // a configuration error (see ConfigErrors); kept defined
        float rise = start <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, start + r, t));
        float fall = end >= 1f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(end - r, end, t));
        return Mathf.Min(rise, fall);
    }

    public IEnumerable<string> ConfigErrors()
    {
        if (start < 0f || end > 1f || end <= start)
            yield return $"time window {start}..{end} must lie within 0..1 and end after it starts";
        if (!IsWholeRun && ramp <= 0f)
            yield return "a time window shorter than the whole run needs a ramp above 0, or a step would show in the land";
    }
}
