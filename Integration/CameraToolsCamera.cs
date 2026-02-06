using System;
using UnityEngine;

namespace CinematicRecorder.Integration
{
    /// <summary>
    /// ICamera wrapper for CameraTools.
    /// Uses CameraToolsCameraController internally.
    /// </summary>
    public class CameraToolsCamera : ICamera
    {
        private readonly CameraToolsCameraController _controller;
        private readonly CameraToolsSettings _settings;
        private readonly string _displayName;
        private bool _wasActive;

        public bool IsActive => _controller.IsActive;

        public string DisplayName => _displayName;

        public string CameraId => $"ct_{_settings?.Mode}_{_settings?.GetHashCode() ?? 0}";

        public Vector3 Position
        {
            get => FlightCamera.fetch?.transform.position ?? Vector3.zero;
            set
            {
                // CT uses relative positioning via controller
                // Geographic or offset modes handle this during ApplyPreset
            }
        }

        public float FieldOfView
        {
            get => _controller.ManualFOV;
            set => _controller.ManualFOV = value;
        }

        public float MaxFieldOfView => 120f;
        public float MinFieldOfView => 2f;

        public event Action OnActivated;
        public event Action OnDeactivated;

        public CameraToolsCamera(CameraToolsSettings settings, CameraToolsCameraController controller = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _controller = controller ?? new CameraToolsCameraController();
            _displayName = settings.GetDisplayName();
        }

        public void Activate()
        {
            if (!IsActive)
            {
                _controller.ActivateMode(_settings.Mode, _settings);

                // Schedule position fixup if using geographic positioning
                if (_settings.UseGeographicPosition && _controller.HasPendingGeographicRestoration())
                {
                    // Defer to next frame
                    DeferredPositionFixup();
                }

                OnActivated?.Invoke();
            }
        }

        private void DeferredPositionFixup()
        {
            // Schedule the fixup for next frame using Unity's coroutine or update loop
            // Since we don't have a MonoBehaviour here, we'll use a static queue
            CinematicCameraManager.ScheduleFixup(this);
        }

        internal void ExecuteFixup()
        {
            if (_controller.HasPendingGeographicRestoration())
            {
                _controller.PostActivationPositionFixup();
            }
        }

        public void Deactivate()
        {
            if (IsActive)
            {
                _controller.Deactivate();
                OnDeactivated?.Invoke();
            }
        }

        public void ReleaseControl()
        {
            _controller.ReleaseControlWithoutReverting();
        }

        public void SetFieldOfViewImmediate(float fov)
        {
            _controller.EnforceAutoZoomFOVImmediate(fov);
        }

        public void Update(float deltaTime)
        {
            // Handle auto-zoom consistency if enabled
            if (IsActive && _settings.UseConsistentAutoZoom && _settings.Mode == ToolModes.StationaryCamera)
            {
                _controller.ApplyConsistentAutoZoom(true, _settings.ZoomPadding);
            }

            // Track state changes for events
            bool isActive = IsActive;
            if (isActive && !_wasActive)
                OnActivated?.Invoke();
            else if (!isActive && _wasActive)
                OnDeactivated?.Invoke();

            _wasActive = isActive;
        }

        public CameraToolsSettings GetSettings() => _settings;
    }
}