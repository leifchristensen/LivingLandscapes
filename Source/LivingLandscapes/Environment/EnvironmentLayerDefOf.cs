using RimWorld;

namespace LivingLandscapes.Environment;

[DefOf]
public static class EnvironmentLayerDefOf
{
    public static EnvironmentLayerDef LL_Elevation;
    public static EnvironmentLayerDef LL_Landform;
    public static EnvironmentLayerDef LL_Height;
    public static EnvironmentLayerDef LL_RockStrength;
    public static EnvironmentLayerDef LL_SoilDepth;
    public static EnvironmentLayerDef LL_DrainageArea;
    public static EnvironmentLayerDef LL_Slope;
    public static EnvironmentLayerDef LL_Aspect;
    public static EnvironmentLayerDef LL_Wetness;
    public static EnvironmentLayerDef LL_SurfaceWater;
    public static EnvironmentLayerDef LL_WaterDistance;
    public static EnvironmentLayerDef LL_Moisture;
    public static EnvironmentLayerDef LL_Sun;
    public static EnvironmentLayerDef LL_Shade;
    public static EnvironmentLayerDef LL_Fitness;
    public static EnvironmentLayerDef LL_Settlement;
    public static EnvironmentLayerDef LL_Interest;
    public static EnvironmentLayerDef LL_Alluvium;

    static EnvironmentLayerDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(EnvironmentLayerDefOf));
    }
}
