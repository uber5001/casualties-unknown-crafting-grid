# Initial integration inspection

Inspection performed November 10, 2026, against the installed retail game. This is static assembly inspection, not a real-game validation run.

## Verified environment

| Component | Observed version or identity |
| --- | --- |
| Steam application | 4576490, Casualties: Unknown (retail) |
| Steam build | 26525321 |
| Executable | CasualtiesUnknown.exe, Windows x64 |
| Unity | 2022.3.62f3 (96770f904ca7) |
| Backend | Managed Mono; MonoBleedingEdge and Assembly-CSharp.dll present |
| Retail game assembly SHA-256 | DE2462A80D1A6D0A95A3D8032589A1B30E784853CB6BBD3951A52EEB67FC7335 |
| Installed .NET SDKs | 6.0.406, 7.0.201, 9.0.306 |
| Locally available loader | BepInEx 5.4.23.5, in the separately installed demo |
| Locally available analysis tools | Mono.Cecil in the demo loader; ICSharpCode.Decompiler 7.1.0.6543 in Visual Studio 2022; Windows SDK ILDasm |

At initial inspection the retail installation had no BepInEx directory or Doorstop bootstrap. The demo loader log confirms that BepInEx 5.4.23.5 has run under Unity 2022.3 on Windows x64. Compatibility with the retail installation is **not yet exercised in a running game**. Do not copy the demo's plugins, patchers, configuration, multiplayer updater, or personal logs into the retail game or this repository.

The official [BepInEx installation documentation](https://docs.bepinex.dev/articles/user_guide/installation/index.html) recommends a stable loader matched to executable architecture. The locally installed stable x64 Mono loader is a candidate; no download has been performed or authorized. Local build paths must remain configurable and untracked.

## Native members verified in the retail assembly

The supplied [NativeUILib research](https://github.com/Dylanvip2024/NativeUILib/blob/main/docs/native-ui-research.zh-CN.md) concerns the demo and was used only as a lead. The following members were independently located in the retail Assembly-CSharp.dll using Mono.Cecil:

- `Recipes.recipes`: `List<Recipe>`; `Recipes.SetUpRecipes(): void`; `Recipes.GetVisibleRecipes(Item): List<Recipe>`.
- `Recipe`: `INT`, `items: List<RecipeItem>`, `result: RecipeResult`, `category: Recipes.RecipeCategory`, `index`, `specialKnown`, `hasMadeBefore`, `isRepair`; properties `visible`, `fullName`, `simpleName`, `description`, and `resultSprite: (Sprite, Color)`.
- `Recipe.GetItemsForRecipe(): List<Item>` and `GetItemsForRecipeThorough(): List<Item>`.
- `RecipeItem.GetMatchingItem(List<Item>, List<Item>): Item`, `DoesUseItemType(Item): bool`, and `UseItem(Item): void`.
- `RecipeItem` fields include `specific`, `specificId`, `isLiquid`, `quality`, `minimumCondition`, `destroyItem`, `ignoredId`, and `noMaxCondition`.
- `RecipeResult` fields include `id`, `isLiquid`, `amount`, `resultCondition`, and `dontDrainResultLiquid`.
- `PlayerCamera.OpenCraftScreen(): void`, `RefreshRecipeList(): void`, `SelectRecipe(int): void`, `TryCraft(): void`, and `RecipeINTToColor(int): Color`.

Static method-body inspection confirms these relationships:

1. `OpenCraftScreen` operates the native screen and calls `RefreshRecipeList` when opening.
2. `RefreshRecipeList` obtains `Recipes.GetVisibleRecipes`, applies ordering and filters, constructs the stock controls, and wires recipe buttons. That list is a presentation list, not the full registry.
3. `SelectRecipe(int)` stores `selectedRecipe`, calls `PlayUISound`, and refreshes native recipe details. It must not be used for hover inspection.
4. `TryCraft` resolves `Recipes.recipes[selectedRecipe]`, calls that recipe's `TryMake` once, then calls `RefreshRecipeList`.
5. `TryMake` calls the native item allocation, `RecipeItem.UseItem`, `RecipeResult.SpawnResult`, audio, and progression. It is a mutation path and must never be used to estimate counts.
6. Both native allocation methods collect carried inventory and nearby world items, including a `Body.DoPickupCheck` call, then invoke `RecipeItem.GetMatchingItem`. A carried-inventory-only estimate would miss part of the native scope.
7. `GetMatchingItem` inspects water-container amounts, scaled liquid qualities, container contents, item qualities, and previously allocated items. Tool/liquid matching must be understood before implementing quantities or repeated-attempt counts.

These observations do not yet establish all branches, side effects, level rules, registration readiness, secrecy semantics, or snapshot-count behavior. Read the complete relevant methods with the locally available decompiler before implementing the adapter. Keep analysis outputs local; do not commit game decompilations or assets.

## Reference access and prototype scope

The supplied ZIP contained only `PROMPT.md` and `crafting-reference.html`; both are preserved verbatim under `handoff/`.

The handoff explicitly requires opening and interacting with the reference **before implementation**. The in-app browser timed out reaching a loopback HTTP server. Selecting the existing Firefox window through Computer Use returned `Computer Use was not approved to use Firefox`. No interaction with the reference has yet succeeded.

The user subsequently waived hands-on reference interaction, requested a quick prototype for their own in-game test, and emphasized dynamic mod/update compatibility over fixed positions. Implementation proceeded from the HTML/CSS/JavaScript source. Optional repeated-craft counts were explicitly deferred for the prototype.

The relevant retail classes were decompiled locally with the installed Visual Studio decompiler. Full method inspection confirmed the above allocation and craft path, `Recipe.visible` knowledge/level behavior, risk effects in `RecipeResult.SpawnResult`, the native liquid `resultCondition` volume, retained-tool wear, and the native `craftingcategory...` localization keys. No decompiled game source is distributed here.

The initial patch is deliberately limited to a postfix on `PlayerCamera.RefreshRecipeList`. The stock refresh and other mods' hooks still run; stock controls remain operational under hidden, non-interactive CanvasGroups. The grid reads the full registry when the actual menu opens, after native initialization and normal recipe registration. Clicks locate the current index with `Recipes.recipes.IndexOf(recipe)`, set `selectedRecipe` directly without selection sounds, then call `TryCraft` once. Other mods' patches to that native craft action remain in the call path. No craft, spawn, RNG, or ingredient-use method is used by hover or eligibility display.

The native visible rule permits risky recipes up to three INT above the player and always permits `specialKnown` recipes. The prototype uses that rule instead of the fixture's risk model. `RecipeItem.GetMatchingItem` accepts liquids only when one vessel meets the slot requirement and reserves destroyed non-liquid providers between slots; the display uses the game's thorough allocation for these decisions.

## Validation and handoff

- Release build succeeded against the installed retail assemblies and local BepInEx 5.4.23.5 with zero warnings and zero errors. No package feeds were used.
- 131 executable geometry checks passed: fixture recipe/output counts, 17×10 desktop layout, exact reference band widths, canonical ordering independent of registry enumeration, whole-band wrapping, contiguous ingredient pockets, overflow, and new levels/categories.
- `scripts/Install.ps1` installed the loader core/bootstrap from the existing local demo installation and the prototype DLL into the retail game. Only `BepInEx/core` and `BepInEx/plugins/CraftingGrid` were created under the retail BepInEx folder. No demo plugin, patcher, or updater was copied.
- Installed DLL SHA-256 matched the build: `35B3B46CC3D9A3F635F75C778A56EE1EDDFEDB9E7772A3F3D64168D7D315FA58`.
- `artifacts/CraftingGrid-0.1.0.zip` contains the plugin DLL and README, without game or loader binaries.
- No real-game menu, rendering, craft, failure, inventory, or controller checks have been run. The user requested doing the in-game test themselves.

Shortest check: start the retail game through Steam, load a run, press C, hover one known recipe, click it once, and check the inventory change. Then try a blocked tile and close/reopen. For a loading/UI failure, inspect `BepInEx/LogOutput.log` for `Crafting Grid` and `Crafting grid attached`; the attachment line records the actual live catalog size and game/Unity versions.

## 0.1.1 startup persistence correction

The first user test opened the vanilla crafting screen. BepInEx loaded 0.1.0 without errors, but no grid attachment was logged. A diagnostic 0.1.1 build verified that the Harmony postfix was registered on the correct retail assembly, then logged `OnDisable` and `OnDestroy` immediately after startup. This removed the hook before the user could reach crafting.

The supported correction is `[Chainloader] HideManagerGameObject = true` in `BepInEx/config/BepInEx.cfg`, as documented in [BepInEx troubleshooting](https://docs.bepinex.dev/articles/user_guide/troubleshooting.html). The installer now sets this for the retail game, preserves the rest of the config, and saves `BepInEx.cfg.previous`. The applied config was compared with its backup: only this setting changed.

0.1.1 also verifies patch ownership during startup and records the first native refresh's activation state and live registry size. It retains the existing Harmony/menu integration rather than introducing a second activation path. The release build passed with zero warnings/errors. After applying the documented loader setting and restarting, the plugin remained enabled through main-menu startup. A real in-game C press then reached the hook and logged successful grid attachment: 137 registry entries, 136 displayed entries (one genuinely secret entry concealed), 17 columns, one strip, retail game 1.0, Unity 2022.3.62f3.

The user confirmed the menu works and supplied an in-game screenshot. The screenshot verifies grid rendering, native item art, reserved anonymous unknown slots, inspection INT/yield badges, related-recipe category highlights, and a contiguous ingredient group showing red `0/2` and `0/1` shortages. Actual craft mutation, failure outcomes, and controller navigation have not yet been confirmed.

## 0.2.0 inline native interface revision

The user revised the design to keep the original crafting interface, transpose the grid into the native left scroll area, add a persistent Grid checkbox beside search, preserve native category/search controls, and update the right-hand information after about 100 ms of hover. They also requested much darker unavailable art.

- The native `recipeScrollRect`, viewport, scrollbar, list content, and all right-hand controls are reused. A separate grid content object is switched into `ScrollRect.content`; the original list's geometry remains unchanged. Only the list content's CanvasGroup is hidden in grid mode. Each mode saves its scroll position.
- `UseGrid` persists the checkbox choice in BepInEx config. Search is narrowed to reserve the checkbox's space, with its external clear button moved along if needed. Those offsets are restored when the mod is disabled.
- Canonical geometry is transposed dynamically: the 137-entry fixture becomes 10 columns × 17 rows. No catalog is copied into the plugin, and added levels/categories still derive from live metadata.
- Category and search predicates follow native search-over-category behavior. Filtered tiles use invisible, non-interactive CanvasGroups while retaining their cells. Known related ingredient producers can override the filter; unknown identities cannot be exposed through search or ingredient highlights.
- Hover inspection is immediate in the grid. A cancellable 100 ms dwell sets the selected live registry index and calls `RefreshCurrentlySelectedRecipe()` directly. It never calls sound-playing `SelectRecipe()`. Crafting remains a separate one-click `TryCraft()` call.
- The surface is black and unavailable idle icons use 7% opacity; the inspected unavailable icon retains its local brightness lift.
- Release build: zero warnings/errors. 151 executable checks passed, including transposed geometry, modded catalogs, ingredient pockets, filter exceptions, and hover delay/cancellation.
- The installed DLL matched the build (SHA-256 `C5C77794DD1095BDDC5F85E71719EF9177438FD2CDC048CC456BEC22C329CA18`). A real in-game C press logged successful inline attachment in game 1.0: 137 registered recipes, 136 displayed, 10 columns × 17 rows. The checkbox/filter/hover visual and behavior check is pending user feedback.
