using System;
using UnityEngine;
using static CinematicRecorder.Integration.HullCamBridge;

namespace CinematicRecorder.Integration
{
    [Serializable]
    public class CameraSlot
    {
        #region Fields
        public string buttonID;

        // HullCam specific
        public string cameraName;
        public uint partPersistentId;
        public string vesselId;
        public bool allowAnyVessel;

        public bool isCameraToolsSlot;
        private CameraToolsSettings _ctSettings;

        public bool hullCamUseConsistentAutoZoom = false;
        public float hullCamZoomPadding = 1.5f;
        public float hullCamManualFOV = 60f;
        #endregion
        #region Properties
        public CameraToolsSettings ctSettings
        {
            get { return _ctSettings; }
            set { _ctSettings = value?.Clone(); }
        }
        #endregion
        #region Zoom Settings Abstraction
        /// <summary>
        /// Gets whether consistent auto-zoom is enabled for this slot (works for both HullCam and CT).
        /// </summary>
        public bool GetUseConsistentAutoZoom()
        {
            if (isCameraToolsSlot)
                return _ctSettings?.UseConsistentAutoZoom ?? false;
            return hullCamUseConsistentAutoZoom;
        }

        /// <summary>
        /// Sets whether consistent auto-zoom is enabled for this slot (works for both HullCam and CT).
        /// </summary>
        public void SetUseConsistentAutoZoom(bool value)
        {
            if (isCameraToolsSlot && _ctSettings != null)
                _ctSettings.UseConsistentAutoZoom = value;
            else
                hullCamUseConsistentAutoZoom = value;
        }

        /// <summary>
        /// Gets the zoom padding for this slot (works for both HullCam and CT).
        /// </summary>
        public float GetZoomPadding()
        {
            if (isCameraToolsSlot)
                return _ctSettings?.ZoomPadding ?? 1.5f;
            return hullCamZoomPadding;
        }

        /// <summary>
        /// Sets the zoom padding for this slot (works for both HullCam and CT).
        /// </summary>
        public void SetZoomPadding(float value)
        {
            if (isCameraToolsSlot && _ctSettings != null)
                _ctSettings.ZoomPadding = value;
            else
                hullCamZoomPadding = value;
        }

        /// <summary>
        /// Gets the manual FOV for this slot (works for both HullCam and CT).
        /// </summary>
        public float GetManualFOV()
        {
            if (isCameraToolsSlot)
                return _ctSettings?.ManualFOV ?? 60f;
            return hullCamManualFOV;
        }

        /// <summary>
        /// Sets the manual FOV for this slot (works for both HullCam and CT).
        /// </summary>
        public void SetManualFOV(float value)
        {
            if (isCameraToolsSlot && _ctSettings != null)
                _ctSettings.ManualFOV = value;
            else
                hullCamManualFOV = value;
        }
        #endregion
        #region Public API
        public string GetDisplayName()
        {
            if (isCameraToolsSlot)
                return _ctSettings?.GetDisplayName() ?? "CameraTools";
            return cameraName ?? "Unknown";
        }

        /// <summary>
        /// Determines slot status considering current vessel and explicit activation state
        /// </summary>
        public SlotStatus GetStatus(Vessel currentVessel = null, bool isExplicitlyActive = false)
        {
            if (isCameraToolsSlot)
            {
                var controller = new CameraToolsCameraController();
                if (!controller.IsAvailable) return SlotStatus.Unavailable;
                if (_ctSettings == null) return SlotStatus.Unassigned;

                // Only this specific slot is "Active" if it's the explicitly selected one
                if (isExplicitlyActive && controller.IsActive)
                {
                    // Verify the active camera actually matches this slot's mode
                    if (controller.CurrentMode == _ctSettings.Mode)
                        return SlotStatus.Active;
                }

                // If CT is active but this isn't the explicit slot, show as Assigned (not Active)
                // regardless of whether settings match
                if (controller.IsActive)
                {
                    // Special check for Pathing - validate path still exists
                    if (_ctSettings.Mode == ToolModes.Pathing)
                    {
                        if (!controller.PathExists(_ctSettings.SelectedPathIndex))
                            return SlotStatus.Unavailable;
                    }

                    // Show as assigned (yellow) even if settings match - only explicit slot is active
                    return SlotStatus.Assigned;
                }

                // Pathing validation when not active
                if (_ctSettings.Mode == ToolModes.Pathing)
                {
                    if (!controller.PathExists(_ctSettings.SelectedPathIndex))
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
        #endregion
        #region Types
        public enum SlotStatus
        {
            Unassigned,
            Assigned,
            Active,
            Remote,
            Unavailable
        }
        #endregion
    }
}