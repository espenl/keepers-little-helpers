using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;

namespace KeepersJournal
{
    public sealed class StorageHelpers : MonoBehaviour
    {
        internal static StorageHelpers Instance;
        private ConfigFile config;
        internal ConfigEntry<bool> Protection, Finder, Names;
        private ConfigEntry<string> protectedIds;
        private readonly HashSet<string> kept = new HashSet<string>();
        private readonly Dictionary<string, string> chestNames = new Dictionary<string, string>();
        internal readonly HashSet<string> Found = new HashSet<string>();
        private GameSave save;
        private WorldZoneData searchZone;
        private float nextRefresh, foundUntil;
        private UIDialogWindowButton button;
        private Action pending;
        private bool buttonClears;
        private int buttonLanguage = -1;
        internal string FindFeedback = "";
        internal string FoundItem;
        private string activeQuery;
        private bool exactQuery;
        private float nextSearchRefresh;
        internal void ClearSearch()
        {
            Found.Clear(); FoundItem = null; FindFeedback = ""; searchZone = null; activeQuery = null;
            WorldHelpers.RefreshMarkers();
        }
        internal void FindItem(string id)
        {
            if (!Finder.Value || save == null) return;
            SearchItems(id, true);
        }
        private void SearchItems(string query, bool exact, bool renew = true)
        {
            Found.Clear(); activeQuery = query; exactQuery = exact;
            searchZone = MainGame.PlayerData.CurrentWorldZoneData;
            if (renew) foundUntil = Time.unscaledTime + 60;
            nextSearchRefresh = Time.unscaledTime + .5f;
            FoundItem = exact ? query : null;
            int units = 0;
            foreach (var chest in Chests())
                foreach (var item in chest.Inventory.Data.Inventory)
                    if (item != null && !item.IsEmpty && (exact ? item.id == query : LLBase.L(item.id).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0))
                    { Found.Add(chest.UniqueId.Id); units += item.Count; }
            string name = exact ? LLBase.L(query) : query;
            FindFeedback = units == 0 ? L.F("{0}: none in this area's chests.", name) : L.F("{0}: {1} in this area's chests.", name, units);
            WorldHelpers.RefreshMarkers();
        }

        public void Initialize(ConfigFile file, Harmony harmony)
        {
            Instance = this; config = file;
            Protection = file.Bind("Storage", "ProtectedItems", true, "Ctrl-click a carried item to protect its item type from the helper's Quick Stack. All stacks of that type stay carried.");
            Finder = file.Bind("Storage", "Finder", true, "Search chest contents in your current area. Markers clear after one minute or when leaving the area.");
            Names = file.Bind("Storage", "ChestNames", true, "Rename opened chests; show the name in their native interaction label.");
            harmony.Patch(AccessTools.Method(typeof(UIItemCell), "OnPress"), prefix: new HarmonyMethod(typeof(StorageHelpers), "ProtectClick"));
            harmony.Patch(AccessTools.Method(typeof(UIItemCell), "OnDown"), prefix: new HarmonyMethod(typeof(StorageHelpers), "SuppressDrag"));
            harmony.Patch(AccessTools.Method(typeof(UIItemCell), "OnOver"), postfix: new HarmonyMethod(typeof(StorageHelpers), "AddBadge"));
            foreach (var method in typeof(UIItemCell).GetMethods())
                if (method.Name == "Draw" && method.GetParameters().Length > 0 && method.GetParameters()[0].ParameterType == typeof(Item))
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(StorageHelpers), "AddBadge"));
            harmony.Patch(AccessTools.Method(typeof(UIBaseChestWindow), "Open", new[] { typeof(UIBaseChestWindowData) }), postfix: new HarmonyMethod(typeof(StorageHelpers), "ChestOpened"));
            MainGame.OnGameStarted += Started; MainGame.OnGoToMainMenu += Clear;
        }
        private void Started()
        {
            Clear(); save = MainGame.Instance.GameSave;
            string scope = MainGame.PlayerData.inventory.Data.UniqueId.Id;
            protectedIds = config.Bind("ProtectedItems", scope, "", "Protected item types for this character.");
            foreach (var id in protectedIds.Value.Split('|')) if (!string.IsNullOrEmpty(id)) kept.Add(id);
        }
        private void Clear()
        {
            save = null; kept.Clear(); chestNames.Clear(); ClearSearch(); pending = null;
            if (button != null) Destroy(button.gameObject);
            button = null; buttonClears = false;
        }
        internal static bool IsKept(Item item)
        {
            return item != null && Instance != null && Instance.Protection.Value && Instance.kept.Contains(item.id);
        }
        private static bool Owned(Item item)
        {
            return item != null && MainGame.PlayerData != null && MainGame.PlayerData.inventory.Data.Inventory.Contains(item);
        }
        private static bool ControlClick(UIItemCell cell)
        {
            return Instance != null && Instance.Protection.Value && Owned(cell.DisplayingItem)
                && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
        }
        private static bool SuppressDrag(UIItemCell __instance) { return !ControlClick(__instance); }
        private static bool ProtectClick(UIItemCell __instance)
        {
            if (!ControlClick(__instance)) return true;
            string id = __instance.DisplayingItem.id;
            if (!Instance.kept.Add(id)) Instance.kept.Remove(id);
            Instance.SaveProtection(); AddBadge(__instance);
            return false;
        }
        private void SaveProtection()
        {
            if (protectedIds == null) return;
            var ids = new List<string>(kept); ids.Sort(StringComparer.Ordinal);
            protectedIds.Value = string.Join("|", ids.ToArray()); config.Save();
        }
        private static void AddBadge(UIItemCell __instance)
        {
            if (Instance == null || !Owned(__instance.DisplayingItem)) return;
            var badge = __instance.GetComponent<ProtectedItemBadge>();
            if (badge == null) { badge = __instance.gameObject.AddComponent<ProtectedItemBadge>(); badge.Cell = __instance; }
        }
        internal List<WgoData> Chests()
        {
            var result = new List<WgoData>();
            var zone = MainGame.PlayerData == null ? null : MainGame.PlayerData.CurrentWorldZoneData;
            if (save == null || zone == null) return result;
            foreach (var id in new List<SGuid>(zone.wgoDataList))
            {
                var chest = save.worldData.GetWgoData(id);
                if (chest != null && !chest.IsHidden && !chest.isTempObject && chest.WorldZoneData == zone
                    && chest.Definition.interactionType == WGODef.InteractionType.Chest && chest.Definition.OpenInMultiInventory
                    && chest.Definition.inventorySize > 0 && !chest.id.EndsWith("_place", StringComparison.Ordinal)) result.Add(chest);
            }
            return result;
        }
        internal string ChestName(WgoData chest)
        {
            if (!Names.Value || chest == null) return "";
            string name;
            if (!chestNames.TryGetValue(chest.UniqueId.Id, out name))
            {
                name = NativeHelpers.Plain(config.Bind("ChestLabels", chest.UniqueId.Id, "", "Custom chest name.").Value);
                chestNames[chest.UniqueId.Id] = name;
            }
            return name;
        }
        private static void ChestOpened(UIBaseChestWindow __instance, LazyWidgetDataBase data)
        {
            if (Instance == null) return;
            var chestData = data as UIBaseChestWindowData;
            if (chestData == null || chestData.Chest == null) return;
            var old = __instance.transform.Find("LittleHelpersRename");
            if (old != null) { old.gameObject.SetActive(false); Destroy(old.gameObject); }
            if (!Instance.Names.Value) return;
            string name = Instance.ChestName(chestData.Chest);
            var header = (TextMeshProUGUI)AccessTools.Field(typeof(UIBaseChestWindow), "headerRight").GetValue(__instance);
            if (!string.IsNullOrEmpty(name)) header.text = name;
            var rename = NativeHelpers.Button(__instance.transform, L.T("Rename chest"), delegate {
                var chest = chestData.Chest; var expectedSave = Instance.save;
                __instance.Close();
                NativeHelpers.Input(L.T("Chest name (blank restores default)"), Instance.ChestName(chest), delegate(string value) {
                    if (!ReferenceEquals(expectedSave, Instance.save)) return;
                    string clean = NativeHelpers.Plain(value);
                    Instance.config.Bind("ChestLabels", chest.UniqueId.Id, "", "Custom chest name.").Value = clean;
                    Instance.chestNames[chest.UniqueId.Id] = clean; Instance.config.Save();
                });
            });
            rename.name = "LittleHelpersRename";
            var rect = (RectTransform)rename.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0); rect.pivot = new Vector2(.5f, 0);
            rect.anchoredPosition = new Vector2(0, 24);
        }
        private void OpenSearch()
        {
            if (Found.Count > 0) { ClearSearch(); return; }
            NativeHelpers.Input(L.T("Find an item in this area's chests"), "", delegate(string query) {
                pending = delegate {
                    query = NativeHelpers.Plain(query);
                    if (query.Length == 0) return;
                    SearchItems(query, false);
                    string line = Found.Count == 0 ? L.F("I don't have any {0} in this area's chests.", query)
                        : L.F("I marked the chests with {0}.", query);
                    if (UnityEngine.Object.FindObjectsByType<UIDialogBubble>(FindObjectsSortMode.None).Length == 0)
                        Bubble.Talk(new PhraseData(true, null, line, null, null,
                            SpeechBubbleType.Think, UIBasicBubble.ForceCornerPosition.Auto, 3f));
                };
            });
        }
        private void Update()
        {
            if (save == null) return;
            if (MainGame.Instance == null || !ReferenceEquals(save, MainGame.Instance.GameSave)) { Clear(); return; }
            if (pending != null) { var action = pending; pending = null; action(); }
            if (activeQuery != null)
            {
                if (!Finder.Value || MainGame.PlayerData.CurrentWorldZoneData != searchZone || Time.unscaledTime >= foundUntil) ClearSearch();
                else if (Time.unscaledTime >= nextSearchRefresh) SearchItems(activeQuery, exactQuery, false);
            }
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .4f;
            bool show = NativeHelpers.FreeToAct() && Finder.Value && Chests().Count > 0;
            if (show && button == null)
            {
                button = NativeHelpers.Button(LazyUI.Get<HUD>().transform, L.T("Find item"), OpenSearch);
                var rect = (RectTransform)button.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0, 0); rect.pivot = new Vector2(0, 0);
                rect.anchoredPosition = new Vector2(22, 76);
            }
            if (button != null)
            {
                bool clear = Found.Count > 0;
                if (clear != buttonClears || buttonLanguage != L.Revision)
                {
                    buttonLanguage = L.Revision;
                    buttonClears = clear;
                    button.Draw(new UIDialogWindowData.ButtonData(OpenSearch, clear ? L.T("Clear markers") : L.T("Find item"), null, false, GameKey.Select));
                }
                button.gameObject.SetActive(show);
            }
        }
        private void LateUpdate() { if (button != null && !NativeHelpers.FreeToAct()) button.gameObject.SetActive(false); }
        private void OnDestroy() { MainGame.OnGameStarted -= Started; MainGame.OnGoToMainMenu -= Clear; Clear(); Instance = null; }
    }
    public sealed class ProtectedItemBadge : MonoBehaviour
    {
        public UIItemCell Cell;
        private TextMeshProUGUI text;
        private void LateUpdate()
        {
            bool show = Cell != null && StorageHelpers.IsKept(Cell.DisplayingItem);
            if (show && text == null)
            {
                var template = (TextMeshProUGUI)AccessTools.Field(typeof(UIDialogWindow), "itemCounterText").GetValue(LazyUI.GetWindow<UIDialogWindow>());
                text = Instantiate(template.gameObject, transform, false).GetComponent<TextMeshProUGUI>();
                text.name = "LittleHelpersKeep"; text.raycastTarget = false;
                var fitter = text.GetComponent<UnityEngine.UI.ContentSizeFitter>(); if (fitter != null) { fitter.enabled = false; Destroy(fitter); }
                text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(.5f, 1);
                text.rectTransform.pivot = new Vector2(.5f, 1); text.rectTransform.anchoredPosition = Vector2.zero;
                text.rectTransform.sizeDelta = new Vector2(60, 18);
            }
            if (text != null) { text.gameObject.SetActive(show); text.text = L.T("Keep"); text.fontSize = 12; text.alignment = TextAlignmentOptions.Top; text.color = new Color32(236, 198, 101, 255); }
        }
    }
}
