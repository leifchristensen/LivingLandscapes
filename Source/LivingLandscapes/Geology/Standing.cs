using System;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>
/// The one test for standing water in the channel stage (hydrology unification E1, #156): where the surface water would stand
/// lies deeper above the bed than the channel itself would be, the water is still and the cell is a base level. Before this,
/// the Confluences exclusion, Paint's hand-over to the lake model, the budget's base level, the spring lake and the
/// feedback passes each had their own version, with two different depths and a gap between them.
///
/// Amendments in force (HYDRO-UNIFICATION sections 10 and 11):
///   1. The depth is always the PRE-scour depth. Junction scour is a bed effect and never moves the standing decision.
///   2. (D5) A body stands at its level L = min(spill, balance level), not simply at spill. <see cref="Standing"/> takes L per
///      cell. The channel stage passes the filled surface (the spill estimate); the balance level (P23, rain and runoff less
///      evaporation and seepage, #149) joins when the last pass runs on the lake model's own L. The signature does not change.
///
/// Game-free (no Verse types) so the unit tests link it. The lake model's rule (<c>HoldsLake</c>) stays where it is: it decides
/// how still water is expressed (lake, marsh, pool), not whether the budget treats a cell as base level, so it is passed in.
/// </summary>
public sealed class Standing
{
    private readonly float[] level;
    private readonly bool[] sea;
    private readonly float minDepth;
    private readonly float tolerance;
    private readonly float springLakeDepth;

    /// <param name="level">The level (L) each cell's water would stand at: the spill (filled) surface until the balance level is known.</param>
    /// <param name="sea">Sea cells (always standing), or null.</param>
    /// <param name="minDepth">A channel is never shallower than this (metres).</param>
    /// <param name="tolerance">Standing needs this much more depth than the channel's (metres): <c>standingTolerance</c> in XML.</param>
    /// <param name="springLakeDepth">A headwater's spring pools where the level lies this much above the natural ground (metres).</param>
    public Standing(float[] level, bool[] sea, float minDepth, float tolerance, float springLakeDepth)
    {
        this.level = level;
        this.sea = sea;
        this.minDepth = minDepth;
        this.tolerance = tolerance;
        this.springLakeDepth = springLakeDepth;
    }

    /// <summary>The level (L) the water at <paramref name="i"/> stands at.</summary>
    public float LevelAt(int i) => level[i];

    /// <summary>The sea.</summary>
    public bool IsSea(int i) => sea != null && sea[i];

    /// <summary>
    /// Land that lies deeper below its level than the channel through it would be: <c>L - bed &gt; max(depth, minDepth) + tolerance</c>.
    /// <paramref name="depth"/> is the channel depth before junction scour.
    /// </summary>
    public bool Deep(int i, float bed, float depth) => level[i] - bed > Mathf.Max(depth, minDepth) + tolerance;

    /// <summary>
    /// (D5, #186) The room (accommodation) at a cell: how far its bed can rise before the cell stops being standing water,
    /// <c>max(0, L - bed - (max(depth, minDepth) + tolerance))</c>. It is exactly the rise at which <see cref="Deep"/> turns off,
    /// so a deposit capped by it never lifts the bed above the water surface; past that point the normal land rule applies.
    /// The sea has no room to fill here (0): building land into it is the shore pass' work, not the feedback's.
    /// </summary>
    public float Room(int i, float bed, float depth) =>
        IsSea(i) ? 0f : Mathf.Max(0f, level[i] - bed - (Mathf.Max(depth, minDepth) + tolerance));

    /// <summary>Standing water: the sea, or <see cref="Deep"/>. This is what makes a cell a base level for the budget and for junctions.</summary>
    public bool Is(int i, float bed, float depth) => IsSea(i) || Deep(i, bed, depth);

    /// <summary>A hollow deep enough that a headwater's springs pool in it (the spring lake's own, lower, threshold).</summary>
    public bool InSpringHollow(int i, float bed) => level[i] - bed > springLakeDepth;

    /// <summary>
    /// A cell the channel stage leaves to the lake model: standing land water in a hollow the lake model says can hold a lake.
    /// By construction every such cell is <see cref="Is"/>, so none is counted as flowing water (invariant 8).
    /// </summary>
    public bool HandedToLake(int i, float bed, float depth, bool holdsLake) => holdsLake && Deep(i, bed, depth);
}
