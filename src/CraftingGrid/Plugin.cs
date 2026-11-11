using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CasualtiesUnknown.CraftingGrid
{
    [BepInPlugin(Id, "Crafting Grid", "0.3.4")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "uber5001.casualtiesunknown.craftinggrid";
        internal static Plugin Instance;
        internal static ManualLogSource Log;
        ConfigEntry<bool> active;
        ConfigEntry<bool> useGrid;
        Harmony harmony;
        bool failed;
        bool refreshObserved;
        GridView view;
        void Awake()
        {
            Instance = this; Log = Logger;
            active = Config.Bind("General", "Enabled", true, "Replace the crafting presentation. False restores vanilla crafting.");
            useGrid = Config.Bind("General", "UseGrid", true, "Use the icon grid in the native recipe scroll area. The in-menu Grid checkbox changes this preference.");
            active.SettingChanged += (_, __) => { if (!active.Value) RemoveView(); };
            useGrid.SettingChanged += (_, __) => { if (view && view.Camera) view.Camera.RefreshRecipeList(); };
            InstallPatches();
            Logger.LogInfo("Crafting Grid 0.3.4 ready; catalog is read from the live retail registry when crafting opens.");
        }
        internal bool UseGrid => useGrid.Value;
        internal void SetGridMode(bool value) { useGrid.Value = value; }
        void OnEnable() { if (Instance == this) { Logger.LogInfo("Crafting Grid enabled."); InstallPatches(); } }
        void OnDisable() { Logger.LogInfo("Crafting Grid disabled; removing its patches and presentation."); RemoveView(); harmony?.UnpatchSelf(); harmony = null; }
        void OnDestroy() { if (Instance == this) { Logger.LogInfo("Crafting Grid component destroyed."); RemoveView(); harmony?.UnpatchSelf(); Instance = null; } }
        void InstallPatches()
        {
            if (harmony != null) return;
            harmony = new Harmony(Id);
            harmony.PatchAll(typeof(Plugin).Assembly);
            var target = AccessTools.Method(typeof(PlayerCamera), nameof(PlayerCamera.RefreshRecipeList), Type.EmptyTypes);
            var info = target == null ? null : Harmony.GetPatchInfo(target);
            if (info == null || !info.Postfixes.Any(p => p.owner == Id))
                throw new InvalidOperationException("Crafting Grid failed to register its RefreshRecipeList postfix. Vanilla crafting remains available.");
            var open = AccessTools.Method(typeof(PlayerCamera), nameof(PlayerCamera.OpenCraftScreen), Type.EmptyTypes);
            var closeInfo = open == null ? null : Harmony.GetPatchInfo(open);
            if (closeInfo == null || !closeInfo.Prefixes.Any(p => p.owner == Id))
                throw new InvalidOperationException("Crafting Grid failed to register its right-click close-binding guard.");
            Logger.LogInfo("Verified Harmony postfix on "+target.DeclaringType.FullName+"."+target.Name+" from "+target.Module.Assembly.Location);
        }
        void RemoveView() { if (view) { view.RestoreVanilla(); Destroy(view.gameObject); view = null; } }
        internal void Fail(Exception ex)
        {
            failed = true; RemoveView();
            Logger.LogError("Crafting grid disabled for this session; vanilla remains usable. Retail API/UI may have changed. Attach this log when reporting: "+ex);
        }
        bool Refresh(PlayerCamera camera)
        {
            if (!refreshObserved)
            {
                refreshObserved = true;
                Logger.LogInfo($"Native recipe refresh reached grid hook: componentActive={isActiveAndEnabled}, setting={active.Value}, panelActive={camera.craftingPanel.activeSelf}, registryCount={Recipes.recipes?.Count ?? -1}.");
            }
            if (!isActiveAndEnabled || failed || !active.Value || !camera.craftingPanel.activeSelf || Recipes.recipes == null || Recipes.recipes.Count == 0) return false;
            try
            {
                if (!view || view.Camera != camera)
                {
                    RemoveView();
                    var root = new GameObject("CraftingGrid", typeof(RectTransform));
                    root.layer = camera.craftingPanel.layer;
                    root.transform.SetParent(camera.craftingPanel.transform, false);
                    view = root.AddComponent<GridView>(); view.Initialize(camera);
                }
                view.RefreshState(); return true;
            }
            catch (Exception ex) { Fail(ex); return false; }
        }
        [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.RefreshRecipeList))]
        static class RefreshPatch
        {
            [HarmonyPostfix]
            static void Postfix(PlayerCamera __instance) { if (Instance) Instance.Refresh(__instance); }
        }
        [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.OpenCraftScreen))]
        static class KeepOpenForQuickCraft
        {
            static bool Prefix(PlayerCamera __instance)
            {
                if (!Instance || !Instance.view) return true;
                try { return !Instance.view.ShouldKeepOpenForQuickCraft(__instance); }
                catch (Exception ex) { Instance.Fail(ex); return true; }
            }
        }
    }
}
