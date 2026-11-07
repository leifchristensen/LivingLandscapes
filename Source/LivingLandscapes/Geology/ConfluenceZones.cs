using System.Collections.Generic;
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

    /// <summary>Per cell, the slow water's capacity share (u_slow / u_reach)^2 from the junction's lost cross momentum (S5); null where <see cref="SlowCapacity"/> applies everywhere.</summary>
    public float[] SlowCapacityAt;

    public int Junctions;

    /// <summary>The cell below each junction that shaped zones (parallel to <see cref="JunctionWidths"/>), for the audit.</summary>
    public List<int> JunctionCells = new List<int>();
    public List<float> JunctionWidths = new List<float>();
    public float Strongest;
}

/// <summary>What the junction invariant (10, CONFLUENCE-ZONES acceptance) found on the final network.</summary>
public class JunctionAudit
{
    /// <summary>Junctions of moving water, and those where the joined channel is narrower than the widest arm (#220).</summary>
    public int Joins, Narrowed;

    /// <summary>Whether width continuity was applied (the one hydraulics on); the width check only fails the invariant then.</summary>
    public bool WidthEnforced;
    public float WorstWidthRatio = 1f;
    public int WorstWidthCell = -1;

    /// <summary>Junctions that shaped zones, those whose scour is not deepest at the junction, and those that laid no deposit in their zone.</summary>
    public int Zoned, ScourOffCentre, NoDeposit;

    /// <summary>Deposit (budget units) laid in the junction zones (two joined widths below each junction) and on the whole network.</summary>
    public float DepositAtJoins, DepositTotal;
}
