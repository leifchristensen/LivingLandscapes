using System.IO;
using System.Text;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using LivingLandscapes.Overlay;
using UnityEngine;
using Verse;

namespace LivingLandscapes.DevTools;

/// <summary>
/// Writes each overlay layer of a map to a PNG (colour ramp, contours, water tinted blue; north up) plus a text
/// summary, so the fields can be inspected without plants, fog or UI in the way.
/// </summary>
public static class LayerImageExporter
{
    private const int PixelsPerCell = 4;
    private static readonly Color WaterTint = new Color(0.2f, 0.45f, 0.95f);
    private static readonly Color CliffMark = new Color(0.55f, 0.05f, 0.05f);
    private static readonly Color StepsMark = Color.white;

    public static string Folder => Path.Combine(GenFilePaths.SaveDataFolderPath, "LivingLandscapes", "Exports");

    public static void ExportAll(Map map)
    {
        var environment = map.GetComponent<MapComponent_Environment>();
        if (environment == null)
            return;
        Directory.CreateDirectory(Folder);
        var summary = new StringBuilder();
        summary.AppendLine($"map {map} | biome {map.Biome?.defName} | size {map.Size.x}x{map.Size.z} | tile {map.Tile} | hilliness {map.TileInfo?.hilliness} | rainfall {map.TileInfo?.rainfall:F0} mm");
        foreach (EnvironmentLayerDef layer in EnvironmentLayerOrder.Sorted)
        {
            if (layer.overlay == null || !environment.TryGetGrid(layer, out EnvironmentGrid grid))
                continue;
            string path = Path.Combine(Folder, $"{layer.defName}.png");
            bool markCliffs = layer == EnvironmentLayerDefOf.LL_Height || layer == EnvironmentLayerDefOf.LL_Landform;
            Export(map, grid, layer.overlay.ResolveFor(grid.Min, grid.Max), path, markCliffs);
            summary.AppendLine($"{layer.defName}: min {grid.Min:F3} max {grid.Max:F3} {layer.unit} -> {path}");
        }
        File.WriteAllText(Path.Combine(Folder, "summary.txt"), summary.ToString());
        Log.Message($"[LivingLandscapes] Exported environment layers to {Folder}");
    }

    /// <summary>
    /// Writes one layer. With <paramref name="markCliffs"/>, terrace cliffs (dark red) and steps (white) are drawn
    /// over it, so their placement can be checked against the contours.
    /// </summary>
    private static void Export(Map map, EnvironmentGrid grid, OverlayProperties style, string path, bool markCliffs)
    {
        int sizeX = map.Size.x;
        int sizeZ = map.Size.z;
        int width = sizeX * PixelsPerCell;
        int height = sizeZ * PixelsPerCell;
        float[] values = grid.ToArray();

        // Sample the field per pixel (bilinear between cell centres) so contours come out smooth.
        var sampled = new float[width * height];
        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
                sampled[py * width + px] = Sample(values, sizeX, sizeZ, (px + 0.5f) / PixelsPerCell - 0.5f, (py + 0.5f) / PixelsPerCell - 0.5f);
        }

        var pixels = new Color[width * height];
        bool contours = style.contourInterval > 0f;
        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
            {
                int i = py * width + px;
                float v = sampled[i];
                Color color = style.FillColor(v);
                if (map.terrainGrid.TerrainAt(CellIndicesUtility.CellToIndex(px / PixelsPerCell, py / PixelsPerCell, sizeX)) is { } terrain
                    && (terrain.IsWater || terrain.IsRiver))
                    color = Color.Lerp(color, WaterTint, 0.6f);
                if (contours && v <= style.contourMax)
                {
                    int level = ContourLevelCrossed(sampled, width, height, px, py, style.contourInterval);
                    if (level != int.MinValue)
                        color = Color.Lerp(color, style.contourColor, style.IsIndexLevel(level) ? 1f : 0.7f);
                }
                if (markCliffs && map.edificeGrid[CellIndicesUtility.CellToIndex(px / PixelsPerCell, py / PixelsPerCell, sizeX)] is Building_Cliff cliff)
                    color = cliff.def.passability == Traversability.Standable ? StepsMark : CliffMark;
                color.a = 1f;
                pixels[i] = color;
            }
        }

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels);
        texture.Apply();
        texture.SaveAsPNG(path);
        Object.Destroy(texture);
    }

    /// <summary>The contour level between this pixel and its east or north neighbour, or int.MinValue if none.</summary>
    private static int ContourLevelCrossed(float[] sampled, int width, int height, int px, int py, float interval)
    {
        int band = Mathf.FloorToInt(sampled[py * width + px] / interval);
        if (px + 1 < width)
        {
            int east = Mathf.FloorToInt(sampled[py * width + px + 1] / interval);
            if (east != band)
                return Mathf.Max(east, band);
        }
        if (py + 1 < height)
        {
            int north = Mathf.FloorToInt(sampled[(py + 1) * width + px] / interval);
            if (north != band)
                return Mathf.Max(north, band);
        }
        return int.MinValue;
    }

    private static float Sample(float[] values, int sizeX, int sizeZ, float x, float z)
    {
        x = Mathf.Clamp(x, 0f, sizeX - 1);
        z = Mathf.Clamp(z, 0f, sizeZ - 1);
        int x0 = (int)x, z0 = (int)z;
        int x1 = Mathf.Min(x0 + 1, sizeX - 1), z1 = Mathf.Min(z0 + 1, sizeZ - 1);
        float fx = x - x0, fz = z - z0;
        float bottom = Mathf.Lerp(values[z0 * sizeX + x0], values[z0 * sizeX + x1], fx);
        float top = Mathf.Lerp(values[z1 * sizeX + x0], values[z1 * sizeX + x1], fx);
        return Mathf.Lerp(bottom, top, fz);
    }
}
