# Casualties: Unknown crafting grid

Native crafting presentation mod requested by uber5001. The complete specification and interactive visual fixture are preserved in [handoff/PROMPT.md](handoff/PROMPT.md) and [handoff/crafting-reference.html](handoff/crafting-reference.html).

**Status:** quick prototype, version 0.3.3. Reads the live retail recipe registry and reuses native icons, tooltips, selection, and crafting. Additional recipes, levels, categories, and alternate recipes are derived dynamically. Inventory, translation, and hovering do not determine tile positions.

Open `handoff/crafting-reference.html` in a desktop browser to exercise the sample. Its catalog, stock, art, font, and crafting model are test fixtures only; none are embedded in the plugin. The user waived the initial hands-on reference requirement in favor of using its source and trying a quick native prototype.

The grid lives inside the original **left-hand recipe scroll area**, on the native black background. Its clipped viewport excludes overlapping scrollbar tracks, with a small gap, so icons and clicks cannot extend underneath them. Search, category buttons, right-hand recipe details, and native close/back controls stay visible. The **Grid** checkbox beside search switches immediately between grid and vanilla list and remembers the preference. Each mode keeps its own scroll position; the vanilla content's anchors, viewport, and dimensions are restored in list mode.

Arrangement comes from the embedded `recipe-layout.json`, copied from the user's `recipe-layout-12col.json`. Its seven bands use the authored group widths and recipe order, producing a 12-column × 12-row base layout. Groups have no visible labels. Recipes fill each group left to right, then downward. Narrower viewports wrap whole groups in order; a group wider than the viewport wraps its recipes within the available columns. Absent or concealed recipes leave their authored slot empty. Every unmapped live recipe is appended after the authored bands in canonical output-name/identity order, so mods and future additions remain accessible.

Selectors match canonical English output names and native repair/ingredient metadata, including both bio-chem variants, independently of the current UI language or registry order. Only arrangement data is embedded; recipe definitions, art, and eligibility still come from the live game. Cells fill the usable width and overflow vertically as needed. Icons use the native preview's pixel-based sizing instead of stretching small sprites to fill their cells. Atlas regions receive the same sizing rule using their own dimensions.

**Left-click selects** the recipe through the original `SelectRecipe` action and updates the right-hand information. **Right-click quick-crafts** one attempt through `TryCraft`, after resolving the current registry entry and rechecking native eligibility. Right-clicking the large native recipe icon also crafts the selected recipe, in both Grid and vanilla-list modes, and keeps the menu open. Its native left-click action is preserved. A short “Right click to quick craft” hint occupies the top line. The default right-click close binding is suppressed only over grid tiles or the native recipe icon; C, Escape, and clicks elsewhere keep their native behavior.

The selected recipe has a white outline that stays on it while other tiles are hovered. Native Unity Button navigation remains enabled: arrow keys move focus, and Submit selects a recipe through the same left-click action. No custom arrow-key system is added.

Hover uses the same native `UITooltip` component and name/description as a vanilla recipe row. It reveals only the numeric INT requirement in the top-left corner, using the native list font/style and `RecipeINTToColor` colors. Native green check / red X sprites appear **only on recipes producing inputs for the currently selected recipe**. They reflect the selected recipe's native ingredient allocation, not each tile's own craftability. Repeated requirements show X if any required slot is missing. A fulfilled alternative marks its actual provider; a missing alternative marks eligible producer choices. Unrelated, unknown, and selected-parent tiles have no badge. Full-color ready art, 7% dimmed grayscale unavailable art, and anonymous unknown slots remain.

Category buttons tint matching known recipes using the **hue of their native filter icon**, sampled from the actual icon sprite/text and its tint. All-categories clears those backgrounds. Search still hides unmatched tiles in place, preserving every slot. The search text area and placeholder resize with the field so the Grid checkbox does not clip the start of “Search recipes”.

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

Start retail Casualties: Unknown through Steam, load a run, press **C**, and check the **Grid** checkbox beside search. Toggle it off and on to compare the vanilla list and grid. Hover for the native tooltip and INT color, left-click for right-hand information, and right-click a funded recipe once to craft. Category buttons should tint matching tiles without hiding other categories. The game keeps all native risk/failure effects.

Config is `BepInEx/config/uber5001.casualtiesunknown.craftinggrid.cfg`. `[General] UseGrid` is controlled by the in-menu checkbox. Set `Enabled = false` and restart to remove the checkbox and use the untouched vanilla interface. Removing only `BepInEx/plugins/CraftingGrid/CasualtiesUnknown.CraftingGrid.dll` also removes the mod. On an integration exception the grid restores stock controls and logs the exception to `BepInEx/LogOutput.log`.

## Prototype limits

- This build intentionally omits custom item/category tooltips, output/count badges, hover timers, ingredient tiles, and related-recipe highlighting. Requirements and results are shown by the native interface.
- The earlier overlay and inline grid were visually confirmed in-game. This simplified revision, controller navigation, actual craft outcomes, and compatibility with other installed mods need live validation. A changed native API falls back to vanilla; arbitrary API changes cannot be guaranteed compatible.
- Geometry is cached between inspections; live eligibility is checked after native refreshes and inventory/context changes polled every 0.4 seconds while the panel is open. Catalog additions and registry replacement rebuild the layout. Renamed outputs or changed variant ingredients may stop matching an authored selector; those live recipes move to the appended tail.

See [docs/integration-notes.md](docs/integration-notes.md) for verified installation and API findings. Game binaries, decompiled source, personal saves, loader binaries, and machine-specific build paths are excluded from this repository.
