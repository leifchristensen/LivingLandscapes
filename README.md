# Living Landscapes

A RimWorld mod that brings biological and geological rules to map generation.

In vanilla RimWorld, wild plants are chosen from the biome's plant list and scattered fairly evenly across the map. Real landscapes don't look like that. Living Landscapes aims to make plant distribution follow real-world patterns:

- **Clustering:** some species grow in dense patches rather than as scattered individuals.
- **Water dependence:** some plants only grow near rivers, lakes or coastlines.
- **Canopy and shade:** understory plants favour the shade of forests, and sun-loving plants avoid it.

> **Status:** early development. The features above are the project's goals and are not implemented yet.

## Requirements
- RimWorld **1.7**
- [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077) (Harmony 3 build for RimWorld 1.7). Load it before this mod.

## Installation
Subscribe on the Steam Workshop (link coming once published), or download this repository and place it in your `RimWorld/Mods` folder.

## Compatibility
- Safe to add to an existing save. It only affects maps generated, and plants that spawn, after it is enabled.
- Plants from other mods use sensible defaults. Mod authors can define habitat rules for their own plants in XML (documentation will follow once the format is stable).

## Building from source
The repository root is the mod itself: `About/` and `1.7/` are what RimWorld loads, and the C# project in `Source/` builds into `1.7/Assemblies`.

You need:
- The .NET 10 SDK, with Visual Studio 2026 or Rider 2026 (earlier versions do not support .NET 10).
- A RimWorld 1.7 installation. The project references the game's assemblies from your install folder rather than shipping copies of them.

Set the `RimWorldDir` property to your RimWorld install folder, either as an environment variable or on the command line, then build:

```
dotnet build Source/LivingLandscapes/LivingLandscapes.csproj -p:RimWorldDir="C:\path\to\RimWorld"
```

Harmony is referenced from NuGet at compile time only. At runtime it is provided by the Harmony mod.

### Testing your build
The simplest way to test is to make your clone visible to the game by linking it into the `Mods` folder, so every build is picked up without copying files. On Windows:

```
mklink /J "C:\path\to\RimWorld\Mods\LivingLandscapes" "C:\path\to\your\clone"
```

RimWorld 1.7 also accepts a `-mod=` launch argument that loads only the listed mods (plus the base game and DLC), which keeps test sessions fast:

```
RimWorld.exe -mod=brrainz.harmony,leifchristensen.livinglandscapes
```

### Debugging
RimWorld 1.7 runs on its own build of the .NET runtime. Its debugger libraries are unsigned, so Visual Studio refuses to attach with *"Signature validation failed for a .NET Runtime Debugger library"*. Start Visual Studio from a command prompt with signature validation turned off for that session:

```
set VSDebugger_ValidateDotnetDebugLibSignatures=0
devenv LivingLandscapes.slnx
```

See [Microsoft's notes on this error](https://github.com/dotnet/runtime/blob/main/docs/workflow/debugging/coreclr/debugging-runtime.md#resolving-signature-validation-errors-in-visual-studio) for details.

## Contributing
Issues and pull requests are welcome. If you fork the mod to publish your own version, change the `packageId` in `About/About.xml` so the two can coexist.

## License
[MIT](LICENSE). This covers the mod's own code and content only. RimWorld and its assemblies are the property of Ludeon Studios and are not included in this repository.
