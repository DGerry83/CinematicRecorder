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
                        ManualPosition = slot.ctSettings.ManualPosition,
                        AutoZoom = slot.ctSettings.AutoZoom,
                        ManualFOV = slot.ctSettings.ManualFOV,
                        AutoFlybyPosition = slot.ctSettings.AutoFlybyPosition,
                        ManualOffset = slot.ctSettings.ManualOffset,
                        ManualOffsetForward = slot.ctSettings.ManualOffsetForward,
                        ManualOffsetRight = slot.ctSettings.ManualOffsetRight,
                        ManualOffsetUp = slot.ctSettings.ManualOffsetUp,
                        AutoLandingPosition = slot.ctSettings.AutoLandingPosition,
                        UsePresetOffset = slot.ctSettings.UsePresetOffset,
                        PresetOffset = slot.ctSettings.PresetOffset,
                        HasTarget = slot.ctSettings.HasTarget,
                        TargetPartPersistentId = slot.ctSettings.TargetPartPersistentId,
                        TargetCoM = slot.ctSettings.TargetCoM,
                        MaintainInitialVelocity = slot.ctSettings.MaintainInitialVelocity,
                        UseOrbital = slot.ctSettings.UseOrbital,
                        SelectedPathIndex = slot.ctSettings.SelectedPathIndex,
                        PathTimeScale = slot.ctSettings.PathTimeScale,
                        CurrentKeyframeIndex = slot.ctSettings.CurrentKeyframeIndex,
                        IsPlayingPath = slot.ctSettings.IsPlayingPath,
                        UseRealTime = slot.ctSettings.UseRealTime,
                        PathStartTime = slot.ctSettings.PathStartTime,
                        TargetSelf = slot.ctSettings.TargetSelf
                    } : null
                });
            }

            return clone;
        }
    }
}