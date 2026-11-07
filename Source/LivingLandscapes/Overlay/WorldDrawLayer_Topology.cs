using System.Collections;
using System.Collections.Generic;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using RimWorld.Planet;
using Unity.Collections;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Overlay;

/// <summary>
/// The topology overlay on the world map (#201): every land tile filled with the colour of its altitude, in fixed bands
/// (<see cref="WorldAltitudeDef.topologyBand"/>) on the same hypsometric ramp the map's topology overlay uses, so what the
/// mod reads for slopes and temperature can be seen. Uses <see cref="WorldAltitude"/> (the smooth field, or the world's own
/// elevation where Realistic Planets 2 or the setting turns the model off). Display state only; nothing is saved.
/// </summary>
public class WorldDrawLayer_Topology : WorldDrawLayer
{
    private const int RenderQueue = 3590;

    /// <summary>Whether the overlay is shown; toggled by its button beside the world map's own.</summary>
    public static bool Enabled;

    private Material material;

    public override bool Visible => Enabled && base.Visible;

    public override bool VisibleWhenLayerNotSelected => false;

    public override bool VisibleInBackground => false;

    public override IEnumerable Regenerate()
    {
        if (material == null)
            material = new Material(WorldMaterials.VertexColorTransparent) { renderQueue = RenderQueue };
        foreach (object item in base.Regenerate())
            yield return item;

        WorldAltitudeDef def = WorldAltitudeDef.Current;
        List<ColorStop> ramp = EnvironmentLayerDefOf.LL_Landform?.overlay?.colors;
        if (ramp == null || ramp.Count == 0)
        {
            FinalizeMesh(MeshParts.All);
            yield break;
        }
        float top = def.topologyTop, band = Mathf.Max(1f, def.topologyBand);
        int bands = Mathf.Max(1, Mathf.CeilToInt(top / band));
        byte alpha = (byte)Mathf.Clamp(Mathf.RoundToInt(def.topologyOpacity * 255f), 0, 255);

        NativeArray<int> offsets = planetLayer.UnsafeTileIDToVerts_offsets;
        NativeArray<Vector3> verts = planetLayer.UnsafeVerts;
        for (int i = 0; i < planetLayer.TilesCount; i++)
        {
            var tile = new PlanetTile(i, planetLayer);
            Tile info = Find.WorldGrid[tile];
            if (info.WaterCovered)
                continue;
            int index = Mathf.Clamp(Mathf.FloorToInt(WorldAltitude.At(tile) / band), 0, bands - 1);
            Color color = Evaluate(ramp, bands <= 1 ? 0f : index / (bands - 1f));
            Color32 c = new Color32((byte)(color.r * 255f), (byte)(color.g * 255f), (byte)(color.b * 255f), alpha);

            LayerSubMesh subMesh = GetSubMesh(material, out int _);
            int count = subMesh.verts.Count, k = 0;
            int end = i + 1 < offsets.Length ? offsets[i + 1] : verts.Length;
            for (int j = offsets[i]; j < end; j++)
            {
                subMesh.verts.Add(verts[j]);
                subMesh.colors.Add(c);
                if (j < end - 2)
                {
                    subMesh.tris.Add(count + k + 2);
                    subMesh.tris.Add(count + k + 1);
                    subMesh.tris.Add(count);
                }
                k++;
            }
        }
        FinalizeMesh(MeshParts.All);
    }

    /// <summary>The ramp's colour at t (0..1), blending between the two stops it falls between.</summary>
    private static Color Evaluate(List<ColorStop> ramp, float t)
    {
        if (t <= ramp[0].value)
            return ramp[0].color;
        for (int s = 1; s < ramp.Count; s++)
        {
            if (t <= ramp[s].value)
                return Color.Lerp(ramp[s - 1].color, ramp[s].color, Mathf.InverseLerp(ramp[s - 1].value, ramp[s].value, t));
        }
        return ramp[ramp.Count - 1].color;
    }

    /// <summary>Shows or hides the overlay and redraws it.</summary>
    public static void Toggle()
    {
        Enabled = !Enabled;
        Find.World?.renderer.SetDirty<WorldDrawLayer_Topology>(Find.WorldGrid.Surface);
    }
}
