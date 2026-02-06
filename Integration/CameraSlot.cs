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

        public SlotStatus GetStatus(Vessel currentVessel = null, bool isExplicitlyActive = false)
        {
            if (isCameraToolsSlot)
            {
                var controller = new CameraToolsCameraController();
                if (!controller.IsAvailable) return SlotStatus.Unavailable;
                if (ctSettings == null) return SlotStatus.Unassigned;

                // CRITICAL FIX: Only THIS specific slot is "Active" if it's the explicitly selected one
                // CameraTools is mutually exclusive - only one slot can be active at a time
                if (isExplicitlyActive && controller.IsActive)
                {
                    // Verify the active camera actually matches this slot's mode
                    if (controller.CurrentMode == ctSettings.Mode)
                        return SlotStatus.Active;
                }

                // If CT is active but this isn't the explicit slot, show as Assigned (not Active)
                // regardless of whether settings match
                if (controller.IsActive)
                {
                    // Special check for Pathing - validate path still exists
                    if (ctSettings.Mode == ToolModes.Pathing)
                    {
                        if (!controller.PathExists(ctSettings.SelectedPathIndex))
                            return SlotStatus.Unavailable;
                    }

                    // Show as assigned (yellow) even if settings match - only explicit slot is active
                    return SlotStatus.Assigned;
                }

                // Pathing validation when not active
                if (ctSettings.Mode == ToolModes.Pathing)
                {
                    if (!controller.PathExists(ctSettings.SelectedPathIndex))
                        return SlotStatus.Unavailable;
                }

                return SlotStatus.Assigned;
            }

            // HullCam handling
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