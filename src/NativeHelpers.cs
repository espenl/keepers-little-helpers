using System;
using System.Collections.Generic;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeepersJournal
{
    internal static class NativeHelpers
    {
        internal static LazyButton SmallButton(Transform parent, string caption, Action action)
        {
            var dialog = LazyUI.GetWindow<UIDialogWindow>();
            var source = (UIDialogWindowButton)AccessTools.Field(typeof(UIDialogWindow), "buttonPrefab").GetValue(dialog);
            var back = source.LazyButton.targetGraphic as Image;
            if (back == null) back = source.GetComponentInChildren<Image>(true);
            var go = new GameObject("HelperButton_" + caption, typeof(RectTransform), typeof(Image), typeof(LazyButton));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = back.sprite; image.material = back.material; image.color = back.color; image.type = Image.Type.Sliced;
            var button = go.GetComponent<LazyButton>(); button.targetGraphic = image;
            button.onClick.AddListener(delegate { action(); });
            var nativeLabel = (TextMeshProUGUI)AccessTools.Field(typeof(UIDialogWindowButton), "label").GetValue(source);
            var text = UnityEngine.Object.Instantiate(nativeLabel.gameObject, go.transform, false).GetComponent<TextMeshProUGUI>();
            var fitter = text.GetComponent<ContentSizeFitter>(); if (fitter != null) { fitter.enabled = false; UnityEngine.Object.Destroy(fitter); }
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(2, 0); text.rectTransform.offsetMax = new Vector2(-2, 0);
            text.text = caption; text.raycastTarget = false; text.enableAutoSizing = false;
            text.alignment = TextAlignmentOptions.Center; text.textWrappingMode = TextWrappingModes.NoWrap;
            L.Font(text); text.enableAutoSizing = true; text.fontSizeMin = 8; text.fontSizeMax = text.fontSize;
            text.gameObject.SetActive(true);
            return button;
        }
        internal static UIDialogWindowButton Button(Transform parent, string text, Action action)
        {
            var dialog = LazyUI.GetWindow<UIDialogWindow>();
            var source = (UIDialogWindowButton)AccessTools.Field(typeof(UIDialogWindow), "buttonPrefab").GetValue(dialog);
            var result = UnityEngine.Object.Instantiate(source.gameObject, parent, false).GetComponent<UIDialogWindowButton>();
            result.Draw(new UIDialogWindowData.ButtonData(action, text, null, false, GameKey.Select));
            result.gameObject.SetActive(true);
            return result;
        }
        internal static void Dialog(string title, string text, params UIDialogWindowData.ButtonData[] buttons)
        {
            var window = LazyUI.GetWindow<UIDialogWindow>();
            var data = new UIDialogWindowData(title, text, new List<UIDialogWindowData.ButtonData>(buttons));
            data.ShowCloseButton = true;
            data.CloseButtonAction = window.Close;
            window.Open(data);
        }
        internal static UIDialogWindowData.ButtonData Action(string text, Action action)
        {
            return new UIDialogWindowData.ButtonData(action, text, null, false, GameKey.Select);
        }
        internal static void Input(string title, string initial, Action<string> submit)
        {
            var window = LazyUI.GetWindow<UIDialogWindow>();
            var inputTemplate = LazyUI.GetWindow<UIDialogInputWindow>();
            var source = (TMP_InputField)AccessTools.Field(typeof(UIDialogInputWindow), "idInputField").GetValue(inputTemplate);
            var info = (TextMeshProUGUI)AccessTools.Field(typeof(UIDialogWindow), "information").GetValue(window);
            var field = UnityEngine.Object.Instantiate(source.gameObject, info.transform.parent, false).GetComponent<TMP_InputField>();
            field.name = "LittleHelpersTextInput";
            field.transform.SetSiblingIndex(info.transform.GetSiblingIndex() + 1);
            var layout = field.GetComponent<LayoutElement>() ?? field.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = 310;
            layout.minHeight = layout.preferredHeight = 36;
            var cell = (UIItemCell)AccessTools.Field(typeof(UIDialogWindow), "itemCell").GetValue(window);
            var back = (Image)AccessTools.Field(typeof(UIItemCell), "background").GetValue(cell);
            var fieldBack = field.GetComponent<Image>();
            fieldBack.sprite = back.sprite; fieldBack.type = Image.Type.Sliced; fieldBack.color = back.color;
            field.transition = Selectable.Transition.None;
            var oldText = field.textComponent;
            var oldPlaceholder = field.placeholder;
            var text = UnityEngine.Object.Instantiate(info.gameObject, field.textViewport, false).GetComponent<TextMeshProUGUI>();
            text.name = "InputText"; L.Font(text);
            var fit = text.GetComponent<ContentSizeFitter>(); if (fit != null) { fit.enabled = false; UnityEngine.Object.Destroy(fit); }
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(4, 0); text.rectTransform.offsetMax = new Vector2(-4, 0);
            text.enableAutoSizing = false; text.richText = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.gameObject.SetActive(true);
            field.textComponent = text; field.placeholder = null;
            if (oldText != null) { oldText.gameObject.SetActive(false); UnityEngine.Object.Destroy(oldText.gameObject); }
            if (oldPlaceholder != null) { oldPlaceholder.gameObject.SetActive(false); UnityEngine.Object.Destroy(oldPlaceholder.gameObject); }
            field.characterLimit = 40; field.text = initial ?? "";
            field.gameObject.SetActive(true);
            var data = new UIDialogWindowData(L.T("Storage helpers"), title,
                NativeHelpers.Action(L.T("Apply"), delegate { string value = field.text; window.Close(); submit(value); }));
            data.ShowCloseButton = true; data.CloseButtonAction = window.Close;
            window.Open(data, delegate { if (field != null) { field.gameObject.SetActive(false); UnityEngine.Object.Destroy(field.gameObject); } });
            ((RectTransform)window.transform).RefreshContentFitter();
            field.Select(); field.ActivateInputField();
        }
        internal static string Plain(string value)
        {
            return HelperRules.PlainName(value, 40);
        }
        internal static bool FreeToAct()
        {
            return MainGame.Instance != null && MainGame.Instance.gameState == MainGame.GameState.InGame
                && MainGame.PlayerData != null && !MainGame.IsGamePaused && MainGame.PlayerController != null
                && MainGame.PlayerController.IsControlsEnabled && !LazyWindowsStackController.HasAnyModalWindowOpened
                && (BuildController.Instance == null || !BuildController.Instance.IsBuildModeActive);
        }
    }
}
