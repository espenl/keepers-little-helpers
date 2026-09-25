using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace KeepersJournal
{
    public sealed class QuickStack : MonoBehaviour
    {
        private ConfigEntry<bool> enabledSetting, automaticSetting;
        private UIDialogWindowButton button;
        private GameSave save;
        private readonly AreaVisitTrigger visit = new AreaVisitTrigger();
        private float nextCheck;
        private bool primed;
        public void Initialize(ConfigFile config)
        {
            enabledSetting = config.Bind("QuickStack", "Enabled", true,
                "Show a native Quick Stack button when matching chest storage is available. Uses main inventory only; skips bags and tool belt.");
            // A separate key avoids inheriting automatic behaviour from the
            // earlier test build's QuickStack.Enabled=true setting.
            automaticSetting = config.Bind("QuickStack", "AutomaticOnArrival", false,
                "Optional automatic deposit on area entry. Off by default; requires Quick Stack enabled. Never runs merely from loading a save.");
            MainGame.OnGameStarted += OnStarted;
            MainGame.OnGoToMainMenu += Clear;
        }
        private void OnStarted()
        {
            save = MainGame.Instance.GameSave;
            visit.Reset(MainGame.PlayerData.CurrentWorldZoneData, enabledSetting.Value && automaticSetting.Value);
            primed = false;
            nextCheck = Time.unscaledTime + 2f;
        }
        private void Clear()
        {
            save = null; visit.Reset(null, false);
            if (button != null) { button.gameObject.SetActive(false); Destroy(button.gameObject); button = null; }
        }
        private bool CanAct()
        {
            return save != null && MainGame.Instance != null && ReferenceEquals(save, MainGame.Instance.GameSave)
                && enabledSetting.Value && MainGame.PlayerData != null
                && MainGame.Instance.gameState == MainGame.GameState.InGame && !MainGame.IsGamePaused
                && MainGame.PlayerController != null && MainGame.PlayerController.IsControlsEnabled
                && !LazyWindowsStackController.HasAnyModalWindowOpened
                && (BuildController.Instance == null || !BuildController.Instance.IsBuildModeActive);
        }
        private List<Inventory> MatchingChests(WorldZoneData zone)
        {
            var result = new List<Inventory>();
            if (zone == null) return result;
            var source = MainGame.PlayerData.inventory;
            foreach (var guid in new List<SGuid>(zone.wgoDataList))
            {
                var chest = save.worldData.GetWgoData(guid);
                if (chest == null || chest.IsHidden || chest.isTempObject || chest.WorldZoneData != zone
                    || chest.Definition.interactionType != WGODef.InteractionType.Chest
                    || !chest.Definition.OpenInMultiInventory || chest.Definition.inventorySize <= 0
                    || chest.id.EndsWith("_place", StringComparison.Ordinal)) continue;
                var inventory = chest.Inventory;
                if (inventory == null || ReferenceEquals(inventory, source) || result.Contains(inventory)) continue;
                if (HasEligible(source, inventory)) result.Add(inventory);
            }
            return result;
        }
        private void ShowButton(bool show)
        {
            if (show && button == null)
            {
                var dialog = LazyUI.GetWindow<UIDialogWindow>();
                var template = (UIDialogWindowButton)AccessTools.Field(typeof(UIDialogWindow), "buttonPrefab").GetValue(dialog);
                button = Instantiate(template.gameObject, LazyUI.Get<HUD>().transform, false).GetComponent<UIDialogWindowButton>();
                button.name = "LittleHelpersQuickStack";
                button.Draw(new UIDialogWindowData.ButtonData(Deposit, L.T("Quick Stack"), CanAct, false, GameKey.Select));
                var rect = (RectTransform)button.transform;
                // Keep the bottom-right native pickup-notification stack clear.
                rect.anchorMin = rect.anchorMax = new Vector2(0, 0);
                rect.pivot = new Vector2(0, 0);
                rect.anchoredPosition = new Vector2(22, 28);
            }
            if (button != null)
            {
                if (buttonLanguage != L.Revision)
                {
                    buttonLanguage = L.Revision;
                    button.Draw(new UIDialogWindowData.ButtonData(Deposit, L.T("Quick Stack"), CanAct, false, GameKey.Select));
                }
                button.gameObject.SetActive(show);
            }
        }
        private int buttonLanguage = -1;
        private void LateUpdate()
        {
            // Hide immediately when another window takes control, rather than
            // allowing a visible stale button until the next inventory poll.
            if (button != null && !CanAct()) button.gameObject.SetActive(false);
        }
        private void Update()
        {
            if (save == null || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.25f;
            if (MainGame.Instance == null || !ReferenceEquals(save, MainGame.Instance.GameSave)) { Clear(); return; }
            var player = MainGame.PlayerData;
            if (player == null) return;
            var zone = player.CurrentWorldZoneData;
            // The player may not have an area yet during OnGameStarted.
            // Seed from the first settled area, never deposit as a load side effect.
            if (!primed)
            {
                if (zone == null) return;
                visit.Reset(zone, enabledSetting.Value && automaticSetting.Value);
                primed = true;
                return;
            }
            bool safe = CanAct();
            bool autoDue = visit.Poll(zone, enabledSetting.Value && automaticSetting.Value, safe, Time.unscaledTime);
            try
            {
                ShowButton(safe && MatchingChests(zone).Count > 0);
                if (autoDue) Deposit();
            }
            catch (Exception e) { ShowButton(false); Debug.LogError("Little Helpers quick-stack availability failed: " + e); }
        }
        private void Deposit()
        {
            // Recheck the current area and capacity on click, never use the
            // inventory list that originally made the button appear.
            if (!CanAct()) return;
            var player = MainGame.PlayerData;
            var zone = player.CurrentWorldZoneData;
            if (zone == null) return;
            visit.Reset(zone, enabledSetting.Value && automaticSetting.Value);
            try
            {
                int before = CountMainInventory(player.inventory);
                foreach (var inventory in MatchingChests(zone))
                    TransferEligible(player.inventory, inventory);
                int moved = before - CountMainInventory(player.inventory);
                ShowButton(CanAct() && MatchingChests(zone).Count > 0);
                if (moved > 0)
                {
                    Debug.Log("Little Helpers quick stack: deposited " + moved + " items in area " + zone.id);
                    if (UnityEngine.Object.FindObjectsByType<UIDialogBubble>(FindObjectsSortMode.None).Length == 0)
                        Bubble.Talk(new PhraseData(true, null, L.F("I put away {0} items in storage.", moved), null, null,
                            SpeechBubbleType.Think, UIBasicBubble.ForceCornerPosition.Auto, 3f));
                }
            }
            catch (Exception e)
            {
                // No automatic retry this visit: earlier native transfers may have succeeded.
                Debug.LogError("Little Helpers quick stack stopped: " + e);
            }
        }
        internal static bool HasEligible(Inventory source, Inventory target)
        {
            foreach (var item in source.Data.Inventory)
                if (EligibleCount(item, target) > 0) return true;
            return false;
        }
        private static int EligibleCount(Item item, Inventory target)
        {
            if (item == null || item.IsEmpty || item.IsBag || item.Definition.stackCount <= 1 || StorageHelpers.IsKept(item)) return 0;
            bool matching = target.Data.Inventory.Exists(delegate(Item other) { return other != null && !other.IsEmpty && other.id == item.id; });
            if (!matching) return 0;
            return HelperRules.TransferCount(item.Count, target.Data.CanAddItemCountToInventory(item, true, null, true), true, false, true);
        }
        internal static void TransferEligible(Inventory source, Inventory target)
        {
            foreach (var item in new List<Item>(source.Data.Inventory))
            {
                if (!source.Data.Inventory.Contains(item)) continue;
                int count = EligibleCount(item, target);
                if (count <= 0) continue;
                // Low-level native operations have no inventory event callbacks.
                // Complete both halves before notifying UI or quest listeners.
                var copy = Item.Copy(item); copy.Count = count;
                List<Item> added;
                target.Data.AddItemToInventory(copy, out added, null, true);
                int moved = count - copy.Count;
                if (moved <= 0) continue;
                var removed = source.Data.RemoveItemFromInventoryByUID(item.UniqueId.Guid, moved);
                if (removed == null || removed.IsEmpty || removed.Count != moved)
                    throw new InvalidOperationException("Quick Stack source changed during transfer.");
                source.NotifyItemsRemoved(new List<Item> { removed });
                target.NotifyItemsAdded(added);
            }
        }
        private static int CountMainInventory(Inventory inventory)
        {
            int count = 0;
            foreach (var item in inventory.Data.Inventory) if (item != null) count += item.Count;
            return count;
        }
        private void OnDestroy()
        {
            MainGame.OnGameStarted -= OnStarted;
            MainGame.OnGoToMainMenu -= Clear;
            Clear();
        }
    }
}
