using System.IO;
using System.Reflection;
using Newtonsoft.Json;

namespace RevitMCPCommandSet.Localization
{
    /// <summary>
    /// Optional localisation for user-facing messages.
    ///
    /// English, in the code, is the source of truth. This adds translations on top,
    /// opt-in and off by default.
    ///
    /// Why it exists: this command set was written in Simplified Chinese by its
    /// original authors. Converting the code to English should not throw their
    /// wording away, so the original strings are kept here as a catalogue.
    ///
    /// LANGUAGE TAG: the catalogue is "zh-Hans" - Chinese in the SIMPLIFIED script
    /// (BCP 47: language "zh" plus script subtag "Hans"). That is not the same as
    /// "Mandarin": Mandarin ("cmn") is a SPOKEN variety, while Hans/Hant describe
    /// the WRITING system, and speakers of several varieties write both. For a
    /// string table the script is what matters, so the tag is zh-Hans. Measured
    /// against the original sources: simplified forms throughout, with no
    /// traditional-only characters present.
    ///
    /// Selection: set the environment variable REVIT_MCP_LOCALE=zh-Hans before
    /// starting Revit. Anything else, unset, or an unreadable catalogue means
    /// English - a translation problem must never break a command.
    /// </summary>
    public static class Strings
    {
        public const string DefaultLocale = "en";

        private static readonly object Gate = new object();
        private static Dictionary<string, string> _map;
        private static string _loadedFor;

        /// <summary>The locale actually in force. "en" unless one is configured.</summary>
        public static string ActiveLocale
        {
            get
            {
                string raw = Environment.GetEnvironmentVariable("REVIT_MCP_LOCALE");
                return string.IsNullOrWhiteSpace(raw) ? DefaultLocale : raw.Trim();
            }
        }

        private static string CatalogueDirectory()
        {
            string asm = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return string.IsNullOrEmpty(asm) ? null : Path.Combine(asm, "Localization");
        }

        private static Dictionary<string, string> Catalogue()
        {
            string locale = ActiveLocale;
            lock (Gate)
            {
                if (_map != null && _loadedFor == locale) return _map;

                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                if (!string.Equals(locale, DefaultLocale, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string dir = CatalogueDirectory();
                        string file = dir == null ? null : Path.Combine(dir, locale + ".json");
                        if (file != null && File.Exists(file))
                        {
                            var doc = JsonConvert.DeserializeObject<CatalogueFile>(File.ReadAllText(file));
                            if (doc?.Strings != null)
                            {
                                foreach (var kv in doc.Strings)
                                {
                                    if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                                        map[kv.Key] = kv.Value;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Deliberately swallowed and left empty: a malformed catalogue
                        // degrades to English rather than taking a Revit command down.
                        // The caller still gets a correct English message.
                        map.Clear();
                    }
                }

                _map = map;
                _loadedFor = locale;
                return _map;
            }
        }

        /// <summary>
        /// Translate one English string. Returns it unchanged when there is no
        /// translation, so an incomplete catalogue degrades per-string.
        /// </summary>
        public static string T(string english)
        {
            if (string.IsNullOrEmpty(english)) return english;
            var map = Catalogue();
            if (map.Count == 0) return english;
            return map.TryGetValue(english, out string hit) && !string.IsNullOrEmpty(hit) ? hit : english;
        }

        /// <summary>Entry count of the active catalogue; 0 when running in English.</summary>
        public static int EntryCount => Catalogue().Count;

        /// <summary>
        /// Localized aliases for a term that Revit's own UI produces (a parameter name,
        /// a family type name, the words a yes/no parameter takes). Returns an empty
        /// list when running in English or when the catalogue defines none, so callers
        /// always match the English form and only ever GAIN alternatives.
        ///
        /// See RevitUiTerms for why these are data rather than literals in control flow.
        /// </summary>
        public static IReadOnlyList<string> TermAliases(string englishTerm)
        {
            if (string.IsNullOrEmpty(englishTerm)) return EmptyAliases;
            var terms = Terms();
            return terms.TryGetValue(englishTerm, out var aliases) && aliases != null
                ? aliases
                : EmptyAliases;
        }

        private static readonly string[] EmptyAliases = new string[0];
        private static Dictionary<string, string[]> _terms;
        private static string _termsFor;

        private static Dictionary<string, string[]> Terms()
        {
            string locale = ActiveLocale;
            lock (Gate)
            {
                if (_terms != null && _termsFor == locale) return _terms;

                var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                if (!string.Equals(locale, DefaultLocale, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string dir = CatalogueDirectory();
                        string file = dir == null ? null : Path.Combine(dir, locale + ".json");
                        if (file != null && File.Exists(file))
                        {
                            var doc = JsonConvert.DeserializeObject<CatalogueFile>(File.ReadAllText(file));
                            if (doc?.Terms != null)
                            {
                                foreach (var kv in doc.Terms)
                                {
                                    if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                                        map[kv.Key] = kv.Value;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // As with Strings.T: a malformed catalogue degrades to English
                        // rather than taking a Revit command down.
                        map.Clear();
                    }
                }

                _terms = map;
                _termsFor = locale;
                return _terms;
            }
        }

        /// <summary>Test seam: force the next call to re-read from disk.</summary>
        public static void Reset()
        {
            lock (Gate)
            {
                _map = null;
                _loadedFor = null;
                _terms = null;
                _termsFor = null;
            }
        }

        private class CatalogueFile
        {
            [JsonProperty("locale")] public string Locale { get; set; }
            [JsonProperty("language")] public string Language { get; set; }
            [JsonProperty("script")] public string Script { get; set; }
            [JsonProperty("note")] public string Note { get; set; }
            [JsonProperty("strings")] public Dictionary<string, string> Strings { get; set; }

            // Values Revit itself produces in a localized UI, keyed by their English
            // form. Additive aliases, never replacements.
            [JsonProperty("terms")] public Dictionary<string, string[]> Terms { get; set; }
        }
    }
}
