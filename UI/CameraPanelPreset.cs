using System;
using System.Collections.Generic;
using CinematicRecorder.Integration;

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
                    buttonID = $"Cam_{i}"
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
                        buttonID = $"Cam_{buttonAssignments.Count}"
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
                        buttonID = $"Cam_{buttonAssignments.Count}"
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
                presetName = this.presetName + " (Copy)",
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
                    allowAnyVessel = slot.allowAnyVessel
                });
            }

            return clone;
        }
    }
}