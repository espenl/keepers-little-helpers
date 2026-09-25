using System;
using System.IO;
using BepInEx.Configuration;
using LazyBearTechnology;
using UnityEngine;
using TMPro;

namespace KeepersJournal
{
    public static class L
    {
        private static readonly TranslationCatalog catalog = new TranslationCatalog();
        private static ConfigEntry<string> language;
        private static string directory, current;
        private static Action<string> warn;
        private static LazyFontData regularFont;
        public static int Revision { get; private set; }
        public static string CurrentLanguage { get { return current ?? "en"; } }
        public static void Initialize(ConfigFile config, string pluginDirectory, Action<string> warning)
        {
            language = config.Bind("Localization", "Language", "auto", "auto follows the game language. Or set a language code such as ko, ru or pt_br. Restart after editing language files.");
            directory = Path.Combine(pluginDirectory, "Localization"); warn = warning;
            // BepInEx Awake runs before Unity has finished bootstrapping. Do not
            // touch game singletons/font assets here; resolve auto in Update.
            current = string.Equals(language.Value, "auto", StringComparison.OrdinalIgnoreCase) ? "en" : TranslationCatalog.Normalize(language.Value);
            catalog.Load(directory, current, warn); Revision++;
        }
        internal static void Refresh()
        {
            if (language == null) return;
            string selected = language.Value;
            if (string.Equals(selected, "auto", StringComparison.OrdinalIgnoreCase))
            {
                selected = LLBase.CurrentLang;
                if (string.IsNullOrEmpty(selected)) return;
            }
            selected = TranslationCatalog.Normalize(selected);
            if (current == selected) return;
            current = selected; catalog.Load(directory, selected, warn); Revision++;
        }
        public static string T(string source) { return catalog.Get(source); }
        public static string F(string source, params object[] args) { return catalog.Format(source, args); }
        internal static void Font(TextMeshProUGUI text)
        {
            if (text == null) return;
            if (text.GetComponent<HelperLanguageFont>() == null) text.gameObject.AddComponent<HelperLanguageFont>();
            // Use the game's own language-specific glyph atlas, including CJK.
            if (regularFont == null)
                foreach (var data in Resources.FindObjectsOfTypeAll<LazyFontData>())
                    if (data.name == "small_font") { regularFont = data; break; }
            if (regularFont == null) return;
            var font = regularFont.GetFontAssetFor(CurrentLanguage.Replace('_', '-'), false, false);
            if (font != null && text.font != font) { text.font = font; text.fontSharedMaterial = font.material; }
        }
    }
    // Native TextStyleComponent.Start can run after creation; apply our language
    // atlas after that initial style pass and again when the language changes.
    internal sealed class HelperLanguageFont : MonoBehaviour
    {
        private int revision = -1;
        private void OnEnable() { revision = -1; }
        private void LateUpdate()
        {
            if (revision == L.Revision) return;
            L.Font(GetComponent<TextMeshProUGUI>()); revision = L.Revision;
        }
    }
}
