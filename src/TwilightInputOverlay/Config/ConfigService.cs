using System.IO;
using BepInEx;

namespace TwilightInputOverlay
{
    /// <summary>
    /// Facade over the plugin's settings and localization. Initialized once in
    /// Plugin.Awake; HUD and panels read from the shared instance every frame so
    /// edits apply live. Changes are persisted on panel close / game exit.
    /// </summary>
    public sealed class ConfigService
    {
        public static ConfigService Instance { get; private set; }

        public static void Init(ConfigService instance)
        {
            if (Instance == null && instance != null)
                Instance = instance;
        }

        public SettingsModel Settings { get; } = new SettingsModel();
        public LocalizationService Localization { get; } = new LocalizationService();

        /// <summary>
        /// Raised after <see cref="SaveSettings"/> has written the settings file.
        /// Mirrors HSRTimer's config-saved event so external subscribers (and,
        /// in the standalone case, any future internal auto-save hooks) can run
        /// at the same moments the config is persisted.
        /// </summary>
        internal event System.Action SettingsSaved;

        /// <summary>Load settings and (re)build language maps, restoring the saved language.</summary>
        public void Load()
        {
            PersistenceService.EnsureDirs();
            Settings.Load();
            Localization.Reload();
            Localization.SetLanguage(Settings.CurrentLang);
        }

        public void SaveSettings()
        {
            Settings.CurrentLang = Localization.CurrentCode;
            Settings.Save();

            var saved = SettingsSaved;
            if (saved != null)
            {
                try { saved(); }
                catch (System.Exception ex)
                {
                    Plugin.Logger.LogWarning($"TwilightInputOverlay: config-saved handler threw: {ex.Message}");
                }
            }
        }

        /// <summary>Re-scan lang/*.txt and switch to the saved language code.</summary>
        public void ReloadLanguage()
        {
            Localization.Reload();
            Localization.SetLanguage(Settings.CurrentLang);
        }

        /// <summary>Copy the shipped default language files into the runtime lang dir on first run.</summary>
        public void EnsureDefaultLangFiles()
        {
            string dir = PersistenceService.LangDir;
            try
            {
                Directory.CreateDirectory(dir);
                CopyIfMissing("en.txt");
                CopyIfMissing("zh-Hans.txt");
            }
            catch (System.Exception ex)
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay: failed to seed default lang files: {ex.Message}");
            }
        }

        private void CopyIfMissing(string name)
        {
            string dst = Path.Combine(PersistenceService.LangDir, name);
            if (File.Exists(dst)) return;
            string src = Path.Combine(Paths.PluginPath, PluginInfo.PLUGIN_GUID, "lang", name);
            if (!File.Exists(src))
            {
                string location = typeof(ConfigService).Assembly.Location;
                if (!string.IsNullOrEmpty(location))
                    src = Path.Combine(Path.GetDirectoryName(location) ?? "", "lang", name);
            }
            if (!File.Exists(src)) return;
            try { File.Copy(src, dst, overwrite: false); }
            catch (System.Exception ex) { Plugin.Logger.LogWarning($"TwilightInputOverlay: copy {name} failed: {ex.Message}"); }
        }
    }
}
