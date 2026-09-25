using BepInEx;
using GK2.Framework;
using KeepersJournal;

namespace KeepersLittleHelpers.FrameworkIntegration
{
    [BepInPlugin("local.espen.keepersjournal.framework", "Keeper's Little Helpers - Framework Integration", "0.3.7")]
    [BepInDependency("local.espen.keepersjournal", "0.3.7")]
    [BepInDependency("ru.superman4eg.gk2.framework", "0.1.8")]
    public sealed class FrameworkBridgePlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            var main = JournalPlugin.Instance;
            if (main == null) { Logger.LogError("Keeper's Little Helpers did not initialize."); return; }
            FrameworkApi.RegisterMod(new Bridge(), main.Config);
        }
        private sealed class Bridge : Gk2ModBase
        {
            public override Gk2ModMetadata Metadata
            {
                get { return new Gk2ModMetadata("local.espen.keepersjournal", "Keeper's Little Helpers", "Kontuu", "0.3.7",
                    L.T("Building, storage and reminder helpers. Also available from the pause menu."),
                    supportsRuntimeToggle: false, requiresKnownBuild: false, frameworkManagesEnabledState: false); }
            }
            public override void OnRegister(Gk2ModContext context)
            {
                foreach (var helper in HelpersSettings.SharedSettings)
                {
                    var entry = helper.Entry;
                    // Identical ConfigFile/Section/Key/type means both menus edit the same entry.
                    context.Settings.AddToggle(entry.Definition.Section, entry.Definition.Key, (bool)entry.DefaultValue,
                        L.T(helper.Name), L.T(helper.Description));
                }
                context.Settings.AddText("Localization", "Language", "auto", L.T("Language"),
                    L.T("auto follows the game. Use a language code to override it. Restart after editing translation files."));
                context.Settings.AddReadOnly("Information", "Compatibility", L.T("Compatibility"), "",
                    delegate { return L.T("Optional settings integration; this does not certify other mods or untested game builds."); });
            }
        }
    }
}
