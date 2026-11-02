# Contributing to Living Landscapes

Thanks for helping. The most useful contributions right now are **bug reports and playtest feedback with a map that can be
reproduced**; code contributions are welcome too.

## Reporting a bug or a map that looks wrong
Open an issue with the bug or map-feedback template. A map we can regenerate is worth more than any description:

1. Turn on **Development mode** (Options → General).
2. On the world map, select the tile and open its **Terrain** tab. Under "Debug world tile ID" Living Landscapes shows
   **Debug world seed**; click it to copy the arguments that regenerate that tile.
3. Paste them into the issue, with a screenshot, your mod list (in load order) and, for errors, `Player.log`
   (`%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log` on Windows). Lines from this
   mod start with `[LivingLandscapes]`.

## Building
The repository root is the mod itself: `About/` and `1.7/` are what RimWorld loads, and the C# project in `Source/` builds
into `1.7/Assemblies`.

You need:
- The .NET 10 SDK, with Visual Studio 2026 or Rider 2026 (earlier versions do not support .NET 10).
- A RimWorld 1.7 installation. The project references the game's assemblies from your install folder rather than shipping
  copies of them. Harmony is referenced from NuGet at compile time only; at runtime the Harmony mod provides it.

```
dotnet build Source/LivingLandscapes/LivingLandscapes.csproj -p:RimWorldDir="C:\path\to\RimWorld"
```

Tell the build where RimWorld is installed in one of three ways:
- an environment variable `RimWorldDir`,
- `-p:RimWorldDir="C:\path\to\RimWorld"` on the command line,
- a file `LocalBuild.props` at the repository root (git-ignored):

```xml
<Project>
  <PropertyGroup>
    <RimWorldDir Condition="'$(RimWorldDir)' == ''">D:\Games\Steam\steamapps\common\RimWorld</RimWorldDir>
  </PropertyGroup>
</Project>
```

Without any of these the build looks in Steam's default library and stops with a clear message if the game is not there.

## Testing your build
Link your clone into the game's `Mods` folder so every build is picked up without copying files. On Windows:

```
mklink /J "C:\path\to\RimWorld\Mods\LivingLandscapes" "C:\path\to\your\clone"
```

The `-mod=` launch argument loads only the listed mods (plus the base game and DLC), which keeps test sessions fast:

```
RimWorld.exe -mod=brrainz.harmony,leifchristensen.livinglandscapes
```

## Debugging
RimWorld 1.7 runs on its own build of the .NET runtime. Its debugger libraries are unsigned, so Visual Studio refuses to
attach with *"Signature validation failed for a .NET Runtime Debugger library"*. Start Visual Studio from a command prompt
with signature validation turned off for that session:

```
set VSDebugger_ValidateDotnetDebugLibSignatures=0
devenv LivingLandscapes.slnx
```

See [Microsoft's notes on this error](https://github.com/dotnet/runtime/blob/main/docs/workflow/debugging/coreclr/debugging-runtime.md#resolving-signature-validation-errors-in-visual-studio).

## Regenerating a map from the command line
The mod reads launch arguments that make test maps reproducible:
- `-quicktest -ll-seed=<text>`: a new colony on a world and tile fixed by the text.
- The arguments copied from **Debug world seed** (see above) regenerate one particular tile.
- `-ll-hilliness=...`, `-ll-biome=...`, `-ll-mutators=A+B`, `-ll-coast=North|East|South|West`, `-ll-rivers=...` force a
  tile's conditions.

## How the mod is built
- **Principles first.** Terrain, water and plants come from natural principles (an equation over data the mod already
  computes), implemented once and reused. A drawn shape or a special case is accepted only as a clearly marked stand-in.
- **Data in layers.** New map data is an `EnvironmentLayerDef` with a worker in `Layers/`, not a one-off field.
- **Tuning in XML.** Behaviour per plant, rock, regime or landform lives in a `DefModExtension` in `1.7/`, so other mods
  can patch it.
- **Harmony.** One patch class per file, named `Patch_<Type>_<Method>`. Prefer prefix and postfix; match the 1.7
  signature exactly (Harmony 3 type-checks injected parameters).
- **No game code or assets.** Never commit decompiled code, game DLLs or Ludeon's art.
- **Readable code.** Descriptive names; comments say why, not what.

## Pull requests
- Branch from `main`, one topic per pull request, and reference the issue it addresses.
- Say what you tested: which seeds or tiles, with which mods.
- Everything applies to newly generated maps only; a change must not alter existing maps or break saves.
