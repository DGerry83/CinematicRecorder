using CinematicRecorder.Core;
using UnityEngine;

namespace CinematicRecorder.Capture
{
    /// <summary>
    /// MonoBehaviour host for capture coroutines. Lives for exactly one session, watches for KSP
    /// camera-mode changes, and forwards retarget requests to the capture controller.
    /// </summary>
    public sealed class CaptureRunner : MonoBehaviour
    {
        /// <summary>
        /// The capture controller to forward camera-mode retargets to.
        /// </summary>
        public OfflineCaptureController Controller { get; set; }

        /// <summary>
        /// Interval (unscaled seconds) between compositing-chain re-resolutions. Mods like
        /// Through The Eyes enable/disable the InternalCamera pass without firing
        /// OnCameraChange, so event subscription alone misses those transitions.
        /// </summary>
        private const float ResolveIntervalSeconds = 0.5f;

        private bool _subscribed;
        private float _nextResolveTime;

        private void Start()
        {
            if (Controller != null && CameraManager.Instance != null)
            {
                GameEvents.OnCameraChange.Add(OnCameraChanged);
                _subscribed = true;
            }
        }

        private void OnDestroy()
        {
            if (_subscribed)
            {
                GameEvents.OnCameraChange.Remove(OnCameraChanged);
                _subscribed = false;
            }
        }

        private void Update()
        {
            if (!DeterministicCaptureSession.IsRunning)
                return;

            if (Controller == null)
                return;

            if (CameraManager.Instance == null)
                return;

            if (Time.unscaledTime < _nextResolveTime)
                return;

            _nextResolveTime = Time.unscaledTime + ResolveIntervalSeconds;

            // No-op when the resolved camera is unchanged; the controller applies any
            // real swap at the next capture-loop frame boundary.
            Controller.RequestCameraRetarget(CaptureCameraResolver.ResolveForCurrentMode());
        }

        private void OnCameraChanged(CameraManager.CameraMode mode)
        {
            if (!DeterministicCaptureSession.IsRunning)
                return;

            if (Controller == null)
                return;

            if (CameraManager.Instance == null)
                return;

            Controller.RequestCameraRetarget(CaptureCameraResolver.ResolveForCurrentMode());
        }
    }
}
