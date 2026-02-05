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
                var adapter = CameraToolsAdapter.Instance;
                if (!adapter.IsAvailable) return SlotStatus.Unavailable;
                if (ctSettings == null) return SlotStatus.Unassigned;

                // NEW: Explicit activation check (Step 4)
                // If this specific slot was explicitly activated, show Active state
                // as long as CameraTools is still active with the correct mode
                if (isExplicitlyActive && adapter.IsActive && adapter.CurrentMode == ctSettings.Mode)
                    return SlotStatus.Active;

                // Special check for Pathing - validate path still exists
                if (ctSettings.Mode == ToolModes.Pathing)
                {
                    if (!adapter.PathExists(ctSettings.SelectedPathIndex))
                        return SlotStatus.Unavailable;
                }

                if (!adapter.IsActive) return SlotStatus.Assigned;
                if (adapter.CurrentMode != ctSettings.Mode) return SlotStatus.Assigned;

                // Mode matches - check if ACTIVE by comparing key properties (not full capture)
                bool matches = false;
                switch (ctSettings.Mode)
                {
                    case ToolModes.StationaryCamera:
                        // Compare by checking if target resolution would yield same result
                        var camTarget = adapter.CamTarget;
                        bool targetMatches = false;
                        if (ctSettings.TargetSelf && camTarget != null && FlightGlobals.ActiveVessel != null)
                        {
                            targetMatches = camTarget.vessel == FlightGlobals.ActiveVessel;
                        }
                        else if (!ctSettings.TargetSelf && ctSettings.HasTarget && camTarget != null)
                        {
                            targetMatches = camTarget.persistentId == ctSettings.TargetPartPersistentId;
                        }
                        else if (!ctSettings.HasTarget && camTarget == null)
                        {
                            targetMatches = true;
                        }

                        // Check positioning mode matches
                        bool posModeMatches = ctSettings.UseGeographicPosition == adapter.UsePresetOffset &&
                                            ctSettings.AutoFlybyPosition == adapter.AutoFlybyPosition &&
                                            ctSettings.ManualOffset == adapter.ManualOffset;

                        bool posMatches = false;
                        if (ctSettings.UseGeographicPosition)
                        {
                            // For geographic mode, check if we're using preset offset (which indicates geographic restoration is active)
                            // Since we can't easily compare the exact world position without calculating it,
                            // we just check that the adapter is in preset mode (indicating geographic coordinates were applied)
                            posMatches = adapter.UsePresetOffset;
                        }
                        else if (ctSettings.ManualOffset)
                        {
                            // For manual offset mode, compare offset values
                            posMatches = Mathf.Abs(ctSettings.ManualOffsetForward - adapter.ManualOffsetForward) < 1.0f &&
                                       Mathf.Abs(ctSettings.ManualOffsetRight - adapter.ManualOffsetRight) < 1.0f &&
                                       Mathf.Abs(ctSettings.ManualOffsetUp - adapter.ManualOffsetUp) < 1.0f;
                        }
                        else
                        {
                            // Auto modes - just check the flag is enough
                            posMatches = true;
                        }

                        matches = targetMatches && posModeMatches && posMatches;
                        break;

                    case ToolModes.DogfightCamera:
                        matches = Mathf.Abs(ctSettings.DogfightDistance - adapter.DogfightDistance) < 5.0f;
                        break;

                    case ToolModes.Pathing:
                        matches = ctSettings.SelectedPathIndex == adapter.SelectedPathIndex;
                        break;
                }

                return matches ? SlotStatus.Active : SlotStatus.Assigned;
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