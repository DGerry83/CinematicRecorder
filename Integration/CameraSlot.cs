using System;
using UnityEngine;
using static CinematicRecorder.Integration.HullCamBridge;

namespace CinematicRecorder.Integration
{
    [Serializable]
    public class CameraSlot
    {
        public string buttonID;

        // HullCam specific
        public string cameraName;
        public uint partPersistentId;
        public string vesselId;
        public bool allowAnyVessel;

        // CameraTools specific
        public bool isCameraToolsSlot;
        public CameraToolsSettings ctSettings;

        public string GetDisplayName()
        {
            if (isCameraToolsSlot)
                return ctSettings?.GetDisplayName() ?? "CameraTools";
            return cameraName ?? "Unknown";
        }

        public SlotStatus GetStatus(Vessel currentVessel = null)
        {
            if (isCameraToolsSlot)
            {
                if (!CameraToolsBridge.IsAvailable) return SlotStatus.Unavailable;
                if (ctSettings == null) return SlotStatus.Unassigned;

                // Special check for Pathing - validate path still exists
                if (ctSettings.Mode == ToolModes.Pathing)
                {
                    if (!CameraToolsBridge.PathExists(ctSettings.SelectedPathIndex))
                        return SlotStatus.Unavailable; // Red - path was deleted
                }

                if (!CameraToolsBridge.IsActive()) return SlotStatus.Assigned;
                if (CameraToolsBridge.GetCurrentMode() != ctSettings.Mode) return SlotStatus.Assigned;

                // Mode matches, check if settings match
                var current = CameraToolsBridge.CaptureCurrentSettings();
                if (current != null && ctSettings.ApproximatelyMatches(current))
                    return SlotStatus.Active;

                return SlotStatus.Assigned;
            }

            // Existing HullCam handling
            if (!IsAvailable)
                return SlotStatus.Unavailable;

            if (partPersistentId == 0 && string.IsNullOrEmpty(cameraName))
                return SlotStatus.Unassigned;

            var cam = ResolveCameraSlot(this, currentVessel);

            if (cam == null)
                return SlotStatus.Unavailable;

            if (!IsCameraAvailable(cam))
                return SlotStatus.Unavailable;

            if (IsCameraActive(cam))
                return SlotStatus.Active;

            if (currentVessel != null)
            {
                Component comp = cam as Component;
                if (comp != null)
                {
                    Part part = comp.GetComponentInParent<Part>();
                    if (part != null && part.vessel != null && part.vessel != currentVessel)
                        return SlotStatus.Remote;
                }
            }

            return SlotStatus.Assigned;
        }

        public enum SlotStatus
        {
            Unassigned,
            Assigned,
            Active,
            Remote,
            Unavailable
        }
    }
}