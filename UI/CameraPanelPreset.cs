using System;
using System.Collections.Generic;
using CinematicRecorder.Integration;
using static CinematicRecorder.UI.CinematicUIStrings;

namespace CinematicRecorder.UI
{
    /// <summary>
    /// Container for a complete camera panel state (one preset).
    /// Serializable for ConfigNode storage.
    /// </summary>
    [Serializable]
    public class CameraPanelPreset
    {
        public string presetName;
        public string vesselId; // "GLOBAL" or specific vessel GUID string
        public bool autoLoadForVessel; // Auto-switch when vessel changes

        // Window position (optional persistence)
        public float panelX;
        public float panelY;

        // 16 camera slots (4x4 grid)
        public List<CameraSlot> buttonAssignments;

        public CameraPanelPreset()
        {
            buttonAssignments = new List<CameraSlot>();
            // Initialize empty slots 0-15
            for (int i = 0; i < 16; i++)
            {
                buttonAssignments.Add(new CameraSlot
                {
                    buttonID = string.Format(CameraController.ButtonIdFormat, i)
                });
            }
        }

        public CameraSlot GetSlot(int index)
        {
            if (index < 0 || index >= 16) return null;
            if (buttonAssignments.Count <= index)
            {
                // Handle legacy saves with fewer slots
                while (buttonAssignments.Count <= index)
                {
                    buttonAssignments.Add(new CameraSlot
                    {
                        buttonID = string.Format(CameraController.ButtonIdFormat, buttonAssignments.Count)
                    });
                }
            }
            return buttonAssignments[index];
        }

        public void SetSlot(int index, CameraSlot slot)
        {
            if (index < 0 || index >= 16) return;
            if (buttonAssignments.Count <= index)
            {
                while (buttonAssignments.Count <= index)
                {
                    buttonAssignments.Add(new CameraSlot
                    {
                        buttonID = string.Format(CameraController.ButtonIdFormat, buttonAssignments.Count)
                    });
                }
            }
            buttonAssignments[index] = slot;
        }

        /// <summary>
        /// Creates a deep copy for "Save As" operations.
        /// </summary>
        public CameraPanelPreset Clone()
        {
            var clone = new CameraPanelPreset
            {
                presetName = this.presetName + CameraController.PresetCopySuffix,
                vesselId = this.vesselId,
                autoLoadForVessel = this.autoLoadForVessel,
                panelX = this.panelX,
                panelY = this.panelY
            };

            clone.buttonAssignments.Clear();
            foreach (var slot in this.buttonAssignments)
            {
                clone.buttonAssignments.Add(new CameraSlot
                {
                    buttonID = slot.buttonID,
                    cameraName = slot.cameraName,
                    partPersistentId = slot.partPersistentId,
                    vesselId = slot.vesselId,
                    allowAnyVessel = slot.allowAnyVessel,
                    isCameraToolsSlot = slot.isCameraToolsSlot,
                    ctSettings = slot.ctSettings != null ? new CameraToolsSettings
                    {
                        Mode = slot.ctSettings.Mode,
                        DogfightDistance = slot.ctSettings.DogfightDistance,
                        DogfightOffsetX = slot.ctSettings.DogfightOffsetX,
                        DogfightOffsetY = slot.ctSettings.DogfightOffsetY,
                        DogfightChasePlaneMode = slot.ctSettings.DogfightChasePlaneMode,
                        DogfightTargetId = slot.ctSettings.DogfightTargetId,

                        // Geographic positioning (NEW)
                        UseGeographicPosition = slot.ctSettings.UseGeographicPosition,
                        Latitude = slot.ctSettings.Latitude,
                        Longitude = slot.ctSettings.Longitude,
                        Altitude = slot.ctSettings.Altitude,
                        BodyName = slot.ctSettings.BodyName,

                        // Positioning modes
                        AutoFlybyPosition = slot.ctSettings.AutoFlybyPosition,
                        AutoLandingPosition = slot.ctSettings.AutoLandingPosition,
                        ManualOffset = slot.ctSettings.ManualOffset,
                        ManualOffsetForward = slot.ctSettings.ManualOffsetForward,
                        ManualOffsetRight = slot.ctSettings.ManualOffsetRight,
                        ManualOffsetUp = slot.ctSettings.ManualOffsetUp,

                        // Target tracking
                        HasTarget = slot.ctSettings.HasTarget,
                        TargetSelf = slot.ctSettings.TargetSelf,
                        TargetPartPersistentId = slot.ctSettings.TargetPartPersistentId,
                        TargetCoM = slot.ctSettings.TargetCoM,

                        // Camera settings
                        MaintainInitialVelocity = slot.ctSettings.MaintainInitialVelocity,
                        UseOrbital = slot.ctSettings.UseOrbital,
                        AutoZoom = slot.ctSettings.AutoZoom,
                        ManualFOV = slot.ctSettings.ManualFOV,
                        InitialVelocity = slot.ctSettings.InitialVelocity,

                        // Additional settings
                        SaveRotation = slot.ctSettings.SaveRotation,
                        FmPivotMode = slot.ctSettings.FmPivotMode,
                        PathingSecondarySmoothing = slot.ctSettings.PathingSecondarySmoothing,

                        // Pathing
                        SelectedPathIndex = slot.ctSettings.SelectedPathIndex,
                        PathTimeScale = slot.ctSettings.PathTimeScale,
                        CurrentKeyframeIndex = slot.ctSettings.CurrentKeyframeIndex,
                        IsPlayingPath = slot.ctSettings.IsPlayingPath,
                        UseRealTime = slot.ctSettings.UseRealTime,
                        PathStartTime = slot.ctSettings.PathStartTime
                    } : null
                });
            }

            return clone;
        }
    }
}