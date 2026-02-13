using System;
using UnityEngine;

namespace CinematicRecorder.Integration
{
    /// <summary>
    /// ICamera wrapper for CameraTools.
    /// Uses CameraToolsCameraController internally with new public API.
    /// </summary>
    public class CameraToolsCamera : ICamera
    {
        private readonly CameraToolsCameraController _controller;
        private readonly CameraToolsSettings _settings;
        private readonly string _displayName;
        private readonly bool _useDeterministic;
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
            get => _controller.CurrentFOV;
            set => _controller.EnforceAutoZoomFOVImmediate(value);
        }

        public float MaxFieldOfView => 120f;
        public float MinFieldOfView => 2f;

        public event Action OnActivated;
        public event Action OnDeactivated;

        /// <summary>
        /// Creates a CameraTools camera wrapper.
        /// </summary>
        /// <param name="settings">Camera configuration settings</param>
        /// <param name="controller">Optional controller instance (creates new if null)</param>
        /// <param name="useDeterministic">Whether to use deterministic physics-step control</param>
        public CameraToolsCamera(CameraToolsSettings settings, CameraToolsCameraController controller = null, bool useDeterministic = false)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            // Clone settings to ensure this camera instance has isolated state
            // This prevents modifications to the slot's stored settings from affecting active camera
            _settings = settings.Clone();

            _controller = controller ?? new CameraToolsCameraController();
            _displayName = _settings.GetDisplayName();
            _useDeterministic = useDeterministic;
        }

        public void Activate()
        {
            if (!IsActive)
            {
                // First activation
                _controller.ActivateMode(_settings.Mode, _settings);

                if (_settings.UseGeographicPosition && _controller.HasPendingGeographicRestoration())
                {
                    _controller.PostActivationPositionFixup();
                }

                OnActivated?.Invoke();
            }
            else
            {
                // Already active (shouldn't happen via normal flow, but handle gracefully)
                // Use SwitchMode to apply new settings
                _controller.SwitchMode(_settings.Mode, _settings);
            }
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
                // Use controller's deactivate for proper cleanup
                _controller.Deactivate();

                // Notify listeners
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
                _controller.ApplyConsistentFraming();
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

        /// <summary>
        /// Updates the playback timing mode for pathing cameras.
        /// Should be called before activation if timing mode needs to change.
        /// </summary>
        public void SetPlaybackTiming(bool usePlaybackTime)
        {
            _settings.LockPathingToPlaybackRate = usePlaybackTime;
            if (IsActive && _settings.Mode == ToolModes.Pathing)
            {
                _controller.SetPlaybackTiming(usePlaybackTime);
            }
        }
    }
}