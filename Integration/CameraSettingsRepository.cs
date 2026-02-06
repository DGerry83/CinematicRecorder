using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CinematicRecorder.Integration
{
    /// <summary>
    /// Handles CameraToolsSettings DTO <-> CameraTools object mapping.
    /// Separates serialization concerns from runtime application.
    /// </summary>
    public class CameraSettingsRepository
    {
        public CameraToolsSettings CaptureSettings()
        {
            if (!CameraToolsReflectionProvider.IsAvailable) return null;

            var settings = new CameraToolsSettings
            {
                Mode = CameraToolsReflectionProvider.ConvertToLocalToolModes(
                    CameraToolsReflectionProvider.GetField<object>(CameraToolsReflectionProvider.ToolModeField))
            };

            try
            {
                switch (settings.Mode)
                {
                    case ToolModes.DogfightCamera:
                        CaptureDogfightSettings(settings);
                        break;
                    case ToolModes.StationaryCamera:
                        CaptureStationarySettings(settings);
                        break;
                    case ToolModes.Pathing:
                        CapturePathingSettings(settings);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraSettingsRepository] Capture failed: {ex}");
                return null;
            }

            return settings;
        }

        public void ApplySettings(CameraToolsSettings settings)
        {
            if (!CameraToolsReflectionProvider.IsAvailable || settings == null) return;

            try
            {
                // Set mode first
                var enumValue = CameraToolsReflectionProvider.ConvertToCameraToolsToolModes(settings.Mode);
                if (enumValue != null)
                    CameraToolsReflectionProvider.SetField(CameraToolsReflectionProvider.ToolModeField, enumValue);

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
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraSettingsRepository] Apply failed: {ex}");
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
            settings.ManualFOV = CameraToolsReflectionProvider.GetFloat(CameraToolsReflectionProvider.ManualFOVField, 60f);

            // Target tracking
            CaptureTargetTrackingState(settings, currentVessel);
        }

        private void CaptureTargetTrackingState(CameraToolsSettings settings, Vessel currentVessel)
        {
            settings.HasTarget = CameraToolsReflectionProvider.GetBool(CameraToolsReflectionProvider.HasTargetField, false);
            settings.TargetCoM = CameraToolsReflectionProvider.GetBool(CameraToolsReflectionProvider.TargetCoMField, false);

            Part camTarget = CameraToolsReflectionProvider.GetReference<Part>(CameraToolsReflectionProvider.CamTargetField);
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
            settings.IsPlayingPath = CameraToolsReflectionProvider.GetBool(CameraToolsReflectionProvider.IsPlayingPathField, false);
            settings.UseRealTime = CameraToolsReflectionProvider.GetBool(CameraToolsReflectionProvider.UseRealTimeField, true);
            settings.PathStartTime = CameraToolsReflectionProvider.GetFloat(CameraToolsReflectionProvider.PathStartTimeField, 0f);
            settings.PathingSecondarySmoothing = CameraToolsReflectionProvider.GetFloat(CameraToolsReflectionProvider.PathingSecondarySmoothingField, 0f);
            settings.PathTimeScale = CameraToolsReflectionProvider.ExtractPathTimeScale(settings.SelectedPathIndex);

            if (!CameraToolsReflectionProvider.PathExists(settings.SelectedPathIndex))
                settings.SelectedPathIndex = -1;
        }

        private void ApplyDogfightSettings(CameraToolsSettings settings)
        {
            if (settings.DogfightDistance > 0)
                CameraToolsReflectionProvider.SetFloat(CameraToolsReflectionProvider.DogfightDistanceField, settings.DogfightDistance);

            CameraToolsReflectionProvider.SetFloat(CameraToolsReflectionProvider.DogfightOffsetXField, settings.DogfightOffsetX);
            CameraToolsReflectionProvider.SetFloat(CameraToolsReflectionProvider.DogfightOffsetYField, settings.DogfightOffsetY);
            CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.DogfightChasePlaneModeField, settings.DogfightChasePlaneMode);

            if (!string.IsNullOrEmpty(settings.DogfightTargetId))
            {
                var target = FlightGlobals.Vessels.FirstOrDefault(v => v.id.ToString() == settings.DogfightTargetId);
                CameraToolsReflectionProvider.SetReference(CameraToolsReflectionProvider.DogfightTargetField, target);
            }
            else
            {
                CameraToolsReflectionProvider.SetReference<Vessel>(CameraToolsReflectionProvider.DogfightTargetField, null);
            }
        }

        private void ApplyStationarySettings(CameraToolsSettings settings)
        {
            Vessel currentVessel = FlightGlobals.ActiveVessel;
            if (currentVessel == null) return;

            // Reset positioning flags
            CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.AutoFlybyPositionField, false);
            CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.AutoLandingPositionField, false);
            CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.ManualOffsetField, false);

            // Apply common settings
            var pivotModeValue = CameraToolsReflectionProvider.ConvertToCameraToolsFMPivotMode(settings.FmPivotMode);
            if (pivotModeValue != null)
                CameraToolsReflectionProvider.SetField(CameraToolsReflectionProvider.FmPivotModeField, pivotModeValue);

            ApplyTargetState(settings, currentVessel);

            CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.MaintainInitialVelocityField, settings.MaintainInitialVelocity);
            CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.UseOrbitalField, settings.UseOrbital);
            CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.AutoZoomStationaryField, settings.AutoZoom);
            CameraToolsReflectionProvider.SetFloat(CameraToolsReflectionProvider.ManualFOVField, settings.ManualFOV);

            if (settings.MaintainInitialVelocity && settings.InitialVelocity != Vector3.zero)
                CameraToolsReflectionProvider.SetVector3(CameraToolsReflectionProvider.InitialVelocityField, settings.InitialVelocity);

            // Positioning modes
            if (settings.UseGeographicPosition)
            {
                var body = GeographicCoordinateSystem.ResolveBody(settings.BodyName);
                Vector3 restoredWorldPos = GeographicCoordinateSystem.GetWorldPosition(body, settings.Latitude, settings.Longitude, settings.Altitude);

                CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.SetPresetOffsetField, true);
                CameraToolsReflectionProvider.SetVector3(CameraToolsReflectionProvider.PresetOffsetField, restoredWorldPos);
            }
            else if (settings.ManualOffset)
            {
                CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.ManualOffsetField, true);
                CameraToolsReflectionProvider.SetFloat(CameraToolsReflectionProvider.ManualOffsetForwardField, settings.ManualOffsetForward);
                CameraToolsReflectionProvider.SetFloat(CameraToolsReflectionProvider.ManualOffsetRightField, settings.ManualOffsetRight);
                CameraToolsReflectionProvider.SetFloat(CameraToolsReflectionProvider.ManualOffsetUpField, settings.ManualOffsetUp);
            }
            else if (settings.AutoFlybyPosition || settings.AutoLandingPosition)
            {
                CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.AutoFlybyPositionField, settings.AutoFlybyPosition);
                CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.AutoLandingPositionField, settings.AutoLandingPosition);
            }
        }

        private void ApplyPathingSettings(CameraToolsSettings settings)
        {
            if (!CameraToolsReflectionProvider.PathExists(settings.SelectedPathIndex))
            {
                Debug.LogError($"[CameraSettingsRepository] Path index {settings.SelectedPathIndex} invalid");
                return;
            }

            CameraToolsReflectionProvider.SetInt(CameraToolsReflectionProvider.SelectedPathIndexField, settings.SelectedPathIndex);
            CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.UseRealTimeField, settings.UseRealTime);
            CameraToolsReflectionProvider.ApplyPathTimeScale(settings.SelectedPathIndex, settings.PathTimeScale);
            CameraToolsReflectionProvider.SetFloat(CameraToolsReflectionProvider.PathingSecondarySmoothingField, settings.PathingSecondarySmoothing);

            if (settings.CurrentKeyframeIndex >= 0)
                CameraToolsReflectionProvider.SetInt(CameraToolsReflectionProvider.CurrentKeyframeIndexField, settings.CurrentKeyframeIndex);

            CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.IsPlayingPathField, settings.IsPlayingPath);
            CameraToolsReflectionProvider.SetFloat(CameraToolsReflectionProvider.PathStartTimeField, settings.PathStartTime);
        }

        private void ApplyTargetState(CameraToolsSettings settings, Vessel currentVessel)
        {
            CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.HasTargetField, settings.HasTarget);
            CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.TargetCoMField, settings.TargetCoM);

            if (!settings.HasTarget)
            {
                CameraToolsReflectionProvider.SetReference<Part>(CameraToolsReflectionProvider.CamTargetField, null);
                return;
            }

            Part targetPart = ResolveTargetPart(settings, currentVessel);
            CameraToolsReflectionProvider.SetReference(CameraToolsReflectionProvider.CamTargetField, targetPart);

            if (targetPart == null)
                CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.HasTargetField, false);
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