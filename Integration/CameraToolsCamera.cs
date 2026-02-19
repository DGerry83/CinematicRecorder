using CinematicRecorder.Core;
using System;
using UnityEngine;

namespace CinematicRecorder.Integration
{
    /// <summary>
    /// ICamera wrapper for CameraTools.
    /// </summary>
    public class CameraToolsCamera : ICamera
    {
        #region Fields
        private readonly CameraToolsCameraController _controller;
        private readonly CameraToolsZoomController _zoomController; // NEW
        private readonly CameraToolsSettings _settings;
        private readonly string _displayName;
        private readonly bool _useDeterministic;
        private bool _wasActive;
        #endregion

        #region Properties
        public bool IsActive => _controller.IsActive;
        public string DisplayName => _displayName;
        public string CameraId => $"ct_{_settings?.Mode}_{_settings?.GetHashCode() ?? 0}";
        public Vector3 Position
        {
            get => FlightCamera.fetch?.transform.position ?? Vector3.zero;
            set { }
        }

        // CHANGED: Delegate to zoom controller
        public float FieldOfView
        {
            get => _zoomController.CurrentFoV;
            set => _zoomController.ResetZoom(value);
        }

        public float MaxFieldOfView => 120f;
        public float MinFieldOfView => 2f;
        #endregion

        #region Events
        public event Action OnActivated;
        public event Action OnDeactivated;
        #endregion

        #region Public API
        public CameraToolsCamera(CameraToolsSettings settings, CameraToolsCameraController controller = null, bool useDeterministic = false)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            _settings = settings.Clone();
            _controller = controller ?? new CameraToolsCameraController();
            _zoomController = new CameraToolsZoomController(); // NEW
            _displayName = _settings.GetDisplayName();
            _useDeterministic = useDeterministic;
        }

        public void Activate()
        {
            if (!IsActive)
            {
                _controller.ActivateMode(_settings.Mode, _settings);

                if (_settings.UseGeographicPosition && _controller.HasPendingGeographicRestoration())
                {
                    _controller.PostActivationPositionFixup();
                }

                // NEW: Sync zoom settings from slot settings to controller
                _zoomController.UseConsistentAutoZoom = _settings.UseConsistentAutoZoom;
                _zoomController.ConsistentZoomPadding = _settings.ZoomPadding;

                OnActivated?.Invoke();
            }
            else
            {
                _controller.SwitchMode(_settings.Mode, _settings);
            }
        }

        public void Deactivate()
        {
            if (IsActive)
            {
                _controller.Deactivate();
                _zoomController.CancelActiveZoom(); // NEW
                OnDeactivated?.Invoke();
            }
        }

        public void ReleaseControl()
        {
            _controller.ReleaseControlWithoutReverting();
        }

        public void SetFieldOfViewImmediate(float fov)
        {
            _zoomController.ResetZoom(fov); // CHANGED
        }

        public void Update(float deltaTime)
        {
            // CHANGED: Use zoom controller for consistent framing
            if (IsActive && _settings.UseConsistentAutoZoom && _settings.Mode == ToolModes.StationaryCamera)
            {
                _zoomController.ApplyConsistentFraming();
            }

            bool isActive = IsActive;
            if (isActive && !_wasActive)
                OnActivated?.Invoke();
            else if (!isActive && _wasActive)
                OnDeactivated?.Invoke();

            _wasActive = isActive;
        }

        public void SetPlaybackTiming(bool usePlaybackTime)
        {
            _settings.LockPathingToPlaybackRate = usePlaybackTime;
            if (IsActive && _settings.Mode == ToolModes.Pathing)
            {
                _controller.SetPlaybackTiming(usePlaybackTime);
            }
        }

        public CameraToolsSettings GetSettings() => _settings;

        // NEW: Expose zoom controller for CameraPanelController integration
        internal CameraToolsZoomController GetZoomController() => _zoomController;
        #endregion

        #region Internal
        internal void ExecuteFixup()
        {
            if (_controller.HasPendingGeographicRestoration())
            {
                _controller.PostActivationPositionFixup();
            }
        }
        #endregion
    }
}