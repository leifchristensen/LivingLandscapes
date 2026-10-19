using LivingLandscapes.Environment;
using LivingLandscapes.Flora;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>
/// Shade 0..1 cast by the plants standing on the map now, by each species' <see cref="PlantHabitat.shadeCast"/>.
/// Before plants are scattered it is zero; the placement pass will fill a <see cref="ShadeField"/> stratum by
/// stratum with the same maths.
/// </summary>
public class ShadeWorker : EnvironmentLayerWorker
{
    public override float[] Compute(EnvironmentContext context)
    {
        var field = new ShadeField(context.SizeX, context.SizeZ);
        float latitude = SunWorker.LatitudeOf(context.Map);
        foreach (Thing thing in context.Map.listerThings.ThingsInGroup(ThingRequestGroup.Plant))
        {
            PlantHabitat habitat = PlantHabitat.For(thing.def);
            if (habitat.shadeCast <= 0f)
                continue;
            float radius = ShadeRadius(thing.def, habitat);
            field.Cast(thing.Position.x, thing.Position.z, habitat.shadeCast, radius, ShadowShift(latitude, radius));
        }
        return field.Values;
    }

    public static float ShadeRadius(ThingDef plant, PlantHabitat habitat)
    {
        return habitat.shadeRadius > 0f ? habitat.shadeRadius : Mathf.Max(1f, plant.plant.visualSizeRange.max * 0.6f);
    }

    /// <summary>Shadows fall towards the pole, by half the shade radius.</summary>
    public static Vector2 ShadowShift(float latitude, float radius) => new Vector2(0f, (latitude >= 0f ? 1f : -1f) * radius * 0.5f);
}
