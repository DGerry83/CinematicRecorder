using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CinematicRecorder.Integration
{
    /// <summary>
    /// Mirrors CameraTools.ToolModes enum (defined at namespace level, not inside CamTools class)
    /// </summary>
    public enum ToolModes { StationaryCamera, DogfightCamera, Pathing }

    public static class CameraToolsBridge
    {
        private static Type s_CamToolsType;
        private static Type s_ToolModesEnumType;
        private static FieldInfo s_FetchField;
        private static FieldInfo s_ToolModeField;
        private static MethodInfo s_CameraActivateMethod;
        private static MethodInfo s_RevertMethod;
        private static FieldInfo s_CameraToolActiveField;

        // Stationary positioning fields
        private static FieldInfo s_AutoFlybyPositionField;
        private static FieldInfo s_ManualOffsetField;
        private static FieldInfo s_ManualOffsetForwardField;
        private static FieldInfo s_ManualOffsetRightField;
        private static FieldInfo s_ManualOffsetUpField;
        private static FieldInfo s_AutoLandingPositionField;
        private static FieldInfo s_SetPresetOffsetField;  // bool
        private static FieldInfo s_PresetOffsetField;     // Vector3
        private static FieldInfo s_CamTargetField;        // Part
        private static FieldInfo s_HasTargetField;        // bool
        private static FieldInfo s_TargetCoMField;        // bool
        private static FieldInfo s_MaintainInitialVelocityField;
        private static FieldInfo s_UseOrbitalField;
        private static FieldInfo s_VesselField;           // To get current vessel for CoM calculation


        // Mode-specific fields
        private static FieldInfo s_DogfightDistanceField;
        private static FieldInfo s_DogfightOffsetXField;
        private static FieldInfo s_DogfightOffsetYField;
        private static FieldInfo s_DogfightTargetField;
        private static FieldInfo s_DogfightChasePlaneModeField;
        private static FieldInfo s_ManualPositionField; // PRIVATE field - requires NonPublic
        private static FieldInfo s_AutoZoomStationaryField;
        private static FieldInfo s_ManualFOVField; // Actually 'manualFOV' in source
        private static FieldInfo s_SelectedPathIndexField;
        private static FieldInfo s_CurrentPathField; // To check if path exists

        // Pathing Fields
        private static FieldInfo s_IsPlayingPathField;
        private static FieldInfo s_CurrentKeyframeIndexField;
        private static FieldInfo s_UseRealTimeField;
        private static FieldInfo s_PathStartTimeField;
        private static PropertyInfo s_CurrentPathProperty; // currentPath is a property, not field

        public static bool IsAvailable => s_CamToolsType != null && Fetch != null;

        public static object Fetch
        {
            get
            {
                if (s_FetchField == null) return null;
                return s_FetchField.GetValue(null);
            }
        }

        static CameraToolsBridge()
        {
            Initialize();
        }

        private static void Initialize()
        {
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "CameraTools");

                if (asm == null) return;

                s_CamToolsType = asm.GetType("CameraTools.CamTools");
                s_ToolModesEnumType = asm.GetType("CameraTools.ToolModes");

                if (s_CamToolsType == null || s_ToolModesEnumType == null) return;

                s_FetchField = s_CamToolsType.GetField("fetch", BindingFlags.Public | BindingFlags.Static);
                s_ToolModeField = s_CamToolsType.GetField("toolMode"); // Public instance field
                s_CameraActivateMethod = s_CamToolsType.GetMethod("CameraActivate", BindingFlags.Public | BindingFlags.Instance);
                s_RevertMethod = s_CamToolsType.GetMethod("RevertCamera", BindingFlags.Public | BindingFlags.Instance);
                s_CameraToolActiveField = s_CamToolsType.GetField("cameraToolActive"); // Public

                // Dogfight fields (all public)
                s_DogfightDistanceField = s_CamToolsType.GetField("dogfightDistance");
                s_DogfightOffsetXField = s_CamToolsType.GetField("dogfightOffsetX");
                s_DogfightOffsetYField = s_CamToolsType.GetField("dogfightOffsetY");
                s_DogfightTargetField = s_CamToolsType.GetField("dogfightTarget");
                s_DogfightChasePlaneModeField = s_CamToolsType.GetField("dogfightChasePlaneMode");

                // Stationary fields
                // CRITICAL FIX: manualPosition is private in source (no access modifier)
                s_ManualPositionField = s_CamToolsType.GetField("manualPosition", BindingFlags.NonPublic | BindingFlags.Instance);
                s_AutoZoomStationaryField = s_CamToolsType.GetField("autoZoomStationary");
                s_ManualFOVField = s_CamToolsType.GetField("manualFOV", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? s_CamToolsType.GetField("currentFOV"); // Fallback to currentFOV if manualFOV is named differently
                s_AutoFlybyPositionField = s_CamToolsType.GetField("autoFlybyPosition");
                s_ManualOffsetField = s_CamToolsType.GetField("manualOffset");
                s_ManualOffsetForwardField = s_CamToolsType.GetField("manualOffsetForward");
                s_ManualOffsetRightField = s_CamToolsType.GetField("manualOffsetRight");
                s_ManualOffsetUpField = s_CamToolsType.GetField("manualOffsetUp");
                s_AutoLandingPositionField = s_CamToolsType.GetField("autoLandingPosition");
                s_SetPresetOffsetField = s_CamToolsType.GetField("setPresetOffset", BindingFlags.NonPublic | BindingFlags.Instance);
                s_PresetOffsetField = s_CamToolsType.GetField("presetOffset", BindingFlags.NonPublic | BindingFlags.Instance); // Check if private
                s_CamTargetField = s_CamToolsType.GetField("camTarget");
                s_HasTargetField = s_CamToolsType.GetField("hasTarget");
                s_TargetCoMField = s_CamToolsType.GetField("targetCoM");
                s_MaintainInitialVelocityField = s_CamToolsType.GetField("maintainInitialVelocity");
                s_UseOrbitalField = s_CamToolsType.GetField("useOrbital");
                s_VesselField = s_CamToolsType.GetField("vessel");

                // Pathing fields (public)
                s_SelectedPathIndexField = s_CamToolsType.GetField("selectedPathIndex");
                s_CurrentPathField = s_CamToolsType.GetField("availablePaths"); // List<CameraPath>
                s_IsPlayingPathField = s_CamToolsType.GetField("isPlayingPath");
                s_CurrentKeyframeIndexField = s_CamToolsType.GetField("currentKeyframeIndex");
                s_UseRealTimeField = s_CamToolsType.GetField("useRealTime");
                s_PathStartTimeField = s_CamToolsType.GetField("pathStartTime");
                s_CurrentPathProperty = s_CamToolsType.GetProperty("currentPath", BindingFlags.NonPublic | BindingFlags.Instance);

                if (s_FetchField != null)
                    Debug.Log("[CameraToolsBridge] CameraTools detected and bound");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CameraToolsBridge] Init failed: " + ex.Message);
                s_CamToolsType = null;
            }
        }

        /// <summary>
        /// Activates CameraTools with specific mode and settings.
        /// Does NOT revert HullCam first - call ClearHullCamStaticState() before this if switching from HullCam.
        /// </summary>
        public static void ActivateMode(ToolModes mode, CameraToolsSettings settings = null)
        {
            if (!IsAvailable) return;

            var instance = Fetch;
            if (instance == null) return;

            // Set the mode using the actual enum type from the assembly
            var enumValue = Enum.ToObject(s_ToolModesEnumType, (int)mode);
            s_ToolModeField.SetValue(instance, enumValue);

            // Apply settings if provided
            if (settings != null)
            {
                ApplySettings(instance, mode, settings);
                Debug.Log($"[CTBridge] Activating mode {mode} with pathIndex {settings.SelectedPathIndex}");
            }
            Debug.Log($"[CTBridge] Activating Stationary: autoFlyby={settings?.AutoFlybyPosition}, " +
          $"manualOffset={settings?.ManualOffset}, usePreset={settings?.UsePresetOffset}, " +
          $"manualPos={settings?.ManualPosition}");
            // Activate
            s_CameraActivateMethod.Invoke(instance, null);
        }

        public static void Revert()
        {
            if (!IsAvailable) return;
            s_RevertMethod?.Invoke(Fetch, null);
        }

        /// <summary>
        /// Releases control without reverting camera to stock position.
        /// Use this when switching directly to HullCam to avoid intermediate frame.
        /// </summary>
        public static void ReleaseControlWithoutReverting()
        {
            if (!IsAvailable) return;

            var instance = Fetch;
            if (instance == null) return;

            // Just clear the active flag - camera parenting remains unchanged
            s_CameraToolActiveField.SetValue(instance, false);

            // Optional: Call OnResetCTools event if other mods need to know
            // This is safer than full Revert() which reparents camera
        }

        public static bool IsActive()
        {
            if (!IsAvailable) return false;
            return (bool)s_CameraToolActiveField.GetValue(Fetch);
        }

        public static ToolModes GetCurrentMode()
        {
            if (!IsAvailable) return ToolModes.StationaryCamera;
            var val = s_ToolModeField.GetValue(Fetch);
            return (ToolModes)Convert.ToInt32(val);
        }

        public static bool PathExists(int index)
        {
            if (!IsAvailable) return false;
            var paths = s_CurrentPathField?.GetValue(Fetch) as System.Collections.IList;
            return paths != null && index >= 0 && index < paths.Count;
        }

        public static CameraToolsSettings CaptureCurrentSettings()
        {
            if (!IsAvailable) return null;

            var instance = Fetch;
            var mode = GetCurrentMode();
            var settings = new CameraToolsSettings { Mode = mode };
            Vessel currentVessel = s_VesselField?.GetValue(instance) as Vessel;
            Vector3 vesselCoM = currentVessel?.CoM ?? Vector3.zero;

            try
            {
                switch (mode)
                {
                    case ToolModes.DogfightCamera:
                        settings.DogfightDistance = (float)s_DogfightDistanceField.GetValue(instance);
                        settings.DogfightOffsetX = (float)s_DogfightOffsetXField.GetValue(instance);
                        settings.DogfightOffsetY = (float)s_DogfightOffsetYField.GetValue(instance);
                        settings.DogfightChasePlaneMode = (bool)(s_DogfightChasePlaneModeField?.GetValue(instance) ?? false);
                        var target = s_DogfightTargetField.GetValue(instance) as Vessel;
                        settings.DogfightTargetId = target?.id.ToString();
                        break;

                    case ToolModes.StationaryCamera:
                        // CRITICAL FIX: Don't just read manualPosition (it's often zero)!
                        // Calculate it from actual camera position vs CoM
                        if (FlightCamera.fetch != null && currentVessel != null)
                        {
                            settings.CalculateManualPositionFromWorld(
                                FlightCamera.fetch.transform.position,
                                vesselCoM
                            );
                        }
                        else
                        {
                            settings.ManualPosition = (Vector3)(s_ManualPositionField?.GetValue(instance) ?? Vector3.zero);
                        }

                        settings.AutoZoom = (bool)(s_AutoZoomStationaryField?.GetValue(instance) ?? false);
                        settings.ManualFOV = (float)(s_ManualFOVField?.GetValue(instance) ?? 60f);

                        // Capture positioning mode flags
                        settings.AutoFlybyPosition = (bool)(s_AutoFlybyPositionField?.GetValue(instance) ?? false);
                        settings.ManualOffset = (bool)(s_ManualOffsetField?.GetValue(instance) ?? false);
                        settings.ManualOffsetForward = (float)(s_ManualOffsetForwardField?.GetValue(instance) ?? 500f);
                        settings.ManualOffsetRight = (float)(s_ManualOffsetRightField?.GetValue(instance) ?? 50f);
                        settings.ManualOffsetUp = (float)(s_ManualOffsetUpField?.GetValue(instance) ?? 5f);
                        settings.AutoLandingPosition = (bool)(s_AutoLandingPositionField?.GetValue(instance) ?? false);
                        settings.UsePresetOffset = (bool)(s_SetPresetOffsetField?.GetValue(instance) ?? false);
                        settings.PresetOffset = (Vector3)(s_PresetOffsetField?.GetValue(instance) ?? Vector3.zero);

                        bool hasPositioningMode = (bool)(s_AutoFlybyPositionField?.GetValue(instance) ?? false) ||
                          (bool)(s_AutoLandingPositionField?.GetValue(instance) ?? false) ||
                          (bool)(s_ManualOffsetField?.GetValue(instance) ?? false) ||
                          (bool)(s_SetPresetOffsetField?.GetValue(instance) ?? false);

                        if (!hasPositioningMode)
                        {
                            settings.UsePresetOffset = true; // Force preset mode for restoration
                            Debug.Log("[CTBridge] Captured as preset position (no mode flags set)");
                        }

                        settings.MaintainInitialVelocity = (bool)(s_MaintainInitialVelocityField?.GetValue(instance) ?? false);
                        settings.UseOrbital = (bool)(s_UseOrbitalField?.GetValue(instance) ?? false);

                        // Target info
                        settings.HasTarget = (bool)(s_HasTargetField?.GetValue(instance) ?? false);
                        settings.TargetCoM = (bool)(s_TargetCoMField?.GetValue(instance) ?? false);

                        var targetPart = s_CamTargetField?.GetValue(instance) as Part;
                        if (targetPart != null && currentVessel != null)
                        {
                            // Check if target is on the active vessel
                            if (targetPart.vessel == currentVessel)
                            {
                                settings.TargetSelf = true;  // Target belongs to active vessel
                                settings.TargetPartPersistentId = 0; // Don't save specific ID
                            }
                            else
                            {
                                settings.TargetSelf = false; // Target is another vessel
                                settings.TargetPartPersistentId = targetPart.persistentId;
                            }
                        }
                        else
                        {
                            settings.TargetSelf = false;
                            settings.TargetPartPersistentId = 0;
                        }
                        break;

                    case ToolModes.Pathing:
                        settings.SelectedPathIndex = (int)s_SelectedPathIndexField.GetValue(instance);
                        settings.CurrentKeyframeIndex = (int)(s_CurrentKeyframeIndexField?.GetValue(instance) ?? -1);
                        settings.IsPlayingPath = (bool)(s_IsPlayingPathField?.GetValue(instance) ?? false);
                        settings.UseRealTime = (bool)(s_UseRealTimeField?.GetValue(instance) ?? true);
                        settings.PathStartTime = (float)(s_PathStartTimeField?.GetValue(instance) ?? 0f);

                        // CRITICAL: Validate path exists before claiming we saved it
                        var paths = s_CurrentPathField?.GetValue(instance) as System.Collections.IList;
                        if (paths == null || settings.SelectedPathIndex < 0 || settings.SelectedPathIndex >= paths.Count)
                        {
                            Debug.LogWarning("[CameraToolsBridge] Cannot save Pathing camera - invalid path index");
                            return null;
                        }

                        // Get timeScale from the specific path object, not the list
                        var selectedPath = paths[settings.SelectedPathIndex];
                        if (selectedPath != null)
                        {
                            var timeScaleField = selectedPath.GetType().GetField("timeScale");
                            settings.PathTimeScale = (float)(timeScaleField?.GetValue(selectedPath) ?? 1f);

                            // Check keyframe count
                            var keyframeCountProp = selectedPath.GetType().GetProperty("keyframeCount");
                            int keyframeCount = (int)(keyframeCountProp?.GetValue(selectedPath) ?? 0);
                            if (keyframeCount <= 0)
                            {
                                Debug.LogWarning("[CameraToolsBridge] Cannot save Pathing camera - path has no keyframes");
                                return null;
                            }
                        }
                        else
                        {
                            settings.PathTimeScale = 1f;
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CameraToolsBridge] Failed to capture settings: {ex.Message}");
            }

            return settings;
        }

        private static void ApplySettings(object instance, ToolModes mode, CameraToolsSettings settings)
        {
            try
            {
                switch (mode)
                {
                    case ToolModes.DogfightCamera:
                        if (settings.DogfightDistance > 0)
                            s_DogfightDistanceField.SetValue(instance, settings.DogfightDistance);
                        s_DogfightOffsetXField.SetValue(instance, settings.DogfightOffsetX);
                        s_DogfightOffsetYField.SetValue(instance, settings.DogfightOffsetY);

                        if (s_DogfightChasePlaneModeField != null && settings.DogfightChasePlaneMode)
                            s_DogfightChasePlaneModeField.SetValue(instance, true);

                        // Resolve target vessel if specified
                        if (!string.IsNullOrEmpty(settings.DogfightTargetId))
                        {
                            var target = FlightGlobals.Vessels.FirstOrDefault(v => v.id.ToString() == settings.DogfightTargetId);
                            s_DogfightTargetField.SetValue(instance, target);
                        }
                        break;

                    case ToolModes.StationaryCamera:
                        var vessel = s_VesselField?.GetValue(instance) as Vessel;
                        if (vessel == null) return;

                        // CRITICAL: Explicitly reset ALL positioning state to prevent carry-over
                        s_AutoFlybyPositionField?.SetValue(instance, false);
                        s_AutoLandingPositionField?.SetValue(instance, false);
                        s_ManualOffsetField?.SetValue(instance, false);
                        s_SetPresetOffsetField?.SetValue(instance, false);
                        s_HasTargetField?.SetValue(instance, settings.HasTarget);
                        s_TargetCoMField?.SetValue(instance, settings.TargetCoM);

                        if (settings.HasTarget)
                        {
                            Part targetPart = null;

                            if (settings.TargetSelf)
                            {
                                // Resolve to current active vessel's reference part
                                var currentVessel = FlightGlobals.ActiveVessel;
                                if (currentVessel != null)
                                {
                                    targetPart = currentVessel.GetReferenceTransformPart();
                                    if (targetPart == null)
                                        targetPart = currentVessel.rootPart;

                                    Debug.Log($"[CTBridge] TargetSelf resolved to: {targetPart?.partInfo?.title}");
                                }
                            }
                            else if (settings.TargetPartPersistentId != 0)
                            {
                                // Use the vessel we already got at the top of this case
                                targetPart = vessel.Parts.FirstOrDefault(p => p.persistentId == settings.TargetPartPersistentId);

                                // If not found, fall back to self
                                if (targetPart == null && FlightGlobals.ActiveVessel != null)
                                {
                                    Debug.LogWarning($"[CTBridge] Target part {settings.TargetPartPersistentId} not found, falling back to TargetSelf");
                                    targetPart = FlightGlobals.ActiveVessel.GetReferenceTransformPart() ?? FlightGlobals.ActiveVessel.rootPart;
                                }
                            }

                            s_CamTargetField?.SetValue(instance, targetPart);

                            // CRITICAL: Update hasTarget based on whether we actually found a target
                            if (targetPart == null)
                            {
                                Debug.LogWarning("[CTBridge] No target resolved, clearing hasTarget");
                                s_HasTargetField?.SetValue(instance, false);
                            }
                        }
                        else
                        {
                            s_CamTargetField?.SetValue(instance, null);
                        }

                        // Force the saved positioning mode
                        if (settings.AutoFlybyPosition)
                        {
                            s_AutoFlybyPositionField?.SetValue(instance, true);
                        }
                        else if (settings.AutoLandingPosition)
                        {
                            s_AutoLandingPositionField?.SetValue(instance, true);
                        }
                        else if (settings.ManualOffset)
                        {
                            s_ManualOffsetField?.SetValue(instance, true);
                            s_ManualOffsetForwardField?.SetValue(instance, settings.ManualOffsetForward);
                            s_ManualOffsetRightField?.SetValue(instance, settings.ManualOffsetRight);
                            s_ManualOffsetUpField?.SetValue(instance, settings.ManualOffsetUp);
                        }
                        else
                        {
                            // For specific manual positions (where user dragged camera), use preset mechanism
                            // This bypasses the velocity-based calculations
                            s_SetPresetOffsetField?.SetValue(instance, true);
                            // presetOffset is WORLD position, so add current CoM to saved offset
                            Vector3 worldPos = vessel.CoM + settings.ManualPosition;
                            s_PresetOffsetField?.SetValue(instance, worldPos);
                        }

                        // Other settings
                        s_MaintainInitialVelocityField?.SetValue(instance, settings.MaintainInitialVelocity);
                        s_UseOrbitalField?.SetValue(instance, settings.UseOrbital);
                        s_AutoZoomStationaryField?.SetValue(instance, settings.AutoZoom);
                        s_ManualFOVField?.SetValue(instance, settings.ManualFOV);
                        break;

                    case ToolModes.Pathing:
                        // Validate path still exists before activating
                        var availablePaths = s_CurrentPathField?.GetValue(instance) as System.Collections.IList;
                        if (availablePaths == null || settings.SelectedPathIndex < 0 || settings.SelectedPathIndex >= availablePaths.Count)
                        {
                            Debug.LogError("[CameraToolsBridge] Cannot activate Pathing camera - path no longer exists");
                            // Optionally revert or show message
                            return;
                        }

                        s_SelectedPathIndexField.SetValue(instance, settings.SelectedPathIndex);
                        s_UseRealTimeField?.SetValue(instance, settings.UseRealTime);

                        // Set path timescale on the path object itself
                        var selectedPath = availablePaths[settings.SelectedPathIndex];
                        if (selectedPath != null)
                        {
                            var timeScaleField = selectedPath.GetType().GetField("timeScale");
                            timeScaleField?.SetValue(selectedPath, settings.PathTimeScale);
                        }

                        // Restore playback state?
                        // Note: We start at specific keyframe if saved
                        if (settings.CurrentKeyframeIndex >= 0 && s_CurrentKeyframeIndexField != null)
                        {
                            s_CurrentKeyframeIndexField.SetValue(instance, settings.CurrentKeyframeIndex);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsBridge] Failed to apply settings: {ex.Message}");
            }
        }
    }

    [Serializable]
    public class CameraToolsSettings
    {
        public ToolModes Mode;

        // Dogfight params
        public float DogfightDistance = 50f;
        public float DogfightOffsetX = 0f;
        public float DogfightOffsetY = 5f;
        public bool DogfightChasePlaneMode = false;
        public string DogfightTargetId;

        // Stationary params - EXPANDED
        public Vector3 ManualPosition;              // Offset from CoM (calculated at capture)
        public bool AutoZoom;
        public float ManualFOV = 60f;

        // Positioning mode flags (critical for reconstruction)
        public bool AutoFlybyPosition;              // Uses auto-calculated flyby position
        public bool ManualOffset;                   // Uses manualOffsetForward/Right/Up
        public float ManualOffsetForward = 500f;
        public float ManualOffsetRight = 50f;
        public float ManualOffsetUp = 5f;
        public bool AutoLandingPosition;            // Landing prediction mode
        public bool UsePresetOffset;                // Uses PresetOffset world position
        public Vector3 PresetOffset;                // Direct world position (if setPresetOffset was true)

        // Target tracking - expanded
        public bool HasTarget;
        public bool TargetSelf;                    // NEW: Target the active vessel (dynamic)
        public uint TargetPartPersistentId;        // Specific part (if TargetSelf is false)
        public bool TargetCoM;

        // Velocity/orbit tracking
        public bool MaintainInitialVelocity;
        public bool UseOrbital;

        // Pathing params
        public int SelectedPathIndex = -1;
        public float PathTimeScale = 1f;
        public int CurrentKeyframeIndex = -1;  // Which keyframe was being viewed
        public bool IsPlayingPath = false;      // Were they playing or editing?
        public bool UseRealTime = true;         // Realtime vs game-time
        public float PathStartTime = 0f;        // Where to start in the path


        // Helper to calculate ManualPosition from world position at capture time
        public void CalculateManualPositionFromWorld(Vector3 worldPos, Vector3 vesselCoM)
        {
            ManualPosition = worldPos - vesselCoM;
        }


        // Display helper
        public string GetDisplayName()
        {
            switch (Mode)
            {
                case ToolModes.DogfightCamera:
                    return $"Dogfight {(string.IsNullOrEmpty(DogfightTargetId) ? "(Free)" : "(Target)")}";
                case ToolModes.StationaryCamera:
                    return "Stationary";
                case ToolModes.Pathing:
                    return SelectedPathIndex >= 0 ? $"Path #{SelectedPathIndex}" : "Pathing";
                default:
                    return "CameraTools";
            }
        }

        public bool ApproximatelyMatches(CameraToolsSettings other)
        {
            if (other == null || other.Mode != this.Mode) return false;

            switch (Mode)
            {
                case ToolModes.StationaryCamera:
                    // If both are auto-flyby, they're the same "type" of camera
                    // (even if vessel position changed, it's the same preset)
                    if (this.AutoFlybyPosition && other.AutoFlybyPosition)
                        return true;

                    // If both are auto-landing
                    if (this.AutoLandingPosition && other.AutoLandingPosition)
                        return true;

                    // If both use manual offset inputs (not direct positioning)
                    if (this.ManualOffset && other.ManualOffset)
                    {
                        return Mathf.Abs(this.ManualOffsetForward - other.ManualOffsetForward) < 1.0f &&
                               Mathf.Abs(this.ManualOffsetRight - other.ManualOffsetRight) < 1.0f &&
                               Mathf.Abs(this.ManualOffsetUp - other.ManualOffsetUp) < 1.0f;
                    }

                    // For preset/manual specific positions, check actual position
                    return Vector3.Distance(this.ManualPosition, other.ManualPosition) < 1.0f &&
                           Mathf.Abs(this.ManualFOV - other.ManualFOV) < 5.0f;

                case ToolModes.DogfightCamera:
                    return Mathf.Abs(this.DogfightDistance - other.DogfightDistance) < 5.0f &&
                           Mathf.Abs(this.DogfightOffsetX - other.DogfightOffsetX) < 1.0f &&
                           Mathf.Abs(this.DogfightOffsetY - other.DogfightOffsetY) < 1.0f;

                case ToolModes.Pathing:
                    return this.SelectedPathIndex == other.SelectedPathIndex;
            }
            return false;
        }
    }
}