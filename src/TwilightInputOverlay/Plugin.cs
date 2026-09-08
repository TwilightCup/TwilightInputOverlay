using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace TwilightInputOverlay
{
    /// <summary>
    /// BepInEx plugin entry point. Loads config + localization, spawns the
    /// persistent input-overlay HUD, and either integrates into HSRTimer's
    /// settings panel (when HSRTimer is present) or spawns the standalone
    /// IMGUI settings panel. HSRTimer is a soft dependency; without it the
    /// plugin still works and provides its own panel.
    /// </summary>
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency("HSRTimer", BepInEx.BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;

        private void Awake()
        {
            Logger = base.Logger;
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} v{PluginInfo.PLUGIN_VERSION} is loaded!");

            // 1. Config + localization.
            var config = new ConfigService();
            ConfigService.Init(config);
            config.EnsureDefaultLangFiles();
            config.Load();

            // 2. HUD singleton, persistent across scene loads.
            var hudGo = new GameObject("TwilightInputOverlay.Hud");
            Object.DontDestroyOnLoad(hudGo);
            hudGo.AddComponent<InputHud>();

            // 3. Optional HSRTimer integration. This is pure reflection, so the
            //    plugin loads fine when HSRTimer is absent. When integrated, the
            //    config UI lives in HSRTimer's settings panel and the standalone
            //    panel is not spawned.
            bool integrated = HsrtimerIntegration.TryRegister(this);
            if (!integrated)
            {
                var panelGo = new GameObject("TwilightInputOverlay.Panel");
                Object.DontDestroyOnLoad(panelGo);
                panelGo.AddComponent<SettingsPanel>();
            }
        }

        private void OnDestroy()
        {
            // Avoid a stale delegate if the plugin is ever unloaded.
            HsrtimerIntegration.Unsubscribe();
        }
    }
}
