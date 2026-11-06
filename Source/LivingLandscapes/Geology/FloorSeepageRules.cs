namespace LivingLandscapes.Geology;

/// <summary>
/// Which floor class the ground under a closed hollow belongs to, how much its wetted floor seeps, and how far the
/// water table below the floor caps that (P23 extended, #149). Feeds <see cref="WaterBalanceMath.LakeShare"/>:
///
///   A_lake = runoff x rain x catchment / (evaporation - rain + S_eff),   S_eff = S(floor class) x cap(d)
///
/// S is the effective seepage through the wetted floor in mm/yr - what a lake bed actually lets through once its own
/// fines have sealed it (colmation), not the saturated conductivity of the material; FloorSeepageDef (XML) documents
/// each value. The cap is the water table under the floor, d metres below it: a floor at or below the water table is a
/// window on the groundwater (the lake and the ground share one level, so nothing drains away); once the water table lies
/// disconnectDepth or more below the floor the bed is disconnected and drains under gravity at its full S.
///
/// Pure arithmetic, no game types: the same rules serve the erosion model's basins and the map's hollows.
/// </summary>
public class FloorSeepageRules
{
    // Floor classes, the order of the XML S table (FloorSeepageDef).
    public const int ClayHardpan = 0, Silt = 1, JointedRock = 2, Sand = 3, Gravel = 4, Karst = 5;

    /// <summary>Names of the floor classes for reports, same order as the class indices and <see cref="Seepage"/>.</summary>
    public string[] ClassNames = { "clay/hardpan", "silt", "jointed rock", "sand", "gravel", "karst" };

    /// <summary>Effective seepage through the wetted floor (mm/yr) per class, same order as ClassNames.</summary>
    public float[] Seepage = { 0f, 20f, 600f, 1500f, 8000f, 20000f };

    /// <summary>LL_GrainSize under this is fine alluvium (a silt floor); under GrainGravel it is sand, over it gravel.</summary>
    public float grainSilt = 0.25f;
    public float grainGravel = 0.7f;

    /// <summary>Soil depth (m, LL_SoilDepth) that counts as deep ground: weathered silt, not bedrock.</summary>
    public float soilSiltDepth = 1f;

    /// <summary>Open joint (RockStrata Site.joint, 0..1) at or above which a rock floor counts as jointed.</summary>
    public float jointOpen = 0.5f;

    /// <summary>Depth of the water table below the floor (m) from which the bed is disconnected and seeps its full S.</summary>
    public float disconnectDepth = 3f;

    /// <summary>
    /// The floor class of a tile, from what lies under it: river-laid alluvium sorted by grain size, else deep soil,
    /// else the rock beneath (carbonate rock -> karst, open joints -> jointed rock, else massive hardpan). Alluvium
    /// and soil seal what lies under them, so they win over the rock classes.
    /// </summary>
    public int ClassOf(float alluvium, float grainSize, float soilDepth, bool karst, float joint)
    {
        if (alluvium > 0f)
            return grainSize < grainSilt ? Silt : grainSize <= grainGravel ? Sand : Gravel;
        if (soilDepth >= soilSiltDepth) return Silt;
        if (karst) return Karst;
        if (joint >= jointOpen) return JointedRock;
        return ClayHardpan;
    }

    /// <summary>Effective seepage of the class (mm/yr), before the water-table cap.</summary>
    public float SeepageOf(int floorClass) => Seepage[floorClass];

    /// <summary>
    /// How much of its S a floor loses, from the depth of the water table below it (m): nothing where the floor reaches
    /// the water table (groundwater holds the lake up), rising to all of it once the water table lies disconnectDepth
    /// below (the bed drains under gravity, so a deeper water table adds nothing more).
    /// </summary>
    public float Cap(float waterTableDepth)
    {
        if (waterTableDepth <= 0f) return 0f;
        if (waterTableDepth >= disconnectDepth) return 1f;
        return waterTableDepth / disconnectDepth;
    }

    /// <summary>The seepage the balance uses: the class's S, capped by how far the water table lies below the floor.</summary>
    public float SeepageEff(int floorClass, float waterTableDepth) => Seepage[floorClass] * Cap(waterTableDepth);
}
