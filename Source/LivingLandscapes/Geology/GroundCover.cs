namespace LivingLandscapes.Geology;

/// <summary>Ground that the geology places itself, instead of leaving the choice to vanilla's fertility and noise.</summary>
public enum GroundCover
{
    /// <summary>Vanilla chooses: rock or gravel from the elevation value, soil types from fertility.</summary>
    None,
    ShallowWater,
    DeepWater,
    ShallowMovingWater,
    DeepMovingWater,
    Marsh,
    Mud,

    /// <summary>The biome's gravel (river bars of coarse alluvium).</summary>
    Gravel,

    /// <summary>The biome's lake beach sand (sandy banks and levees).</summary>
    Sand,

    /// <summary>The biome's most fertile soil (fine alluvium of floodplains).</summary>
    RichSoil,

    /// <summary>Marshy soil: damp ground between a marsh and dry soil, where the water table lies just below the surface.</summary>
    MarshySoil,

    /// <summary>A playa: the dry, barren floor of a desert hollow that only floods after rare storms (dry lake bed; sand without Odyssey).</summary>
    Playa,
}
