using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;

namespace TwilightInputOverlay
{
    /// <summary>
    /// Public static bridge that the runtime-emitted HSRTimer tab proxy
    /// delegates to. Keeping this public is required: the proxy lives in a
    /// separate dynamic assembly, so it may only call public members of this
    /// plugin's assembly. The bridge also implements the
    /// ILocalizableSettingsPanelTab shape so the tab follows HSRTimer's language
    /// selection.
    /// </summary>
    public static class HsrtimerTabBridge
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
                Plugin.Logger.LogWarning($"TwilightInputOverlay: HSRTimer tab Title failed: {ex.Message}");
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
                Plugin.Logger.LogWarning($"TwilightInputOverlay: HSRTimer tab Draw failed: {ex.Message}");
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
                Plugin.Logger.LogWarning($"TwilightInputOverlay: HSRTimer tab SupportedLanguages failed: {ex.Message}");
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
                Plugin.Logger.LogWarning($"TwilightInputOverlay: HSRTimer tab SetLanguage failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when HSRTimer raises <c>SettingsPanelTabRegistry.SettingsSaved</c>
        /// (R9.3), so this plugin's config is persisted at the same moments
        /// HSRTimer saves its own config.
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
                Plugin.Logger.LogWarning($"TwilightInputOverlay: HSRTimer SettingsSaved handler failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Optional HSRTimer settings-panel integration implemented purely by
    /// reflection: a small runtime-generated proxy implementing HSRTimer's
    /// ISettingsPanelTab (and ILocalizableSettingsPanelTab) is registered into
    /// HSRTimer.SettingsPanelTabRegistry. The proxy only calls public static
    /// methods on <see cref="HsrtimerTabBridge"/>, mirroring the pattern used by
    /// the proven TrueFirstPerson integration. When HSRTimer is absent this
    /// class is a no-op and the plugin uses its own standalone settings panel.
    /// </summary>
    internal static class HsrtimerIntegration
    {
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

            try
            {
                var interfaceType = Type.GetType("HSRTimer.ISettingsPanelTab, HSRTimer");
                var localizableType = Type.GetType("HSRTimer.ILocalizableSettingsPanelTab, HSRTimer");
                var registryType = Type.GetType("HSRTimer.SettingsPanelTabRegistry, HSRTimer");
                if (interfaceType == null || registryType == null)
                {
                    Plugin.Logger.LogInfo("TwilightInputOverlay: HSRTimer settings-panel tab API not found; standalone settings panel stays available.");
                    return false;
                }

                var instanceProp = registryType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                var registry = instanceProp != null ? instanceProp.GetValue(null, null) : null;
                if (registry == null)
                {
                    Plugin.Logger.LogWarning("TwilightInputOverlay: HSRTimer registry not available; standalone panel stays available.");
                    return false;
                }

                var register = registryType.GetMethod("Register", new[] { typeof(string), interfaceType });
                if (register == null)
                {
                    Plugin.Logger.LogWarning("TwilightInputOverlay: HSRTimer.SettingsPanelTabRegistry.Register(string, ISettingsPanelTab) not found.");
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
                    SubscribeToSettingsSaved(registryType, registry);
                    Plugin.Logger.LogInfo("TwilightInputOverlay: settings tab registered in HSRTimer settings panel.");
                    return true;
                }

                Plugin.Logger.LogWarning("TwilightInputOverlay: HSRTimer rejected settings tab registration (duplicate or invalid plugin GUID?).");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay: HSRTimer settings tab registration failed: {ex.Message}");
                return false;
            }
        }

        private static void SubscribeToSettingsSaved(Type registryType, object registry)
        {
            try
            {
                var ev = registryType.GetEvent("SettingsSaved", BindingFlags.Public | BindingFlags.Instance);
                if (ev == null)
                {
                    Plugin.Logger.LogWarning("TwilightInputOverlay: HSRTimer SettingsSaved event not found; config will not auto-save from HSRTimer.");
                    return;
                }

                var handler = new Action(HsrtimerTabBridge.SaveConfig);
                ev.AddEventHandler(registry, handler);
                _settingsSavedEvent = ev;
                _registry = registry;
                _settingsSavedHandler = handler;
                Plugin.Logger.LogInfo("TwilightInputOverlay: subscribed to HSRTimer SettingsSaved event.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay: failed to subscribe to HSRTimer SettingsSaved: {ex.Message}");
            }
        }

        /// <summary>Unsubscribe from HSRTimer's SettingsSaved event when this plugin unloads.</summary>
        public static void Unsubscribe()
        {
            try
            {
                if (_settingsSavedEvent != null && _registry != null && _settingsSavedHandler != null)
                {
                    _settingsSavedEvent.RemoveEventHandler(_registry, _settingsSavedHandler);
                    Plugin.Logger.LogInfo("TwilightInputOverlay: unsubscribed from HSRTimer SettingsSaved event.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay: failed to unsubscribe from HSRTimer SettingsSaved: {ex.Message}");
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
                Plugin.Logger.LogWarning($"TwilightInputOverlay: failed to build HSRTimer tab proxy: {ex.Message}");
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
                var bridge = typeof(HsrtimerTabBridge).GetMethod(
                    "Title", BindingFlags.Public | BindingFlags.Static);
                il.Emit(OpCodes.Call, bridge);
            }
            else if (mi.Name == "get_SupportedLanguages")
            {
                var bridge = typeof(HsrtimerTabBridge).GetMethod(
                    "SupportedLanguages", BindingFlags.Public | BindingFlags.Static);
                il.Emit(OpCodes.Call, bridge);
            }
            else if (mi.Name == "SetLanguage")
            {
                var bridge = typeof(HsrtimerTabBridge).GetMethod(
                    "SetLanguage", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(string) }, null);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, bridge);
            }
            else // Draw
            {
                var bridge = typeof(HsrtimerTabBridge).GetMethod(
                    "Draw", BindingFlags.Public | BindingFlags.Static);
                il.Emit(OpCodes.Call, bridge);
            }
            il.Emit(OpCodes.Ret);

            tb.DefineMethodOverride(method, mi);
        }
    }
}
