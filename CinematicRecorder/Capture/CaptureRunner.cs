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

        private bool _subscribed;

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
