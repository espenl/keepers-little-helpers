using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeepersJournal
{
    // A session-only shopping list. No save resources or recipe unlocks are changed.
    public sealed class MaterialsPin : MonoBehaviour
    {
        private static MaterialsPin instance;
        private ConfigEntry<bool> enabledSetting;
        private List<NeedItemData> needs;
        private string blueprintName;
        private GameObject panel;
        private TextMeshProUGUI label;
        private float nextRefresh;
        private GameSave save;
        private static readonly System.Reflection.FieldInfo DataField = AccessTools.Field(typeof(UIBuildingWidget), "data");

        public void Initialize(ConfigFile config, Harmony harmony)
        {
            instance = this;
            enabledSetting = config.Bind("Materials", "Enabled", true, "Shift-click a blueprint to pin its materials. Click the X to clear the pin. Counts show carried items only.");
            harmony.Patch(AccessTools.Method(typeof(UIBuildingWidget), "OnPress"), prefix: new HarmonyMethod(typeof(MaterialsPin), "PinInsteadOfBuild"));
            harmony.Patch(AccessTools.Method(typeof(UIBuildingWidget), "Redraw"), postfix: new HarmonyMethod(typeof(MaterialsPin), "DrawHint"));
            harmony.Patch(AccessTools.Method(typeof(UIBuildingWidget), "OnOver"), postfix: new HarmonyMethod(typeof(MaterialsPin), "HoverHint"));
            harmony.Patch(AccessTools.Method(typeof(UIBuildingWidget), "OnOut"), postfix: new HarmonyMethod(typeof(MaterialsPin), "UnhoverHint"));
            MainGame.OnGoToMainMenu += Clear;
        }
        private static bool PinInsteadOfBuild(UIBuildingWidget __instance)
        {
            if (instance == null || !instance.enabledSetting.Value || !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) return true;
            var data = DataField.GetValue(__instance) as UIBuildingWidgetData;
            if (data == null || data.BuildData == null || data.BuildData.Definition == null) return true;
            // Intercept before CanBuild: materials can be pinned even when unaffordable.
            var chosen = data.GetCurrentNeedItems();
            if (chosen == null || chosen.Count == 0) return false;
            instance.needs = new List<NeedItemData>();
            foreach (var need in chosen) instance.needs.Add(new NeedItemData(need.Id, need.GetCount()));
            instance.blueprintName = LLBase.L(data.BuildData.Definition.id);
            instance.save = MainGame.Instance.GameSave;
            instance.EnsurePanel(__instance);
            instance.nextRefresh = 0;
            return false;
        }
        private static void DrawHint(UIBuildingWidget __instance, TextMeshProUGUI ___nameLabel)
        {
            var data = DataField.GetValue(__instance) as UIBuildingWidgetData;
            bool show = instance != null && instance.enabledSetting.Value && data != null
                && data.BuildData != null && data.BuildData.Definition != null && data.GetCurrentNeedItems().Count > 0;
            BlueprintRowNotes.Get(__instance, ___nameLabel).SetHint(show);
        }
        private static void HoverHint(UIBuildingWidget __instance)
        {
            var notes = __instance.GetComponent<BlueprintRowNotes>();
            if (notes != null) notes.SetHovered(instance != null && instance.enabledSetting.Value);
        }
        private static void UnhoverHint(UIBuildingWidget __instance)
        {
            var notes = __instance.GetComponent<BlueprintRowNotes>();
            if (notes != null) notes.SetHovered(false);
        }
        private void EnsurePanel(UIBuildingWidget source)
        {
            if (panel != null) return;
            var hud = LazyUI.Get<HUD>();
            panel = new GameObject("LittleHelpersMaterials", typeof(RectTransform), typeof(CanvasGroup));
            var rect = panel.GetComponent<RectTransform>();
            rect.SetParent(hud.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(1, 1);
            rect.anchoredPosition = new Vector2(-22, -120);
            rect.sizeDelta = new Vector2(290, 180);
            var canvasGroup = panel.GetComponent<CanvasGroup>();
            canvasGroup.interactable = true; canvasGroup.blocksRaycasts = true;
            // Copy only the native panel sprite and text style, not window behaviour.
            var window = source.GetComponentInParent<UIBuildingWindow>();
            if (window != null) foreach (var nativeImage in window.GetComponentsInChildren<Image>(true))
            {
                if (nativeImage.sprite == null || nativeImage.type != Image.Type.Sliced) continue;
                var background = panel.AddComponent<Image>();
                background.sprite = nativeImage.sprite; background.type = Image.Type.Sliced;
                background.color = nativeImage.color; background.raycastTarget = false;
                break;
            }
            var original = AccessTools.Field(typeof(UIBuildingWidget), "nameLabel").GetValue(source) as TextMeshProUGUI;
            var textObject = new GameObject("Materials", typeof(RectTransform));
            textObject.transform.SetParent(panel.transform, false);
            label = textObject.AddComponent<TextMeshProUGUI>();
            label.font = original.font; label.fontSharedMaterial = original.fontSharedMaterial;
            label.fontSize = 17; label.color = original.color; label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.TopLeft;
            var textRect = label.rectTransform;
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(16, 12); textRect.offsetMax = new Vector2(-42, -12);
            var closeObject = new GameObject("CloseMaterials", typeof(RectTransform), typeof(Image), typeof(Button));
            closeObject.transform.SetParent(panel.transform, false);
            var closeRect = closeObject.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = Vector2.one;
            closeRect.pivot = Vector2.one; closeRect.anchoredPosition = new Vector2(-6, -6);
            closeRect.sizeDelta = new Vector2(32, 32);
            var hit = closeObject.GetComponent<Image>(); hit.color = new Color(1, 1, 1, 0.01f);
            var close = closeObject.GetComponent<Button>(); close.targetGraphic = hit; close.onClick.AddListener(Clear);
            var xObject = new GameObject("X", typeof(RectTransform)); xObject.transform.SetParent(closeObject.transform, false);
            var x = xObject.AddComponent<TextMeshProUGUI>();
            x.font = original.font; x.fontSharedMaterial = original.fontSharedMaterial; x.fontSize = 23;
            x.color = original.color; x.text = "X"; x.alignment = TextAlignmentOptions.Center; x.raycastTarget = false;
            x.rectTransform.anchorMin = Vector2.zero; x.rectTransform.anchorMax = Vector2.one;
            x.rectTransform.offsetMin = x.rectTransform.offsetMax = Vector2.zero;
        }
        private void Update()
        {
            if (needs == null || panel == null) return;
            if (MainGame.Instance == null || !ReferenceEquals(save, MainGame.Instance.GameSave)) { Clear(); return; }
            panel.SetActive(enabledSetting.Value && MainGame.Instance.gameState == MainGame.GameState.InGame);
            if (Time.unscaledTime < nextRefresh || !enabledSetting.Value) return;
            nextRefresh = Time.unscaledTime + 0.5f;
            var inventory = new MultiInventory(MainGame.PlayerData);
            var text = new StringBuilder("<b>" + blueprintName + "</b>\n<size=75%>Materials carried</size>\n");
            foreach (var need in needs)
            {
                int have = inventory.GetTotalCount(need.Id), required = need.GetCount();
                text.Append(have >= required ? "<color=#B5CFA3>" : "<color=#E6C58A>");
                text.Append(LLBase.L(need.Id)).Append("  ").Append(have).Append('/').Append(required).Append("</color>\n");
            }
            label.text = text.ToString();
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(290, 90 + needs.Count * 29);
        }
        private void Clear() { needs = null; save = null; if (panel != null) Destroy(panel); panel = null; label = null; }
        private void OnDestroy() { MainGame.OnGoToMainMenu -= Clear; Clear(); instance = null; }
    }
    // A real layout child, rather than extra lines painted over the native labels.
    public sealed class BlueprintRowNotes : MonoBehaviour
    {
        private TextMeshProUGUI label;
        private VerticalLayoutGroup column;
        private LayoutElement rowLayout;
        private LayoutElement noteLayout;
        private float originalMinHeight;
        private string count;
        private bool hint, hovered;
        public static BlueprintRowNotes Get(UIBuildingWidget row, TextMeshProUGUI original)
        {
            var notes = row.GetComponent<BlueprintRowNotes>();
            if (notes != null) return notes;
            notes = row.gameObject.AddComponent<BlueprintRowNotes>();
            notes.column = original.GetComponentInParent<VerticalLayoutGroup>();
            var go = new GameObject("LittleHelpersBlueprintNotes", typeof(RectTransform));
            go.transform.SetParent(original.transform.parent, false);
            go.transform.SetAsLastSibling();
            notes.label = go.AddComponent<TextMeshProUGUI>();
            notes.label.font = original.font; notes.label.fontSharedMaterial = original.fontSharedMaterial;
            notes.label.fontSize = original.fontSize * 0.8f;
            notes.label.color = Color.white; notes.label.raycastTarget = false;
            notes.label.alignment = TextAlignmentOptions.TopLeft;
            notes.noteLayout = go.AddComponent<LayoutElement>();
            notes.noteLayout.flexibleWidth = 1;
            notes.rowLayout = row.GetComponent<LayoutElement>();
            if (notes.rowLayout == null) notes.rowLayout = row.gameObject.AddComponent<LayoutElement>();
            notes.originalMinHeight = notes.rowLayout.minHeight;
            return notes;
        }
        public void SetCount(string value) { count = value; Refresh(); }
        public void SetHint(bool value) { hint = value; hovered = false; Refresh(); }
        public void SetHovered(bool value) { hovered = value; Refresh(); }
        private void Refresh()
        {
            label.text = string.IsNullOrEmpty(count) ? "" : "<color=#B5CFA3>" + count + "</color>";
            if (hint) label.text += (label.text.Length == 0 ? "" : "\n") + (hovered ? "<color=#E6C58A>" : "<color=#E6C58A00>") + "Shift-click to pin materials</color>";
            bool visible = label.text.Length > 0;
            label.gameObject.SetActive(visible);
            rowLayout.minHeight = originalMinHeight;
            if (visible && column != null)
            {
                column.spacing = 2;
                float width = Mathf.Max(1, ((RectTransform)column.transform).rect.width);
                float height = label.GetPreferredValues(label.text, width, Mathf.Infinity).y;
                noteLayout.minHeight = noteLayout.preferredHeight = height;
                label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)column.transform);
                rowLayout.minHeight = Mathf.Max(originalMinHeight, LayoutUtility.GetPreferredHeight((RectTransform)column.transform) + 16);
            }
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
        }
    }

}
