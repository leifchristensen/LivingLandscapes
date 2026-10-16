namespace LivingLandscapes.Environment;

/// <summary>Points in map generation where environment layers are computed.</summary>
public enum GenerationStage
{
    /// <summary>Right after vanilla's elevation/fertility step, before rock, terrain and water are placed.
    /// Layers computed here can drive terrain placement (geological terrain on).</summary>
    Elevation,

    /// <summary>Just before plants are scattered, when terrain, water and rock are final.</summary>
    Plants,
}
