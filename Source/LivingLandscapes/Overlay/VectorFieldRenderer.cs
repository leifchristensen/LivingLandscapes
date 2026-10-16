using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Overlay;

/// <summary>
/// Bakes a vector field (magnitude layer + direction layer) into arrow meshes, one arrow every few cells. Arrows are
/// grouped into a handful of colour classes from the overlay's ramp, so the whole field draws in a few calls per frame.
/// </summary>
public static class VectorFieldRenderer
{
    private const int ColorClasses = 8;
    private const float ShaftWidth = 0.12f;

    public static List<(Material material, List<Mesh> meshes)> Build(float[] magnitude, float[] direction, int sizeX, int sizeZ,
        OverlayProperties style, float min, float max, int renderQueue)
    {
        float altitude = AltitudeLayer.MetaOverlays.AltitudeFor();
        float range = Mathf.Max(max - min, 1e-6f);
        int spacing = style.arrowSpacing;
        var builders = new LineMeshBuilder[ColorClasses];

        for (int z = spacing / 2; z < sizeZ; z += spacing)
        {
            for (int x = spacing / 2; x < sizeX; x += spacing)
            {
                int i = z * sizeX + x;
                float t = Mathf.Clamp01((magnitude[i] - min) / range);
                int colorClass = Mathf.Min((int)(t * ColorClasses), ColorClasses - 1);
                if (ClassColor(style, colorClass, min, range).a < 0.05f)
                    continue;
                builders[colorClass] ??= new LineMeshBuilder(altitude);
                AddArrow(builders[colorClass], new Vector2(x + 0.5f, z + 0.5f), direction[i], spacing * style.arrowScale * Mathf.Lerp(0.3f, 1f, t));
            }
        }

        var result = new List<(Material, List<Mesh>)>();
        for (int c = 0; c < ColorClasses; c++)
        {
            if (builders[c] == null)
                continue;
            Material material = SolidColorMaterials.NewSolidColorMaterial(ClassColor(style, c, min, range), ShaderDatabase.SolidColor);
            material.renderQueue = renderQueue;
            result.Add((material, builders[c].Finish()));
        }
        return result;
    }

    private static Color ClassColor(OverlayProperties style, int colorClass, float min, float range) =>
        style.Evaluate(min + (colorClass + 0.5f) / ColorClasses * range);

    /// <summary>An arrow centred on the cell: a shaft and two head strokes.</summary>
    private static void AddArrow(LineMeshBuilder lines, Vector2 centre, float angle, float length)
    {
        var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        var side = new Vector2(-dir.y, dir.x);
        Vector2 tail = centre - dir * (length * 0.5f);
        Vector2 tip = centre + dir * (length * 0.5f);
        float head = Mathf.Max(length * 0.35f, 0.3f);
        lines.Add(tail, tip, ShaftWidth);
        lines.Add(tip, tip - (dir + side * 0.6f) * head, ShaftWidth);
        lines.Add(tip, tip - (dir - side * 0.6f) * head, ShaftWidth);
    }
}
