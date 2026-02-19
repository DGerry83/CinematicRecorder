using CinematicRecorder.Integration;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static CinematicRecorder.UI.CinematicUIStrings;

namespace CinematicRecorder.UI
{
    /// <summary>
    /// Manages the 16 camera slots, their assignments, and active state tracking.
    /// Pure logic class - no rendering. Notifies subscribers of state changes.
    /// </summary>
    public class CameraSlotManager
    {
        #region Fields & State
        private readonly List<CameraSlot> cameraSlots = new List<CameraSlot>();
        private int _activeSlotIndex = -1;
        private bool _wasCameraToolsActive = false;
        private ToolModes _lastCameraToolsMode = ToolModes.StationaryCamera;
        #endregion
        #region Public API
        public IReadOnlyList<CameraSlot> Slots => cameraSlots;
        public int ActiveSlotIndex => _activeSlotIndex;
        public bool HasActiveSlot => _activeSlotIndex >= 0 && _activeSlotIndex < cameraSlots.Count;
        public CameraSlot ActiveSlot => HasActiveSlot ? cameraSlots[_activeSlotIndex] : null;

        public event Action<int> OnActiveSlotChanged;
        public event Action OnSlotsChanged;
        public event Action<int> OnSlotCleared;

        public CameraSlotManager()
        {
            InitializeSlots();
        }
        public void LoadPreset(CameraPanelPreset preset)
        {
            if (preset?.buttonAssignments != null && preset.buttonAssignments.Count == CinematicUIResources.Layout.Camera.TOTAL_SLOTS)
            {
                cameraSlots.Clear();
                cameraSlots.AddRange(preset.buttonAssignments.Select(s => new CameraSlot
                {
                    buttonID = s.buttonID,
                    cameraName = s.cameraName,
                    partPersistentId = s.partPersistentId,
                    vesselId = s.vesselId,
                    allowAnyVessel = s.allowAnyVessel,
                    isCameraToolsSlot = s.isCameraToolsSlot,
                    ctSettings = s.ctSettings  // Property setter handles Clone()
                }));

                OnSlotsChanged?.Invoke();

                // Validate active slot index after preset load
                if (_activeSlotIndex >= cameraSlots.Count)
                {
                    SetActiveSlot(-1);
                }
            }
        }
        public int FindFirstOpenSlot()
        {
            for (int i = 0; i < cameraSlots.Count; i++)
            {
                if (cameraSlots[i].GetStatus() == CameraSlot.SlotStatus.Unassigned)
                    return i;
            }
            return -1;
        }
        public void SetActiveSlot(int index)
        {
            if (index < -1 || index >= cameraSlots.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            if (_activeSlotIndex != index)
            {
                _activeSlotIndex = index;
                OnActiveSlotChanged?.Invoke(index);
            }
        }
        public void ClearActiveSlot()
        {
            SetActiveSlot(-1);
        }
        public void ClearSlot(int index)
        {
            if (index < 0 || index >= cameraSlots.Count) return;

            cameraSlots[index] = new CameraSlot
            {
                buttonID = string.Format(CameraController.ButtonIdFormat, index)
            };

            if (_activeSlotIndex == index)
            {
                SetActiveSlot(-1);
            }

            OnSlotCleared?.Invoke(index);
            OnSlotsChanged?.Invoke();
        }
        public CameraSlot GetSlot(int index)
        {
            if (index < 0 || index >= cameraSlots.Count) return null;
            return cameraSlots[index];
        }
        public CameraSlot.SlotStatus GetSlotStatus(int index, Vessel currentVessel = null)
        {
            if (index < 0 || index >= cameraSlots.Count) return CameraSlot.SlotStatus.Unavailable;
            bool isExplicitlyActive = (index == _activeSlotIndex);
            return cameraSlots[index].GetStatus(currentVessel, isExplicitlyActive);
        }
        public bool AssignCameraToolsToSlot(int index, CameraToolsSettings settings)
        {
            if (index < 0 || index >= cameraSlots.Count) return false;
            if (settings == null) return false;

            if (settings.Mode == ToolModes.Pathing && settings.SelectedPathIndex < 0)
            {
                ScreenMessages.PostScreenMessage("Cannot save: No path selected in CameraTools", 2f);
                return false;
            }

            cameraSlots[index] = new CameraSlot
            {
                buttonID = string.Format(CameraController.ButtonIdFormat, index),
                isCameraToolsSlot = true,
                ctSettings = settings,  // Property setter will call Clone()
                cameraName = settings.GetDisplayName()
            };

            UnityEngine.Debug.Log($"[AssignCameraToolsToSlot] Slot {index} assigned CT {settings.Mode} " +
                $"(Hash: {cameraSlots[index].ctSettings.GetHashCode()}, PathIndex: {settings.SelectedPathIndex})");

            OnSlotsChanged?.Invoke();
            return true;
        }
        public bool AssignHullCamToSlot(int index, object camera, Vessel vessel)
        {
            if (index < 0 || index >= cameraSlots.Count) return false;
            if (camera == null || vessel == null) return false;

            Part part = GetPartFromCamera(camera);
            string camName = HullCamBridge.GetCameraName(camera) ?? "";

            cameraSlots[index] = new CameraSlot
            {
                buttonID = string.Format(CameraController.ButtonIdFormat, index),
                cameraName = camName,
                partPersistentId = part != null ? part.persistentId : 0u,
                vesselId = vessel.id.ToString(),
                allowAnyVessel = false,
                isCameraToolsSlot = false,
                ctSettings = null
            };

            OnSlotsChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Checks for external deactivation (e.g., user pressed CT revert button manually)
        /// and clears active slot if necessary. Call from LateUpdate.
        /// </summary>
        public void CheckExternalDeactivation()
        {
            if (_activeSlotIndex < 0)
            {
                UpdateTrackingState();
                return;
            }

            var slot = cameraSlots[_activeSlotIndex];
            ICamera activeCam = CinematicCameraManager.Instance.ActiveCamera;

            if (slot.isCameraToolsSlot)
            {
                bool wasCTActive = activeCam is CameraToolsCamera;
                if (!wasCTActive && _wasCameraToolsActive)
                {
                    SetActiveSlot(-1);
                }
                else if (activeCam is CameraToolsCamera ctCam)
                {
                    // Check if mode changed
                    if (ctCam.GetSettings().Mode != slot.ctSettings.Mode)
                    {
                        SetActiveSlot(-1);
                    }
                }
            }
            else
            {
                if (_wasCameraToolsActive && !(activeCam is HullCamController))
                {
                    if (!HullCamBridge.IsAnyCameraActive())
                    {
                        SetActiveSlot(-1);
                    }
                }
                else if (!_wasCameraToolsActive && activeCam == null)
                {
                    if (!HullCamBridge.IsAnyCameraActive())
                    {
                        SetActiveSlot(-1);
                    }
                }
            }

            UpdateTrackingState();
        }
        public void HandleVesselChange()
        {
            SetActiveSlot(-1);
        }
        public void HandleSceneChange()
        {
            SetActiveSlot(-1);
        }
        #endregion
        #region Private Implementation
        private void InitializeSlots()
        {
            cameraSlots.Clear();
            for (int i = 0; i < CinematicUIResources.Layout.Camera.TOTAL_SLOTS; i++)
            {
                cameraSlots.Add(new CameraSlot { buttonID = string.Format(CameraController.ButtonIdFormat, i) });
            }
        }
        private void UpdateTrackingState()
        {
            ICamera activeCam = CinematicCameraManager.Instance.ActiveCamera;
            _wasCameraToolsActive = activeCam is CameraToolsCamera;
            if (_wasCameraToolsActive)
            {
                _lastCameraToolsMode = ((CameraToolsCamera)activeCam).GetSettings().Mode;
            }
        }
        private Part GetPartFromCamera(object cameraModule)
        {
            try
            {
                Component comp = cameraModule as Component;
                if (comp == null) return null;

                Transform current = comp.transform;
                while (current != null)
                {
                    Part part = current.GetComponent<Part>();
                    if (part != null) return part;
                    current = current.parent;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log(CameraController.GetPartFromCameraFail + ex.Message);
            }
            return null;
        }
        #endregion
    }
}