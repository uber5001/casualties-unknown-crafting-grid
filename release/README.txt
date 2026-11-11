CRAFTING GRID 0.4.0
Casualties: Unknown full release | Windows | MIT license | by uber5001

INSTALL
1. Requires BepInEx 5 Windows x64 / Mono. If you already use BepInEx 5 mods, keep your existing loader.
   Otherwise get BepInEx_win_x64_5.4.23.5.zip from:
   https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5
   Extract that loader into the folder containing CasualtiesUnknown.exe.
2. Close the game. Copy this ZIP's BepInEx folder into that same game folder and merge folders.
   The plugin belongs at BepInEx/plugins/CraftingGrid/CasualtiesUnknown.CraftingGrid.dll.
3. Start the game, load a run, and press C. Use the Grid checkbox beside search to switch views.

No manual configuration edits or additional library mods are required.
Your existing loader settings and other plugins are preserved.

CONTROLS
Left click: select a recipe and view its native details.
Right click: attempt to craft once. Also works on the large native recipe icon in either view.
Hover: native tooltip and INT requirement in the top-left corner.
Check / X: ingredients of the selected recipe, available / missing.
White dot (top right): recipes that use the selected recipe's output, including tool requirements.
White outline: selected recipe. Arrow keys and Submit use native button navigation.
Category buttons: colored backgrounds. Search hides unmatched recipes in place.

The hand-picked layout has 12 columns when space allows. New and modded recipes appear at the end.
Crafting costs, results, and progression use the original game logic.

UPDATE
Close the game and replace the old Crafting Grid DLL with this one. Preferences are preserved.

UNINSTALL
Close the game and delete BepInEx/plugins/CraftingGrid/.
Optional preferences: BepInEx/config/uber5001.casualtiesunknown.craftinggrid.cfg.

SUPPORT
Tested on the full game 1.0 with BepInEx 5.4.23.5. Demo and multiplayer are untested.
If the Grid checkbox is missing, check the install path, loader version, and BepInEx/LogOutput.log.
Source, updates, and issues:
https://github.com/uber5001/casualties-unknown-crafting-grid
