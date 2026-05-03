using CinematicRecorder.Core;
using CinematicRecorder.UI;
using System;
using System.Linq;
using UnityEngine;

namespace CinematicRecorder.Integration
{
    /// <summary>
    /// High-level operations for CameraTools camera control.
    /// Zoom/FOV control has been moved to CameraToolsZoomController.
    /// This class handles: activation, mode switching, pathing, and geographic restoration.
    /// </summary>
    public class CameraToolsCameraController
    {
        #region Fields
        private readonly CameraSettingsRepository _settingsRepo;

        // Deferred restoration state for PostActivationPositionFixup
        private Vector3 _pendingRestoredPosition;
        private CameraToolsSettings _pendingGeographicSettings;
        #endregion
        #region Properties
        public bool IsAvailable => CameraToolsAPIManager.IsAvailable;
        public bool IsActive => CameraToolsAPIManager.IsCameraActive();
        public ToolModes CurrentMode => CameraToolsAPIManager.GetToolMode();
        public float ManualFOV => CameraToolsAPIManager.GetManualFOV();
        public float CurrentFOV => CameraToolsAPIManager.GetActualFOV();
        public Part CamTarget => CameraToolsReflectionProvider.CamTarget;
        public bool UsePresetOffset => CameraToolsReflectionProvider.SetPresetOffset;
        public int SelectedPathIndex => CameraToolsReflectionProvider.GetInt(CameraToolsReflectionProvider.SelectedPathIndexField, -1);
        #endregion
        #region Constructor
        public CameraToolsCameraController()
        {
            _settingsRepo = new CameraSettingsRepository();
        }
        #endregion
        #region Core Operations
        /// <summary>
        /// Sets the playback timing mode for pathing cameras.
        /// </summary>
        public void SetPlaybackTiming(bool usePlaybackTime)
        {
            if (!IsAvailable) return;
            CameraToolsAPIManager.SetLockPathingToPlaybackRate(usePlaybackTime);
        }

        /// <summary>
        /// Activates CameraTools using the new API.
        /// </summary>
        public void Activate()
        {
            CameraToolsAPIManager.ActivateCamera();
        }

        /// <summary>
        /// Deactivates CameraTools and returns to stock camera.
        /// </summary>
        public void Deactivate()
        {
            CameraToolsAPIManager.DeactivateCamera();
            ClearPendingRestoration();
        }

        /// <summary>
        /// Switches to a new CT mode without stock camera flicker.
        /// Use for CT→CT transitions instead of Deactivate+Activate.
        /// </summary>
        public void SwitchMode(ToolModes newMode, CameraToolsSettings settings = null)
        {
            if (!IsAvailable) return;

            // Apply new settings first
            if (settings != null)
            {
                bool useDeterministic = settings.UseDeterministicControl || DeterministicCaptureSession.IsRunning;
                CameraToolsAPIManager.SetCinematicRecorderControl(enabled: true, deterministicMode: useDeterministic);

                _settingsRepo.ApplySettings(settings, activateImmediately: false);

                // Store geographic settings for potential fixup
                if (settings.UseGeographicPosition)
                {
                    var body = GeographicCoordinateSystem.ResolveBody(settings.BodyName);
                    _pendingRestoredPosition = GeographicCoordinateSystem.GetWorldPosition(body, settings.Latitude, settings.Longitude, settings.Altitude);
                    _pendingGeographicSettings = settings;
                }
                else
                {
                    _pendingGeographicSettings = null;
                }
            }

            // Use SwitchCamera API for seamless transition
            CameraToolsAPIManager.SwitchCamera(newMode);

            if (_pendingGeographicSettings != null)
            {
                PostActivationPositionFixup();
            }

            if (newMode == ToolModes.Pathing && settings?.IsPlayingPath == true)
            {
                CameraToolsAPIManager.StartPathPlayback();
            }
        }

        /// <summary>
        /// Releases control without fully reverting (for camera switching).
        /// </summary>
        public void ReleaseControlWithoutReverting()
        {
            if (IsActive)
                CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.CameraToolActiveField, false);
        }

        /// <summary>
        /// Applies preset settings using the repository.
        /// </summary>
        public void ApplyPreset(CameraToolsSettings settings)
        {
            if (!IsAvailable || settings == null) return;
            _settingsRepo.ApplySettings(settings, activateImmediately: false);
        }

        /// <summary>
        /// Activates a specific camera mode with settings using the public API.
        /// </summary>
        public void ActivateMode(ToolModes mode, CameraToolsSettings settings = null)
        {
            if (!IsAvailable)
            {
                UnityEngine.Debug.LogWarning("[ActivateMode] CameraTools not available");
                return;
            }

            UnityEngine.Debug.Log($"[ActivateMode] Activating {mode} with settings: {settings?.GetDisplayName()}");

            // Determine deterministic mode
            bool useDeterministic = settings?.UseDeterministicControl ?? false;
            if (DeterministicCaptureSession.IsRunning)
            {
                useDeterministic = true;
            }

            CameraToolsAPIManager.SetCinematicRecorderControl(enabled: true, deterministicMode: useDeterministic);
            UnityEngine.Debug.Log($"[ActivateMode] CR Control enabled, deterministic: {useDeterministic}");

            CameraToolsAPIManager.SetToolMode(mode);
            UnityEngine.Debug.Log($"[ActivateMode] Mode set to {mode}");

            // Apply settings BEFORE activating camera
            if (settings != null)
            {
                _settingsRepo.ApplySettings(settings, activateImmediately: false);

                // Store geographic settings for fixup (do NOT clear here - let the camera call PostActivationPositionFixup)
                if (settings.UseGeographicPosition)
                {
                    var body = GeographicCoordinateSystem.ResolveBody(settings.BodyName);
                    _pendingRestoredPosition = GeographicCoordinateSystem.GetWorldPosition(body, settings.Latitude, settings.Longitude, settings.Altitude);
                    _pendingGeographicSettings = settings;
                }
            }

            UnityEngine.Debug.Log("[ActivateMode] Activating CameraTools camera");
            CameraToolsAPIManager.ActivateCamera();

            // Start path playback if in pathing mode
            if (mode == ToolModes.Pathing && settings?.IsPlayingPath == true)
            {
                UnityEngine.Debug.Log("[ActivateMode] Starting path playback");
                CameraToolsAPIManager.StartPathPlayback();
            }

            UnityEngine.Debug.Log("[ActivateMode] Activation complete");
        }

        /// <summary>
        /// Captures current CameraTools settings.
        /// </summary>
        public CameraToolsSettings CaptureCurrentSettings()
        {
            return _settingsRepo.CaptureSettings();
        }
        #endregion
        #region REMOVED: Rate-Based Zoom Section
        // REMOVED: ApplyRateStep()
        // REMOVED: UpdateRate()
        // REMOVED: DecayRateInput()
        // REMOVED: GetRateInput()
        // MOVED TO: CameraToolsZoomController
        #endregion
        #region REMOVED: Target-Based Zoom Section
        // REMOVED: UpdateTarget()
        // REMOVED: QueueTargetZoom()
        // REMOVED: QueueConsistentTransition()
        // REMOVED: CancelActiveZoom()
        // MOVED TO: CameraToolsZoomController
        #endregion
        #region REMOVED: Consistent Framing Section
        // REMOVED: ApplyConsistentFraming()
        // REMOVED: ApplyConsistentAutoZoom()
        // REMOVED: ResetZoom()
        // MOVED TO: CameraToolsZoomController
        #endregion
        #region Geographic Restoration
        public bool HasPendingGeographicRestoration() =>
            _pendingGeographicSettings != null && _pendingGeographicSettings.UseGeographicPosition;

        public void ClearPendingRestoration()
        {
            _pendingRestoredPosition = Vector3.zero;
            _pendingGeographicSettings = null;
        }

        /// <summary>
        /// Executes deferred position correction for geographic coordinates after activation
        /// </summary>
        public void PostActivationPositionFixup()
        {
            if (_pendingGeographicSettings == null || !IsActive) return;

            Vessel currentVessel = FlightGlobals.ActiveVessel;
            if (currentVessel == null)
            {
                Debug.LogWarning("[CT-FIXUP] Cannot fixup - null vessel");
                ClearPendingRestoration();
                return;
            }

            Vector3 restoredWorldPos = _pendingRestoredPosition;
            Vector3 targetOffset = restoredWorldPos - currentVessel.CoM;

            CameraToolsReflectionProvider.ManualPosition = targetOffset;

            var cameraParent = CameraToolsReflectionProvider.CameraParent;
            if (cameraParent != null)
                cameraParent.transform.position = restoredWorldPos;

            if (FlightCamera.fetch != null)
            {
                FlightCamera.fetch.transform.position = restoredWorldPos;
                if (FlightCamera.fetch.transform.parent != null)
                    FlightCamera.fetch.transform.localPosition = Vector3.zero;
            }

            CameraToolsReflectionProvider.LastVesselCoM = currentVessel.CoM;

            // Apply FOV if using auto-zoom modes
            if (_pendingGeographicSettings.UseConsistentAutoZoom)
            {
                float targetFOV = ZoomMathUtility.CalculateConsistentFramingFOV(
                    currentVessel, restoredWorldPos, _pendingGeographicSettings.ZoomPadding);
                CameraToolsAPIManager.SetExternalFOV(targetFOV);
            }
            else if (_pendingGeographicSettings.AutoZoom)
            {
                float targetFOV = CalculateAutoZoomFOV(_pendingGeographicSettings, currentVessel);
                CameraToolsAPIManager.SetExternalFOV(targetFOV);
            }

            Debug.Log($"[CT-FIXUP] World: {restoredWorldPos}, Offset: {targetOffset}");
            ClearPendingRestoration();
        }
        #endregion
        #region Helpers
        /// <summary>
        /// Applies FOV using the API for immediate effect.
        /// KEPT for geographic restoration use.
        /// </summary>
        private void ApplyFOV(float fov)
        {
            fov = Mathf.Clamp(fov, 2f, 120f);
            CameraToolsAPIManager.SetExternalFOV(fov);
        }

        /// <summary>
        /// KEPT for legacy native auto-zoom support in geographic restoration.
        /// </summary>
        private float CalculateAutoZoomFOV(CameraToolsSettings settings, Vessel vessel)
        {
            if (vessel == null) return 60f;

            Vector3 targetPos = (settings.HasTarget && !settings.TargetSelf)
                ? (CamTarget?.transform.position ?? vessel.CoM)
                : vessel.CoM;

            Vector3 cameraPos = Vector3.zero;
            if (FlightCamera.fetch != null)
                cameraPos = FlightCamera.fetch.transform.position;
            float distance = Vector3.Distance(targetPos, cameraPos);
            float margin = CameraToolsReflectionProvider.GetFloat(CameraToolsReflectionProvider.AutoZoomMarginStationaryField, 30f);

            float targetFoV = (7000f / (distance + 100f)) - 14f + margin;
            return Mathf.Clamp(targetFoV, 2f, 60f);
        }

        public bool PathExists(int index) => CameraToolsReflectionProvider.PathExists(index);

        /// <summary>
        /// Force resets CameraTools to a completely clean state.
        /// MODIFIED: Removed zoom-specific reset logic (handled by CameraToolsZoomController).
        /// </summary>
        public void ForceReset()
        {
            ClearPendingRestoration();

            if (IsActive)
            {
                CameraToolsAPIManager.DeactivateCamera();
            }
        }
        #endregion
    }
}