using System;
using System.Reflection;
using UnityEngine;

namespace CinematicRecorder.Integration
{
    /// <summary>
    /// High-level operations for CameraTools camera control.
    /// Uses GeographicCoordinateSystem for position restoration and
    /// CameraSettingsRepository for state management.
    /// </summary>
    public class CameraToolsCameraController
    {
        private readonly CameraSettingsRepository _settingsRepo;

        // Deferred restoration state for PostActivationPositionFixup
        private Vector3 _pendingRestoredPosition;
        private CameraToolsSettings _pendingGeographicSettings;

        public CameraToolsCameraController()
        {
            _settingsRepo = new CameraSettingsRepository();
        }

        public bool IsAvailable => CameraToolsReflectionProvider.IsAvailable;

        public bool IsActive => CameraToolsReflectionProvider.GetBool(CameraToolsReflectionProvider.CameraToolActiveField, false);

        public ToolModes CurrentMode => CameraToolsReflectionProvider.ConvertToLocalToolModes(
            CameraToolsReflectionProvider.GetField<object>(CameraToolsReflectionProvider.ToolModeField));

        public float ManualFOV
        {
            get => CameraToolsReflectionProvider.GetFloat(CameraToolsReflectionProvider.ManualFOVField, 60f);
            set => CameraToolsReflectionProvider.SetFloat(CameraToolsReflectionProvider.ManualFOVField, value);
        }

        public Part CamTarget => CameraToolsReflectionProvider.GetReference<Part>(CameraToolsReflectionProvider.CamTargetField);

        public bool UsePresetOffset => CameraToolsReflectionProvider.GetBool(CameraToolsReflectionProvider.SetPresetOffsetField, false);

        #region Core Operations

        public float GetFloat(FieldInfo field, float defaultValue = 0f) =>
    CameraToolsReflectionProvider.GetFloat(field, defaultValue);

        public void SetFloat(FieldInfo field, float value) =>
            CameraToolsReflectionProvider.SetFloat(field, value);

        public bool GetBool(FieldInfo field, bool defaultValue = false) =>
            CameraToolsReflectionProvider.GetBool(field, defaultValue);

        public void SetBool(FieldInfo field, bool value) =>
            CameraToolsReflectionProvider.SetBool(field, value);

        public void Activate()
        {
            CameraToolsReflectionProvider.Activate();
        }

        public void Deactivate()
        {
            CameraToolsReflectionProvider.Revert();
        }

        public void ReleaseControlWithoutReverting()
        {
            if (IsActive)
                CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.CameraToolActiveField, false);
        }

        public void ApplyPreset(CameraToolsSettings settings)
        {
            if (!IsAvailable || settings == null) return;
            _settingsRepo.ApplySettings(settings);
        }

        public CameraToolsSettings CaptureCurrentSettings()
        {
            return _settingsRepo.CaptureSettings();
        }

        public void ActivateMode(ToolModes mode, CameraToolsSettings settings = null)
        {
            if (!IsAvailable) return;

            var enumValue = CameraToolsReflectionProvider.ConvertToCameraToolsToolModes(mode);
            if (enumValue != null)
                CameraToolsReflectionProvider.SetField(CameraToolsReflectionProvider.ToolModeField, enumValue);

            if (settings != null)
            {
                ApplyPreset(settings);

                // Store pending state for geographic fixup
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

            Activate();
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

        /// <summary>
        /// Phase 3: Post-activation position fixup to override terrain-corrupted coordinates.
        /// MUST be called on the next frame after CameraActivate() when UseGeographicPosition is true.
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
            var instance = CameraToolsReflectionProvider.GetFetchInstance();

            // Access cameraParent for manual override
            var cameraParent = CameraToolsReflectionProvider.GetReference<GameObject>(CameraToolsReflectionProvider.CameraParentField);

            // Phase 3: Override the terrain corruption
            CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.SetPresetOffsetField, false);
            CameraToolsReflectionProvider.SetVector3(CameraToolsReflectionProvider.ManualPositionField, targetOffset);

            if (cameraParent != null)
                cameraParent.transform.position = restoredWorldPos;

            // Force camera transform to mathematically correct position
            if (FlightCamera.fetch != null)
            {
                FlightCamera.fetch.transform.position = restoredWorldPos;
                if (FlightCamera.fetch.transform.parent != null)
                    FlightCamera.fetch.transform.localPosition = Vector3.zero;
            }

            // Reset lastVesselCoM to prevent drift correction jump
            if (CameraToolsReflectionProvider.LastVesselCoMField != null)
                CameraToolsReflectionProvider.LastVesselCoMField.SetValue(instance, currentVessel.CoM);

            // Apply auto-zoom immediately after position fixup
            if (_pendingGeographicSettings.UseConsistentAutoZoom)
            {
                float targetFOV = CalculateConsistentAutoZoom(currentVessel, restoredWorldPos, _pendingGeographicSettings.ZoomPadding);
                EnforceAutoZoomFOVImmediate(targetFOV);
            }
            else if (_pendingGeographicSettings.AutoZoom)
            {
                float targetFOV = CalculateAutoZoomFOV(_pendingGeographicSettings, currentVessel);
                EnforceAutoZoomFOVImmediate(targetFOV);
            }

            Debug.Log($"[CT-FIXUP] World: {restoredWorldPos}, Offset: {targetOffset}");
            ClearPendingRestoration();
        }

        #endregion

        #region Auto-Zoom Logic

        /// <summary>
        /// Calculates FOV using angular size formula for consistent framing.
        /// </summary>
        public float CalculateConsistentAutoZoom(Vessel vessel, Vector3 cameraPosition, float paddingMultiplier)
        {
            if (vessel == null) return 60f;

            float distance = Vector3.Distance(cameraPosition, vessel.CoM);
            if (distance < 0.01f) distance = 0.01f;

            float radius = CalculateVesselBoundingRadius(vessel);
            float fov = 2f * Mathf.Rad2Deg * Mathf.Atan((radius * paddingMultiplier) / distance);

            return Mathf.Clamp(fov, 2f, 120f);
        }

        /// <summary>
        /// Applies consistent auto-zoom settings every frame.
        /// </summary>
        public void ApplyConsistentAutoZoom(bool enable, float padding)
        {
            if (!IsAvailable) return;

            // Disable native auto-zoom when custom is active to prevent conflicts
            CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.AutoZoomStationaryField, !enable);

            if (enable && FlightGlobals.ActiveVessel != null && FlightCamera.fetch != null)
            {
                Vector3 camPos = FlightCamera.fetch.transform.position;
                float targetFov = CalculateConsistentAutoZoom(FlightGlobals.ActiveVessel, camPos, padding);

                // Set manualFOV (target value)
                CameraToolsReflectionProvider.SetFloat(CameraToolsReflectionProvider.ManualFOVField, targetFov);

                // Bypass CameraTools' 0.1f lerp by setting currentFOV directly
                CameraToolsReflectionProvider.SetFloat(CameraToolsReflectionProvider.CurrentFOVField, targetFov);

                // Apply immediately to FlightCamera
                FlightCamera.fetch.SetFoV(targetFov);
            }
        }

        /// <summary>
        /// Immediately snaps FOV to target value, bypassing CameraTools' 0.1f lerp.
        /// </summary>
        public void EnforceAutoZoomFOVImmediate(float targetFOV)
        {
            CameraToolsReflectionProvider.SetFloat(CameraToolsReflectionProvider.ManualFOVField, targetFOV);
            CameraToolsReflectionProvider.SetFloat(CameraToolsReflectionProvider.CurrentFOVField, targetFOV);
            FlightCamera.fetch?.SetFoV(targetFOV);
        }

        /// <summary>
        /// Legacy auto-zoom calculation using CameraTools empirical formula.
        /// </summary>
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

        private float CalculateVesselBoundingRadius(Vessel vessel)
        {
            if (vessel?.Parts == null || vessel.Parts.Count == 0) return 5f;

            float maxDistSq = 0f;
            Vector3 com = vessel.CoM;

            foreach (Part p in vessel.Parts)
            {
                if (p?.transform == null) continue;
                float distSq = (p.transform.position - com).sqrMagnitude;
                if (distSq > maxDistSq) maxDistSq = distSq;
            }

            return Mathf.Sqrt(maxDistSq);
        }

        #endregion

        #region Pathing Helpers

        public bool PathExists(int index) => CameraToolsReflectionProvider.PathExists(index);

        public int SelectedPathIndex => CameraToolsReflectionProvider.GetInt(CameraToolsReflectionProvider.SelectedPathIndexField, -1);

        #endregion
    }
}