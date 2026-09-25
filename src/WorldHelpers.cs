using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.UI;
using System.Runtime.CompilerServices;

namespace KeepersJournal
{
    public sealed class WorldHelpers : MonoBehaviour
    {
        internal static WorldHelpers Instance;
        private static readonly ConditionalWeakTable<UIWorkbenchAdditionWorldIconWidgetData, object> helperIcons = new ConditionalWeakTable<UIWorkbenchAdditionWorldIconWidgetData, object>();
        private ConfigFile config;
        private ConfigEntry<bool> statusEnabled, smartEnabled;
        private ConfigEntry<string> completionState;
        private readonly HashSet<string> done = new HashSet<string>();
        private readonly HashSet<string> acknowledgedStations = new HashSet<string>();
        private readonly HashSet<string> completedStations = new HashSet<string>();
        private readonly HashSet<Wgo> displayed = new HashSet<Wgo>();
        private int day = -1, selected;
        private GameSave save;
        private float nextRefresh;

        public void Initialize(ConfigFile file, Harmony harmony)
        {
            Instance = this; config = file;
            statusEnabled = file.Bind("Workstations", "StatusIcons", true, "Native interaction labels for finished work, missing fuel and blocked output when approaching a workstation.");
            smartEnabled = file.Bind("Morning", "SmartReminders", true, "Skip today's completed or checked activities. Use Today's tasks in the helper settings to mark a task manually.");
            harmony.Patch(AccessTools.Method(typeof(Wgo), "GetWidgetData"), postfix: new HarmonyMethod(typeof(WorldHelpers), "WorldLabels"));
            harmony.Patch(AccessTools.Method(typeof(UICraftWindow), "Redraw"), postfix: new HarmonyMethod(typeof(WorldHelpers), "WorkbenchOpened"));
            Patch(harmony, typeof(UIWorkbenchAdditionWorldIconWidget), "Redraw", "SizeIcon");
            Patch(harmony, typeof(CraftComponent), "Finish", "CraftFinished");
            Patch(harmony, typeof(UIPrayReportWindow), "Redraw", "SermonDone");
            Patch(harmony, typeof(UIVendorOrdersWindow), "Redraw", "BoardChecked");
            Patch(harmony, typeof(UIPrefightWindow), "Redraw", "BattleChecked");
            Patch(harmony, typeof(UIResurrectionWindow), "Redraw", "StormChecked");
            Patch(harmony, typeof(PanicReductionMachineInteractionHandler), "Interact", "PanicChecked");
            MainGame.OnGameStarted += Started; MainGame.OnGoToMainMenu += Clear;
        }
        private static void Patch(Harmony harmony, Type type, string method, string handler)
        { harmony.Patch(AccessTools.Method(type, method), postfix: new HarmonyMethod(typeof(WorldHelpers), handler)); }
        private void Started()
        {
            Clear(); save = MainGame.Instance.GameSave; day = save.environmentData.Day;
            completionState = config.Bind("DailyTasks", MainGame.PlayerData.inventory.Data.UniqueId.Id, "", "Checked tasks for this character and day.");
            var parts = completionState.Value.Split('|');
            int savedDay;
            if (parts.Length > 0 && int.TryParse(parts[0], out savedDay) && savedDay == day)
                for (int i = 1; i < parts.Length; i++) done.Add(parts[i]);
        }
        private void Clear()
        {
            save = null; day = -1; done.Clear(); acknowledgedStations.Clear(); completedStations.Clear(); displayed.Clear(); completionState = null;
        }
        private void CheckDay()
        {
            if (save == null || save.environmentData.Day == day) return;
            day = save.environmentData.Day; done.Clear(); Persist();
        }
        private void Persist()
        {
            if (completionState == null) return;
            var values = new List<string>(done); values.Sort(StringComparer.Ordinal);
            completionState.Value = day + "|" + string.Join("|", values.ToArray()); config.Save();
        }
        private static void Mark(string key)
        {
            if (Instance == null || Instance.save == null) return;
            Instance.CheckDay(); if (Instance.done.Add(key)) Instance.Persist();
        }
        private static void SermonDone() { Mark("sermon"); }
        private static void BoardChecked() { if (MainGame.PlayerData != null && MainGame.PlayerData.GetResInt("chalk_board_enabled") > 0) Mark("board"); }
        private static void BattleChecked() { Mark("battle"); }
        private static void StormChecked() { Mark("storm"); }
        private static void PanicChecked(bool __result) { if (__result) Mark("panic"); }
        internal static bool Suppressed(string line)
        {
            if (Instance == null || !Instance.smartEnabled.Value || Instance.save == null) return false;
            Instance.CheckDay();
            return Instance.done.Contains(line);
        }
        internal static void ShowToday()
        {
            if (Instance == null || Instance.save == null) return;
            Instance.CheckDay();
            var lines = JournalPlugin.CurrentMorningLines(false);
            if (lines.Count == 0) { NativeHelpers.Dialog(L.T("Today's tasks"), L.T("No known activities are available today.")); return; }
            Instance.selected %= lines.Count;
            string key = lines[Instance.selected], line = ReminderText.Get(key);
            bool checkedOff = Instance.done.Contains(key);
            NativeHelpers.Dialog(L.T("Today's tasks"), (checkedOff ? L.T("Checked off\n\n") : L.T("Still to do\n\n")) + line + "\n\n" + (Instance.selected + 1) + " / " + lines.Count,
                NativeHelpers.Action(checkedOff ? L.T("Mark to do") : L.T("Done today"), delegate {
                    if (checkedOff) Instance.done.Remove(key); else Instance.done.Add(key);
                    Instance.Persist(); ShowToday();
                }),
                NativeHelpers.Action(L.T("Next"), delegate { Instance.selected++; ShowToday(); }));
        }
        private static void WorkbenchOpened(UIBaseCraftWindowData ___data)
        {
            var craft = ___data;
            if (Instance != null && craft != null && craft.AssignedWgo != null)
                Instance.acknowledgedStations.Add(craft.AssignedWgo.Data.UniqueId.Id);
        }
        private static void CraftFinished(CraftComponent __instance)
        {
            var data = __instance.CraftableObject as WgoData;
            if (Instance != null && data != null)
            {
                Instance.acknowledgedStations.Remove(data.UniqueId.Id);
                Instance.completedStations.Add(data.UniqueId.Id);
            }
        }
        private static void AddLabel(List<LazyWidgetDataBase> widgets, string icon, string text)
        {
            if (!string.IsNullOrEmpty(icon))
            {
                var data = new UIWorkbenchAdditionWorldIconWidgetData(icon, true);
                helperIcons.Add(data, new object()); widgets.Add(data);
            }
            if (string.IsNullOrEmpty(text)) return;
            widgets.Add(new UIInteractionHintWidgetData(new UIInteractionHintRowWidgetData(new InteractionInfo(text))));
        }
        private static void SizeIcon(UIWorkbenchAdditionWorldIconWidget __instance, UIWorkbenchAdditionWorldIconWidgetData ___data)
        {
            object marker;
            var sizing = __instance.GetComponent<HelperIconSize>();
            bool ours = ___data != null && helperIcons.TryGetValue(___data, out marker);
            if (sizing == null && ours) sizing = __instance.gameObject.AddComponent<HelperIconSize>();
            if (sizing != null) sizing.Apply(ours);
        }
        private string Status(WgoData data, out string icon)
        {
            icon = "craft_status_not_enough_items";
            if (!statusEnabled.Value || data.id.EndsWith("_place", StringComparison.Ordinal) || data.Definition.interactionType == WGODef.InteractionType.Chest) return null;
            if (data.Definition.interactionType == WGODef.InteractionType.Autopsy) return null;
            var craft = data.CraftComponent;
            if (craft == null || craft.IsDestroyingCraftActive) return null;
            var current = craft.CurrentCraftElement;
            if (current != null && (current.Def.isHidden || current.Def.isAutopsyCraft)) return null;
            if (current != null && current.CraftStatus == CraftStatus.NotEnoughFuel) return L.T("Needs fuel");
            if (current != null && (current.CraftStatus == CraftStatus.NotEnoughSpaceInWgo || current.CraftStatus == CraftStatus.NotEnoughSpaceInMultiInventory))
            { icon = "craft_status_not_enough_space"; return L.T("Output blocked"); }
            if (craft.Status == CraftComponentStatus.ReadyToFinishAutoCraft || craft.Status == CraftComponentStatus.WaitingForWorkerPickUp)
            { icon = "comm-header_2-type_icon-simple_chest"; return L.T("Ready"); }
            if (craft.Status != CraftComponentStatus.Finished)
            {
                acknowledgedStations.Remove(data.UniqueId.Id);
                completedStations.Remove(data.UniqueId.Id);
            }
            // Finished is also the game's default idle state. Only announce a
            // completion actually observed this session, never an idle corpse/table.
            if (craft.Status == CraftComponentStatus.Finished && completedStations.Contains(data.UniqueId.Id) && !acknowledgedStations.Contains(data.UniqueId.Id))
            { icon = "comm-header_2-type_icon-simple_chest"; return L.T("Work finished"); }
            return null;
        }
        private static void WorldLabels(Wgo __instance, List<LazyWidgetDataBase> __result)
        {
            if (Instance == null || Instance.save == null || MainGame.PlayerData == null || !NativeHelpers.FreeToAct()) return;
            var data = __instance.Data;
            if (data == null || data.IsHidden || data.isTempObject || data.WorldZoneData != MainGame.PlayerData.CurrentWorldZoneData) return;
            var storage = StorageHelpers.Instance;
            if (storage != null && storage.Found.Contains(data.UniqueId.Id))
                AddLabel(__result, "comm-header_2-type_icon-simple_chest", L.T("Found here"));
            else if (storage != null && data.Definition.interactionType == WGODef.InteractionType.Chest
                && MainGame.PlayerController.PlayerInteractionComponent.WgoUnderInteraction == __instance)
            {
                string name = storage.ChestName(data);
                if (name.Length > 0) AddLabel(__result, null, name);
            }
            string icon; string status = Instance.Status(data, out icon);
            if (status != null && MainGame.PlayerController.PlayerInteractionComponent.WgoUnderInteraction == __instance)
                AddLabel(__result, null, status);
        }
        internal static void RefreshMarkers()
        {
            if (Instance != null && Instance.save != null) { Instance.nextRefresh = 0; Instance.Update(); }
        }
        private void Update()
        {
            if (save == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .75f;
            if (MainGame.Instance == null || !ReferenceEquals(save, MainGame.Instance.GameSave)) { Clear(); return; }
            CheckDay();
            var current = new HashSet<Wgo>();
            var zone = MainGame.PlayerData.CurrentWorldZoneData;
            if (zone != null && NativeHelpers.FreeToAct())
                foreach (var id in new List<SGuid>(zone.wgoDataList))
                {
                    var view = GameScene.GetWgoViewGlobal(id);
                    if (view == null || view.Data == null || view.Data.IsHidden || !view.gameObject.activeInHierarchy) continue;
                    string icon;
                    bool found = StorageHelpers.Instance != null && StorageHelpers.Instance.Found.Contains(id.Id);
                    bool targeted = MainGame.PlayerController.PlayerInteractionComponent.WgoUnderInteraction == view;
                    bool named = targeted && StorageHelpers.Instance != null && StorageHelpers.Instance.Names.Value && view.Data.Definition.interactionType == WGODef.InteractionType.Chest;
                    if (found || named || (targeted && Status(view.Data, out icon) != null)) { current.Add(view); WgoBubbleDisplayHandler.Display(view); }
                }
            // Refresh through the native manager; never remove other helpers' or quest widgets.
            foreach (var view in displayed) if (view != null && !current.Contains(view)) WgoBubbleDisplayHandler.Display(view);
            displayed.Clear(); foreach (var view in current) displayed.Add(view);
        }
        private void OnDestroy() { MainGame.OnGameStarted -= Started; MainGame.OnGoToMainMenu -= Clear; Clear(); Instance = null; }
    }
    public sealed class HelperIconSize : MonoBehaviour
    {
        private Vector3 originalScale;
        private LayoutElement layout;
        private float minWidth, minHeight, preferredWidth, preferredHeight;
        private bool added;
        private void Awake()
        {
            originalScale = transform.localScale;
            layout = GetComponent<LayoutElement>(); added = layout == null;
            if (added) layout = gameObject.AddComponent<LayoutElement>();
            minWidth = layout.minWidth; minHeight = layout.minHeight;
            preferredWidth = layout.preferredWidth; preferredHeight = layout.preferredHeight;
        }
        public void Apply(bool small)
        {
            transform.localScale = small ? originalScale * .45f : originalScale;
            layout.enabled = small || !added;
            layout.minWidth = small ? 24 : minWidth; layout.minHeight = small ? 24 : minHeight;
            layout.preferredWidth = small ? 24 : preferredWidth; layout.preferredHeight = small ? 24 : preferredHeight;
        }
    }

}
