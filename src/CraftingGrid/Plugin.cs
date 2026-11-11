using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CasualtiesUnknown.CraftingGrid
{
    [BepInPlugin(Id, "Crafting Grid", "0.1.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "uber5001.casualtiesunknown.craftinggrid";
        internal static Plugin Instance;
        internal static ManualLogSource Log;
        ConfigEntry<bool> active;
        Harmony harmony;
        bool failed;
        GridView view;
        void Awake()
        {
            Instance = this; Log = Logger;
            active = Config.Bind("General", "Enabled", true, "Replace the crafting presentation. False restores vanilla crafting.");
            active.SettingChanged += (_, __) => { if (!active.Value) RemoveView(); };
            InstallPatches();
            Logger.LogInfo("Crafting Grid 0.1.0 ready; catalog is read from the live retail registry when crafting opens.");
        }
        void OnEnable() { if (Instance == this) InstallPatches(); }
        void OnDisable() { RemoveView(); harmony?.UnpatchSelf(); harmony = null; }
        void OnDestroy() { if (Instance == this) { RemoveView(); harmony?.UnpatchSelf(); Instance = null; } }
        void InstallPatches() { if (harmony == null) { harmony = new Harmony(Id); harmony.PatchAll(typeof(Plugin).Assembly); } }
        void RemoveView() { if (view) { view.RestoreVanilla(); Destroy(view.gameObject); view = null; } }
        internal void Fail(Exception ex)
        {
            failed = true; RemoveView();
            Logger.LogError("Crafting grid disabled for this session; vanilla remains usable. Retail API/UI may have changed. Attach this log when reporting: "+ex);
        }
        bool Refresh(PlayerCamera camera)
        {
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
            static void Postfix(PlayerCamera __instance) { if (Instance) Instance.Refresh(__instance); }
        }
    }
}
