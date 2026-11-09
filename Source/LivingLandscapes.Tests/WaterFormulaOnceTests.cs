using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// #132 (plan decision D4): the wetness and the moisture layer are one water definition, so the law of how the water
/// sources combine - "either source can wet the ground on its own, 1 - (1-a)(1-b)" - is written exactly once, in
/// Layers/WaterFieldMath.cs. This guards that: no other file of the mod writes a product of complements of its own,
/// every reader of the ground's water asks it through the one water field (Layers/WaterField.cs, or WaterFieldMath for
/// the plants' full moisture), and the two layer descriptions in XML say what each layer is now: LL_Wetness the internal
/// topographic input, LL_Moisture the one visible field of rain, topography and nearness to open water.
/// </summary>
public class WaterFormulaOnceTests
{
    /// <summary>
    /// A product of two complements, "(1 - a) * (1 - b)" with or without the f suffix and across line breaks: the shape
    /// of the water law. Written anywhere but WaterFieldMath.cs it is that law a second time, which is what #132 removes.
    /// </summary>
    private static readonly Regex ComplementProduct =
        new Regex(@"\(1f?\s*-\s*[^()]*\)\s*\*\s*\(1f?\s*-", RegexOptions.Compiled);

    [Fact]
    public void WaterSourcesAreCombinedInOneFileOnly()
    {
        string math = RepoFile(Path.Combine("Source", "LivingLandscapes", "Layers", "WaterFieldMath.cs"));
        string root = Directory.GetParent(Path.GetDirectoryName(math)).FullName; // Source/LivingLandscapes
        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (string.Equals(file, math, StringComparison.OrdinalIgnoreCase))
                continue;
            string text = File.ReadAllText(file);
            foreach (Match match in ComplementProduct.Matches(text))
                offenders.Add($"  {file.Substring(root.Length + 1).Replace('\\', '/')}:{LineOf(text, match.Index)}  {match.Value}");
        }
        Assert.True(offenders.Count == 0,
            "The water sources are combined in one place only, Layers/WaterFieldMath.cs (#132). The same law is written "
            + "elsewhere - call WaterFieldMath (or WaterField.TerrainWater for the ground) instead:\n" + string.Join("\n", offenders));
    }

    [Theory]
    [InlineData("Patches", "Patch_GenStep_ElevationFertility_Generate.cs")] // the wet-ground writer
    [InlineData("Geology", "VanillaTerrainWriter.cs")]                       // the ground rules and Stagnation
    [InlineData("Layers", "SettlementWorker.cs")]                            // settlement siting
    [InlineData("Layers", "MoistureWorker.cs")]                              // the plants' full moisture
    public void WaterReadersAskTheOneWaterField(string folder, string file)
    {
        string text = File.ReadAllText(RepoFile(Path.Combine("Source", "LivingLandscapes", folder, file)));
        Assert.Contains("WaterField", text);
    }

    [Fact]
    public void LayerDescriptionsSayWhatEachWaterLayerIsNow()
    {
        string xml = File.ReadAllText(RepoFile(Path.Combine("1.7", "Defs", "EnvironmentLayerDefs", "EnvironmentLayers.xml")));
        string wetness = DescriptionOf(xml, "LL_Wetness").ToLowerInvariant();
        string moisture = DescriptionOf(xml, "LL_Moisture").ToLowerInvariant();
        Assert.Contains("internal", wetness);     // the first stage of the water field, not a layer to read for its own sake
        Assert.Contains("ll_moisture", wetness);  // ...the topographic term of
        Assert.Contains("rain", moisture);        // the three sources of the one field
        Assert.Contains("upslope", moisture);     // topography
        Assert.Contains("open water", moisture);  // nearness to water
    }

    private static string DescriptionOf(string xml, string defName)
    {
        int name = xml.IndexOf($"<defName>{defName}</defName>", StringComparison.Ordinal);
        Assert.True(name > 0, $"{defName} def not found in EnvironmentLayers.xml");
        int from = xml.IndexOf("<description>", name, StringComparison.Ordinal);
        int to = xml.IndexOf("</description>", from, StringComparison.Ordinal);
        Assert.True(from > 0 && to > from, $"{defName} has no description in EnvironmentLayers.xml");
        return xml.Substring(from + "<description>".Length, to - from - "<description>".Length);
    }

    private static int LineOf(string text, int index)
    {
        int line = 1;
        for (int i = 0; i < index; i++)
            if (text[i] == '\n') line++;
        return line;
    }

    /// <summary>A file of the mod, found by walking up from the test binaries to the checkout root (as WaterDefinitionTests does).</summary>
    private static string RepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException(relative);
    }
}
