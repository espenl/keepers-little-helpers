using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

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
        private TextMeshProUGUI subtitle;
        private readonly List<MaterialRow> rows = new List<MaterialRow>();
        private sealed class MaterialRow
        {
            public GameObject Root;
            public TextMeshProUGUI Name, Count;
            public TextStyleComponent CountStyle;
            public int Have = -1;
        }
        private TextStyle enoughStyle, missingStyle;
        private float nextRefresh;
        private Vector2 panelPosition = new Vector2(-22, -120);
        private MaterialsPanelDrag dragHandle;
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
            instance.Clear();
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
            var dialog = LazyUI.GetWindow<UIDialogWindow>();
            var layout = dialog.GetComponentInChildren<GenericWindowLayout>(true);
            var nativeFrame = layout.transform.Find("Frame");
            var nativeInfo = AccessTools.Field(typeof(UIDialogWindow), "information").GetValue(dialog) as TextMeshProUGUI;
            if (nativeFrame == null || nativeInfo == null) throw new InvalidOperationException("Native materials window template is unavailable.");

            // Clone the complete native frame, including its masked tiled background,
            // header decorations and LazyButton close control. Never open/copy the
            // modal window itself: a pinned list must not pause or capture gameplay.
            panel = new GameObject("LittleHelpersMaterials", typeof(RectTransform));
            panel.SetActive(false);
            var rect = panel.GetComponent<RectTransform>();
            rect.SetParent(LazyUI.Get<HUD>().transform, false);
            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = panelPosition;
            rect.sizeDelta = new Vector2(290, 150);
            var frame = Instantiate(nativeFrame.gameObject, panel.transform, false);
            frame.name = "NativeFrame";
            var frameRect = frame.GetComponent<RectTransform>();
            frameRect.anchorMin = Vector2.zero; frameRect.anchorMax = Vector2.one;
            frameRect.offsetMin = frameRect.offsetMax = Vector2.zero;
            frame.SetActive(true);
            var tips = frame.transform.Find("ButtonTipsStr");
            if (tips != null) tips.gameObject.SetActive(false);
            var header = frame.transform.Find("HeaderGroup");
            header.gameObject.SetActive(true);
            var title = header.Find("Header").GetComponent<TextMeshProUGUI>();
            title.text = "Materials";
            title.raycastTarget = false;
            var close = header.Find("CloseButton").GetComponent<LazyButton>();
            close.onClick = new Button.ButtonClickedEvent();
            close.onClick.AddListener(Clear);
            close.gameObject.SetActive(true);
            close.interactable = true;
            header.Find("Background").GetComponent<Image>().raycastTarget = true;
            dragHandle = header.gameObject.AddComponent<MaterialsPanelDrag>();
            dragHandle.Initialize(rect, close.transform, delegate(Vector2 position) { panelPosition = position; });

            var nativeTitle = header.Find("Header").GetComponent<TextMeshProUGUI>();
            label = CopyText(nativeTitle, panel.transform, "BlueprintName");
            label.text = blueprintName;
            label.alignment = TextAlignmentOptions.TopLeft;
            float titleHeight = Mathf.Ceil(label.GetPreferredValues(blueprintName, 250, Mathf.Infinity).y);
            Place(label.rectTransform, 20, 44, 250, titleHeight);
            subtitle = CopyText(nativeInfo, panel.transform, "CountLegend");
            subtitle.text = "Have / Need";
            subtitle.fontSize = nativeInfo.fontSize * 0.85f;
            float legendHeight = Mathf.Ceil(subtitle.GetPreferredValues(subtitle.text, 250, Mathf.Infinity).y);
            Place(subtitle.rectTransform, 20, 46 + titleHeight, 250, legendHeight);

            var nativeCell = AccessTools.Field(typeof(UIDialogWindow), "itemCell").GetValue(dialog) as UIItemCell;
            var nativeCount = AccessTools.Field(typeof(UIDialogWindow), "itemCounterText").GetValue(dialog) as TextMeshProUGUI;
            enoughStyle = (TextStyle)AccessTools.Field(typeof(UIDialogWindow), "itemCounterEnough").GetValue(dialog);
            missingStyle = (TextStyle)AccessTools.Field(typeof(UIDialogWindow), "itemCounterNotEnough").GetValue(dialog);
            foreach (var need in needs)
            {
                var row = new MaterialRow();
                row.Root = new GameObject("Material_" + need.Id, typeof(RectTransform));
                row.Root.transform.SetParent(panel.transform, false);
                // Use the native item renderer so icon materials, quality marks and
                // empty-icon fallbacks agree with the game's own inventory cells.
                var cell = Instantiate(nativeCell.gameObject, row.Root.transform, false).GetComponent<UIItemCell>();
                cell.Draw(new Item(need.Id), drawCounter: false, noSelectionFrames: true);
                cell.LazyButton.enabled = false;
                foreach (var graphic in cell.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
                var cellRect = cell.GetComponent<RectTransform>();
                Place(cellRect, 0, 0, 48, 48);
                cellRect.localScale = new Vector3(0.625f, 0.625f, 1);
                cell.gameObject.SetActive(true);
                row.Name = CopyText(nativeInfo, row.Root.transform, "ItemName");
                row.Name.text = LLBase.L(need.Id);
                row.Name.alignment = TextAlignmentOptions.MidlineLeft;
                row.Count = CopyText(nativeCount, row.Root.transform, "ItemCount");
                row.Count.alignment = TextAlignmentOptions.MidlineRight;
                row.Count.textWrappingMode = TextWrappingModes.NoWrap;
                row.CountStyle = row.Count.GetComponent<TextStyleComponent>();
                rows.Add(row);
            }
            panel.SetActive(true);
            RefreshRows();
        }
        private static TextMeshProUGUI CopyText(TextMeshProUGUI source, Transform parent, string name)
        {
            var go = Instantiate(source.gameObject, parent, false);
            go.name = name;
            var fitter = go.GetComponent<ContentSizeFitter>();
            if (fitter != null) { fitter.enabled = false; Destroy(fitter); }
            var text = go.GetComponent<TextMeshProUGUI>();
            text.raycastTarget = false;
            text.enableAutoSizing = false;
            text.characterSpacing = 0;
            text.wordSpacing = 0;
            go.SetActive(true);
            return text;
        }
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
        private void RefreshRows()
        {
            var inventory = new MultiInventory(MainGame.PlayerData);
            float countWidth = 54;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                int have = inventory.GetTotalCount(needs[i].Id), required = needs[i].GetCount();
                if (row.Have != have)
                {
                    row.CountStyle.SetTextStyle(have >= required ? enoughStyle : missingStyle);
                    row.Count.text = have + " / " + required;
                    row.Have = have;
                }
                countWidth = Mathf.Max(countWidth, Mathf.Ceil(row.Count.GetPreferredValues(row.Count.text).x));
            }
            float width = Mathf.Max(290, 40 + 38 + 110 + 8 + countWidth);
            float contentWidth = width - 40;
            float titleHeight = Mathf.Ceil(label.GetPreferredValues(blueprintName, contentWidth, Mathf.Infinity).y);
            Place(label.rectTransform, 20, 44, contentWidth, titleHeight);
            float legendHeight = Mathf.Ceil(subtitle.GetPreferredValues(subtitle.text, contentWidth, Mathf.Infinity).y);
            Place(subtitle.rectTransform, 20, 46 + titleHeight, contentWidth, legendHeight);
            float y = 50 + titleHeight + legendHeight;
            foreach (var row in rows)
            {
                float nameWidth = contentWidth - 38 - 8 - countWidth;
                float height = Mathf.Max(30, Mathf.Ceil(row.Name.GetPreferredValues(row.Name.text, nameWidth, Mathf.Infinity).y) + 4);
                Place((RectTransform)row.Root.transform, 20, y, contentWidth, height);
                Place(row.Name.rectTransform, 38, 0, nameWidth, height);
                Place(row.Count.rectTransform, contentWidth - countWidth, 0, countWidth, height);
                y += height + 2;
            }
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(width, y + 14);
            if (dragHandle != null) dragHandle.ClampToParent();
        }
        private void Update()
        {
            if (needs == null || panel == null) return;
            if (MainGame.Instance == null || !ReferenceEquals(save, MainGame.Instance.GameSave)) { Clear(); return; }
            panel.SetActive(enabledSetting.Value && MainGame.Instance.gameState == MainGame.GameState.InGame);
            if (Time.unscaledTime < nextRefresh || !enabledSetting.Value) return;
            nextRefresh = Time.unscaledTime + 0.5f;
            RefreshRows();
        }
        private void LateUpdate()
        {
            // Native TextStyleComponent applies its initial style in Start.
            // Keep the regular native font/outline, but lift the name colour to
            // the readable parchment tone used by native window headings.
            var readable = new Color32(224, 219, 204, 255);
            foreach (var row in rows)
                if (row.Name.color != (Color)readable) row.Name.color = readable;
        }
        private void Clear()
        {
            needs = null; save = null; rows.Clear();
            if (panel != null) { panel.SetActive(false); Destroy(panel); }
            panel = null; label = null; subtitle = null; dragHandle = null;
        }
        private void OnDestroy() { MainGame.OnGoToMainMenu -= Clear; Clear(); instance = null; }
    }
    // Only the header starts dragging. The native close button keeps its click
    // behaviour, and all coordinates are in canvas units (not screen pixels).
    public sealed class MaterialsPanelDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private RectTransform panel, parent;
        private Transform closeButton;
        private Action<Vector2> moved;
        private Vector2 startPointer, startPosition;
        private bool dragging;
        private Vector2 lastParentSize;
        public void Initialize(RectTransform target, Transform close, Action<Vector2> onMoved)
        {
            panel = target; parent = target.parent as RectTransform;
            closeButton = close; moved = onMoved;
        }
        public void OnBeginDrag(PointerEventData data)
        {
            dragging = false;
            if (data.button != PointerEventData.InputButton.Left || parent == null) return;
            var hit = data.pointerPressRaycast.gameObject;
            if (hit != null && (hit.transform == closeButton || hit.transform.IsChildOf(closeButton))) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, data.position, data.pressEventCamera, out startPointer)) return;
            startPosition = panel.anchoredPosition;
            dragging = true;
        }
        public void OnDrag(PointerEventData data)
        {
            Vector2 pointer;
            if (!dragging || !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, data.position, data.pressEventCamera, out pointer)) return;
            panel.anchoredPosition = startPosition + pointer - startPointer;
            ClampToParent();
        }
        public void OnEndDrag(PointerEventData data) { dragging = false; }
        private void OnDisable() { dragging = false; }
        private void LateUpdate()
        {
            if (parent != null && parent.rect.size != lastParentSize) ClampToParent();
        }
        public void ClampToParent()
        {
            if (parent == null || panel == null) return;
            lastParentSize = parent.rect.size;
            if (lastParentSize.x <= 0 || lastParentSize.y <= 0) return;
            // Panel has a top-right anchor/pivot. Keep its header and close
            // control reachable even when the viewport is smaller than the list.
            var position = panel.anchoredPosition;
            position.x = Mathf.Clamp(position.x, Mathf.Min(-8, panel.rect.width - lastParentSize.x + 8), -8);
            position.y = Mathf.Clamp(position.y, Mathf.Min(-8, Mathf.Min(panel.rect.height, lastParentSize.y - 16) - lastParentSize.y + 8), -8);
            panel.anchoredPosition = position;
            if (moved != null) moved(position);
        }
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
