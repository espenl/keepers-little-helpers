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
        private readonly string[] names = { "Building counts", "Morning reminders", "Materials pin", "Move buildings (preview)" };
        private readonly string[] details = {
            "Show existing structures in each building desk's menu.",
            "Show unlocked activities in a native thought bubble each morning. F8 repeats today's reminders.",
            "Shift-click a blueprint to pin its materials. The side panel counts carried items. Click the X to clear it. Pins last for this session.",
            "Use Move in a building desk menu. Select an idle, freestanding structure and then a clear spot in the same area. Right-click cancels. Tool-rack bonuses follow placement. Fitted attachments and special structures are not supported yet."
        };
        private int selected;
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
                file.Bind("Moving", "Enabled", true, "Enable the local moving preview.")
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
            // Native localization runs when the cloned button first becomes active.
            if (pauseButton == null || !pauseButton.gameObject.activeInHierarchy) return;
            foreach (var label in pauseButton.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (label.text != "Keeper's Little Helpers") label.text = "Keeper's Little Helpers";
        }
        private void Show()
        {
            string text = "";
            for (int i=0;i<names.Length;i++) text += (i==selected ? "> " : "  ") + names[i] + ": " + (switches[i].Value ? "ON" : "OFF") + "\n";
            text += "\n" + details[selected];
            var buttons = new List<UIDialogWindowData.ButtonData> {
                new UIDialogWindowData.ButtonData(Toggle, switches[selected].Value ? "Turn off" : "Turn on", null, false, GameKey.Select),
                new UIDialogWindowData.ButtonData(Next, "Next helper", null, false, GameKey.Select),
                new UIDialogWindowData.ButtonData(CloseSettings, "Done", null, false, GameKey.Select)
            };
            var data = new UIDialogWindowData("Keeper's Little Helpers", text, buttons);
            data.ShowCloseButton = true; data.CloseButtonAction = CloseSettings;
            window.Open(data);
        }
        private void CloseSettings() { window.Close(); var pause = returnPause; returnPause = null; if (pause != null) pause.Open(null); }
        private void Toggle() { switches[selected].Value = !switches[selected].Value; config.Save(); Show(); }
        private void Next() { selected = (selected + 1) % switches.Length; Show(); }
    }
}
