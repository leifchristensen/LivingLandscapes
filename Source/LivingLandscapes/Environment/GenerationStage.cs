namespace LivingLandscapes.Environment;

/// <summary>Points in map generation where environment layers are computed.</summary>
public enum GenerationStage
{
    /// <summary>Right after vanilla's elevation/fertility step, before rock, terrain and water are placed.
    /// Layers computed here can drive terrain placement (geological terrain on).</summary>
    Elevation,

    /// <summary>Right after the ground is classified (elevation grid, water, marsh and cliffs planned), before rocks, terrain,
    /// roads and the start spot. Layers computed here read the planned ground and steer what vanilla places (settlement, resources).</summary>
    Siting,

    /// <summary>Just before plants are scattered, when terrain, water and rock are final.</summary>
    Plants,
}
