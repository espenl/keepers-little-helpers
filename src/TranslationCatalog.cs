using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace KeepersJournal
{
    // English source strings are stable catalog keys (gettext-style).
    public sealed class TranslationCatalog
    {
        private readonly Dictionary<string, string> entries = new Dictionary<string, string>(StringComparer.Ordinal);
        public static string Normalize(string language)
        {
            string code = (language ?? "en").Trim().ToLowerInvariant().Replace('-', '_');
            if (code == "kr" || code == "kor") code = "ko";
            if (code == "") code = "en";
            return Regex.IsMatch(code, @"^[a-z]{2,3}(_[a-z0-9]{2,8})?$") ? code : "en";
        }
        public void Load(string directory, string language, Action<string> warn)
        {
            entries.Clear();
            string code = Normalize(language);
            var layers = new List<string> { "en" };
            string neutral = code.Split('_')[0];
            if (neutral != "en") layers.Add(neutral);
            if (code != neutral) layers.Add(code);
            foreach (string layer in layers)
            {
                string path = Path.Combine(directory, layer + ".json");
                if (!File.Exists(path)) continue;
                try
                {
                    var info = new FileInfo(path);
                    if (info.Length > 1024 * 1024) throw new InvalidDataException("Language file exceeds 1 MB.");
                    var json = JObject.Parse(File.ReadAllText(path));
                    foreach (var entry in json.Properties())
                    {
                        if (entry.Value.Type != JTokenType.String) { warn("Non-text translation: " + entry.Name); continue; }
                        string value = (string)entry.Value;
                        if (string.IsNullOrWhiteSpace(value) || !ValidFormat(entry.Name, value))
                        { warn("Invalid translation or placeholders: " + entry.Name); continue; }
                        entries[entry.Name] = value;
                    }
                }
                catch (Exception e) { warn("Cannot load " + layer + ".json: " + e.Message); }
            }
        }
        private static HashSet<string> Placeholders(string value)
        {
            var set = new HashSet<string>();
            foreach (Match match in Regex.Matches(value, @"(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})")) set.Add(match.Groups[1].Value);
            return set;
        }
        public static bool ValidFormat(string source, string value)
        {
            if (!Placeholders(source).SetEquals(Placeholders(value))) return false;
            // Reject broken composite formatting before it reaches gameplay UI.
            try { string.Format(CultureInfo.InvariantCulture, value, new object[32]); return true; }
            catch (FormatException) { return false; }
        }
        public string Get(string source) { string value; return entries.TryGetValue(source, out value) ? value : source; }
        public string Format(string source, params object[] args)
        {
            try { return string.Format(CultureInfo.CurrentCulture, Get(source), args); }
            catch (FormatException) { return string.Format(CultureInfo.CurrentCulture, source, args); }
        }
    }
}
