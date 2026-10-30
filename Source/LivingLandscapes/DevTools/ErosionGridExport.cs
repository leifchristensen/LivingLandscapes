using System.IO;
using Verse;

namespace LivingLandscapes.DevTools;

/// <summary>
/// Test runs with "-ll-export-raw": the erosion model's own grid (heights, routing, drainage, the antecedent floor) at a few
/// steps of the run, and the map window before and after the regional tilt, written next to the layer exports as
/// Model_*.f32 (row-major, x fastest) with their sizes and notes in Model.txt. For measuring the model, not the map.
/// </summary>
public static class ErosionGridExport
{
    public static bool Enabled => GenCommandLine.CommandLineArgPassed("ll-export-raw");

    public static void Begin()
    {
        if (!Enabled)
            return;
        Directory.CreateDirectory(LayerImageExporter.Folder);
        File.WriteAllText(Path.Combine(LayerImageExporter.Folder, "Model.txt"), "");
    }

    public static void Note(string line)
    {
        if (Enabled)
            File.AppendAllText(Path.Combine(LayerImageExporter.Folder, "Model.txt"), line + "\n");
    }

    public static void Write(string name, float[] values, int sizeX, int sizeZ)
    {
        if (!Enabled || values == null)
            return;
        var bytes = new byte[values.Length * sizeof(float)];
        System.Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        File.WriteAllBytes(Path.Combine(LayerImageExporter.Folder, $"Model_{name}.f32"), bytes);
        Note($"grid {name} {sizeX}x{sizeZ}");
    }

    public static void Write(string name, int[] values, int sizeX, int sizeZ)
    {
        if (!Enabled || values == null)
            return;
        var floats = new float[values.Length];
        for (int i = 0; i < values.Length; i++)
            floats[i] = values[i];
        Write(name, floats, sizeX, sizeZ);
    }

    public static void Write(string name, bool[] values, int sizeX, int sizeZ)
    {
        if (!Enabled || values == null)
            return;
        var floats = new float[values.Length];
        for (int i = 0; i < values.Length; i++)
            floats[i] = values[i] ? 1f : 0f;
        Write(name, floats, sizeX, sizeZ);
    }
}
