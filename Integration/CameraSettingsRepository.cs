using CinematicRecorder.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CinematicRecorder.Integration
{
    /// <summary>
    /// Handles CameraToolsSettings DTO <-> CameraTools object mapping.
    /// Uses CinematicRecorderIntegration API for application and reflection for capture.
    /// </summary>
    public class CameraSettingsRepository
    {
        /// <summary>
        /// Captures current CameraTools state into a settings DTO.
        /// Uses GetCurrentState() API where available, reflection for remaining fields.
        /// </summary>
        public CameraToolsSettings CaptureSettings()
        {
            if (!CameraToolsAPIManager.IsAvailable)
            {
                UnityEngine.Debug.LogWarning("[CaptureSettings] CameraTools API not available");
                return null;
            }

            var settings = new CameraToolsSettings();

            // Use API Getters instead of reflection where available
            settings.Mode = CameraToolsAPIManager.GetToolMode();
            settings.IsPlayingPath = CameraToolsAPIManager.IsCameraActive(); // Or specific pathing state if available
            settings.PathStartTime = CameraToolsAPIManager.GetCurrentPathTime();

            // FOV via API
            settings.ManualFOV = CameraToolsAPIManager.GetManualFOV();
            if (settings.ManualFOV <= 0) // Fallback
                settings.ManualFOV = CameraToolsReflectionProvider.CurrentFOV;

            UnityEngine.Debug.Log($"[CaptureSettings] API State captured - Mode: {settings.Mode}, IsPlayingPath: {settings.IsPlayingPath}");

            try
            {
                switch (settings.Mode)
                {
                    case ToolModes.DogfightCamera:
                        UnityEngine.Debug.Log("[CaptureSettings] Capturing Dogfight settings");
                        CaptureDogfightSettings(settings);
                        break;
                    case ToolModes.StationaryCamera:
                        UnityEngine.Debug.Log("[CaptureSettings] Capturing Stationary settings");
                        CaptureStationarySettings(settings);
                        break;
                    case ToolModes.Pathing:
                        UnityEngine.Debug.Log("[CaptureSettings] Capturing Pathing settings");
                        CapturePathingSettings(settings);
                        break;
                    default:
                        UnityEngine.Debug.LogWarning($"[CaptureSettings] Unrecognized mode: {settings.Mode}, defaulting to Stationary capture");
                        CaptureStationarySettings(settings);
                        break;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[CameraSettingsRepository] Capture failed: {ex}");
                return null;
            }

            UnityEngine.Debug.Log($"[CaptureSettings] Capture complete: {settings.Mode} " +
                $"(PathIndex: {settings.SelectedPathIndex}, UsePlaybackTiming: {settings.LockPathingToPlaybackRate})");

            return settings;
        }

        /// <summary>
        /// Applies settings to CameraTools using the public API.
        /// Uses CinematicRecorderIntegration methods for configuration.
        /// </summary>
        public void ApplySettings(CameraToolsSettings settings, bool activateImmediately = true)
        {
            if (!CameraToolsAPIManager.IsAvailable || settings == null)
            {
                UnityEngine.Debug.LogWarning($"[ApplySettings] Aborting - Available: {CameraToolsAPIManager.IsAvailable}, Settings null: {settings == null}");
                return;
            }

            try
            {
                UnityEngine.Debug.Log($"[ApplySettings] Applying {settings.Mode} to CameraTools " +
                    $"(PathIndex: {settings.SelectedPathIndex}, UsePlaybackTiming: {settings.LockPathingToPlaybackRate})");

                // Set mode first using API
                CameraToolsAPIManager.SetToolMode(settings.Mode);
                UnityEngine.Debug.Log($"[ApplySettings] Mode set to {settings.Mode}");

                // Enable CR control mode (immediate FOV, no smoothing)
                // Determine deterministic mode based on session state and settings
                bool useDeterministic = settings.UseDeterministicControl || DeterministicCaptureSession.IsRunning;
                CameraToolsAPIManager.SetCinematicRecorderControl(enabled: true, deterministicMode: useDeterministic);
                UnityEngine.Debug.Log($"[ApplySettings] CR Control enabled, deterministic: {useDeterministic}");

                switch (settings.Mode)
                {
                    case ToolModes.DogfightCamera:
                        ApplyDogfightSettings(settings);
                        break;
                    case ToolModes.StationaryCamera:
                        ApplyStationarySettings(settings);
                        break;
                    case ToolModes.Pathing:
                        ApplyPathingSettings(settings);
                        break;
                }

                // Apply FOV immediately if specified
                if (settings.ManualFOV > 0)
                {
                    float effectiveFOV = settings.ManualFOV;
                    effectiveFOV = Mathf.Clamp(effectiveFOV, 2f, 120f);

                    CameraToolsAPIManager.SetExternalFOV(effectiveFOV);
                    UnityEngine.Debug.Log($"[ApplySettings] FOV set to {effectiveFOV}");
                }

                // Activate if requested
                if (activateImmediately)
                {
                    UnityEngine.Debug.Log("[ApplySettings] Activating camera");
                    CameraToolsAPIManager.ActivateCamera();

                    // Start path playback if in pathing mode and marked as playing
                    if (settings.Mode == ToolModes.Pathing && settings.IsPlayingPath)
                    {
                        UnityEngine.Debug.Log("[ApplySettings] Starting path playback");
                        CameraToolsAPIManager.StartPathPlayback();
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[CameraSettingsRepository] Apply failed: {ex}");
            }
        }

        private void CaptureDogfightSettings(CameraToolsSettings settings)
        {
            settings.DogfightDistance = CameraToolsReflectionProvider.GetFloat(CameraToolsReflectionProvider.DogfightDistanceField, 50f);
            settings.DogfightOffsetX = CameraToolsReflectionProvider.GetFloat(CameraToolsReflectionProvider.DogfightOffsetXField, 0f);
            settings.DogfightOffsetY = CameraToolsReflectionProvider.GetFloat(CameraToolsReflectionProvider.DogfightOffsetYField, 5f);
            settings.DogfightChasePlaneMode = CameraToolsReflectionProvider.GetBool(CameraToolsReflectionProvider.DogfightChasePlaneModeField, false);

            var target = CameraToolsReflectionProvider.GetReference<Vessel>(CameraToolsReflectionProvider.DogfightTargetField);
            settings.DogfightTargetId = target?.id.ToString();

            // Capture FOV
            settings.ManualFOV = CameraToolsReflectionProvider.CurrentFOV;
        }

        private void CaptureStationarySettings(CameraToolsSettings settings)
        {
            Vessel currentVessel = FlightGlobals.ActiveVessel;
            if (currentVessel == null) return;

            // Positioning modes
            bool autoFlyby = CameraToolsReflectionProvider.GetBool(CameraToolsReflectionProvider.AutoFlybyPositionField, false);
            bool autoLanding = CameraToolsReflectionProvider.GetBool(CameraToolsReflectionProvider.AutoLandingPositionField, false);
            bool manualOffset = CameraToolsReflectionProvider.GetBool(CameraToolsReflectionProvider.ManualOffsetField, false);

            if (autoFlyby || autoLanding)
            {
                settings.AutoFlybyPosition = autoFlyby;
                settings.AutoLandingPosition = autoLanding;
                settings.UseGeographicPosition = false;
                settings.ManualOffset = false;
            }
            else if (manualOffset)
            {
                settings.ManualOffset = true;
                settings.UseGeographicPosition = false;
                settings.AutoFlybyPosition = false;
                settings.AutoLandingPosition = false;
                settings.ManualOffsetForward = CameraToolsReflectionProvider.GetFloat(CameraToolsReflectionProvider.ManualOffsetForwardField, 500f);
                settings.ManualOffsetRight = CameraToolsReflectionProvider.GetFloat(CameraToolsReflectionProvider.ManualOffsetRightField, 50f);
                settings.ManualOffsetUp = CameraToolsReflectionProvider.GetFloat(CameraToolsReflectionProvider.ManualOffsetUpField, 5f);
            }
            else
            {
                // Geographic capture
                settings.UseGeographicPosition = true;
                settings.AutoFlybyPosition = false;
                settings.AutoLandingPosition = false;
                settings.ManualOffset = false;

                CelestialBody body = FlightGlobals.currentMainBody;
                if (body != null)
                {
                    Vector3 cameraWorldPos = FlightCamera.fetch?.transform.position ?? Vector3.zero;
                    var coords = GeographicCoordinateSystem.GetCoordinates(body, cameraWorldPos);
                    settings.Latitude = coords.Latitude;
                    settings.Longitude = coords.Longitude;
                    settings.Altitude = coords.Altitude;
                    settings.BodyName = coords.BodyName;
                }
            }

            // Common settings
            settings.SaveRotation = CameraToolsReflectionProvider.GetBool(CameraToolsReflectionProvider.SaveRotationField, false);
            settings.FmPivotMode = CameraToolsReflectionProvider.ConvertToLocalFMPivotMode(
                CameraToolsReflectionProvider.GetField<object>(CameraToolsReflectionProvider.FmPivotModeField));
            settings.InitialVelocity = CameraToolsReflectionProvider.GetVector3(CameraToolsReflectionProvider.InitialVelocityField, Vector3.zero);
            settings.MaintainInitialVelocity = CameraToolsReflectionProvider.GetBool(CameraToolsReflectionProvider.MaintainInitialVelocityField, false);
            settings.UseOrbital = CameraToolsReflectionProvider.GetBool(CameraToolsReflectionProvider.UseOrbitalField, false);
            settings.AutoZoom = CameraToolsReflectionProvider.GetBool(CameraToolsReflectionProvider.AutoZoomStationaryField, false);

            // Capture current FOV from reflection
            settings.ManualFOV = CameraToolsReflectionProvider.CurrentFOV;

            // Target tracking
            CaptureTargetTrackingState(settings, currentVessel);
        }

        private void CaptureTargetTrackingState(CameraToolsSettings settings, Vessel currentVessel)
        {
            settings.HasTarget = CameraToolsReflectionProvider.HasTarget;
            settings.TargetCoM = CameraToolsReflectionProvider.GetBool(CameraToolsReflectionProvider.TargetCoMField, false);

            Part camTarget = CameraToolsReflectionProvider.CamTarget;
            if (camTarget != null && currentVessel != null)
            {
                if (camTarget.vessel == currentVessel)
                {
                    settings.TargetSelf = true;
                    settings.TargetPartPersistentId = 0;
                }
                else
                {
                    settings.TargetSelf = false;
                    settings.TargetPartPersistentId = camTarget.persistentId;
                }
            }
            else
            {
                settings.TargetSelf = false;
                settings.TargetPartPersistentId = 0;
            }
        }

        private void CapturePathingSettings(CameraToolsSettings settings)
        {
            settings.SelectedPathIndex = CameraToolsReflectionProvider.GetInt(CameraToolsReflectionProvider.SelectedPathIndexField, -1);
            settings.CurrentKeyframeIndex = CameraToolsReflectionProvider.GetInt(CameraToolsReflectionProvider.CurrentKeyframeIndexField, -1);
            settings.UseRealTime = CameraToolsReflectionProvider.GetBool(CameraToolsReflectionProvider.UseRealTimeField, true);
            settings.PathingSecondarySmoothing = CameraToolsReflectionProvider.GetFloat(CameraToolsReflectionProvider.PathingSecondarySmoothingField, 0f);
            settings.PathTimeScale = CameraToolsReflectionProvider.ExtractPathTimeScale(settings.SelectedPathIndex);

            // FIX: Capture playback timing preference from current session state
            // Since we can't read this back from CameraTools via reflection/API getter, 
            // we capture the current global setting as the "active" value
            settings.LockPathingToPlaybackRate = SessionState.CameraPathPlaybackTiming;

            if (!CameraToolsReflectionProvider.PathExists(settings.SelectedPathIndex))
            {
                UnityEngine.Debug.LogWarning($"[CapturePathingSettings] Path index {settings.SelectedPathIndex} no longer exists, marking as invalid");
                settings.SelectedPathIndex = -1;
            }

            // Capture FOV
            settings.ManualFOV = CameraToolsReflectionProvider.CurrentFOV;

            UnityEngine.Debug.Log($"[CapturePathingSettings] Captured: PathIndex={settings.SelectedPathIndex}, " +
                $"Keyframe={settings.CurrentKeyframeIndex}, UsePlaybackTiming={settings.LockPathingToPlaybackRate}, FOV={settings.ManualFOV}");
        }

        private void ApplyDogfightSettings(CameraToolsSettings settings)
        {
            // Use API to set dogfight configuration (replaces individual reflection calls)
            CameraToolsAPIManager.SetDogfightConfig(
                distance: settings.DogfightDistance,
                offsetX: settings.DogfightOffsetX,
                offsetY: settings.DogfightOffsetY,
                chasePlane: settings.DogfightChasePlaneMode
            );

            // Use API to set dogfight target (replaces reflection)
            if (!string.IsNullOrEmpty(settings.DogfightTargetId))
            {
                var target = FlightGlobals.Vessels.FirstOrDefault(v => v.id.ToString() == settings.DogfightTargetId);
                CameraToolsAPIManager.SetDogfightTarget(target);
            }
            else
            {
                CameraToolsAPIManager.SetDogfightTarget(null);
            }
        }

        private void ApplyStationarySettings(CameraToolsSettings settings)
        {
            Vessel currentVessel = FlightGlobals.ActiveVessel;
            if (currentVessel == null) return;

            // Use API to set positioning mode flags (replaces reflection)
            CameraToolsAPIManager.SetStationaryFlags(
                presetOffset: settings.UseGeographicPosition || settings.ManualOffset,
                autoFlyby: settings.AutoFlybyPosition,
                autoLanding: settings.AutoLandingPosition,
                manualOffset: settings.ManualOffset
            );

            // Use API to set manual offset values (replaces reflection)
            if (settings.ManualOffset)
            {
                CameraToolsAPIManager.SetManualOffset(
                    settings.ManualOffsetForward,
                    settings.ManualOffsetRight,
                    settings.ManualOffsetUp
                );
            }

            // Use API to set stationary advanced options (replaces reflection)
            CameraToolsAPIManager.SetStationaryAdvanced(
                saveRot: settings.SaveRotation,
                maintainVel: settings.MaintainInitialVelocity,
                useOrb: settings.UseOrbital,
                autoZoom: settings.AutoZoom
            );

            // Set position via API (existing)
            if (settings.UseGeographicPosition)
            {
                var body = GeographicCoordinateSystem.ResolveBody(settings.BodyName);
                Vector3 restoredWorldPos = GeographicCoordinateSystem.GetWorldPosition(body, settings.Latitude, settings.Longitude, settings.Altitude);
                Vector3 targetOffset = restoredWorldPos - currentVessel.CoM;
                CameraToolsAPIManager.SetStationaryPosition(targetOffset, null);
            }
            else if (settings.ManualOffset)
            {
                Vector3 forward = currentVessel.transform.forward * settings.ManualOffsetForward;
                Vector3 right = currentVessel.transform.right * settings.ManualOffsetRight;
                Vector3 up = currentVessel.transform.up * settings.ManualOffsetUp;
                Vector3 offsetPos = forward + right + up;
                CameraToolsAPIManager.SetStationaryPosition(offsetPos, null);
            }
            // else: AutoFlyby/AutoLanding - CameraTools calculates position internally, flags already set above

            // Apply target tracking via API (replaces reflection)
            ApplyTargetState(settings, currentVessel);

            // Pivot mode still requires reflection (no API yet)
            var pivotModeValue = CameraToolsReflectionProvider.ConvertToCameraToolsFMPivotMode(settings.FmPivotMode);
            if (pivotModeValue != null)
                CameraToolsReflectionProvider.SetField(CameraToolsReflectionProvider.FmPivotModeField, pivotModeValue);

            // Initial velocity still requires reflection (no API yet)
            if (settings.MaintainInitialVelocity && settings.InitialVelocity != Vector3.zero)
                CameraToolsReflectionProvider.SetVector3(CameraToolsReflectionProvider.InitialVelocityField, settings.InitialVelocity);

            // FOV is handled by SetExternalFOV in the main ApplySettings method, but set manualFOV field for persistence
            if (settings.ManualFOV > 0)
            {
                CameraToolsReflectionProvider.ManualFOV = settings.ManualFOV;
            }
        }

        private void ApplyPathingSettings(CameraToolsSettings settings)
        {
            UnityEngine.Debug.Log($"[ApplyPathingSettings] Configuring path {settings.SelectedPathIndex} with playback timing: {settings.LockPathingToPlaybackRate}");

            if (settings.SelectedPathIndex < 0)
            {
                UnityEngine.Debug.LogError($"[CameraSettingsRepository] Cannot apply pathing - invalid path index {settings.SelectedPathIndex}");
                return;
            }

            if (!CameraToolsAPIManager.PathExists(settings.SelectedPathIndex))
            {
                UnityEngine.Debug.LogError($"[CameraSettingsRepository] Path index {settings.SelectedPathIndex} no longer exists in CameraTools");
                return;
            }

            // Use API to select path
            CameraToolsAPIManager.SelectPath(settings.SelectedPathIndex);

            // Use API to set playback timing
            CameraToolsAPIManager.SetLockPathingToPlaybackRate(settings.LockPathingToPlaybackRate);

            // Use API to set path state (replaces individual reflection calls)
            CameraToolsAPIManager.SetPathState(
                pathIndex: settings.SelectedPathIndex,
                keyframeIndex: settings.CurrentKeyframeIndex >= 0 ? settings.CurrentKeyframeIndex : 0,
                isPlaying: settings.IsPlayingPath,
                startTime: settings.PathStartTime
            );

            // Use API to set path timing options (replaces reflection)
            CameraToolsAPIManager.SetPathTiming(
                useRealTime: settings.UseRealTime,
                smoothing: settings.PathingSecondarySmoothing
            );

            // Path time scale still requires reflection (per-path object access)
            CameraToolsReflectionProvider.ApplyPathTimeScale(settings.SelectedPathIndex, settings.PathTimeScale);

            UnityEngine.Debug.Log("[ApplyPathingSettings] Pathing configuration applied via API");
        }

        private void ApplyTargetState(CameraToolsSettings settings, Vessel currentVessel)
        {
            if (!settings.HasTarget)
            {
                // Use API to clear target
                CameraToolsAPIManager.SetTarget(null, false);
                return;
            }

            Part targetPart = ResolveTargetPart(settings, currentVessel);

            // Use API to set target (sets both camTarget and hasTarget internally)
            CameraToolsAPIManager.SetTarget(targetPart, settings.TargetCoM);

            if (targetPart == null)
            {
                // Fallback: ensure hasTarget is false if resolution failed
                CameraToolsReflectionProvider.HasTarget = false;
            }
        }

        private Part ResolveTargetPart(CameraToolsSettings settings, Vessel currentVessel)
        {
            if (settings.TargetSelf && currentVessel != null)
                return currentVessel.GetReferenceTransformPart() ?? currentVessel.rootPart;

            if (settings.TargetPartPersistentId != 0 && currentVessel != null)
                return currentVessel.Parts.FirstOrDefault(p => p.persistentId == settings.TargetPartPersistentId)
                    ?? currentVessel.GetReferenceTransformPart()
                    ?? currentVessel.rootPart;

            return currentVessel?.GetReferenceTransformPart() ?? currentVessel?.rootPart;
        }
    }
}