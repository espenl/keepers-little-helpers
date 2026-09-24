using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using TMPro;
using LazyBearTechnology;

namespace KeepersJournal
{
    [BepInPlugin("local.espen.keepersjournal", "Keeper's Little Helpers", "0.3.4")]
    public sealed class JournalPlugin : BaseUnityPlugin
    {
        internal static JournalPlugin Instance;
        private Harmony harmony;
        private ConfigEntry<bool> morningEnabled, quietMorning, buildingsEnabled;
        private ConfigEntry<KeyCode> repeatKey;
        private ConfigEntry<float> bubbleSeconds;
        private readonly MorningTracker tracker = new MorningTracker();
        private GameSave currentSave;
        private bool ready, manualRequest, failureLogged, buildLogged;
        private float nextCheck, safeAfter;
        private readonly Queue<string> pendingLines = new Queue<string>();
        private int pendingDay = -1;
        private bool bubbleShowing;
        private static readonly FieldInfo WidgetData = AccessTools.Field(typeof(UIBuildingWidget), "data");

        private void Awake()
        {
            Instance = this;
            buildingsEnabled = Config.Bind("Buildings", "Enabled", true, "Show existing building counts in the native build menu.");
            morningEnabled = Config.Bind("Morning", "Enabled", true, "A native player speech bubble each morning, after dawn and when you are free to act.");
            quietMorning = Config.Bind("Morning", "MentionQuietMornings", true, "On mornings with no known activities, say there is nothing special on the calendar.");
            repeatKey = Config.Bind("Controls", "RepeatReminderKey", KeyCode.F8, "Repeat today's known reminders using the native speech bubble.");
            bubbleSeconds = Config.Bind("Morning", "BubbleSeconds", 7f,
                new ConfigDescription("How long each reminder stays visible.", new AcceptableValueRange<float>(4f, 20f)));
            harmony = new Harmony("local.espen.keepersjournal");
            harmony.Patch(AccessTools.Method(typeof(UIBuildingWidget), "Redraw"),
                postfix: new HarmonyMethod(typeof(JournalPlugin), "AddBuildingCount"));
            gameObject.AddComponent<MoveHelper>().Initialize(Config, harmony);
            gameObject.AddComponent<MaterialsPin>().Initialize(Config, harmony);
            gameObject.AddComponent<HelpersSettings>().Initialize(Config, harmony);
            MainGame.OnGameStarted += OnStarted;
            MainGame.OnGoToMainMenu += OnLeft;
            Logger.LogInfo("Keeper's Little Helpers 0.3.4 loaded: native blueprint counts + morning speech bubbles. F8 repeats today's reminders.");
        }

        private void OnStarted()
        {
            OnLeft();
            currentSave = MainGame.Instance.GameSave;
            tracker.Load(currentSave.environmentData.Day, currentSave.environmentData.TimeOfDay);
            ready = true;
            safeAfter = Time.unscaledTime + 3f;
            Logger.LogInfo("Save ready; morning reminders scheduled. No reminder during loading.");
        }
        private void OnLeft()
        {
            ready = false; currentSave = null; pendingLines.Clear(); pendingDay = -1;
            bubbleShowing = false; manualRequest = false; buildLogged = false; tracker.Reset();
        }
        private bool CanSpeak()
        {
            if (!ready || MainGame.Instance == null || MainGame.Instance.gameState != MainGame.GameState.InGame
                || !ReferenceEquals(currentSave, MainGame.Instance.GameSave) || MainGame.IsGamePaused
                || MainGame.PlayerController == null || !MainGame.PlayerController.IsControlsEnabled) return false;
            if (Time.unscaledTime < safeAfter || bubbleShowing) return false;
            // Never replace quest dialogue or another character's speech.
            return UnityEngine.Object.FindObjectsByType<UIDialogBubble>(FindObjectsSortMode.None).Length == 0;
        }
        private void Update()
        {
            if (!ready) return;
            if (MainGame.Instance == null || MainGame.Instance.gameState != MainGame.GameState.InGame
                || !ReferenceEquals(currentSave, MainGame.Instance.GameSave)) { OnLeft(); return; }
            if (morningEnabled.Value && Input.GetKeyDown(repeatKey.Value)) manualRequest = true;
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;
            try
            {
                if (!morningEnabled.Value) { pendingLines.Clear(); manualRequest = false; return; }
                var env = currentSave.environmentData;
                if (pendingDay != env.Day) pendingLines.Clear();
                bool due = morningEnabled.Value && tracker.IsDue(env.Day, env.TimeOfDay);
                if (!CanSpeak()) return;
                if (pendingLines.Count == 0 && (due || manualRequest))
                {
                    var days = new Dictionary<string, int>();
                    foreach (var id in LazyConsts.ConstDefs.AllDays) days[id] = ConstDef.Get(id).IntValue;
                    var lines = JournalRules.MorningLines(currentSave.knowledgeSystem.unlockedCustomHudDaySprites,
                        env.CurrentDayNumber, days, MainGame.PlayerData.GetRes("sermon_ready") >= 1f,
                        MainGame.PlayerData.interactedWithChalkBoardOnce, MainGame.PlayerData.GetResInt("chalk_board_enabled") > 0,
                        MainGame.PlayerData.GetResInt("battle_ready") > 0, MainGame.PlayerData.GetResInt("resurrection_has_power") > 0);
                    if (lines.Count == 0 && (quietMorning.Value || manualRequest))
                        lines.Add("Nothing special on my calendar today. Back to work!");
                    foreach (var line in lines) pendingLines.Enqueue(line);
                    pendingDay = env.Day;
                    // A manual repeat before dawn must not consume the coming morning.
                    if (due) tracker.MarkShown(env.Day);
                    manualRequest = false;
                }
                if (pendingLines.Count > 0)
                {
                    string line = pendingLines.Dequeue();
                    bubbleShowing = true;
                    Bubble.Talk(new PhraseData(true, null, line, delegate {
                        bubbleShowing = false; safeAfter = Time.unscaledTime + 1.5f;
                    }, null, SpeechBubbleType.Think, UIBasicBubble.ForceCornerPosition.Auto, bubbleSeconds.Value));
                    // Also recover if a scene change destroys the bubble without invoking its callback.
                    safeAfter = Time.unscaledTime + bubbleSeconds.Value + 2f;
                    Logger.LogInfo("Morning bubble shown for day " + env.Day + ".");
                }
                failureLogged = false;
            }
            catch (Exception e)
            {
                bubbleShowing = false; pendingLines.Clear(); manualRequest = false;
                if (!failureLogged) Logger.LogWarning("Reminder postponed: " + e);
                failureLogged = true; safeAfter = Time.unscaledTime + 15f;
            }
        }
        private void LateUpdate()
        {
            if (bubbleShowing && Time.unscaledTime > safeAfter
                && UnityEngine.Object.FindObjectsByType<UIDialogBubble>(FindObjectsSortMode.None).Length == 0) bubbleShowing = false;
        }

        private static void AddBuildingCount(UIBuildingWidget __instance, TextMeshProUGUI ___nameLabel, TextMeshProUGUI ___descriptionLabel)
        {
            try
            {
                BlueprintRowNotes.Get(__instance, ___nameLabel).SetCount("");
                if (Instance == null || !Instance.buildingsEnabled.Value) return;
                if (WidgetData == null || ___nameLabel == null || MainGame.Instance == null) return;
                var data = WidgetData.GetValue(__instance) as UIBuildingWidgetData;
                if (data == null || data.BuildData == null || data.BuildData.Definition == null || data.WorldZoneData == null) return;
                string caption = DescribeBuilding(data.BuildData, data.WorldZoneData);
                if (string.IsNullOrEmpty(caption)) return;
                BlueprintRowNotes.Get(__instance, ___nameLabel).SetCount(caption);
                if (Instance != null && !Instance.buildLogged)
                {
                    Instance.Logger.LogInfo("Native build-menu counts applied successfully.");
                    Instance.buildLogged = true;
                }
            }
            catch (Exception e)
            {
                if (Instance != null && !Instance.failureLogged)
                {
                    Instance.Logger.LogWarning("Build count unavailable; original blueprint left intact: " + e);
                    Instance.failureLogged = true;
                }
            }
        }
        public static string DescribeBuilding(BuildData build, WorldZoneData zone)
        {
            if (build == null || build.Definition == null || zone == null || MainGame.Instance == null) return null;
            var mode = build.BuildingMode;
            if (mode == BuildingDef.BuildingMode.Remove || mode == BuildingDef.BuildingMode.None) return null;
            var scene = MainGame.Instance.GameSave.worldData.GetGameSceneDataById(zone.gameSceneId);
            if (scene == null) return null;
            var members = new HashSet<string>();
            foreach (var guid in zone.wgoDataList) members.Add(guid.Id);
            var facts = new List<BuildingFact>();
            int standardBeds = 0, upgradedBeds = 0;
            // Scene membership is authoritative. ChangeWgoData removes and re-adds the same
            // object during construction, leaving isRemovingFromData=true in valid saves.
            foreach (var wgo in scene.wgoDataList)
            {
                if (!members.Contains(wgo.UniqueId.Id)) continue;
                facts.Add(new BuildingFact { Id = wgo.id, Zone = zone.id,
                    Group = wgo.Definition == null ? null : wgo.Definition.wgoGroup,
                    Hidden = wgo.IsHidden, Temporary = wgo.isTempObject, Present = true });
                if (!wgo.IsHidden && !wgo.isTempObject && wgo.id == "bed")
                {
                    if (wgo.GetGameResInt("bed_upgrade") > 0) upgradedBeds++; else standardBeds++;
                }
            }
            string blueprint = build.Definition.id;
            if (blueprint == "bed_upgrade_s")
                return upgradedBeds > 0 ? "Current: Upgraded" : standardBeds > 0 ? "Current: Standard" : null;
            if (blueprint == "home_upgrade_s")
                return null; // Script changes the house; it does not place a countable building.
            string placedId = build.WgoId;
            if (string.IsNullOrEmpty(placedId)) return null;
            string mapped = JournalRules.KnownScriptResult(blueprint);
            if (mode == BuildingDef.BuildingMode.Script && mapped == null) return null;
            string resultId = mapped ?? placedId;
            var def = GameBalance.Me.GetDataOrNull<WGODef>(placedId);
            if (placedId.EndsWith("_place", StringComparison.Ordinal) && def != null && def.replaceToWgoOnDie.HasExpression)
            {
                string replacement = def.replaceToWgoOnDie.Evaluate();
                if (!string.IsNullOrEmpty(replacement) && replacement != "0") resultId = replacement;
            }
            var count = JournalRules.CountBuildings(facts, zone.id, resultId, placedId);
            if (blueprint == "kitchen_oven_up_s" || blueprint == "kitchen_table_up_s")
            {
                string baseId = blueprint == "kitchen_oven_up_s" ? "kitchen_oven" : "kitchen_table";
                var lower = JournalRules.CountBuildings(facts, zone.id, baseId, baseId);
                return count.Built > 0 ? "Current: Tier II" : lower.Built > 0 ? "Current: Tier I" : null;
            }
            return count.Caption;
        }
        private void OnDestroy()
        {
            MainGame.OnGameStarted -= OnStarted;
            MainGame.OnGoToMainMenu -= OnLeft;
            if (harmony != null) harmony.UnpatchSelf();
            Instance = null;
        }
    }
}
