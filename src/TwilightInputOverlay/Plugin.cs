using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace TwilightInputOverlay
{
    /// <summary>
    /// BepInEx plugin entry point. Loads config + localization, spawns the
    /// persistent input-overlay HUD, and either integrates into HSRTimer's or
    /// TwilightTimer's settings panel (when one of those timers is present) or
    /// spawns the standalone IMGUI settings panel. Both timers are soft
    /// dependencies; without them the plugin still works and provides its own
    /// panel.
    /// </summary>
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency("HSRTimer", BepInEx.BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("TwilightTimer", BepInEx.BepInDependency.DependencyFlags.SoftDependency)]
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

            // 3. Optional HSRTimer/TwilightTimer integration. This is pure
            //    reflection, so the plugin loads fine when neither timer is
            //    present. When integrated, the config UI lives in that timer's
            //    settings panel and the standalone panel is not spawned.
            bool integrated = TimerSettingsIntegration.TryRegister(this);
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
            TimerSettingsIntegration.Unsubscribe();
        }
    }
}
