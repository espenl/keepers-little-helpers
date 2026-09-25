using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using HarmonyLib;

namespace KeepersJournal
{
    public sealed class HelpersSettings : MonoBehaviour
    {
        private static HelpersSettings instance;
        private LazyButton pauseButton;
        private UIGamePauseWindow returnPause;
        private ConfigFile config;
        private ConfigEntry<bool>[] switches;
        public static IEnumerable<HelperSetting> SharedSettings
        {
            get
            {
                if (instance == null || instance.switches == null) yield break;
                for (int i = 0; i < instance.switches.Length; i++)
                    yield return new HelperSetting { Entry = instance.switches[i], Name = instance.names[i], Description = instance.summaries[i] };
            }
        }
        private readonly string[] names = { "Building counts", "Morning reminders", "Materials pin", "Move buildings (preview)", "Quick Stack button", "Automatic quick stack", "Multiple materials pins", "Protected items", "Storage finder", "Workstation status", "Smart reminders", "Chest names" };
        private int category;
        private GameObject settingsContent;
        private readonly List<GameObject> pages = new List<GameObject>();
        private readonly List<LazyButton> tabs = new List<LazyButton>();
        private readonly List<TextMeshProUGUI> settingsLabels = new List<TextMeshProUGUI>();
        private readonly List<Action> refreshRows = new List<Action>();
        private static readonly string[] categories = { "Building & plans", "Storage", "Reminders" };
        private static readonly int[][] groups = { new[] { 0, 2, 6, 3 }, new[] { 4, 5, 7, 8, 11, 9 }, new[] { 1, 10 } };
        private readonly string[] summaries = {
            "Show how many are already built in this area.",
            "Your character recalls known activities each morning.",
            "Shift-click a blueprint or recipe to pin its materials.",
            "Move idle buildings or unfinished plans. Some structures are unsupported.",
            "Click Quick Stack to store matching items in this area's chests.",
            "Deposit on entering an area. Optional; off by default.",
            "Combine up to six blueprints or recipes in one list.",
            "Ctrl-click carried items to keep them out of Quick Stack.",
            "Find marks matching chests. Clear removes the markers.",
            "Show work status when you approach a workstation.",
            "Skip activities already completed or checked today.",
            "Rename a chest from its storage window."
        };
        private UIDialogWindow window;
        public void Initialize(ConfigFile file, Harmony harmony)
        {
            config = file;
            instance = this;
            harmony.Patch(AccessTools.Method(typeof(UIGamePauseWindow), "Open", new[] { typeof(LazyWidgetDataBase) }), postfix: new HarmonyMethod(typeof(HelpersSettings), "AddPauseButton"));
            switches = new[] {
                file.Bind("Buildings", "Enabled", true, "Show existing building counts in the native build menu."),
                file.Bind("Morning", "Enabled", true, "Enable morning reminders."),
                file.Bind("Materials", "Enabled", true, "Enable the materials pin."),
                file.Bind("Moving", "Enabled", true, "Enable the local moving preview."),
                file.Bind("QuickStack", "Enabled", true, "Enable the manual Quick Stack button."),
                file.Bind("QuickStack", "AutomaticOnArrival", false, "Opt in to automatic quick stack on area entry."),
                file.Bind("Materials", "MultiplePins", true, "Enable multiple blueprint pins."),
                file.Bind("Storage", "ProtectedItems", true, "Protect item types from Quick Stack."),
                file.Bind("Storage", "Finder", true, "Search nearby storage."),
                file.Bind("Workstations", "StatusIcons", true, "Show workstation status."),
                file.Bind("Morning", "SmartReminders", true, "Hide checked activities."),
                file.Bind("Storage", "ChestNames", true, "Enable custom chest names.")
            };
        }
        private static void AddPauseButton(UIGamePauseWindow __instance, LazyButton ___settingsBtn)
        {
            if (instance == null || ___settingsBtn == null) return;
            if (instance.pauseButton == null)
            {
                var clone = Instantiate(___settingsBtn.gameObject, ___settingsBtn.transform.parent, false);
                clone.name = "LittleHelpersSettingsButton";
                instance.pauseButton = clone.GetComponent<LazyButton>();
                instance.pauseButton.onClick = new Button.ButtonClickedEvent();
                instance.pauseButton.onClick.AddListener(delegate {
                    __instance.Close();
                    instance.window = LazyUI.GetWindow<UIDialogWindow>();
                    instance.returnPause = __instance;
                    instance.Show();
                });
                clone.transform.SetSiblingIndex(___settingsBtn.transform.GetSiblingIndex()+1);
                instance.pauseButton.SetCallbacksIntoGamepadNavigationItem();
                if (clone.transform.parent.GetComponent<LayoutGroup>() == null)
                {
                    var rect = clone.GetComponent<RectTransform>();
                    float bottom = rect.anchoredPosition.y;
                    foreach (var button in __instance.GetComponentsInChildren<LazyButton>(true))
                    {
                        if (button == instance.pauseButton || button.transform.parent != clone.transform.parent) continue;
                        bottom = Mathf.Min(bottom, ((RectTransform)button.transform).anchoredPosition.y);
                    }
                    rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, bottom - rect.rect.height - 8);
                }
            }
            instance.pauseButton.gameObject.SetActive(true);
            foreach (var label in instance.pauseButton.GetComponentsInChildren<TextMeshProUGUI>(true)) label.text = "Keeper's Little Helpers";
            var navigation = AccessTools.Property(typeof(UIGamePauseWindow), "GamepadNavigationController").GetValue(__instance, null) as GamepadNavigationController;
            if (navigation != null) navigation.ReinitItems(false);
            ((RectTransform)__instance.transform).RefreshContentFitter();
        }
        private void LateUpdate()
        {
            // Reopening after a game-language change rebuilds labels in the selected language.
            if (pauseButton != null && pauseButton.gameObject.activeInHierarchy)
                foreach (var label in pauseButton.GetComponentsInChildren<TextMeshProUGUI>(true))
                    if (label.text != "Keeper's Little Helpers") label.text = "Keeper's Little Helpers";
            foreach (var label in settingsLabels)
                if (label != null) { L.Font(label); label.alignment = TextAlignmentOptions.TopLeft; label.color = new Color32(224, 219, 204, 255); }
        }
        private void Show()
        {
            L.Refresh();
            CleanupContent();
            var data = new UIDialogWindowData("Keeper's Little Helpers", L.T("Changes are saved immediately."),
                new List<UIDialogWindowData.ButtonData> {
                    NativeHelpers.Action(L.T("Today's tasks"), delegate { window.Close(); returnPause = null; WorldHelpers.ShowToday(); }),
                    NativeHelpers.Action(L.T("Done"), CloseSettings)
                });
            data.ShowCloseButton = true; data.CloseButtonAction = CloseSettings;
            window.Open(data, delegate { CleanupContent(); });
            var info = (TextMeshProUGUI)AccessTools.Field(typeof(UIDialogWindow), "information").GetValue(window);
            settingsContent = new GameObject("LittleHelpersOptions", typeof(RectTransform), typeof(LayoutElement));
            settingsContent.transform.SetParent(info.transform.parent, false);
            settingsContent.transform.SetSiblingIndex(info.transform.GetSiblingIndex() + 1);
            var layout = settingsContent.GetComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = 540;
            layout.minHeight = layout.preferredHeight = 44 + groups[category].Length * 58;
            // This game's content layout preserves child sizes instead of
            // driving them from LayoutElement. Supply the actual bounds too.
            var contentRect = (RectTransform)settingsContent.transform;
            contentRect.sizeDelta = new Vector2(layout.preferredWidth, layout.preferredHeight);
            for (int c = 0; c < categories.Length; c++)
            {
                int tab = c;
                var button = NativeHelpers.SmallButton(settingsContent.transform, L.T(categories[c]), delegate { category = tab; SelectCategory(); });
                Place((RectTransform)button.transform, c * 182, 0, 176, 30);
                tabs.Add(button);
            }
            var container = settingsContent;
            for (int pageIndex = 0; pageIndex < groups.Length; pageIndex++)
            {
            var page = new GameObject("SettingsPage_" + pageIndex, typeof(RectTransform));
            page.transform.SetParent(container.transform, false);
            Place((RectTransform)page.transform, 0, 0, 540, 44 + groups[pageIndex].Length * 58);
            pages.Add(page);
            settingsContent = page;
            int position = 0;
            foreach (int entry in groups[pageIndex])
            {
                int index = entry;
                float y = 44 + position++ * 58;
                Label(info, L.T(names[index]), 0, y, 452, 22);
                var description = Label(info, L.T(summaries[index]), 0, y + 22, 452, 32);
                description.fontSize = info.fontSize * .85f;
                var toggle = NativeHelpers.SmallButton(settingsContent.transform, "", delegate {
                    switches[index].Value = !switches[index].Value;
                    config.Save();
                    foreach (var refresh in refreshRows) refresh();
                });
                Place((RectTransform)toggle.transform, 474, y + 3, 66, 28);
                var caption = toggle.GetComponentInChildren<TextMeshProUGUI>(true);
                refreshRows.Add(delegate {
                    bool available = index == 5 || index == 7 ? switches[4].Value
                        : index == 6 ? switches[2].Value : index == 10 ? switches[1].Value : true;
                    toggle.interactable = available;
                    caption.text = switches[index].Value ? L.T("On") : L.T("Off");
                    description.text = available ? L.T(summaries[index]) : index == 6 ? L.T("Enable Materials pin to use this option.")
                        : index == 10 ? L.T("Enable Morning reminders to use this option.") : L.T("Enable Quick Stack button to use this option.");
                });
            }
            }
            settingsContent = container;
            foreach (var refresh in refreshRows) refresh();
            SelectCategory();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)info.transform.parent);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)window.GetComponentInChildren<GenericWindowLayout>(true).transform);
            ((RectTransform)window.transform).RefreshContentFitter();
        }
        private void SelectCategory()
        {
            for (int i = 0; i < pages.Count; i++) { pages[i].SetActive(i == category); tabs[i].interactable = i != category; }
            float height = 44 + groups[category].Length * 58;
            ((RectTransform)settingsContent.transform).sizeDelta = new Vector2(540, height);
            var layout = settingsContent.GetComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = height;
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)settingsContent.transform.parent);
            ((RectTransform)window.transform).RefreshContentFitter();
        }
        private TextMeshProUGUI Label(TextMeshProUGUI source, string value, float x, float y, float width, float height)
        {
            var label = Instantiate(source.gameObject, settingsContent.transform, false).GetComponent<TextMeshProUGUI>();
            var fitter = label.GetComponent<ContentSizeFitter>();
            if (fitter != null) { fitter.enabled = false; Destroy(fitter); }
            label.text = value; label.raycastTarget = false; label.enableAutoSizing = false;
            label.alignment = TextAlignmentOptions.TopLeft;
            Place(label.rectTransform, x, y, width, height);
            L.Font(label); label.enableAutoSizing = true; label.fontSizeMin = 9; label.fontSizeMax = label.fontSize;
            label.gameObject.SetActive(true); settingsLabels.Add(label);
            return label;
        }
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
        }
        private void CleanupContent()
        {
            settingsLabels.Clear(); refreshRows.Clear(); pages.Clear(); tabs.Clear();
            if (settingsContent != null) { settingsContent.SetActive(false); Destroy(settingsContent); settingsContent = null; }
        }
        private void CloseSettings() { window.Close(); CleanupContent(); var pause = returnPause; returnPause = null; if (pause != null) pause.Open(null); }
        private void OnDestroy() { CleanupContent(); instance = null; }
    }
    public sealed class HelperSetting
    {
        public ConfigEntry<bool> Entry;
        public string Name, Description;
    }
}
