using System.IO;
using System.Text;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using LivingLandscapes.Overlay;
using UnityEngine;
using Verse;

namespace LivingLandscapes.DevTools;

/// <summary>
/// Writes every layer of a map to a PNG (its overlay style, or a grey ramp for layers without one; water tinted
/// blue; north up), a plain map of rock and terrain, and a text summary, so the fields can be inspected without
/// plants, fog or UI in the way.
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
            if (!environment.TryGetGrid(layer, out EnvironmentGrid grid))
                continue;
            string path = Path.Combine(Folder, $"{layer.defName}.png");
            bool markCliffs = layer == EnvironmentLayerDefOf.LL_Height || layer == EnvironmentLayerDefOf.LL_Landform;
            OverlayProperties style = layer.overlay?.ResolveFor(grid.Min, grid.Max) ?? GreyRamp(grid.Min, grid.Max);
            Export(map, grid, style, path, markCliffs);
            summary.AppendLine($"{layer.defName}: min {grid.Min:F3} max {grid.Max:F3} {layer.unit} -> {path}");
        }
        ExportTerrainMap(map, Path.Combine(Folder, "Map.png"));
        Invariants.Report(map, summary);
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
                if (markCliffs && map.edificeGrid[CellIndicesUtility.CellToIndex(px / PixelsPerCell, py / PixelsPerCell, sizeX)] is { } cliff
                    && CliffDefs.IsCliff(cliff.def))
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

    private static OverlayProperties GreyRamp(float min, float max) => new OverlayProperties
    {
        colors = new System.Collections.Generic.List<ColorStop>
        {
            new ColorStop { value = min, color = new Color(0.1f, 0.1f, 0.1f) },
            new ColorStop { value = max, color = new Color(0.95f, 0.95f, 0.95f) },
        },
    };

    /// <summary>
    /// A plain top-down map, one pixel block per cell: natural rock in its own colour (so rock beds show), rough stone
    /// floors as a lighter shade of their rock, water blue (deep darker), cliffs dark red, other ground by type.
    /// </summary>
    private static void ExportTerrainMap(Map map, string path)
    {
        int sizeX = map.Size.x, sizeZ = map.Size.z;
        var floorRock = new System.Collections.Generic.Dictionary<TerrainDef, Color>();
        foreach (ThingDef rock in DefDatabase<ThingDef>.AllDefs)
        {
            if (rock.building?.isNaturalRock == true && rock.building.naturalTerrain != null && rock.graphicData != null)
                floorRock[rock.building.naturalTerrain] = rock.graphicData.color;
        }
        var pixels = new Color[sizeX * PixelsPerCell * sizeZ * PixelsPerCell];
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                int i = z * sizeX + x;
                Building edifice = map.edificeGrid[i];
                TerrainDef terrain = map.terrainGrid.TerrainAt(i);
                Color color;
                if (edifice != null && CliffDefs.IsCliff(edifice.def))
                    color = CliffMark;
                else if (edifice != null && edifice.def.building?.isNaturalRock == true && edifice.def.graphicData != null)
                    color = edifice.def.graphicData.color * 0.75f;
                else if (terrain == null)
                    color = Color.black;
                else if (terrain.IsWater || terrain.IsRiver)
                    color = terrain.passability == Traversability.Impassable || terrain.defName.Contains("Deep")
                        ? new Color(0.12f, 0.25f, 0.55f) : new Color(0.3f, 0.5f, 0.8f);
                else if (floorRock.TryGetValue(terrain, out Color rockColor))
                    color = Color.Lerp(rockColor, Color.white, 0.35f);
                else
                    color = GroundColor(terrain);
                color.a = 1f;
                for (int py = 0; py < PixelsPerCell; py++)
                {
                    for (int px = 0; px < PixelsPerCell; px++)
                        pixels[(z * PixelsPerCell + py) * sizeX * PixelsPerCell + x * PixelsPerCell + px] = color;
                }
            }
        }
        var texture = new Texture2D(sizeX * PixelsPerCell, sizeZ * PixelsPerCell, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels);
        texture.Apply();
        texture.SaveAsPNG(path);
        Object.Destroy(texture);
    }

    private static Color GroundColor(TerrainDef terrain)
    {
        string name = terrain.defName;
        if (name.Contains("Marsh") || name.Contains("Mud"))
            return new Color(0.33f, 0.36f, 0.22f);
        if (name.Contains("Sand"))
            return new Color(0.85f, 0.77f, 0.52f);
        if (name.Contains("Gravel"))
            return new Color(0.6f, 0.58f, 0.55f);
        if (name.Contains("Ice") || name.Contains("Snow"))
            return new Color(0.9f, 0.93f, 0.97f);
        // Soil: richer is darker and greener.
        return Color.Lerp(new Color(0.62f, 0.5f, 0.34f), new Color(0.3f, 0.36f, 0.18f), Mathf.Clamp01(terrain.fertility - 0.5f));
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
