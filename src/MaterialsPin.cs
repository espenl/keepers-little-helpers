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
        private ConfigEntry<bool> enabledSetting, multipleSetting;
        private readonly List<PinnedPlan> plans = new List<PinnedPlan>();
        private RectTransform content, viewport;
        private ScrollRect scroll;
        private TextMeshProUGUI feedback;
        private LazyButton clearMarkers;
        private readonly List<PlanRow> planRows = new List<PlanRow>();
        private sealed class PlanRow
        {
            public TextMeshProUGUI Name;
            public LazyButton Remove;
        }
        private List<NeedItemData> needs;
        private GameObject panel;
        private TextMeshProUGUI subtitle;
        private readonly List<MaterialRow> rows = new List<MaterialRow>();
        private sealed class MaterialRow
        {
            public GameObject Root;
            public TextMeshProUGUI Name, Count;
            public TextStyleComponent CountStyle;
            public LazyButton Find;
            public int Have = -1;
        }
        private TextStyle enoughStyle, missingStyle;
        private float nextRefresh;
        private int languageRevision;
        private float noticeUntil;
        private Vector2 panelPosition = new Vector2(-22, -120);
        private MaterialsPanelDrag dragHandle;
        private GameSave save;
        private static readonly System.Reflection.FieldInfo DataField = AccessTools.Field(typeof(UIBuildingWidget), "data");

        public void Initialize(ConfigFile config, Harmony harmony)
        {
            instance = this;
            multipleSetting = config.Bind("Materials", "MultiplePins", true, "Pin up to six different blueprints or recipes with combined material totals.");
            enabledSetting = config.Bind("Materials", "Enabled", true, "Shift-click a blueprint or recipe to pin materials. Counts show carried items only.");
            harmony.Patch(AccessTools.Method(typeof(UIBuildingWidget), "OnPress"), prefix: new HarmonyMethod(typeof(MaterialsPin), "PinInsteadOfBuild"));
            harmony.Patch(AccessTools.Method(typeof(UIBuildingWidget), "Redraw"), postfix: new HarmonyMethod(typeof(MaterialsPin), "DrawHint"));
            harmony.Patch(AccessTools.Method(typeof(UIBuildingWidget), "OnOver"), postfix: new HarmonyMethod(typeof(MaterialsPin), "HoverHint"));
            harmony.Patch(AccessTools.Method(typeof(UIBuildingWidget), "OnOut"), postfix: new HarmonyMethod(typeof(MaterialsPin), "UnhoverHint"));
            harmony.Patch(AccessTools.Method(typeof(UIBaseCraftWidgetData), "OnPressAction"), prefix: new HarmonyMethod(typeof(MaterialsPin), "PinInsteadOfCraft"));
            harmony.Patch(AccessTools.Method(typeof(UICraftWidget), "Redraw"), postfix: new HarmonyMethod(typeof(MaterialsPin), "CraftHint"));
            harmony.Patch(AccessTools.Method(typeof(UICraftPreviewItemCell), "OpenCraftSetupWindow"), prefix: new HarmonyMethod(typeof(MaterialsPin), "PinPreview"));
            harmony.Patch(AccessTools.Method(typeof(UIBaseCraftSelectionWindowData), "OnPressAction"), prefix: new HarmonyMethod(typeof(MaterialsPin), "PinSelection"));
            harmony.Patch(AccessTools.Method(typeof(UIBaseCraftSelectionWindow), "DrawBaseElements"), postfix: new HarmonyMethod(typeof(MaterialsPin), "DrawSelectionPin"));
            MainGame.OnGoToMainMenu += Clear;
        }
        private static bool PinInsteadOfCraft(UIBaseCraftWidgetData __instance)
        {
            if (instance == null || !instance.enabledSetting.Value || !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) return true;
            // Suppress the original click even if a special recipe is unsupported:
            // a request to pin must never spend materials or start a craft.
            PinRecipe(__instance.CraftDefinition, __instance.GetCurrentNeedItems(), __instance.WgoData, __instance.CraftsCount);
            return false;
        }
        private static bool PinPreview(UICraftPreviewItemCellData ___data)
        {
            if (instance == null || !instance.enabledSetting.Value || !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) return true;
            if (___data == null || ___data.IsUnknown || ___data.IsTab || ___data.IsGravePartRemove) return false;
            // Use the game's ingredient selection logic, without invoking its queue/start callbacks.
            var selection = new UIBaseCraftSelectionWindowData(___data.WgoData, ___data.CraftDef, null, null);
            PinRecipe(selection.CraftDefinition, selection.CurrentNeedItems, selection.WgoData, 1);
            return false;
        }
        private static bool PinSelection(UIBaseCraftSelectionWindowData __instance)
        {
            if (instance == null || !instance.enabledSetting.Value || !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) return true;
            PinRecipe(__instance.CraftDefinition, __instance.CurrentNeedItems, __instance.WgoData, __instance.CraftsCount);
            return false;
        }
        private static void DrawSelectionPin(UIBaseCraftSelectionWindow __instance, UIBaseCraftSelectionWindowData ___data, UICraftSelectionOutputItemCell ___outputItem)
        {
            var control = __instance.GetComponent<ProjectMaterialsPin>();
            if (control == null) control = __instance.gameObject.AddComponent<ProjectMaterialsPin>();
            control.Draw(___data, ___outputItem);
        }
        internal static bool CanPinProject(UIBaseCraftSelectionWindowData data)
        {
            if (instance == null || !instance.enabledSetting.Value || data == null || data.CraftDefinition == null) return false;
            var craft = data.CraftDefinition;
            return !craft.isHidden && !craft.isAutopsyCraft && !craft.isObjDestroyCraft && craft.needItems.Count > 0
                && (!craft.isNeedsUnlock || MainGame.Instance.GameSave.knowledgeSystem.unlockedCrafts.Contains(craft.id));
        }
        internal static void ToggleProject(UIBaseCraftSelectionWindowData data)
        {
            if (CanPinProject(data)) PinRecipe(data.CraftDefinition, data.CurrentNeedItems, data.WgoData, Math.Max(1, data.CraftsCount));
        }
        private static void PinRecipe(CraftDef definition, List<NeedItemData> chosen, WgoData wgo, int batches)
        {
            if (definition == null || definition.isHidden || definition.isAutopsyCraft || definition.isObjDestroyCraft || chosen == null) return;
            var costs = new List<NeedItemData>();
            // The extra durability-use item is equipment, not a consumed ingredient.
            int count = Math.Min(definition.needItems.Count, Math.Max(0, chosen.Count - (definition.hasDurabilityUseItem ? 1 : 0)));
            for (int i = 0; i < count; i++)
            {
                var need = chosen[i];
                if (need == null || need.IsGroup || need.ItemDef == null) return;
                // Fuel units live in the station's separate fuel inventory, not
                // the player's carried inventory. Do not show a false zero.
                if (need.ItemDef.isFuel) continue;
                costs.Add(new NeedItemData(need.Id, (int)Math.Min(int.MaxValue, (long)need.GetCount(wgo) * Math.Max(1, batches))));
            }
            if (costs.Count == 0) return;
            var output = definition.GetOutputPreview(wgo);
            string nameId = output == null || string.IsNullOrEmpty(output.itemId) ? definition.id : output.itemId;
            TogglePlan("craft:" + definition.id, nameId, costs, batches);
        }
        private static void CraftHint(UICraftWidget __instance, TextMeshProUGUI ___description)
        {
            if (instance == null || !instance.enabledSetting.Value || ___description == null) return;
            var craft = __instance.CraftDef;
            if (craft == null || craft.isHidden || craft.isAutopsyCraft || craft.isObjDestroyCraft || craft.needItems.Count == 0) return;
            ___description.text += "\n<size=80%><color=#E6C58A>" + L.T("Shift-click to pin ingredients") + "</color></size>";
        }
        private static bool PinInsteadOfBuild(UIBuildingWidget __instance)
        {
            if (instance == null || !instance.enabledSetting.Value || !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) return true;
            var data = DataField.GetValue(__instance) as UIBuildingWidgetData;
            if (data == null || data.BuildData == null || data.BuildData.Definition == null) return true;
            // Intercept before CanBuild: materials can be pinned even when unaffordable.
            var chosen = data.GetCurrentNeedItems();
            if (chosen == null || chosen.Count == 0) return false;
            string id = data.BuildData.Definition.id;
            TogglePlan("build:" + id, id, chosen, 1);
            return false;
        }
        private static void TogglePlan(string id, string nameId, List<NeedItemData> chosen, int batches)
        {
            var plan = instance.plans.Find(delegate(PinnedPlan entry) { return entry.Id == id; });
            if (plan != null)
            {
                instance.plans.Remove(plan); instance.Rebuild(); return;
            }
            if (!instance.multipleSetting.Value) instance.Clear();
            if (plan == null)
            {
                if (instance.plans.Count >= 6) { instance.noticeUntil = Time.unscaledTime + 4; instance.RefreshRows(); return; }
                plan = new PinnedPlan { Id = id, Name = nameId, Batches = batches };
                foreach (var need in chosen)
                {
                    int old; plan.Costs.TryGetValue(need.Id, out old);
                    plan.Costs[need.Id] = old + need.GetCount();
                }
                instance.plans.Add(plan);
            }
            instance.save = MainGame.Instance.GameSave;
            instance.Rebuild();
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
        private void EnsurePanel()
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
            title.text = L.T("Materials");
            L.Font(title);
            title.raycastTarget = false;
            var close = header.Find("CloseButton").GetComponent<LazyButton>();
            close.onClick = new Button.ButtonClickedEvent();
            close.onClick.AddListener(Clear);
            close.gameObject.SetActive(true);
            close.interactable = true;
            header.Find("Background").GetComponent<Image>().raycastTarget = true;
            dragHandle = header.gameObject.AddComponent<MaterialsPanelDrag>();
            dragHandle.Initialize(rect, close.transform, delegate(Vector2 position) { panelPosition = position; });

            viewport = new GameObject("ListViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect)).GetComponent<RectTransform>();
            viewport.SetParent(panel.transform, false);
            viewport.GetComponent<Image>().color = Color.clear;
            content = new GameObject("ListContent", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            scroll = viewport.GetComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 24;
            foreach (var pinned in plans)
            {
                var plan = pinned;
                var row = new PlanRow();
                row.Name = CopyText(nativeInfo, content, "PlanName"); row.Name.text = plan.Batches > 1 ? L.F("{0} ({1} crafts)", LLBase.L(plan.Name), plan.Batches) : LLBase.L(plan.Name);
                row.Name.alignment = TextAlignmentOptions.MidlineLeft;
                row.Remove = Instantiate(close.gameObject, content, false).GetComponent<LazyButton>();
                row.Remove.onClick = new Button.ButtonClickedEvent();
                row.Remove.onClick.AddListener(delegate { plans.Remove(plan); Rebuild(); });
                planRows.Add(row);
            }
            subtitle = CopyText(nativeInfo, content, "CountLegend");
            subtitle.text = L.T("Materials needed");
            subtitle.alignment = TextAlignmentOptions.TopLeft;
            feedback = CopyText(nativeInfo, panel.transform, "FindFeedback");
            feedback.alignment = TextAlignmentOptions.TopLeft;
            clearMarkers = NativeHelpers.SmallButton(panel.transform, L.T("Clear"), delegate { if (StorageHelpers.Instance != null) StorageHelpers.Instance.ClearSearch(); });

            var nativeCell = AccessTools.Field(typeof(UIDialogWindow), "itemCell").GetValue(dialog) as UIItemCell;
            var nativeCount = AccessTools.Field(typeof(UIDialogWindow), "itemCounterText").GetValue(dialog) as TextMeshProUGUI;
            enoughStyle = (TextStyle)AccessTools.Field(typeof(UIDialogWindow), "itemCounterEnough").GetValue(dialog);
            missingStyle = (TextStyle)AccessTools.Field(typeof(UIDialogWindow), "itemCounterNotEnough").GetValue(dialog);
            foreach (var need in needs)
            {
                var row = new MaterialRow();
                row.Root = new GameObject("Material_" + need.Id, typeof(RectTransform));
                row.Root.transform.SetParent(content, false);
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
                string itemId = need.Id;
                row.Find = NativeHelpers.SmallButton(row.Root.transform, L.T("Find"), delegate {
                    if (StorageHelpers.Instance != null) StorageHelpers.Instance.FindItem(itemId);
                    RefreshRows();
                });
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
            L.Font(text);
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
            var inventory = new MultiInventory(MainGame.PlayerData, false);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                int have = inventory.GetTotalCount(needs[i].Id), required = needs[i].GetCount();
                if (row.Have != have)
                {
                    row.CountStyle.SetTextStyle(have >= required ? enoughStyle : missingStyle);
                    row.Count.text = L.F("Carrying {0} / Need {1}", have, required);
                    row.Have = have;
                }
            }
            var craftingWindow = LazyUI.GetWindow<UICraftWindow>();
            bool besideCrafting = craftingWindow != null && craftingWindow.gameObject.activeInHierarchy;
            float width = besideCrafting ? 300 : 340, contentWidth = width - 40;
            float y = 0;
            foreach (var row in planRows)
            {
                float height = Mathf.Max(28, Mathf.Ceil(row.Name.GetPreferredValues(row.Name.text, contentWidth - 34, Mathf.Infinity).y) + 4);
                Place(row.Name.rectTransform, 0, y, contentWidth - 34, height);
                Place((RectTransform)row.Remove.transform, contentWidth - 27, y + (height - 24) / 2, 24, 24);
                y += height + 6;
            }
            y += 6;
            float legendHeight = Mathf.Ceil(subtitle.GetPreferredValues(subtitle.text, contentWidth, Mathf.Infinity).y);
            Place(subtitle.rectTransform, 0, y, contentWidth, legendHeight); y += legendHeight + 8;
            bool finder = StorageHelpers.Instance != null && StorageHelpers.Instance.Finder.Value;
            foreach (var row in rows)
            {
                // A separate quantity line leaves room for long material names.
                float nameWidth = contentWidth - 38 - (finder ? 52 : 0);
                float nameHeight = Mathf.Ceil(row.Name.GetPreferredValues(row.Name.text, nameWidth, Mathf.Infinity).y);
                float countHeight = Mathf.Ceil(row.Count.GetPreferredValues(row.Count.text).y);
                float height = Mathf.Max(32, nameHeight + countHeight + 2);
                Place((RectTransform)row.Root.transform, 0, y, contentWidth, height);
                Place(row.Name.rectTransform, 38, 0, nameWidth, nameHeight);
                row.Count.alignment = TextAlignmentOptions.MidlineLeft;
                Place(row.Count.rectTransform, 38, nameHeight + 2, nameWidth, countHeight);
                row.Find.gameObject.SetActive(finder);
                Place((RectTransform)row.Find.transform, contentWidth - 48, (height - 26) / 2, 48, 26);
                y += height + 7;
            }
            var storage = StorageHelpers.Instance;
            bool searching = finder && !string.IsNullOrEmpty(storage.FindFeedback);
            feedback.text = Time.unscaledTime < noticeUntil ? L.T("Six plans pinned. Remove one to add another.")
                : searching ? storage.FindFeedback : string.Empty;
            if (searching && storage.Found.Count > 0 && BuildingMenuOpen())
                feedback.text += L.T(" Close the building menu to see markers.");
            float feedbackWidth = contentWidth - (searching ? 54 : 0);
            float footerHeight = string.IsNullOrEmpty(feedback.text) ? 0 : Mathf.Max(26, Mathf.Ceil(feedback.GetPreferredValues(feedback.text, feedbackWidth, Mathf.Infinity).y));
            float parentHeight = ((RectTransform)panel.transform.parent).rect.height;
            float visibleHeight = Mathf.Min(y, Mathf.Max(100, parentHeight - 210 - footerHeight));
            if (!searching && Time.unscaledTime >= noticeUntil && y > visibleHeight)
            {
                feedback.text = L.T("Scroll for more.");
                footerHeight = Mathf.Max(26, Mathf.Ceil(feedback.GetPreferredValues(feedback.text, feedbackWidth, Mathf.Infinity).y));
                visibleHeight = Mathf.Min(y, Mathf.Max(100, parentHeight - 210 - footerHeight));
            }
            Place(viewport, 20, 44, contentWidth, visibleHeight);
            var offset = content.anchoredPosition.y;
            Place(content, 0, 0, contentWidth, y);
            content.anchoredPosition = new Vector2(0, Mathf.Clamp(offset, 0, Mathf.Max(0, y - visibleHeight)));
            Place(feedback.rectTransform, 20, 54 + visibleHeight, feedbackWidth, footerHeight);
            feedback.gameObject.SetActive(footerHeight > 0);
            clearMarkers.gameObject.SetActive(searching);
            Place((RectTransform)clearMarkers.transform, width - 68, 54 + visibleHeight, 48, 26);
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(width, (footerHeight > 0 ? 70 : 58) + visibleHeight + footerHeight);
            if (dragHandle != null) dragHandle.ClampToParent();
        }
        private void Update()
        {
            if (needs == null || panel == null) return;
            if (MainGame.Instance == null || !ReferenceEquals(save, MainGame.Instance.GameSave)) { Clear(); return; }
            panel.SetActive(enabledSetting.Value && MainGame.Instance.gameState == MainGame.GameState.InGame
                && (!LazyWindowsStackController.HasAnyModalWindowOpened || BuildingMenuOpen()));
            if (Time.unscaledTime < nextRefresh || !enabledSetting.Value) return;
            nextRefresh = Time.unscaledTime + 0.5f;
            RefreshRows();
        }
        private void LateUpdate()
        {
            if (languageRevision != L.Revision)
            {
                languageRevision = L.Revision;
                if (plans.Count > 0) Rebuild();
            }
            // Native TextStyleComponent applies its initial style in Start.
            // Keep the regular native font/outline, but lift the name colour to
            // the readable parchment tone used by native window headings.
            var readable = new Color32(224, 219, 204, 255);
            foreach (var row in rows)
            {
                row.Name.color = readable; row.Name.alignment = TextAlignmentOptions.MidlineLeft;
                row.Count.alignment = TextAlignmentOptions.MidlineLeft;
            }
            foreach (var row in planRows)
            {
                row.Name.color = readable; row.Name.alignment = TextAlignmentOptions.MidlineLeft;
            }
            if (subtitle != null) subtitle.alignment = TextAlignmentOptions.TopLeft;
            if (feedback != null) feedback.alignment = TextAlignmentOptions.TopLeft;
        }
        private static bool BuildingMenuOpen()
        {
            var building = LazyUI.GetWindow<UIBuildingWindow>();
            var crafting = LazyUI.GetWindow<UICraftWindow>();
            var selection = LazyUI.GetWindow<UICraftSelectionWindow>();
            var fuel = LazyUI.GetWindow<UIFuelCraftWindow>();
            var single = LazyUI.GetWindow<UISingleCraftWindow>();
            return (building != null && building.gameObject.activeInHierarchy) || (crafting != null && crafting.gameObject.activeInHierarchy)
                || (selection != null && selection.gameObject.activeInHierarchy) || (fuel != null && fuel.gameObject.activeInHierarchy)
                || (single != null && single.gameObject.activeInHierarchy);
        }
        private void Rebuild()
        {
            float offset = content == null ? 0 : content.anchoredPosition.y;
            DestroyPanel();
            if (plans.Count == 0) { Clear(); return; }
            var total = new List<KeyValuePair<string, int>>(HelperRules.Totals(plans));
            total.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return string.Compare(LLBase.L(a.Key), LLBase.L(b.Key), StringComparison.CurrentCulture); });
            needs = new List<NeedItemData>();
            foreach (var pair in total) needs.Add(new NeedItemData(pair.Key, pair.Value));
            EnsurePanel(); nextRefresh = 0;
            content.anchoredPosition = new Vector2(0, Mathf.Clamp(offset, 0, Mathf.Max(0, content.rect.height - viewport.rect.height)));
        }
        private void DestroyPanel()
        {
            rows.Clear(); planRows.Clear();
            if (panel != null) { panel.SetActive(false); Destroy(panel); }
            panel = null; subtitle = feedback = null; dragHandle = null; content = viewport = null; scroll = null; clearMarkers = null;
        }
        private void Clear()
        {
            needs = null; save = null; noticeUntil = 0; plans.Clear(); DestroyPanel();
        }
        private void OnDestroy() { MainGame.OnGoToMainMenu -= Clear; Clear(); instance = null; }
    }
    // Use the existing output icon without adding controls to native layouts.
    // Missing materials must not disable planning or invoke the craft callback.
    public sealed class ProjectMaterialsPin : MonoBehaviour
    {
        private UIBaseCraftSelectionWindowData data;
        private UIItemCell output;
        private Action<UIItemCell> outputClick;
        public void Draw(UIBaseCraftSelectionWindowData value, UICraftSelectionOutputItemCell preview)
        {
            data = value;
            if (output != null && outputClick != null) output.OnItemCellPress -= outputClick;
            output = preview == null ? null : preview.UIItemCell;
            if (outputClick == null) outputClick = delegate(UIItemCell cell) {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) MaterialsPin.ToggleProject(data);
            };
            if (output != null) output.OnItemCellPress += outputClick;
        }
        private void OnDestroy()
        {
            if (output != null && outputClick != null) output.OnItemCellPress -= outputClick;
        }
    }

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
            if (hint) label.text += (label.text.Length == 0 ? "" : "\n") + (hovered ? "<color=#E6C58A>" : "<color=#E6C58A00>") + L.T("Shift-click to pin materials") + "</color>";
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
