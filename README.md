# Living Landscapes

Maps that read as places: hills, rivers, cliffs and plants follow geology, water and soil instead of random noise.

> **Status:** in development, RimWorld 1.7. Only newly generated maps are affected; existing maps are never changed.
> Feedback and playtest notes are welcome as [GitHub issues](https://github.com/leifchristensen/LivingLandscapes/issues).

## What changes
- **Terrain from geology:** hills, valleys, cliffs and rock follow the tile's hilliness, rock beds and erosion; rock coverage matches the world map's terrain.
- **Water that makes sense:** rivers run downhill from the side the world map says they enter, meander and cut off oxbows on flat land, lay down floodplains, gravel bars, islands and deltas, and stop at the sea. Tiles the world gives no river carry at most a creek: trickles become damp ground, marsh or a dry wash.
- **A playable start:** the colony lands on level ground near water with room to farm, ore follows the rock and grows rarer with distance, and discoveries sit in interesting ground.
- **Plants that follow the ground:** species choose wet or dry ground, soil, slope and sun, grow in clumps, shade what grows beneath them, and compete for space.

## Requirements and load order
- RimWorld **1.7**.
- [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077). Load it before Living Landscapes.
- Load Living Landscapes after any world-generation mod (see [Compatibility](#compatibility)).

## Installation
Subscribe on the Steam Workshop (link coming once published), or download this repository and place it in your `RimWorld/Mods` folder.

## Saves
- **Adding to a save:** safe. Maps generated after you enable it, and plants that spawn after that, follow its rules; maps you already have stay as they are.
- **Removing from a save:** not safe. Maps it generated contain its cliffs and its saved map data, which the game cannot load without it.

## Settings
Options → Mod options → Living Landscapes:

| Setting | Default | What it does |
|---|---|---|
| Plant ecology | On | Wild plants follow the ground, cluster and shade each other. Off: vanilla's wild plant placement. |
| Habitat strength | 100% | How strongly the plant ecology applies (0% is vanilla). |
| Gameplay siting | On | Start spot, ore and discoveries follow the terrain. Off: vanilla's random placement. |
| Slope movement | On | Pawns walk more slowly uphill than across or downhill, and plan routes over gentle ground. |
| Scattered ruins and ancient debris | Off | Vanilla's ruins and debris, off while the terrain is in development. |

## Compatibility
| Mod or kind | Verdict |
|---|---|
| [Realistic Planets 2](https://steamcommunity.com/sharedfiles/filedetails/?id=3776715150) | Supported, optional. Load Living Landscapes after it. |
| Plant mods (tried: Vanilla Plants Expanded, ReGrowth 2, Biomes! Core) | Work. Their plants get sensible habitat defaults from their own stats. |
| Mods that change map terrain generation | Listed in the settings window. Tile features from other mods that replace the terrain are left to them; plants still follow the terrain they make. |

## FAQ
**My existing map didn't change.** By design: only maps generated after enabling the mod are affected. Settle or visit a new tile.

**The world map shows no river here, so why is there only a creek?** Rivers come from the world map. A tile without one gets at most a creek, damp ground, marsh or a dry wash.

**Maps look wrong with my terrain mod.** [Report it](#reporting-a-problem) with the mod's name.

## Reporting a problem
Please use [GitHub issues](https://github.com/leifchristensen/LivingLandscapes/issues/new/choose), not comments, so the map can be regenerated:
1. Turn on **Development mode** (Options → General).
2. On the world map, select the tile, open its **Terrain** tab and click **Debug world seed** to copy the arguments that regenerate it.
3. Paste them into the issue with a screenshot, your mod list in load order and, for errors, `Player.log` (lines from this mod start with `[LivingLandscapes]`).

[CONTRIBUTING.md](CONTRIBUTING.md#reporting-a-bug-or-a-map-that-looks-wrong) has the details, including where `Player.log` lives.

## For mod authors and contributors
- **Your own plants:** plants without rules get defaults from their stats. Habitat rules for your plants will be definable in XML; the format will be documented once it is stable.
- **Code, building and testing:** see [CONTRIBUTING.md](CONTRIBUTING.md). The repository root is the mod itself; the C# project in `Source/` builds into `1.7/Assemblies`.
- **Forks:** if you publish your own version, change the `packageId` in `About/About.xml` so the two can coexist.

## Translations
Player-facing text is in `Languages/English/Keyed`. Translations are welcome as pull requests in `Languages/<Language>/`.

## License
[MIT](LICENSE). This covers the mod's own code and content only. RimWorld and its assemblies are the property of Ludeon Studios and are not included in this repository.
