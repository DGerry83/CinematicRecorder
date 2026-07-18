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
        #region Fields
        private static CinematicCameraManager _instance;
        private ICamera _activeCamera;
        private CameraSlot _activeSlot;
        #endregion
        #region Properties
        /// <summary>
        /// Singleton instance accessor
        /// </summary>
        public static CinematicCameraManager Instance => _instance ?? (_instance = new CinematicCameraManager());
        public ICamera ActiveCamera => _activeCamera;
        public CameraSlot ActiveSlot => _activeSlot;
        public bool HasActiveCamera => _activeCamera != null;
        #endregion
        #region Events
        public event Action<ICamera> OnCameraActivated;
        public event Action<ICamera> OnCameraDeactivated;
        #endregion
        #region Public API
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

            // Check if switching between two CameraTools slots (CT→CT)
            bool switchingCTtoCT = (_activeCamera is CameraToolsCamera) && slot.isCameraToolsSlot;

            if (switchingCTtoCT)
            {
                // CT→CT transition: Use SwitchCamera for seamless transition
                if (_activeCamera != null)
                {
                    _activeCamera.OnDeactivated -= OnActiveCameraDeactivated;
                    // Don't call Deactivate - we're staying in CT
                }

                var newCamera = CreateCameraFromSlot(slot) as CameraToolsCamera;
                if (newCamera == null) return;

                _activeCamera = newCamera;
                _activeSlot = slot;
                _activeCamera.OnDeactivated += OnActiveCameraDeactivated;

                if (immediate)
                {
                    // Use SwitchMode instead of Activate for CT→CT
                    var controller = new CameraToolsCameraController();
                    controller.SwitchMode(slot.ctSettings.Mode, slot.ctSettings);

                    OnCameraActivated?.Invoke(_activeCamera);
                }
            }
            else
            {
                // Normal transition (CT→HullCam, HullCam→CT, etc.)
                if (_activeCamera != null)
                {
                    _activeCamera.OnDeactivated -= OnActiveCameraDeactivated;
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
            }
        }

        /// <summary>
        /// Returns to main camera view - works even for cameras not tracked by manager
        /// </summary>
        public void ReturnToMain(bool immediate = false)
        {
            bool ctActive = new CameraToolsCameraController().IsActive;
            bool hullCamActive = HullCamBridge.IsAnyCameraActive();

            if (_activeCamera != null)
            {
                _activeCamera.OnDeactivated -= OnActiveCameraDeactivated;

                if (_activeCamera is CameraToolsCamera ctCam)
                {
                    ctCam.Deactivate();
                }

                _activeCamera = null;
                _activeSlot = null;
            }

            if (ctActive)
            {
                // Use new DeactivateCamera API which properly validates parenting
                CameraToolsAPIManager.DeactivateCamera();
            }
            else if (hullCamActive)
            {
                HullCamBridge.RestoreMain();
            }

            if (FlightCamera.fetch != null)
            {
                FlightCamera.fetch.SetFoV(60f);
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
        #endregion
        #region Event Handlers
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
        #endregion
        #region Zoom Integration
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