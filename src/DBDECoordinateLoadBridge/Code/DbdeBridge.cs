using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DBDECoordinateLoadBridge
{
    internal static class DbdeBridge
    {
        internal const string DbdeGuid = "org.njaecha.plugins.dbde";
#if KK
        internal const string CoordinateLoadOptionGuid = "com.jim60105.kk.coordinateloadoption";
#else
        internal const string CoordinateLoadOptionGuid = "com.jim60105.kks.coordinateloadoption";
#endif

        private const string ControllerTypeName = "DynamicBoneDistributionEditor.DBDECharaController";
#if KK
        private const string CoordinateLoadOptionPatchesTypeName = "KK_CoordinateLoadOption.Patches";
#else
        private const string CoordinateLoadOptionPatchesTypeName = "CoordinateLoadOption.Patches";
#endif
        private const int SafetyCleanupFrames = 1800;

        private static readonly FieldInfo ToggleIsOnField = AccessTools.Field(typeof(Toggle), "m_IsOn");

        private static FieldInfo _dbdeTransferField;
        private static FieldInfo _dbeTransferField;
        private static object _savedDbdeTransferData;
        private static object _savedDbeTransferData;
        private static TransferPhase _transferPhase;
        private static bool _loggedMoveNextHold;

        internal static void TryPatch(Harmony harmony)
        {
            if (harmony == null)
                return;

            if (!Chainloader.PluginInfos.TryGetValue(DbdeGuid, out var dbdeInfo) ||
                !Chainloader.PluginInfos.TryGetValue(CoordinateLoadOptionGuid, out var cloInfo))
            {
                DBDECoordinateLoadBridgePlugin.Log?.LogInfo(
                    "DBDE and Coordinate Load Option were not both found; compatibility bridge is inactive.");
                return;
            }

            try
            {
                var dbdeAssembly = dbdeInfo.Instance?.GetType().Assembly;
                var cloAssembly = cloInfo.Instance?.GetType().Assembly;
                var controllerType = dbdeAssembly?.GetType(ControllerTypeName, false);
                if (controllerType == null)
                    throw new TypeLoadException($"Could not find {ControllerTypeName}.");

                _dbdeTransferField = AccessTools.Field(controllerType, "cloTransferPluginData");
                _dbeTransferField = AccessTools.Field(controllerType, "cloTransferPluginDataDBE");
                if (_dbdeTransferField == null || _dbeTransferField == null)
                    throw new MissingFieldException(controllerType.FullName, "cloTransferPluginData / cloTransferPluginDataDBE");

                PatchCoordinateLoad(harmony, controllerType);
                PatchTransferCleanup(harmony, controllerType);
                PatchTransferCleanupStateMachine(harmony, controllerType);
                PatchCoordinateLoadOptionStart(harmony, cloAssembly);

                DBDECoordinateLoadBridgePlugin.Log?.LogInfo(
                    $"Enabled DBDE {dbdeInfo.Metadata.Version} Coordinate Load Option compatibility.");
            }
            catch (Exception ex)
            {
                DBDECoordinateLoadBridgePlugin.Log?.LogError(
                    $"Failed to enable DBDE Coordinate Load Option compatibility: {ex}");
            }
        }

        private static void PatchCoordinateLoad(Harmony harmony, Type controllerType)
        {
            var target = AccessTools.Method(controllerType, "OnCoordinateBeingLoaded", new[] { typeof(ChaFileCoordinate) });
            if (target == null)
                throw new MissingMethodException(controllerType.FullName, "OnCoordinateBeingLoaded");

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(DbdeBridge), nameof(CoordinateLoadPrefix))),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(DbdeBridge), nameof(CoordinateLoadPostfix))));
        }

        private static void PatchTransferCleanup(Harmony harmony, Type controllerType)
        {
            var patched = new HashSet<MethodBase>();
            foreach (string methodName in new[] { "RemoveCloTransferPluginData", "removeCloTransferPluginData" })
            {
                var target = AccessTools.Method(controllerType, methodName, Type.EmptyTypes);
                if (target == null || !patched.Add(target))
                    continue;

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(DbdeBridge), nameof(TransferCleanupPrefix))));
            }

            if (patched.Count == 0)
                throw new MissingMethodException(controllerType.FullName, "RemoveCloTransferPluginData");
        }

        private static void PatchTransferCleanupStateMachine(Harmony harmony, Type controllerType)
        {
            int patchedCount = 0;
            foreach (var nestedType in controllerType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (nestedType.Name.IndexOf("RemoveCloTransferPluginData", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var moveNext = AccessTools.Method(nestedType, "MoveNext", Type.EmptyTypes);
                if (moveNext == null)
                    continue;

                harmony.Patch(
                    moveNext,
                    prefix: new HarmonyMethod(AccessTools.Method(
                        typeof(DbdeBridge), nameof(TransferCleanupMoveNextPrefix))));
                patchedCount++;
            }

            if (patchedCount == 0)
            {
                DBDECoordinateLoadBridgePlugin.Log?.LogWarning(
                    "DBDE transfer cleanup state machine was not found; the bridge-owned backup will be used.");
            }
        }

        private static void PatchCoordinateLoadOptionStart(Harmony harmony, Assembly cloAssembly)
        {
            var patchesType = cloAssembly?.GetType(CoordinateLoadOptionPatchesTypeName, false);
            var target = patchesType == null
                ? null
                : AccessTools.Method(patchesType, "OnClickLoadPrefix", Type.EmptyTypes);
            if (target == null)
            {
                DBDECoordinateLoadBridgePlugin.Log?.LogWarning(
                    "Coordinate Load Option load-start hook was not found; timeout cleanup will be used as fallback.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(DbdeBridge), nameof(CoordinateLoadOptionStartPrefix))));
        }

        private static void CoordinateLoadPrefix(ref CoordinateLoadState __state)
        {
            __state = new CoordinateLoadState
            {
                IsCompatibilityLoad = IsCoordinateLoadOptionAccessoryReplaceActive()
            };

            if (!__state.IsCompatibilityLoad)
                return;

            if (_transferPhase == TransferPhase.SourceCaptured && HasSavedTransferData())
            {
                RestoreSavedTransferData();
                __state.IsRealCharacterLoad = true;
                _transferPhase = TransferPhase.TargetInjected;
                DBDECoordinateLoadBridgePlugin.Log?.LogDebug(
                    "Restored DBDE transfer data before the real character load.");
            }

            var nativeToggle = FindNativeAccessoryToggle();
            if (nativeToggle == null || nativeToggle.isOn)
                return;

            if (ToggleIsOnField == null)
            {
                DBDECoordinateLoadBridgePlugin.Log?.LogWarning(
                    "Could not access Unity Toggle.m_IsOn; the native Maker accessory toggle override was not applied.");
                return;
            }

            __state.NativeAccessoryToggle = nativeToggle;
            __state.NativeAccessoryToggleOriginalValue = nativeToggle.isOn;
            ToggleIsOnField.SetValue(nativeToggle, true);
            DBDECoordinateLoadBridgePlugin.Log?.LogDebug(
                "Temporarily enabled the native accessory load flag for DBDE.");
        }

        private static void CoordinateLoadPostfix(CoordinateLoadState __state)
        {
            if (__state == null)
                return;

            RestoreNativeAccessoryToggle(__state);
            if (!__state.IsCompatibilityLoad)
                return;

            if (__state.IsRealCharacterLoad)
            {
                CompleteTransferSession("the real character consumed the coordinate data");
                DBDECoordinateLoadBridgePlugin.Log?.LogInfo(
                    "Transferred DBDE coordinate data from the temporary character to the real character.");
            }
            else if (CaptureTransferData())
            {
                _transferPhase = TransferPhase.SourceCaptured;
                DBDECoordinateLoadBridgePlugin.Log?.LogDebug(
                    "Captured DBDE coordinate data from the Coordinate Load Option temporary character.");
            }
        }

        private static bool TransferCleanupPrefix(ref IEnumerator __result)
        {
            if (!IsCoordinateLoadOptionAccessoryReplaceActive())
                return true;

            __result = SafetyCleanup();
            DBDECoordinateLoadBridgePlugin.Log?.LogDebug(
                "Holding DBDE transfer data until the real character consumes it.");
            return false;
        }

        private static bool TransferCleanupMoveNextPrefix(ref bool __result)
        {
            if (!IsCoordinateLoadOptionAccessoryReplaceActive() ||
                (_transferPhase != TransferPhase.SourceCaptured &&
                 _transferPhase != TransferPhase.TargetInjected))
            {
                return true;
            }

            __result = false;
            if (!_loggedMoveNextHold)
            {
                _loggedMoveNextHold = true;
                DBDECoordinateLoadBridgePlugin.Log?.LogDebug(
                    "Stopped DBDE's generated cleanup coroutine from clearing transfer data early.");
            }
            return false;
        }

        private static void CoordinateLoadOptionStartPrefix()
        {
            ResetTransferSession("a new Coordinate Load Option session started");
            _transferPhase = TransferPhase.WaitingForSource;
            DBDECoordinateLoadBridgePlugin.Log?.LogDebug(
                "Started a DBDE Coordinate Load Option transfer session.");
        }

        private static IEnumerator SafetyCleanup()
        {
            for (int i = 0; i < SafetyCleanupFrames; i++)
                yield return null;

            if (HasSavedTransferData() || HasTransferData())
            {
                CompleteTransferSession("the compatibility transfer timed out");
                DBDECoordinateLoadBridgePlugin.Log?.LogWarning(
                    "Discarded stale DBDE Coordinate Load Option transfer data after timeout.");
            }
        }

        private static bool IsCoordinateLoadOptionAccessoryReplaceActive()
        {
            var panel = GameObject.Find("CoordinateTooglePanel");
            if (panel == null || !panel.activeInHierarchy)
                return false;

            var accessories = GameObject.Find("CoordinateTooglePanel/accessories")?.GetComponent<Toggle>();
            if (accessories == null || !accessories.isOn)
                return false;

            var modeText = GameObject.Find(
                "CoordinateTooglePanel/AccessoriesTooglePanel/BtnChangeAccLoadMode")?.GetComponentInChildren<Text>(true);
            return modeText != null && string.Equals(modeText.text, "Replace Mode", StringComparison.Ordinal);
        }

        private static Toggle FindNativeAccessoryToggle()
        {
            var fileControl = GameObject.Find("cosFileControl");
            var fileWindow = fileControl?.GetComponentInChildren<ChaCustom.CustomFileWindow>(true);
            return fileWindow?.tglCoordeLoadAcs;
        }

        private static void RestoreNativeAccessoryToggle(CoordinateLoadState state)
        {
            if (state.NativeAccessoryToggle == null || ToggleIsOnField == null)
                return;

            ToggleIsOnField.SetValue(
                state.NativeAccessoryToggle, state.NativeAccessoryToggleOriginalValue);
            state.NativeAccessoryToggle = null;
        }

        private static bool HasTransferData()
        {
            return _dbdeTransferField?.GetValue(null) != null ||
                   _dbeTransferField?.GetValue(null) != null;
        }

        private static bool HasSavedTransferData()
        {
            return _savedDbdeTransferData != null || _savedDbeTransferData != null;
        }

        private static bool CaptureTransferData()
        {
            _savedDbdeTransferData = _dbdeTransferField?.GetValue(null);
            _savedDbeTransferData = _dbeTransferField?.GetValue(null);
            return HasSavedTransferData();
        }

        private static void RestoreSavedTransferData()
        {
            _dbdeTransferField?.SetValue(null, _savedDbdeTransferData);
            _dbeTransferField?.SetValue(null, _savedDbeTransferData);
        }

        private static bool ClearTransferData(string reason)
        {
            bool hadData = HasTransferData();
            _dbdeTransferField?.SetValue(null, null);
            _dbeTransferField?.SetValue(null, null);
            if (hadData)
            {
                DBDECoordinateLoadBridgePlugin.Log?.LogDebug(
                    $"Cleared DBDE transfer data because {reason}.");
            }
            return hadData;
        }

        private static void ResetTransferSession(string reason)
        {
            ClearTransferData(reason);
            _savedDbdeTransferData = null;
            _savedDbeTransferData = null;
            _transferPhase = TransferPhase.Idle;
            _loggedMoveNextHold = false;
        }

        private static void CompleteTransferSession(string reason)
        {
            ResetTransferSession(reason);
        }

        private sealed class CoordinateLoadState
        {
            public bool IsCompatibilityLoad;
            public bool IsRealCharacterLoad;
            public Toggle NativeAccessoryToggle;
            public bool NativeAccessoryToggleOriginalValue;
        }

        private enum TransferPhase
        {
            Idle,
            WaitingForSource,
            SourceCaptured,
            TargetInjected
        }
    }
}
