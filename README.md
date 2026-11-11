# Casualties: Unknown crafting grid

Native crafting presentation mod requested by uber5001. The complete specification and interactive visual fixture are preserved in [handoff/PROMPT.md](handoff/PROMPT.md) and [handoff/crafting-reference.html](handoff/crafting-reference.html).

**Status:** quick prototype, version 0.2.0. Reads the live retail recipe registry, uses native icons and matching, and delegates each craft to `PlayerCamera.TryCraft()`. Additional recipes, levels, categories, and alternate recipes are derived dynamically. Inventory, translation, and hovering do not determine tile positions.

Open `handoff/crafting-reference.html` in a desktop browser to exercise the sample. Its catalog, stock, art, font, and crafting model are test fixtures only; none are embedded in the plugin. The user waived the initial hands-on reference requirement in favor of using its source and trying a quick native prototype.

The grid lives inside the original **left-hand recipe scroll area**, on the native black background. Search, category buttons, right-hand recipe details, and native close/back controls stay visible. The **Grid** checkbox beside search switches immediately between grid and vanilla list and remembers the preference. Each mode keeps its own scroll position; the vanilla content's anchors and dimensions remain native.

Levels progress downward and categories progress across the grid: the original canonical layout is transposed, giving a 10-column × 17-row reference fixture. Native category/search filters hide unmatched cells without moving any recipe. Known ingredient-producing recipes remain visible when highlighted even if they don't match the current filter.

Known funded attempts retain full-color icons; unavailable icons are grayscale at 7% idle opacity, and unknown entries show ×. Hover reveals a single ingredient group immediately. Staying over a known recipe for 100 ms updates the native right-hand information without playing selection sounds or crafting. Clicking an ingredient only inspects it. Clicking a recipe re-resolves its live registry position, rechecks native eligibility, and invokes the native craft action once.

## Build

Requires an installed .NET SDK (tested with 9.0.306), local .NET Framework 4.7.2 reference assemblies, the retail managed game, and BepInEx 5 x64 Mono. No package feeds or downloads are used.

1. Copy `GamePaths.props.example` to `GamePaths.props` and set your local game and loader paths.
2. Run `dotnet build src/CraftingGrid/CraftingGrid.csproj -c Release`.
3. The plugin is `src/CraftingGrid/bin/Release/net472/CasualtiesUnknown.CraftingGrid.dll`.

Run geometry checks with `dotnet run --project tests/LayoutChecks/LayoutChecks.csproj -c Release` from the repository root. They exercise the supplied fixture and synthetic modded catalogs, not a real inventory or rendered game session.

## Install and try

With the game closed, copy the DLL into `BepInEx/plugins/CraftingGrid/` in a BepInEx 5 x64 Mono retail installation. In `BepInEx/config/BepInEx.cfg`, set `[Chainloader] HideManagerGameObject = true`: the default manager is destroyed during retail startup, removing the plugin before C can reach it. This is the setting recommended by [BepInEx troubleshooting](https://docs.bepinex.dev/articles/user_guide/troubleshooting.html). The installer sets it automatically while preserving other settings and a backup. It can also reuse a compatible loader already installed in another local game:

```powershell
./scripts/Install.ps1 -GamePath 'C:\path\to\Casualties Unknown' -LoaderSource 'C:\path\to\existing\BepInEx-game'
```

`LoaderSource` is the game root containing `winhttp.dll` and `BepInEx/core`, not the `BepInEx` directory. The script copies only loader-core DLLs, bootstrap files, and this plugin. It stops if the game is running or an unrecognized bootstrap would be overwritten.

Start retail Casualties: Unknown through Steam, load a run, press **C**, and check the **Grid** checkbox beside search. Toggle it off and on to compare the vanilla list and grid. Hover a known tile for 100 ms to update the right-hand details. Select a category and check that other recipes disappear in place, with ingredient highlights allowed through. Click a funded recipe once and check the inventory change. The game keeps all native risk/failure effects.

Config is `BepInEx/config/uber5001.casualtiesunknown.craftinggrid.cfg`. `[General] UseGrid` is controlled by the in-menu checkbox. Set `Enabled = false` and restart to remove the checkbox and use the untouched vanilla interface. Removing only `BepInEx/plugins/CraftingGrid/CasualtiesUnknown.CraftingGrid.dll` also removes the mod. On an integration exception the grid restores stock controls and logs the exception to `BepInEx/LogOutput.log`.

## Prototype limits

- Optional repeated-craft counts are deferred. There is no count badge or count calculation in this build.
- The earlier overlay was visually confirmed in-game. The inline revision, controller navigation, actual craft outcomes, and matching with other installed mods need live validation. A changed native API falls back to vanilla; arbitrary API changes cannot be guaranteed compatible.
- Requirement quantities use native allocations. Liquid slots stay discrete and use one-vessel availability. They intentionally preserve the game's own allocation semantics, including reuse rules; the prototype does not add an alternate crafting simulator.
- Generic missing-quality art uses a native example when available, otherwise a short quality label. Some inspection qualifiers currently use English. Category names and recipe/item names use native localization.
- Geometry is cached between inspections; live eligibility is checked after native refreshes and inventory/context changes polled every 0.4 seconds while the panel is open. Catalog additions and registry replacement rebuild the layout; a content update may shift bands.

See [docs/integration-notes.md](docs/integration-notes.md) for verified installation and API findings. Game binaries, decompiled source, personal saves, loader binaries, and machine-specific build paths are excluded from this repository.
