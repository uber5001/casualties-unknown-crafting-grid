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

The retail installation currently has no BepInEx directory or Doorstop bootstrap. The demo loader log confirms that BepInEx 5.4.23.5 has run under Unity 2022.3 on Windows x64. Compatibility with the retail installation is **not yet exercised**. Do not copy the demo's plugins, patchers, configuration, multiplayer updater, or personal logs into the retail game or this repository.

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

## Reference access and next work

The supplied ZIP contained only `PROMPT.md` and `crafting-reference.html`; both are preserved verbatim under `handoff/`.

The handoff explicitly requires opening and interacting with the reference **before implementation**. The in-app browser timed out reaching a loopback HTTP server. Selecting the existing Firefox window through Computer Use returned `Computer Use was not approved to use Firefox`. No interaction with the reference has yet succeeded.

The preferred next step is to permit Firefox access and open the local HTML file there. Do not replace the required interaction with source-only inspection or deploy the fixture to an external host as a workaround. Once access is available:

1. Exercise the desktop grid, Flexiglass shortage, Foliage bag/Canvas highlighting, Casing counts, tooltip edges, keyboard focus, and narrow layout.
2. Decompile and fully trace the retail crafting and matching methods; establish registry readiness and change notifications.
3. Implement and test data-driven layout and ingredient-pocket geometry against the supplied fixture and synthetic modded registries.
4. Build the BepInEx plugin using local game/loader references; preserve vanilla crafting on integration failures.
5. Install the minimal compatible loader and plugin, then exercise the existing C binding and compare native crafting outcomes. Keep the fixture checks separate from real-game validation.
