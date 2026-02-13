using CinematicRecorder.Core;
using CinematicRecorder.UI;
using System;
using System.Linq;
using UnityEngine;

namespace CinematicRecorder.Integration
{
    /// <summary>
    /// High-level operations for CameraTools camera control with unified zoom handling.
    /// Uses CameraToolsAPIManager for configuration and reflection provider for state queries.
    /// </summary>
    public class CameraToolsCameraController
    {
        private readonly CameraSettingsRepository _settingsRepo;

        // Zoom state
        private IZoomStrategy _currentStrategy;
        private float _rateInput;
        private float _manualTargetFOV = 60f;
        private bool _useConsistentAutoZoom;
        private float _consistentZoomPadding = 1.5f;
        private bool _enableConsistentOnComplete;
        private float _rateControlCurrentFOV = 60f;

        // Deferred restoration state for PostActivationPositionFixup
        private Vector3 _pendingRestoredPosition;
        private CameraToolsSettings _pendingGeographicSettings;

        public CameraToolsCameraController()
        {
            _settingsRepo = new CameraSettingsRepository();
        }

        #region State Properties
        public bool IsAvailable => CameraToolsAPIManager.IsAvailable;

        public bool IsActive => CameraToolsAPIManager.IsCameraActive();

        public ToolModes CurrentMode => CameraToolsAPIManager.GetToolMode();

        public float ManualFOV => CameraToolsAPIManager.GetManualFOV();

        public float CurrentFOV => CameraToolsAPIManager.GetActualFOV();

        public bool UseConsistentAutoZoom
        {
            get => _useConsistentAutoZoom;
            set
            {
                _useConsistentAutoZoom = value;
                if (value) CancelActiveZoom();
            }
        }

        public float ConsistentZoomPadding
        {
            get => _consistentZoomPadding;
            set => _consistentZoomPadding = value;
        }

        public Part CamTarget => CameraToolsReflectionProvider.CamTarget;

        public bool UsePresetOffset => CameraToolsReflectionProvider.SetPresetOffset;
        #endregion

        public bool HasActiveStrategy => _currentStrategy != null;

        #region Rate-Based Update

        /// <summary>
        /// Applies a single rate-based zoom step. Used by both real-time and deterministic modes.
        /// </summary>
        public void ApplyRateStep(float intent, float deltaTime)
        {
            if (!IsActive) return;

            // Initialize accumulation on first call
            if (_currentStrategy == null || !(_currentStrategy is RateBasedZoomStrategy))
            {
                _currentStrategy = new RateBasedZoomStrategy(CinematicUIResources.Layout.Zoom.MAX_SPEED);
                _rateControlCurrentFOV = CameraToolsReflectionProvider.CurrentFOV;
            }

            if (_currentStrategy is RateBasedZoomStrategy rateStrategy)
            {
                rateStrategy.SetInput(intent);
                float newFOV = rateStrategy.GetTargetFOV(_rateControlCurrentFOV, deltaTime);

                // Use API for immediate FOV application
                CameraToolsAPIManager.SetExternalFOV(newFOV);
                _rateControlCurrentFOV = newFOV;
            }
        }

        public void UpdateRate(float intent)
        {
            ApplyRateStep(intent, Time.deltaTime);
        }

        /// <summary>
        /// Decays rate input for elastic behavior.
        /// </summary>
        public void DecayRateInput(float deltaTime)
        {
            if (!Input.GetMouseButton(0))
            {
                _rateInput = Mathf.MoveTowards(_rateInput, 0f,
                    deltaTime * CinematicUIResources.Layout.Zoom.RETURN_SPEED);
            }
        }

        /// <summary>
        /// Gets current rate input value (for UI display).
        /// </summary>
        public float GetRateInput() => _rateInput;
        #endregion

        #region Target-Based Update
        /// <summary>
        /// Updates target-based zoom. Call this every frame in Target mode.
        /// </summary>
        public void UpdateTarget()
        {
            if (!IsActive) return;
            if (_currentStrategy == null) return;

            float newFOV = _currentStrategy.GetTargetFOV(CurrentFOV, Time.deltaTime);

            // Use API for immediate FOV application
            CameraToolsAPIManager.SetExternalFOV(newFOV);

            if (_currentStrategy.IsComplete)
            {
                // Handoff to consistent framing if applicable
                if (_currentStrategy is ConsistentFramingTransitionStrategy && _enableConsistentOnComplete)
                {
                    UseConsistentAutoZoom = true;
                    _enableConsistentOnComplete = false;
                }
                _currentStrategy = null;
            }
        }

        /// <summary>
        /// Queues a target zoom. Zero duration = instant.
        /// </summary>
        public void QueueTargetZoom(float targetFOV, float duration, ZoomCurve curve)
        {
            CancelActiveZoom();
            _manualTargetFOV = targetFOV;

            if (duration < 0.001f)
            {
                _currentStrategy = new InstantZoomStrategy(targetFOV);
                // Execute immediately since UpdateTarget might not be called this frame
                CameraToolsAPIManager.SetExternalFOV(targetFOV);
            }
            else
            {
                _currentStrategy = new TargetBasedZoomStrategy(targetFOV, duration, curve);
            }
        }

        /// <summary>
        /// Queues transition to consistent framing FOV.
        /// </summary>
        public void QueueConsistentTransition(float duration, ZoomCurve curve)
        {
            CancelActiveZoom();

            if (duration < 0.001f)
            {
                UseConsistentAutoZoom = true;
                ApplyConsistentFraming();
                return;
            }

            _currentStrategy = new ConsistentFramingTransitionStrategy(
                CurrentFOV, duration, curve, _consistentZoomPadding);
            _enableConsistentOnComplete = true;
        }

        /// <summary>
        /// Cancels any active zoom strategy.
        /// </summary>
        public void CancelActiveZoom()
        {
            _currentStrategy = null;
            _enableConsistentOnComplete = false;
        }
        #endregion

        #region Consistent Framing
        /// <summary>
        /// Applies consistent auto-zoom settings every frame when enabled.
        /// Bypasses any active zoom strategies.
        /// </summary>
        public void ApplyConsistentFraming()
        {
            if (!IsAvailable || !IsActive) return;

            // Disable native auto-zoom when custom is active to prevent conflicts
            CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.AutoZoomStationaryField, false);

            if (FlightGlobals.ActiveVessel != null && FlightCamera.fetch != null)
            {
                Vector3 camPos = FlightCamera.fetch.transform.position;
                float targetFov = ZoomMathUtility.CalculateConsistentFramingFOV(
                    FlightGlobals.ActiveVessel, camPos, _consistentZoomPadding);

                // Clamp to camera bounds
                targetFov = Mathf.Clamp(targetFov, 2f, 120f);

                // Use API for immediate application
                CameraToolsAPIManager.SetExternalFOV(targetFov);
            }
        }

        /// <summary>
        /// Legacy support - immediately applies consistent framing every frame.
        /// </summary>
        public void ApplyConsistentAutoZoom(bool enable, float padding)
        {
            UseConsistentAutoZoom = enable;
            ConsistentZoomPadding = padding;
            if (enable)
            {
                ApplyConsistentFraming();
            }
        }

        /// <summary>
        /// Resets zoom to maximum FOV instantly.
        /// </summary>
        public void ResetZoom()
        {
            CancelActiveZoom();
            UseConsistentAutoZoom = false;
            CameraToolsAPIManager.SetExternalFOV(60f); // "Normal" FOV
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
            // Use the new API method which properly validates camera parenting
            CameraToolsAPIManager.DeactivateCamera();

            // Clear local state
            CancelActiveZoom();
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
                // Determine deterministic mode
                bool useDeterministic = settings.UseDeterministicControl || DeterministicCaptureSession.IsRunning;
                CameraToolsAPIManager.SetCinematicRecorderControl(enabled: true, deterministicMode: useDeterministic);

                // Apply mode-specific settings
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

            // Execute position fixup immediately for geographic cameras
            // This sets FlightCamera position directly and updates LastVesselCoM
            if (_pendingGeographicSettings != null)
            {
                PostActivationPositionFixup();
            }

            // Start path playback if needed
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

            // Enable CR control mode first (immediate FOV, no smoothing)
            CameraToolsAPIManager.SetCinematicRecorderControl(enabled: true, deterministicMode: useDeterministic);
            UnityEngine.Debug.Log($"[ActivateMode] CR Control enabled, deterministic: {useDeterministic}");

            // Set mode first
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

            // NOW activate camera with settings configured
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

        #region Geographic Restoration
        public bool HasPendingGeographicRestoration() =>
            _pendingGeographicSettings != null && _pendingGeographicSettings.UseGeographicPosition;

        public void ClearPendingRestoration()
        {
            _pendingRestoredPosition = Vector3.zero;
            _pendingGeographicSettings = null;
        }

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

            // Use direct field access (now public in API v2.0+)
            CameraToolsReflectionProvider.ManualPosition = targetOffset;

            // Update camera parent if available
            var cameraParent = CameraToolsReflectionProvider.CameraParent;
            if (cameraParent != null)
                cameraParent.transform.position = restoredWorldPos;

            // Update FlightCamera directly
            if (FlightCamera.fetch != null)
            {
                FlightCamera.fetch.transform.position = restoredWorldPos;
                if (FlightCamera.fetch.transform.parent != null)
                    FlightCamera.fetch.transform.localPosition = Vector3.zero;
            }

            // Update lastVesselCoM (now public field)
            CameraToolsReflectionProvider.LastVesselCoM = currentVessel.CoM;

            // Apply zoom if needed
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
        /// </summary>
        private void ApplyFOV(float fov)
        {
            // Clamp to CT limits
            fov = Mathf.Clamp(fov, 2f, 120f);
            CameraToolsAPIManager.SetExternalFOV(fov);
        }

        public void EnforceAutoZoomFOVImmediate(float targetFOV)
        {
            ApplyFOV(targetFOV);
        }

        /// <summary>
        /// Force resets CameraTools to a completely clean state.
        /// Use when returning to main or when state corruption is detected.
        /// </summary>
        public void ForceReset()
        {
            // Clear all internal state
            CancelActiveZoom();
            ClearPendingRestoration();
            _useConsistentAutoZoom = false;
            _rateInput = 0f;
            _currentStrategy = null;

            // Ensure CameraTools is deactivated via API
            if (IsActive)
            {
                CameraToolsAPIManager.DeactivateCamera();
            }
        }

        private float CalculateAutoZoomFOV(CameraToolsSettings settings, Vessel vessel)
        {
            if (vessel == null) return 60f;

            Vector3 targetPos = (settings.HasTarget && !settings.TargetSelf)
                ? (CamTarget?.transform.position ?? vessel.CoM)
                : vessel.CoM;

            Vector3 cameraPos = FlightCamera.fetch?.transform.position ?? Vector3.zero;
            float distance = Vector3.Distance(targetPos, cameraPos);
            float margin = CameraToolsReflectionProvider.GetFloat(CameraToolsReflectionProvider.AutoZoomMarginStationaryField, 30f);

            float targetFoV = (7000f / (distance + 100f)) - 14f + margin;
            return Mathf.Clamp(targetFoV, 2f, 60f);
        }

        public float CalculateConsistentAutoZoom(Vessel vessel, Vector3 cameraPosition, float paddingMultiplier)
        {
            return ZoomMathUtility.CalculateConsistentFramingFOV(vessel, cameraPosition, paddingMultiplier);
        }

        public bool PathExists(int index) => CameraToolsReflectionProvider.PathExists(index);

        public int SelectedPathIndex => CameraToolsReflectionProvider.GetInt(CameraToolsReflectionProvider.SelectedPathIndexField, -1);
        #endregion
    }
}