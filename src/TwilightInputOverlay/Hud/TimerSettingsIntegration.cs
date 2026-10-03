using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;

namespace TwilightInputOverlay
{
    /// <summary>
    /// Public static bridge that the runtime-emitted timer settings tab proxy
    /// delegates to. Keeping this public is required: the proxy lives in a
    /// separate dynamic assembly, so it may only call public members of this
    /// plugin's assembly. The bridge also implements the
    /// ILocalizableSettingsPanelTab shape so the tab follows the host timer's
    /// language selection.
    /// </summary>
    public static class TimerSettingsTabBridge
    {
        public static string Title()
        {
            try
            {
                var cfg = ConfigService.Instance;
                return cfg != null ? cfg.Localization.Get("TAB_TITLE") : "Input Overlay";
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay: timer settings tab Title failed: {ex.Message}");
                return "Input Overlay";
            }
        }

        public static void Draw()
        {
            try
            {
                PanelContent.Draw(true);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay: timer settings tab Draw failed: {ex.Message}");
            }
        }

        public static IEnumerable<string> SupportedLanguages()
        {
            var result = new List<string>();
            try
            {
                var cfg = ConfigService.Instance;
                if (cfg == null)
                {
                    result.Add("en");
                    return result;
                }
                foreach (var lang in cfg.Localization.Languages)
                    result.Add(lang.Code);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay: timer settings tab SupportedLanguages failed: {ex.Message}");
                result.Add("en");
            }
            return result;
        }

        public static void SetLanguage(string code)
        {
            try
            {
                var cfg = ConfigService.Instance;
                if (cfg == null) return;
                if (cfg.Localization.SetLanguage(code))
                    cfg.Settings.CurrentLang = cfg.Localization.CurrentCode;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay: timer settings tab SetLanguage failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when the host timer raises <c>SettingsPanelTabRegistry.SettingsSaved</c>
        /// (R9.3), so this plugin's config is persisted at the same moments the
        /// host timer saves its own config.
        /// </summary>
        public static void SaveConfig()
        {
            try
            {
                if (ConfigService.Instance != null)
                    ConfigService.Instance.SaveSettings();
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay: timer SettingsSaved handler failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Optional timer settings-panel integration implemented purely by
    /// reflection. It supports both HSRTimer and TwilightTimer (the HSRTimer
    /// fork): a small runtime-generated proxy implementing the host timer's
    /// ISettingsPanelTab (and ILocalizableSettingsPanelTab) is registered into
    /// the host timer's SettingsPanelTabRegistry. The proxy only calls public
    /// static methods on <see cref="TimerSettingsTabBridge"/>, mirroring the
    /// pattern used by the proven TrueFirstPerson integration. When neither
    /// timer is present this class is a no-op and the plugin uses its own
    /// standalone settings panel. When both timers are present, HSRTimer is
    /// tried first so existing installations keep their current behavior.
    /// </summary>
    internal static class TimerSettingsIntegration
    {
        private readonly struct TimerPanelApi
        {
            public TimerPanelApi(string displayName, string ns)
            {
                DisplayName = displayName;
                InterfaceTypeName = ns + ".ISettingsPanelTab, " + ns;
                LocalizableTypeName = ns + ".ILocalizableSettingsPanelTab, " + ns;
                RegistryTypeName = ns + ".SettingsPanelTabRegistry, " + ns;
            }

            public string DisplayName { get; }
            public string InterfaceTypeName { get; }
            public string LocalizableTypeName { get; }
            public string RegistryTypeName { get; }
        }

        private static readonly TimerPanelApi[] SupportedTimers =
        {
            new TimerPanelApi("HSRTimer", "HSRTimer"),
            new TimerPanelApi("TwilightTimer", "TwilightTimer"),
        };

        private static bool _tried;
        private static bool _enabled;
        private static object _registry;
        private static EventInfo _settingsSavedEvent;
        private static Action _settingsSavedHandler;

        public static bool Enabled => _enabled;

        public static bool TryRegister(BaseUnityPlugin owner)
        {
            if (_tried) return _enabled;
            _tried = true;

            foreach (var api in SupportedTimers)
            {
                if (TryRegister(owner, api))
                    return true;
            }

            Plugin.Logger.LogInfo("TwilightInputOverlay: could not register with HSRTimer/TwilightTimer settings panel; standalone settings panel stays available.");
            return false;
        }

        private static bool TryRegister(BaseUnityPlugin owner, TimerPanelApi api)
        {
            try
            {
                var interfaceType = Type.GetType(api.InterfaceTypeName);
                var localizableType = Type.GetType(api.LocalizableTypeName);
                var registryType = Type.GetType(api.RegistryTypeName);
                if (interfaceType == null || registryType == null)
                {
                    Plugin.Logger.LogInfo($"TwilightInputOverlay: {api.DisplayName} settings-panel tab API not found; checking next timer.");
                    return false;
                }

                var instanceProp = registryType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                var registry = instanceProp != null ? instanceProp.GetValue(null, null) : null;
                if (registry == null)
                {
                    Plugin.Logger.LogWarning($"TwilightInputOverlay: {api.DisplayName} registry not available; checking next timer.");
                    return false;
                }

                var register = registryType.GetMethod("Register", new[] { typeof(string), interfaceType });
                if (register == null)
                {
                    Plugin.Logger.LogWarning($"TwilightInputOverlay: {api.DisplayName}.SettingsPanelTabRegistry.Register(string, ISettingsPanelTab) not found.");
                    return false;
                }

                Type proxyType = BuildSettingsTabProxy(interfaceType, localizableType);
                if (proxyType == null)
                {
                    // Fall back to a basic (non-language-aware) tab if the
                    // localizable proxy fails to build, so integration still works.
                    proxyType = BuildSettingsTabProxy(interfaceType, null);
                }
                if (proxyType == null)
                    return false;

                object proxy = Activator.CreateInstance(proxyType);
                object result = register.Invoke(registry, new object[] { owner.Info.Metadata.GUID, proxy });
                if (result is bool ok && ok)
                {
                    _enabled = true;
                    SubscribeToSettingsSaved(api, registryType, registry);
                    Plugin.Logger.LogInfo($"TwilightInputOverlay: settings tab registered in {api.DisplayName} settings panel.");
                    return true;
                }

                Plugin.Logger.LogWarning($"TwilightInputOverlay: {api.DisplayName} rejected settings tab registration (duplicate or invalid plugin GUID?).");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay: {api.DisplayName} settings tab registration failed: {ex.Message}");
                return false;
            }
        }

        private static void SubscribeToSettingsSaved(TimerPanelApi api, Type registryType, object registry)
        {
            try
            {
                var ev = registryType.GetEvent("SettingsSaved", BindingFlags.Public | BindingFlags.Instance);
                if (ev == null)
                {
                    Plugin.Logger.LogWarning($"TwilightInputOverlay: {api.DisplayName} SettingsSaved event not found; config will not auto-save from timer panel.");
                    return;
                }

                var handler = new Action(TimerSettingsTabBridge.SaveConfig);
                ev.AddEventHandler(registry, handler);
                _settingsSavedEvent = ev;
                _registry = registry;
                _settingsSavedHandler = handler;
                Plugin.Logger.LogInfo($"TwilightInputOverlay: subscribed to {api.DisplayName} SettingsSaved event.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay: failed to subscribe to {api.DisplayName} SettingsSaved: {ex.Message}");
            }
        }

        /// <summary>Unsubscribe from the host timer's SettingsSaved event when this plugin unloads.</summary>
        public static void Unsubscribe()
        {
            try
            {
                if (_settingsSavedEvent != null && _registry != null && _settingsSavedHandler != null)
                {
                    _settingsSavedEvent.RemoveEventHandler(_registry, _settingsSavedHandler);
                    Plugin.Logger.LogInfo("TwilightInputOverlay: unsubscribed from timer SettingsSaved event.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay: failed to unsubscribe from timer SettingsSaved: {ex.Message}");
            }
            finally
            {
                _settingsSavedEvent = null;
                _registry = null;
                _settingsSavedHandler = null;
            }
        }

        private static Type BuildSettingsTabProxy(Type interfaceType, Type localizableType)
        {
            try
            {
                var asmName = new AssemblyName("TwilightInputOverlay.SettingsTabProxy." + interfaceType.Assembly.GetName().Name);
                var asm = AppDomain.CurrentDomain.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);
                var mod = asm.DefineDynamicModule("SettingsTabProxyModule");

                Type[] interfaces = localizableType != null
                    ? new[] { interfaceType, localizableType }
                    : new[] { interfaceType };

                var tb = mod.DefineType("SettingsTabProxy",
                    TypeAttributes.Public | TypeAttributes.Sealed,
                    typeof(object), interfaces);

                foreach (var mi in interfaceType.GetMethods())
                    EmitProxyMethod(tb, mi);

                if (localizableType != null)
                {
                    foreach (var mi in localizableType.GetMethods())
                    {
                        // Only emit methods declared directly on the derived
                        // interface; inherited ISettingsPanelTab methods are
                        // already emitted above.
                        if (mi.DeclaringType == localizableType)
                            EmitProxyMethod(tb, mi);
                    }
                }

                return tb.CreateType();
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay: failed to build timer settings tab proxy: {ex.Message}");
                return null;
            }
        }

        private static void EmitProxyMethod(TypeBuilder tb, MethodInfo mi)
        {
            var pars = mi.GetParameters();
            var paramTypes = new Type[pars.Length];
            for (int i = 0; i < pars.Length; i++)
                paramTypes[i] = pars[i].ParameterType;

            var method = tb.DefineMethod(
                mi.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot,
                mi.ReturnType, paramTypes);

            var il = method.GetILGenerator();
            if (mi.Name == "get_Title")
            {
                var bridge = typeof(TimerSettingsTabBridge).GetMethod(
                    "Title", BindingFlags.Public | BindingFlags.Static);
                il.Emit(OpCodes.Call, bridge);
            }
            else if (mi.Name == "get_SupportedLanguages")
            {
                var bridge = typeof(TimerSettingsTabBridge).GetMethod(
                    "SupportedLanguages", BindingFlags.Public | BindingFlags.Static);
                il.Emit(OpCodes.Call, bridge);
            }
            else if (mi.Name == "SetLanguage")
            {
                var bridge = typeof(TimerSettingsTabBridge).GetMethod(
                    "SetLanguage", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(string) }, null);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, bridge);
            }
            else // Draw
            {
                var bridge = typeof(TimerSettingsTabBridge).GetMethod(
                    "Draw", BindingFlags.Public | BindingFlags.Static);
                il.Emit(OpCodes.Call, bridge);
            }
            il.Emit(OpCodes.Ret);

            tb.DefineMethodOverride(method, mi);
        }
    }
}
