using LivingLandscapes.Environment;
using LivingLandscapes.Flora;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>
/// Dev view (on demand): canopy shade 0..1 cast by the plants standing now, as the plant ecology keeps it live
/// (<see cref="MapComponent_Ecology.ShadeAt"/>). Not computed where the ecology is off.
/// </summary>
public class ShadeWorker : EnvironmentLayerWorker
{
    public override float[] Compute(EnvironmentContext context)
    {
        var ecology = context.Map.GetComponent<MapComponent_Ecology>();
        if (ecology == null || !ecology.Ready)
            return null;
        var shade = new float[context.CellCount];
        for (int i = 0; i < shade.Length; i++)
            shade[i] = ecology.ShadeAt(i);
        return shade;
    }

    /// <summary>Radius (cells) over which a plant's shade spreads: its habitat's, or from its size.</summary>
    public static float ShadeRadius(ThingDef plant, PlantHabitat habitat)
    {
        return habitat.shadeRadius > 0f ? habitat.shadeRadius : Mathf.Max(1f, plant.plant.visualSizeRange.max * 0.6f);
    }

    /// <summary>Shadows fall towards the pole, by half the shade radius.</summary>
    public static Vector2 ShadowShift(float latitude, float radius) => new Vector2(0f, (latitude >= 0f ? 1f : -1f) * radius * 0.5f);
}
