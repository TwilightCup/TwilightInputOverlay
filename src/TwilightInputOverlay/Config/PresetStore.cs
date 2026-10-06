using System;
using System.IO;
using System.Text;

namespace TwilightInputOverlay
{
    /// <summary>
    /// Configuration presets, mirroring HSRTimer's R11 preset feature. A preset
    /// is a folder under <c>&lt;config&gt;/TwilightInputOverlay/presets/</c>
    /// containing a <c>settings.ini</c> snapshot of the whole configuration
    /// (HUD appearance, mouse overlay, key layout), excluding the global
    /// preferences — the UI language, the standalone panel hotkey and the
    /// [Presets] selection itself — so loading a preset never changes those.
    ///
    /// The currently selected preset is persisted as a normal config item,
    /// <c>[Presets] Current</c> in <c>settings.ini</c>. First-load / old-version
    /// upgrade detection is intentionally directory-based: when the
    /// <c>presets/</c> root or the <c>default</c> preset does not exist, the
    /// plugin creates <c>default</c> from the current config and selects it.
    /// There is no separate "presets initialized" flag.
    /// </summary>
    public static class PresetStore
    {
        public const string DefaultPresetName = "default";

        /// <summary>The presets root directory under the plugin config dir.</summary>
        public static string RootDir => Path.Combine(PersistenceService.PluginDir, "presets");

        /// <summary>Directory for a named preset (folder name is sanitized).</summary>
        public static string DirFor(string name)
            => Path.Combine(RootDir, SanitizeName(name ?? ""));

        /// <summary>
        /// Make a name safe for use as a directory name. The sanitized form is
        /// also the user-visible preset name, which keeps display/storage 1:1.
        /// </summary>
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "_";
            name = name.Trim();
            if (name.Length == 0)
                return "_";
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-' ? c : '_');
            return sb.ToString();
        }

        /// <summary>
        /// All preset folder names, with <c>default</c> always pinned to the top;
        /// the rest are in ordinal order.
        /// </summary>
        public static string[] ListPresets()
        {
            try
            {
                if (!Directory.Exists(RootDir))
                    return new string[0];
                var dirs = Directory.GetDirectories(RootDir);
                var names = new string[dirs.Length];
                for (int i = 0; i < dirs.Length; i++)
                    names[i] = Path.GetFileName(dirs[i]);
                Array.Sort(names, StringComparer.Ordinal);

                int defaultIndex = Array.IndexOf(names, DefaultPresetName);
                if (defaultIndex <= 0)
                    return names;

                var reordered = new string[names.Length];
                reordered[0] = names[defaultIndex];
                int j = 1;
                for (int i = 0; i < names.Length; i++)
                {
                    if (i == defaultIndex) continue;
                    reordered[j++] = names[i];
                }
                return reordered;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay[presets]: failed to list presets: {ex.Message}");
                return new string[0];
            }
        }

        public static bool Exists(string name)
            => !string.IsNullOrEmpty(name) && Directory.Exists(DirFor(name));

        /// <summary>
        /// Create <c>default</c> on first load / upgrade and repair the selected
        /// preset to a valid existing one. Called once from Plugin.Awake after
        /// config load.
        /// </summary>
        public static void EnsureInitialized(ConfigService cfg)
        {
            if (cfg == null)
                return;

            try
            {
                Directory.CreateDirectory(RootDir);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay[presets]: failed to create presets directory: {ex.Message}");
                return;
            }

            if (!Exists(DefaultPresetName))
            {
                CreateSnapshot(DefaultPresetName, cfg);
                cfg.Settings.CurrentPreset = DefaultPresetName;
                cfg.SaveSettings();
                Plugin.Logger.LogInfo("TwilightInputOverlay[presets]: initialized default preset from current config.");
                return;
            }

            if (!Exists(cfg.Settings.CurrentPreset))
            {
                cfg.Settings.CurrentPreset = DefaultPresetName;
                cfg.SaveSettings();
                Plugin.Logger.LogInfo($"TwilightInputOverlay[presets]: selected preset '{cfg.Settings.CurrentPreset}' was missing; switched to default.");
            }
        }

        /// <summary>
        /// Create a new preset from the current config and select it. Returns
        /// false (with a localization key) when the name is empty or already
        /// exists.
        /// </summary>
        public static bool TryCreate(string name, ConfigService cfg, out string errorKey)
        {
            errorKey = null;
            if (cfg == null)
            {
                errorKey = "SETTINGS_PRESET_FAILED";
                return false;
            }

            string safe = SanitizeName(name ?? "");
            if (safe.Length == 0 || safe == "_")
            {
                errorKey = "SETTINGS_PRESET_EMPTY";
                return false;
            }
            if (Exists(safe))
            {
                errorKey = "SETTINGS_PRESET_DUPLICATE";
                return false;
            }

            CreateSnapshot(safe, cfg);
            cfg.Settings.CurrentPreset = safe;
            cfg.SaveSettings();
            return true;
        }

        /// <summary>Write the current live config into the currently selected preset.</summary>
        public static bool SaveToCurrent(ConfigService cfg)
        {
            if (cfg == null || !Exists(cfg.Settings.CurrentPreset))
                return false;
            CreateSnapshot(cfg.Settings.CurrentPreset, cfg);
            return true;
        }

        /// <summary>
        /// Apply the currently selected preset to the live config: restore the
        /// snapshot over <c>settings.ini</c>, reload the in-memory model, then
        /// re-save so the live file is normalized (global preferences that were
        /// left out of the snapshot are written back unchanged). The HUD reads
        /// <see cref="ConfigService.Settings"/> every frame, so the restored
        /// values take effect immediately.
        /// </summary>
        public static bool LoadCurrent(ConfigService cfg)
        {
            if (cfg == null || !Exists(cfg.Settings.CurrentPreset))
                return false;

            string presetDir = DirFor(cfg.Settings.CurrentPreset);
            string src = Path.Combine(presetDir, "settings.ini");
            string dst = PersistenceService.PathFor("settings.ini");
            if (!File.Exists(src))
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay[presets]: preset '{cfg.Settings.CurrentPreset}' has no settings.ini; keeping current config.");
                return false;
            }

            try
            {
                File.Copy(src, dst, true);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay[presets]: failed to restore settings.ini from preset: {ex.Message}");
                return false;
            }

            // Refresh the in-memory model from the restored file. The snapshot
            // has no [Presets] section, so CurrentPreset stays on the preset
            // that was just loaded; language/panel key keep their current values.
            cfg.Settings.Load();
            cfg.SaveSettings();
            return true;
        }

        /// <summary>
        /// Delete the currently selected preset (never <c>default</c>) and
        /// switch selection back to <c>default</c>.
        /// </summary>
        public static bool DeleteCurrent(ConfigService cfg)
        {
            if (cfg == null)
                return false;
            string name = cfg.Settings.CurrentPreset;
            if (string.Equals(name, DefaultPresetName, StringComparison.OrdinalIgnoreCase) || !Exists(name))
                return false;

            try
            {
                Directory.Delete(DirFor(name), true);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay[presets]: failed to delete preset '{name}': {ex.Message}");
                return false;
            }

            cfg.Settings.CurrentPreset = DefaultPresetName;
            cfg.SaveSettings();
            return true;
        }

        // ── snapshot internals ────────────────────────────────────────────

        private static void CreateSnapshot(string name, ConfigService cfg)
        {
            string dir = DirFor(name);
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay[presets]: failed to create preset directory '{dir}': {ex.Message}");
                return;
            }

            try
            {
                cfg.Settings.SaveTo(Path.Combine(dir, "settings.ini"));
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay[presets]: failed to write settings.ini into preset '{name}': {ex.Message}");
            }
        }
    }
}
