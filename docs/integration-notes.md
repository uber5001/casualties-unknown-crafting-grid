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

## 0.3.0 simplified native interactions

The user's in-game screenshot confirmed the inline grid and exposed search text clipping and excess unused width. The user requested removing the custom ingredient presentation and hover-driven selection in favor of native controls.

- Left-click now calls native `SelectRecipe` with the recipe's current registry index. Hover no longer selects or updates the right-hand pane. Right-click rechecks eligibility and calls native `TryCraft` once.
- The default native `iteminteract` binding is `Mouse1`, and `HandleInput` closes crafting on that binding. A narrow prefix on `OpenCraftScreen` preserves the open menu only for right-clicks over grid tiles. Explicit crafting/pause/wound-view keys, other pointer locations, and vanilla-list mode retain native behavior. Patch ownership is verified for both integration points.
- `UITooltip` was verified as a data component read by `UIUtil.GetUITooltip` from the current UI raycasts, then displayed by `GlobalDark.HandleTooltip`. Each grid tile now uses this exact component and the native recipe row's `simpleName`/`description` fields. No custom tooltip renderer remains.
- The native recipe prefab's INT label supplies its font/material/style, and `RecipeINTToColor` supplies its colors. The hover label is the number alone, matching vanilla. Availability badges use the same native `<sprite index=23>` / `<sprite index=24>` check/X glyphs as `IngredientTextForRecipe`.
- Cell size derives only from the viewport width, with a readable minimum and vertical overflow. Native preview sizing was traced to `ImageSizeDelta(texture, 12, 150)`: this natural pixel size and cap are scaled to the grid cell, using individual sprite-region dimensions for atlases. Small icons retain their native relative sizes.
- Category selection now applies category background tint only. Other categories stay visible. Search keeps its existing anonymous-slot-safe filtering behavior.
- The native input field's text viewport, text component, and placeholder are re-anchored to preserve their edge insets before the field shrinks for the checkbox. All original geometry is saved for restoration.
- Removed custom ingredient bills, producer reverse indexes, ingredient-pocket geometry, yield/count badges, tooltip rendering, and hover timing. The top line now reads “Right click to quick craft”.
- Release build succeeded with zero warnings/errors. 83 updated executable checks passed for the retained layout, modded catalogs, category tint, search privacy, and full-width sizing; obsolete ingredient-pocket and hover-timer tests were removed with those features.
- Installed DLL SHA-256 matched the build: `590DD5B9227316EBD981D9327C0447C16807330E5E8B697CF054541FC45879C3`. A real in-game opening logged successful 0.3.0 attachment with 136 displayed recipes, 10 columns × 17 rows, game 1.0, Unity 2022.3.62f3. The new mouse actions, native tooltip/marks, search clipping fix, and icon-size appearance await the user's check.

## 0.3.1 selected-recipe requirement badges

The user's screenshot confirmed native tooltip rendering, corrected search text, and the wider grid, but showed availability badges on every known recipe. That was a misinterpretation: badges should describe only the inputs of the currently selected recipe.

The grid now queries that recipe's `GetItemsForRecipeThorough()` allocation and projects its requirement states onto matching known output tiles. Unrelated recipes and the selected parent receive no badge. Repeated slots aggregate to X if any are missing. A fulfilled alternative marks only the actual allocated provider's output; a missing alternative marks eligible producer choices using native item/liquid quality metadata. Unknown identities remain concealed. Badges update immediately on left-click selection, after native crafting refreshes, and on relevant inventory or selection changes; hovering does not change the badge context.

Build succeeded with zero warnings/errors; the 83 retained layout/search/width checks passed. The installed 0.3.1 DLL matched the release build (SHA-256 `0C6E8527A907EBB2009BBA8E8C036605FADF6466A3A91631AB0E91E3C05CCD18`). Selected-requirement badge appearance still needs the user's live check.

## 0.3.2 scrollbar, native hues, and selected outline

The user clarified that 10×17 is only an artifact of the original prototype. Stable-ish placements across content changes and minimal unused space are soft goals. They will prototype a more logical arrangement separately; category ordering and full-catalog fit are deferred with that work. Native automatic arrow-key navigation is explicitly wanted and remains enabled.

- The grid now has its own RectMask2D viewport inside the original viewport. Its edge insets reserve only the actual overlap of each native scrollbar track, plus a two-unit gap. A scrollbar already outside the original viewport does not subtract space a second time. The native ScrollRect switches between the grid viewport and original viewport with the Grid checkbox; list-mode geometry is restored intact.
- Native filter colors are read from graphics under the corresponding native category button. Image pixels and embedded TMP sprite glyphs are sampled with their actual tint; chromatic pixels take precedence over monochrome outlines. The sampled hues are logged once and blended softly into black. The old separate reference palette is removed.
- A white two-unit frame identifies the native selected recipe, independent of mouse hover or keyboard focus. Its edge graphics do not intercept pointer events. Unity Button automatic navigation is unchanged.
- No catalog regrouping, category reordering, row-count tuning, or fit-all redesign is included in this update. A layout fixture assertion was relabeled to make clear that its current dimensions are not a product requirement.
- Release build succeeded with zero warnings/errors. All 89 retained geometry/search/width checks, including six scrollbar-clearance cases, passed.
- The installed DLL matched the release build (SHA-256 `8857475DA681F98F4FE2E76DC159646DB126ACC10AC2CA5F11FC5DA47D6360D0`). A real C press attached 0.3.2 without errors in game 1.0 / Unity 2022.3.62f3. Native icon sampling succeeded for all five categories: Materials `#939393`, Tools `#9C755C`, Medicine `#FF6969`, Utilities `#6AFF5D`, Food `#FFFA61`. Scrollbar clipping and selected-outline appearance await the user's visual check.

## 0.3.3 hand-picked arrangement and native preview quick craft

- The supplied `recipe-layout-12col.json` replaces the original automatic INT/category packing. It is stored as `recipe-layout.json` and embedded in the DLL. Seven authored bands produce a 12-column by 12-row base layout. Group widths and recipe sequences are respected without visible group labels. Each group fills left to right then downward; groups wrap in order on narrower grids, and oversized groups use the available columns. Cell sizing and scrollbar clearance are retained.
- Match authored selectors against the live catalog using canonical English output names, repair flags, and specific ingredient names for variants. Liquid names follow the native liquid registry's `localeName` / `localeFromItem` metadata: native output ID `water` is the authored Clean water recipe, rather than the generic English quality name Water. All 137 selectors resolved uniquely against the installed recipe definitions, including the two bio-chem variants and repair recipes.
- Concealed or absent entries leave blank authored slots; ordinary unknown recipes retain their anonymous tiles. Every unmapped live recipe is appended after the authored bands in deterministic canonical output-name/identity order. Existing authored positions do not shift when new outputs are added.
- Right-clicking the large native recipe preview attempts the selected recipe through `TryCraft` and suppresses the default right-click close binding. This also works with Grid unchecked. The shortcut uses the native button's raycast target and shares the per-frame crafting guard with grid tiles. It adds no pointer handler that could intercept the existing left-click action. The topmost raycast hit is used for the preview shortcut, respecting overlapping UI, and explicit close/back keys still win.
- Release build succeeded with zero warnings/errors. All 473 layout/search/tint/clearance checks passed against both the supplied reference fixture and a locally inspected installed-game catalog. Tests cover widths 1 through 12, exact authored positions, variant selection, unmapped additions, and missing/secret slots. In-game appearance and the new native-preview shortcut still require the user's live check.
- Installed DLL matches the release build (SHA-256 `06E849F888DCD889B10D86A94AF376FE47BC3E81795FAE0273691E46ED21519E`). Fresh startup loaded 0.3.3, verified its Harmony hooks, and completed the BepInEx chainloader without errors.

## 0.3.4 touching tiles and darker unknown markers

- Removed the two-unit horizontal and vertical tile gaps. Each tile's background, pointer area, selection frame, and native tooltip now cover a complete cell, with adjacent cells meeting edge to edge. Width calculation, narrow-grid column thresholds, positions, and content bounds use the same gap-free sizing. The authored arrangement, outer margins, and scrollbar clearance are preserved.
- Anonymous unknown-recipe crosses changed from RGB `(120,130,114)` to `(32,35,30)`, roughly one quarter of their former brightness. Native selected-recipe ingredient check/X sprites are unchanged.
- Release build succeeded with zero warnings/errors. All 473 existing checks passed after updating the width and column-threshold expectations for gap-free cells. Tooltip continuity and marker darkness await the user's in-game check.
- Installed DLL matches the release build (SHA-256 `9F72CFFE323F212B4EE3E9D5BFF03E846B0FAE55FB9CDDFA5D8C74962F73E24C`).

## 0.4.0 public release, installation, and used-in dots

- MIT license selected by uber5001. Player-first installation/control documentation and a reproducible packaging script replace the developer-oriented flat-DLL download. The ZIP contains `BepInEx/plugins/CraftingGrid/CasualtiesUnknown.CraftingGrid.dll`, README.txt, LICENSE, and NOTICE. It contains no loader binaries, game assets, or configuration files that could overwrite user settings.
- Startup applies `HideFlags.HideAndDontSave` to the manager from plugin Awake, the same flags used by BepInEx's `HideManagerGameObject` option. A fresh retail startup with that loader option set to its default `false` remained active through an actual crafting-menu attachment: 136 displayable recipes, 12 columns by 12 rows. The user's original loader preference was restored after the test. The developer installer now leaves loader configuration alone.
- A five-unit white top-right dot marks known recipes that use the selected recipe's output. Matching reuses the native ingredient helper for specific IDs, liquid/item type, ignored outputs, and quality/tool alternatives. Unknown recipes and the selected parent are excluded; inventory changes and selection refreshes update the indicators without changing crafting behavior. The user confirmed the dot works and supplied the public release screenshot.
- Release build succeeded with zero warnings/errors; all 473 retained checks passed. Installed DLL SHA-256: `2ED6CD2BF1054E284337196FB326F3D453AC604B9199D1762DA521F6E1D461F9`. The ZIP was inspected for the correct folder structure, exact DLL bytes, MIT license, and absence of loader/config replacements. Package SHA-256: `b006e6bc09312ed9ce416506006b322545e175952515005e560236e695a39e23`.
