using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TwilightInputOverlay
{
    /// <summary>
    /// A loaded language: its BCP-47 code (from the filename), a display name
    /// (from the __LANG_NAME__ entry), and its key→value map.
    /// </summary>
    public sealed class Language
    {
        public string Code;          // e.g. "en", "zh-Hans"
        public string DisplayName;   // e.g. "English", "简体中文"
        public readonly Dictionary<string, string> Map = new Dictionary<string, string>();
    }

    /// <summary>
    /// Localization manager following the HSRTimer spec (R7). Scans the lang dir
    /// for *.txt files, builds a language list and per-language maps, and
    /// resolves keys with the fallback chain: current language → English base →
    /// the key itself. Switching languages takes effect immediately;
    /// hot-reload re-reads files.
    /// </summary>
    public sealed class LocalizationService
    {
        private readonly Dictionary<string, Language> _languages = new Dictionary<string, Language>();
        private Language _current;
        private Language _english;

        /// <summary>All loaded languages, ordered: English first then others by code.</summary>
        public IEnumerable<Language> Languages
        {
            get
            {
                if (_english != null) yield return _english;
                foreach (var kvp in _languages)
                    if (kvp.Value != _english) yield return kvp.Value;
            }
        }

        public string CurrentCode => _current != null ? _current.Code : "en";

        // Embedded base files shipped inside the DLL so localization always
        // works even if the lang/ folder is absent next to the plugin.
        private static readonly string[] EmbeddedBases = { "en", "zh-Hans" };

        /// <summary>Scan the lang directory and (re)build all language maps.</summary>
        public void Reload()
        {
            _languages.Clear();
            _english = null;
            _current = null;

            foreach (var code in EmbeddedBases)
            {
                var lang = LoadEmbedded("TwilightInputOverlay.lang." + code + ".txt", code);
                if (lang != null)
                {
                    _languages[code] = lang;
                    if (code == "en") _english = lang;
                }
            }

            string dir = PersistenceService.LangDir;
            if (Directory.Exists(dir))
            {
                foreach (var path in Directory.GetFiles(dir, "*.txt"))
                {
                    string code = Path.GetFileNameWithoutExtension(path);
                    string displayName;
                    var lang = new Language { Code = code };
                    foreach (var e in LanguageFile.Parse(path, out displayName))
                        lang.Map[e.Key] = e.Value;
                    bool hasDisplayName = !string.IsNullOrEmpty(displayName);
                    lang.DisplayName = hasDisplayName ? displayName : code;

                    Language existing;
                    if (_languages.TryGetValue(code, out existing))
                    {
                        foreach (var kv in lang.Map) existing.Map[kv.Key] = kv.Value;
                        if (hasDisplayName) existing.DisplayName = lang.DisplayName;
                    }
                    else
                    {
                        _languages[code] = lang;
                    }
                    if (code == "en") _english = _languages["en"];
                }
            }

            Plugin.Logger.LogInfo($"TwilightInputOverlay: loaded {_languages.Count} language file(s) (en guaranteed by embedded base).");
        }

        /// <summary>Load an embedded resource as a Language, or null if absent/unreadable.</summary>
        private static Language LoadEmbedded(string resourceName, string code)
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;
                    var lines = new List<string>();
                    using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null) lines.Add(line);
                    }
                    var lang = new Language { Code = code };
                    string displayName = null;
                    foreach (var entry in LanguageFile.ParseLines(lines, "embedded:" + code, out displayName))
                        lang.Map[entry.Key] = entry.Value;
                    lang.DisplayName = displayName ?? code;
                    return lang;
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay: failed to load embedded '{code}': {ex.Message}");
                return null;
            }
        }

        /// <summary>Set the current language by code; returns false (and keeps current) if missing.</summary>
        public bool SetLanguage(string code)
        {
            if (code == null) return false;
            Language lang;
            if (!_languages.TryGetValue(code, out lang))
            {
                Plugin.Logger.LogWarning($"TwilightInputOverlay: language '{code}' not found; keeping '{CurrentCode}'.");
                return false;
            }
            _current = lang;
            return true;
        }

        /// <summary>Resolve a key: current → English → key itself.</summary>
        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;
            if (_current != null)
            {
                string v;
                if (_current.Map.TryGetValue(key, out v) && !string.IsNullOrEmpty(v))
                    return v;
            }
            if (_english != null && _current != _english)
            {
                string v;
                if (_english.Map.TryGetValue(key, out v) && !string.IsNullOrEmpty(v))
                    return v;
            }
            return key;
        }

        /// <summary>Convenience: format with string args after lookup.</summary>
        public string Get(string key, params object[] args)
        {
            string s = Get(key);
            try { return string.Format(s, args); }
            catch { return s; }
        }

        /// <summary>The display name to show in a language picker, or the code.</summary>
        public string DisplayNameOf(string code)
        {
            Language lang;
            return _languages.TryGetValue(code, out lang) ? (lang.DisplayName ?? code) : code;
        }
    }
}
