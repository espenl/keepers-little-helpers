using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;

namespace KeepersJournal
{
    // Local preview: reuse native selection/placement, commit only on a valid click.
    public sealed class MoveHelper : MonoBehaviour
    {
        private static MoveHelper instance;
        private ConfigEntry<bool> enabledSetting;
        private static readonly BuildData MoveEntry = BuildData.GetDataForRemove();
        private static readonly FieldInfo WidgetData = AccessTools.Field(typeof(UIBuildingWidget), "data");
        private static readonly FieldInfo Selection = AccessTools.Field(typeof(RemovePointer), "currentRemovingSelection");
        private static readonly FieldInfo Pointer = AccessTools.Field(typeof(BuildController), "buildPointer");
        private static readonly FieldInfo Active = AccessTools.Field(typeof(BuildPointerObject), "shownAsActive");
        private Wgo selected;
        private WorldZone zone;
        private BuildData placement;
        private GameSave save;
        private bool switching;
        private string hint;
        private TextMeshProUGUI statusLabel;

        public void Initialize(ConfigFile config, Harmony harmony)
        {
            instance = this;
            enabledSetting = config.Bind("Moving", "Enabled", true, "Local preview: Move entry for idle, freestanding structures in the same area. Linked equipment and scripted structures are excluded.");
            harmony.Patch(AccessTools.GetDeclaredConstructors(typeof(UIBuildingWindowData))[0], postfix: new HarmonyMethod(typeof(MoveHelper), "AddEntry"));
            harmony.Patch(AccessTools.Method(typeof(UIBuildingWidget), "Redraw"), postfix: new HarmonyMethod(typeof(MoveHelper), "LabelEntry"));
            harmony.Patch(AccessTools.Method(typeof(RemovePointer), "TryDoBuildAction"), prefix: new HarmonyMethod(typeof(MoveHelper), "SelectForMove"));
            harmony.Patch(AccessTools.Method(typeof(BuildPointer), "TryBuildActionInput"), prefix: new HarmonyMethod(typeof(MoveHelper), "CommitMove"));
            harmony.Patch(AccessTools.Method(typeof(WgoBuildPointer), "HasRotation"), prefix: new HarmonyMethod(typeof(MoveHelper), "KeepRotation"));
            harmony.Patch(AccessTools.Method(typeof(BuildPointer), "PrepareAndSpawnWgoBuildPointer"), postfix: new HarmonyMethod(typeof(MoveHelper), "CopyAppearance"));
            harmony.Patch(AccessTools.Method(typeof(BuildController), "DisableBuildMode"), postfix: new HarmonyMethod(typeof(MoveHelper), "OnCancel"));
            MainGame.OnGoToMainMenu += Clear;
        }
        private static void AddEntry(UIBuildingWindowData __instance)
        {
            if (instance == null || !instance.enabledSetting.Value || __instance.AssignedWgo == null) return;
            if (__instance.AssignedWgo.Data.Definition.interactionType == WGODef.InteractionType.FightBuilder) return;
            foreach (var tab in __instance.TabSortedBuilds.Values) tab.Add(MoveEntry);
        }
        private static void LabelEntry(UIBuildingWidget __instance, TextMeshProUGUI ___nameLabel)
        {
            var data = WidgetData.GetValue(__instance) as UIBuildingWidgetData;
            if (data == null || !ReferenceEquals(data.BuildData, MoveEntry)) return;
            ___nameLabel.text = "Move\n<size=70%>Relocate an idle structure</size>";
            instance.RememberFont(___nameLabel);
        }
        private void RememberFont(TextMeshProUGUI original)
        {
            if (statusLabel != null) return;
            var go = new GameObject("LittleHelpersMoveHint", typeof(RectTransform));
            go.transform.SetParent(original.canvas.transform, false);
            statusLabel = go.AddComponent<TextMeshProUGUI>();
            statusLabel.font = original.font; statusLabel.fontSharedMaterial = original.fontSharedMaterial;
            statusLabel.fontSize = 21; statusLabel.color = original.color; statusLabel.raycastTarget = false;
            statusLabel.alignment = TextAlignmentOptions.Center;
            var rect = statusLabel.rectTransform;
            rect.anchorMin = new Vector2(0.15f, 0); rect.anchorMax = new Vector2(0.85f, 0);
            rect.pivot = new Vector2(0.5f, 0); rect.anchoredPosition = new Vector2(0, 72); rect.sizeDelta = new Vector2(0, 65);
            go.SetActive(false);
        }
        private static bool SelectForMove(RemovePointer __instance, ref bool __result)
        {
            if (!ReferenceEquals(__instance.BuildData, MoveEntry)) return true;
            __result = false; // Never call demolition in move mode, even for rejected selections.
            var target = Selection.GetValue(null) as Wgo;
            BuildingDef definition;
            string reason = "Select a structure.";
            if (instance.switching || !TryGetMovable(target, out definition, out reason))
            {
                if (!instance.switching) { instance.hint = reason; Debug.Log("Little Helpers move rejected: " + (target == null ? "no selection" : target.Data.id) + " | " + reason); }
                return false;
            }
            Debug.Log("Little Helpers move selected: " + target.Data.id + " | blueprint=" + definition.id);
            instance.StartCoroutine(instance.BeginPlacement(target, definition));
            return false;
        }
        private static bool TryGetMovable(Wgo target, out BuildingDef definition, out string reason)
        {
            definition = null; reason = "Select a freestanding, idle structure. Right-click to cancel.";
            if (target == null || target.Data == null || !target.IsBuildRemovable()) return false;
            var d = target.Data;
            var current = BuildController.Instance.CurrentWorldZone;
            if (d.IsHidden || d.isTempObject || d.id.EndsWith("_place", StringComparison.Ordinal)) { reason = "Finish construction before moving this."; return false; }
            if (current == null || d.WorldZoneData == null || d.WorldZoneData.id != current.Data.id) { reason = "Select a structure in this building area."; return false; }
            if (d.Worker != null || d.CraftComponent.IsStarted || d.CraftComponent.HasCraftsInQueue)
            { reason = "Finish the work and unassign the worker before moving this."; return false; }
            if (HasFixedExtensions(d) || d.WorkbenchParents.Count > 0 || d.AdditionalWgoPartsData.Count > 0)
            { reason = "This structure has linked equipment and cannot be moved yet."; return false; }
            if (!string.IsNullOrEmpty(d.Definition.attachedScript) || !string.IsNullOrEmpty(d.Definition.npcLifeSimGroup)
                || !string.IsNullOrEmpty(d.occupiedPointOfInterest) || GardenBedNavigation.IsGardenPlot(d))
            { reason = "This special structure cannot be moved yet."; return false; }
            GameBalance.Me.buildableWgos.TryGetValue(d.id, out definition);
            if (definition == null)
            {
                foreach (var candidate in GameBalance.Me.buildingDefs)
                {
                    if (candidate.buildingMode != BuildingDef.BuildingMode.Place) continue;
                    var wgoDef = GameBalance.Me.GetDataOrNull<WGODef>(candidate.wgoId);
                    if (candidate.wgoId == d.id || (candidate.wgoId.EndsWith("_place", StringComparison.Ordinal)
                        && wgoDef != null && wgoDef.replaceToWgoOnDie.HasExpression
                        && wgoDef.replaceToWgoOnDie.Evaluate() == d.id)) { definition = candidate; break; }
                }
            }
            if (definition == null || definition.buildingMode != BuildingDef.BuildingMode.Place
                || (!string.IsNullOrEmpty(definition.customBuildAreaId) && definition.customBuildAreaId != "yard_place")
                || (definition.chooseCustomBuildAreaType != BuildingDef.BuildAreaChoosingType.None
                    && definition.chooseCustomBuildAreaType != BuildingDef.BuildAreaChoosingType.Soft))
            { reason = "Only freestanding structures can be moved in this preview."; return false; }
            return true;
        }
        private static bool IsToolRack(WgoData data)
        {
            return data != null && (data.id == "tool_rack" || data.id == "tool_rack_fine");
        }
        private static bool HasFixedExtensions(WgoData data)
        {
            foreach (var id in data.AttachedWorkbenchExtensions)
                if (!IsToolRack(MainGame.WorldData.GetWgoData(id))) return true;
            return false;
        }
        private static void RefreshToolRackLinks(Wgo wgo)
        {
            // Use the same collider/definition query as native workbench registration.
            // Calculate the full result before changing either side of a link.
            HashSet<Wgo> overlaps;
            SpecialPhysicsCastUtils.TryGetWgosIntersectedByBuffCollider(wgo, true, out overlaps);
            var wanted = new HashSet<SGuid>();
            foreach (var other in overlaps)
                if (other != null && IsToolRack(other.Data) && !other.Data.isTempObject && !other.Data.IsHidden
                    && other.Data.WorldZoneData == wgo.Data.WorldZoneData) wanted.Add(other.Data.UniqueId);
            foreach (var id in new List<SGuid>(wgo.Data.AttachedWorkbenchExtensions))
            {
                var other = MainGame.WorldData.GetWgoData(id);
                if (IsToolRack(other) && !wanted.Contains(id))
                {
                    wgo.Data.RemoveWorkbenchExtension(id);
                    other.RemoveWorkbenchParent(wgo.Data.UniqueId);
                }
            }
            foreach (var id in wanted)
            {
                wgo.Data.AddWorkbenchExtension(id);
                MainGame.WorldData.GetWgoData(id).AddWorkbenchParent(wgo.Data.UniqueId);
            }
        }
        private IEnumerator BeginPlacement(Wgo target, BuildingDef original)
        {
            switching = true;
            yield return null; // Leave the native click stack before replacing its pointer.
            try
            {
                selected = target; zone = BuildController.Instance.CurrentWorldZone; save = MainGame.Instance.GameSave;
                var definition = (BuildingDef)AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(original, null);
                definition.wgoId = target.Data.id; definition.customWgoPlacePreview = target.Data.id;
                definition.needItems = new List<NeedItemData>(); definition.limitMax = 0;
                definition.expressionAfterBuilding = new List<LazyExpression>();
                placement = BuildData.GetDataForBuild(definition);
                BuildController.Instance.DisableBuildMode();
                BuildController.Instance.EnableBuildMode(placement, zone, null, null);
                hint = "Choose a clear spot. Click to move; right-click to cancel.";
            }
            catch (Exception e) { Debug.LogError("Little Helpers move preview failed: " + e); Clear(); }
            finally { switching = false; }
        }
        private static bool KeepRotation(WgoBuildPointer __instance, ref bool __result)
        {
            if (instance == null || !ReferenceEquals(__instance.BuildData, instance.placement)) return true;
            __result = false; return false;
        }
        private static void CopyAppearance(BuildData buildData, WgoBuildPointer __result)
        {
            if (instance == null || !ReferenceEquals(buildData, instance.placement) || instance.selected == null) return;
            var original = instance.selected.Data.MainWgoPartData;
            __result.Target.Data.MainWgoPartData.variationId = original.variationId;
            __result.Target.Data.MainWgoPartData.rotationIndex = original.rotationIndex;
            __result.Target.UpdateWgoPartState();
        }
        private static bool CommitMove(BuildPointer __instance, ref bool __result)
        {
            var self = instance;
            if (self == null || self.placement == null || (__instance.PointerObject == null || !ReferenceEquals(((BuildPointerObject)__instance.PointerObject).BuildData, self.placement))) return true;
            __result = false; // Do not spawn a duplicate or fire a building-completed event.
            var pointer = __instance.PointerObject as WgoBuildPointer;
            if (self.switching || pointer == null || self.selected == null || !ReferenceEquals(self.save, MainGame.Instance.GameSave)) return false;
            pointer.UpdateSelectionCellsState();
            if (!(bool)Active.GetValue(pointer)) { self.hint = "That spot is blocked. Choose a clear spot."; return false; }
            BuildingDef unused; string reason;
            if (!TryGetMovable(self.selected, out unused, out reason)) { self.hint = reason; return false; }
            Vector3 destination = pointer.Target.Data.Position;
            if (!self.zone.Data.wholeZoneRect.Contains(new Vector2(destination.x, destination.z))) return false;
            var originalPosition = self.selected.Data.Position;
            try
            {
                Relocate(self.selected, self.zone.Data, destination);
                self.StartCoroutine(self.Finish());
            }
            catch (Exception e)
            {
                Debug.LogError("Little Helpers move failed; restoring original position: " + e);
                try { Relocate(self.selected, self.zone.Data, originalPosition); }
                catch (Exception rollback) { Debug.LogError("Little Helpers relocation rollback failed: " + rollback); }
                self.hint = "Move failed. Check the log before saving.";
            }
            return false;
        }
        private static void Relocate(Wgo wgo, WorldZoneData zoneData, Vector3 position)
        {
            // Keep the very same data, GUID, inventories, resources and upgrade state.
            AccessTools.Method(typeof(Wgo), "TryUnregisterInChunkManager").Invoke(wgo, null);
            wgo.Data.Position = position;
            wgo.transform.position = position;
            AccessTools.Field(typeof(Wgo), "boundsCalculated").SetValue(wgo, false);
            wgo.Data.ClearSerializedBounds();
            AccessTools.Method(typeof(Wgo), "RegisterGDPointsFromBakedData").Invoke(wgo, null);
            wgo.BindGDPointViews();
            if (wgo.Data.Definition.forceSetNavigationHoleType != (WGODef.ForceSetNavigationHoleType)0)
                AccessTools.Method(typeof(WorldZoneData), "AddCutUnitByBakedData").Invoke(zoneData, new object[] { wgo.Data });
            wgo.Data.RefreshCustomNavMeshCutUnit();
            AccessTools.Method(typeof(Wgo), "TryRegisterInChunkManager").Invoke(wgo, null);
            Physics.SyncTransforms();
            RefreshToolRackLinks(wgo);
        }
        private IEnumerator Finish()
        {
            switching = true;
            yield return null;
            BuildController.Instance.DisableBuildMode();
            LazySingleton<BuildManager>.Instance.Disable();
            switching = false; Clear();
        }
        private static void OnCancel() { if (instance != null && !instance.switching) instance.Clear(); }
        private void Update()
        {
            if (statusLabel == null) return;
            var controller = BuildController.Instance;
            var pointer = controller == null ? null : Pointer.GetValue(controller) as BuildPointer;
            bool selecting = controller != null && controller.IsBuildModeActive && pointer != null && pointer.PointerObject != null && ReferenceEquals(((BuildPointerObject)pointer.PointerObject).BuildData, MoveEntry);
            bool placing = selected != null && controller != null && controller.IsBuildModeActive;
            statusLabel.gameObject.SetActive(selecting || placing);
            statusLabel.text = hint ?? "Select a freestanding, idle structure. Right-click to cancel.";
        }
        private void Clear() { selected = null; zone = null; placement = null; save = null; hint = null; if (statusLabel != null) statusLabel.gameObject.SetActive(false); }
        private void OnDestroy() { MainGame.OnGoToMainMenu -= Clear; Clear(); if(statusLabel != null) Destroy(statusLabel.gameObject); instance = null; }
    }
}
