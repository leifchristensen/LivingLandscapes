using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>
/// Junctions of moving water (#20): wherever a tributary joins a channel, the flow slows in the corner it enters at and in a
/// separation zone along its bank below, and speeds up along the far bank through the narrowed flow, with a scour hole at the
/// junction. Capacity follows the local speed (P7), so the sediment budget lays bars on the tributary's side and the scour
/// deepens the bed; nothing is drawn. Every junction uses the same rules, sized by its width, discharge share and angle.
/// </summary>
public class ConfluenceZones
{
    /// <summary>Share of the channel's width (0..1) taken by slow water at each channel cell; 0 away from junctions.</summary>
    public float[] SlowShare;

    /// <summary>Unit vector toward the bank the slow water lies along (the tributary's side).</summary>
    public Vector2[] SlowSide;

    /// <summary>Multiplier on channel depth (1 away from junctions).</summary>
    public float[] Scour;

    /// <summary>Capacity in the slow water as a share of the reach's: (v / v_mean)^2.</summary>
    public float SlowCapacity;

    public int Junctions;
    public float Strongest;
}
