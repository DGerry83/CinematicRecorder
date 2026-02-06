using System;
using System.Collections.Generic;
using UnityEngine;

namespace CinematicRecorder.Integration
{
    /// <summary>
    /// Factory and lifecycle manager for ICamera instances.
    /// Bridges CameraSlot assignments to active camera instances.
    /// </summary>
    public class CinematicCameraManager
    {
        private static CinematicCameraManager _instance;
        public static CinematicCameraManager Instance => _instance ?? (_instance = new CinematicCameraManager());

        private readonly List<CameraToolsCamera> _pendingFixups = new List<CameraToolsCamera>();

        private ICamera _activeCamera;
        private CameraSlot _activeSlot;

        public ICamera ActiveCamera => _activeCamera;
        public CameraSlot ActiveSlot => _activeSlot;
        public bool HasActiveCamera => _activeCamera != null;

        public event Action<ICamera> OnCameraActivated;
        public event Action<ICamera> OnCameraDeactivated;

        private CinematicCameraManager() { }

        /// <summary>
        /// Creates an ICamera from a slot assignment
        /// </summary>
        public ICamera CreateCameraFromSlot(CameraSlot slot)
        {
            if (slot?.isCameraToolsSlot == true)
            {
                if (slot.ctSettings == null) return null;
                return new CameraToolsCamera(slot.ctSettings);
            }
            else
            {
                // HullCam
                object cam = HullCamBridge.ResolveCameraSlot(slot, FlightGlobals.ActiveVessel);
                if (cam == null) return null;
                return new HullCamController(cam);
            }
        }

        /// <summary>
        /// Switches to the camera defined by the slot with optional transition
        /// </summary>
        public void SwitchToCamera(CameraSlot slot, bool immediate = false)
        {
            if (slot == null) return;

            // Clear previous if switching
            if (_activeCamera != null)
            {
                _activeCamera.OnDeactivated -= OnActiveCameraDeactivated;

                // ALWAYS fully deactivate previous camera when switching
                // ReleaseControl just sets a flag and leaves CT in broken state
                _activeCamera.Deactivate();
            }

            var newCamera = CreateCameraFromSlot(slot);
            if (newCamera == null) return;

            _activeCamera = newCamera;
            _activeSlot = slot;
            _activeCamera.OnDeactivated += OnActiveCameraDeactivated;

            if (immediate)
            {
                _activeCamera.Activate();
                OnCameraActivated?.Invoke(_activeCamera);
            }
            else
            {
                _activeCamera.Activate();
                OnCameraActivated?.Invoke(_activeCamera);
            }
        }

        /// <summary>
        /// Returns to main camera view - works even for cameras not tracked by manager
        /// </summary>
        public void ReturnToMain(bool immediate = false)
        {
            // Check what's actually active and only restore that
            bool ctActive = new CameraToolsCameraController().IsActive;
            bool hullCamActive = HullCamBridge.IsAnyCameraActive();

            // Clear managed state first
            if (_activeCamera != null)
            {
                _activeCamera.OnDeactivated -= OnActiveCameraDeactivated;
                _activeCamera = null;
                _activeSlot = null;
            }

            // ORIGINAL LOGIC: Exclusive OR - only restore what is actually active
            if (ctActive)
            {
                CameraToolsReflectionProvider.Revert();
            }
            else if (hullCamActive)
            {
                HullCamBridge.RestoreMain();
            }
        }

        /// <summary>
        /// Clears active slot reference without deactivating camera
        /// </summary>
        public void ClearActiveSlot()
        {
            _activeSlot = null;
            if (_activeCamera != null)
            {
                _activeCamera.OnDeactivated -= OnActiveCameraDeactivated;
                _activeCamera = null;
            }
        }

        /// <summary>
        /// Validates the current camera is still valid
        /// </summary>
        public bool ValidateActiveCamera()
        {
            if (_activeCamera == null) return false;

            if (_activeCamera is HullCamController hullCam)
                return hullCam.IsValid();

            return true;
        }

        private void OnActiveCameraDeactivated()
        {
            if (_activeCamera != null)
            {
                var cam = _activeCamera;
                _activeCamera = null;
                _activeSlot = null;
                OnCameraDeactivated?.Invoke(cam);
            }
        }

        #region Deferred Fixup Support

        public static void ScheduleFixup(CameraToolsCamera camera)
        {
            if (Instance != null && !Instance._pendingFixups.Contains(camera))
                Instance._pendingFixups.Add(camera);
        }

        public void ProcessPendingFixups()
        {
            if (_pendingFixups.Count == 0) return;

            foreach (var cam in _pendingFixups)
            {
                cam.ExecuteFixup();
            }
            _pendingFixups.Clear();
        }

        #endregion

        #region Zoom Control Integration

        public float GetCurrentFOV()
        {
            return _activeCamera?.FieldOfView ?? 60f;
        }

        public void ApplyZoom(float fov)
        {
            if (_activeCamera != null)
                _activeCamera.FieldOfView = fov;
        }

        public float GetMaxFOV()
        {
            return _activeCamera?.MaxFieldOfView ?? 120f;
        }

        public float GetMinFOV()
        {
            return _activeCamera?.MinFieldOfView ?? 10f;
        }

        #endregion
    }
}