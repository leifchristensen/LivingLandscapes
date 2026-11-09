using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// HYDRO-S8 phase A (HYDRO-UNIFICATION row S8, #186): the boundary-state switch ships off, so every map built without
/// -ll-boundary=on is identical to main. TerrainChannelSettings cannot be linked into this game-free project (its
/// nested settings live in the routing engine's files, which use game types), so this guards the shipped default where
/// it is defined: the field declaration in the source.
/// </summary>
public class BoundaryStateTests
{
    private const string SettingsPath = @"Source\LivingLandscapes\Geology\TerrainChannelSettings.cs";

    private static string SettingsSource()
    {
        // Walk up from the test binaries to the repo root (the folder holding Source\LivingLandscapes).
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, SettingsPath)))
                return File.ReadAllText(Path.Combine(dir.FullName, SettingsPath));
        throw new FileNotFoundException("Could not find " + SettingsPath + " above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void BoundaryStateDefaultsOff()
    {
        Match declared = Regex.Match(SettingsSource(), @"public bool boundaryState\s*=\s*(\w+)\s*;");
        Assert.True(declared.Success, "TerrainChannelSettings.boundaryState is not declared as `public bool boundaryState = ...;`");
        Assert.Equal("false", declared.Groups[1].Value);
    }
}
