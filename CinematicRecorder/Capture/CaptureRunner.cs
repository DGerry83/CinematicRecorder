using CinematicRecorder.Core;
using UnityEngine;

namespace CinematicRecorder.Capture
{
    /// <summary>
    /// MonoBehaviour host for capture coroutines. Lives for exactly one session, watches for KSP
    /// camera-mode changes, and forwards retarget requests to the capture controller.
    /// Runs late in the script execution order so camera changes made by other mods in their
    /// own Update (e.g. Through The Eyes toggling first-person) are detected in the same frame.
    /// </summary>
    [DefaultExecutionOrder(1000)]
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

        private void Update()
        {
            if (!DeterministicCaptureSession.IsRunning)
                return;

            if (Controller == null)
                return;

            if (CameraManager.Instance == null)
                return;

            // Re-resolve every frame: mods like Through The Eyes enable/disable the
            // InternalCamera pass without firing OnCameraChange. Applied immediately
            // (pre-render) so the swap affects this frame's render; no-op when unchanged.
            Controller.RetargetCameraImmediate(CaptureCameraResolver.ResolveForCurrentMode());
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
