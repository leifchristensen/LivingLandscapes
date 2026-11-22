namespace LivingLandscapes.Geology;

/// <summary>
/// The natural conditions a feature type may change, and nothing else (Docs/FEATURE-REGIONS.md section 2.1, decision record
/// 0002: regions set causes, processes make features). A feature type never writes the result (the finished height, water
/// or terrain); the processes (erosion, sediment, hydrology, lakes) read these conditions once, over the whole map.
/// </summary>
public enum Condition
{
    /// <summary>How fast the ground rises over geological time (P2, stream power: rise = U - K A^m S).</summary>
    Uplift,

    /// <summary>Which of the tile's rock types lies where, and how hard it is (beds, intrusions, faults; <see cref="RockStrata"/>).</summary>
    RockArrangement,

    /// <summary>The landscape erosion starts from, and the surface an event leaves (an impact).</summary>
    InheritedSurface,

    /// <summary>Where water can stop or leave, and at what height: sea, lake level, an open map edge.</summary>
    BaseLevel,

    /// <summary>Water arriving: world rivers at the edge, springs, a share of the rainfall.</summary>
    WaterSupply,

    /// <summary>Load arriving with world rivers, loose ground.</summary>
    SedimentSupply,

    /// <summary>Heat rising from below (volcanic or mountainous tiles, along faults).</summary>
    HeatFlow,

    /// <summary>The height of the water table.</summary>
    GroundWater,

    /// <summary>Ice arriving or forming (glaciers, shelves).</summary>
    IceSupply,
}

/// <summary>How the contributions of several regions to one condition combine. Fixed by the condition's physics, never by a feature type.</summary>
public enum CombineRule
{
    /// <summary>Contributions add (rates and conserved supplies: uplift, water, sediment, heat, ice).</summary>
    Add,

    /// <summary>
    /// Contributions apply in geological time order, oldest first (superposition and cross-cutting): a younger body or event
    /// changes what it finds. Contributions of the same age keep their order.
    /// </summary>
    TimeOrder,

    /// <summary>Per drainage: water takes the lowest outlet it can reach; a closed basin keeps the level its water balance gives (P23).</summary>
    PerDrainage,
}

/// <summary>The fixed list of conditions: each one's combination rule and the process that reads it.</summary>
public static class FeatureConditions
{
    /// <summary>The combination rule of a condition (FEATURE-REGIONS.md 2.1).</summary>
    public static CombineRule RuleOf(Condition condition)
    {
        switch (condition)
        {
            case Condition.RockArrangement:
            case Condition.InheritedSurface:
                return CombineRule.TimeOrder;
            case Condition.BaseLevel:
            case Condition.GroundWater:
                return CombineRule.PerDrainage;
            default:
                return CombineRule.Add;
        }
    }

    /// <summary>
    /// Whether a process reads the condition yet. Phase 1 wires uplift and rock arrangement into the erosion model; the other
    /// conditions arrive with their processes (base level, water and sediment supply and the inherited surface with the world
    /// river corridor in phase 2; heat flow, ground water and ice with their processes in phase 7). A contribution to a
    /// condition nobody reads would have no effect, so it is a configuration error rather than a silent no-op.
    /// </summary>
    public static bool IsRead(Condition condition) => condition == Condition.Uplift || condition == Condition.RockArrangement;
}
