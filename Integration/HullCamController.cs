using System;
using UnityEngine;

namespace CinematicRecorder.Integration
{
    /// <summary>
    /// ICamera wrapper for HullCam VDS.
    /// Delegates to HullCamBridge but implements lifecycle management.
    /// </summary>
    public class HullCamController : ICamera
    {
        private readonly object _cameraModule;
        private readonly Part _hostPart;
        private readonly string _cameraName;

        public bool IsActive => HullCamBridge.IsCameraActive(_cameraModule);
        public string DisplayName => _cameraName ?? "HullCam";
        public string CameraId => _hostPart?.persistentId.ToString() ?? "unknown";
        public Vector3 Position
        {
            get
            {
                var t = HullCamBridge.GetCameraTransform(_cameraModule);
                return t?.position ?? Vector3.zero;
            }
            set
            {
                // HullCam position is fixed to part, cannot be set externally
            }
        }
        public float FieldOfView
        {
            get => HullCamBridge.GetCameraFoV(_cameraModule);
            set => HullCamBridge.SetCameraFoV(_cameraModule, value);
        }
        public float MaxFieldOfView => HullCamBridge.GetCameraFoVMax(_cameraModule);
        public float MinFieldOfView => HullCamBridge.GetCameraFoVMin(_cameraModule);
        public event Action OnActivated;
        public event Action OnDeactivated;

        public HullCamController(object cameraModule)
        {
            if (cameraModule == null) throw new ArgumentNullException(nameof(cameraModule));

            _cameraModule = cameraModule;
            _cameraName = HullCamBridge.GetCameraName(cameraModule);

            var comp = cameraModule as Component;
            _hostPart = comp?.GetComponentInParent<Part>();
        }
        public void Activate()
        {
            if (!IsActive)
            {
                HullCamBridge.Activate(_cameraModule);
                OnActivated?.Invoke();
            }
        }
        public void Deactivate()
        {
            if (IsActive)
            {
                HullCamBridge.RestoreMain();
                OnDeactivated?.Invoke();
            }
        }
        public void ReleaseControl()
        {
            // HullCam doesn't distinguish between release and deactivate
            // Always revert to main camera
            HullCamBridge.RestoreMain();
        }
        public void SetFieldOfViewImmediate(float fov)
        {
            HullCamBridge.SetCameraFoV(_cameraModule, fov);
        }
        public void Update(float deltaTime)
        {
            // HullCam zoom is handled by ZoomControlService directly
            // This method is a hook for future per-frame updates if needed
        }

        /// <summary>
        /// Validates the underlying camera is still valid (part not destroyed, etc)
        /// </summary>
        public bool IsValid()
        {
            if (_cameraModule == null) return false;
            if (_hostPart == null || _hostPart.State == PartStates.DEAD) return false;
            return HullCamBridge.IsCameraAvailable(_cameraModule);
        }
    }
}