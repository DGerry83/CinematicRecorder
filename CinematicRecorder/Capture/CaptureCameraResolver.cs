using UnityEngine;

namespace CinematicRecorder.Capture
{
    /// <summary>
    /// Single source of truth for mapping the current KSP camera mode to the
    /// <see cref="Camera"/> that CinematicRecorder should capture.
    /// </summary>
    public static class CaptureCameraResolver
    {
        private static bool _warnedCameraMainFallback;

        /// <summary>
        /// Resolves the capture target for the current KSP camera mode.
        /// </summary>
        /// <returns>
        /// In Flight, <see cref="CameraManager.CameraMode.IVA"/>, and
        /// <see cref="CameraManager.CameraMode.Internal"/> modes: the last camera in the
        /// game-world compositing chain (the enabled, screen-rendering camera with the
        /// highest depth among <see cref="FlightCamera.fetch"/>.mainCamera and the
        /// <see cref="Camera"/> on <see cref="InternalCamera.Instance"/>). This captures the
        /// full composite the player sees — the cockpit interior in IVA (which renders after
        /// the flight-camera exterior pass) and overlay passes such as Through The Eyes'
        /// helmet view in first-person (where TTE enables the InternalCamera while the game
        /// remains in Flight mode). For Map and External modes:
        /// <see cref="FlightCamera.fetch.mainCamera"/>; or <see cref="Camera.main"/> as a
        /// last-resort fallback with a one-time warning.
        /// </returns>
        public static Camera ResolveForCurrentMode()
        {
            CameraManager cameraManager = CameraManager.Instance;
            if (cameraManager != null)
            {
                CameraManager.CameraMode mode = cameraManager.currentCameraMode;

                if (mode == CameraManager.CameraMode.IVA || mode == CameraManager.CameraMode.Internal)
                {
                    // IVA: prefer the InternalCamera on depth ties — in stock IVA it renders
                    // the interior after the flight-camera exterior pass.
                    Camera last = ResolveLastCompositingCamera(preferInternalOnTie: true);
                    if (last != null)
                        return last;
                }
                else if (mode == CameraManager.CameraMode.Flight)
                {
                    // Flight: prefer the flight camera on depth ties; the InternalCamera only
                    // wins when a mod (e.g. Through The Eyes first-person) has enabled it as
                    // a later compositing pass.
                    Camera last = ResolveLastCompositingCamera(preferInternalOnTie: false);
                    if (last != null)
                        return last;
                }
                else
                {
                    // Map / External: unchanged pre-#001 behavior.
                    FlightCamera flightCamera = FlightCamera.fetch;
                    if (flightCamera != null)
                    {
                        Camera mainCamera = flightCamera.mainCamera;
                        if (mainCamera != null)
                            return mainCamera;
                    }
                }
            }

            Camera fallback = Camera.main;
            if (fallback != null && !_warnedCameraMainFallback)
            {
                _warnedCameraMainFallback = true;
                UnityEngine.Debug.LogWarning(
                    "[CaptureCameraResolver] Primary capture camera unavailable; falling back to Camera.main.");
            }

            return fallback;
        }

        /// <summary>
        /// Returns the last camera in the game-world compositing chain: among the flight main
        /// camera and the InternalCamera camera, the enabled one rendering to the screen
        /// (no target texture) with the highest depth. Cameras rendering later see the full
        /// backbuffer composite of all earlier passes, so binding the capture buffer to the
        /// last one records what the player actually sees. Base-game UI is unaffected: IMGUI
        /// and screen-space-overlay uGUI draw after all cameras and are never captured.
        /// </summary>
        private static Camera ResolveLastCompositingCamera(bool preferInternalOnTie)
        {
            Camera flightCam = null;
            FlightCamera flightCamera = FlightCamera.fetch;
            if (flightCamera != null)
                flightCam = flightCamera.mainCamera;

            Camera internalCam = null;
            InternalCamera internalCamera = InternalCamera.Instance;
            if (internalCamera != null)
                internalCam = internalCamera.GetComponent<Camera>();

            Camera best = null;
            if (preferInternalOnTie)
            {
                ConsiderCompositingCamera(flightCam, ref best);
                ConsiderCompositingCamera(internalCam, ref best);
            }
            else
            {
                ConsiderCompositingCamera(internalCam, ref best);
                ConsiderCompositingCamera(flightCam, ref best);
            }

            return best;
        }

        /// <summary>
        /// Considers a candidate for the compositing chain: skips disabled, inactive, or
        /// off-screen (target-texture) cameras; keeps the one with the highest depth.
        /// Later candidates win ties, so evaluation order encodes tie preference.
        /// </summary>
        private static void ConsiderCompositingCamera(Camera candidate, ref Camera best)
        {
            if (candidate == null)
                return;
            if (!candidate.enabled || !candidate.gameObject.activeInHierarchy)
                return;
            if (candidate.targetTexture != null)
                return;
            if (best == null || candidate.depth >= best.depth)
                best = candidate;
        }

        /// <summary>
        /// Determines whether the current KSP camera mode is IVA or Internal.
        /// </summary>
        /// <returns><c>true</c> if the current mode is IVA or Internal; otherwise <c>false</c>.</returns>
        public static bool IsIvaMode()
        {
            CameraManager cameraManager = CameraManager.Instance;
            if (cameraManager == null)
                return false;

            CameraManager.CameraMode mode = cameraManager.currentCameraMode;
            return mode == CameraManager.CameraMode.IVA || mode == CameraManager.CameraMode.Internal;
        }

        /// <summary>
        /// Returns <c>true</c> if the supplied camera is the <see cref="Camera"/> component on
        /// <see cref="InternalCamera.Instance"/>.
        /// </summary>
        internal static bool IsInternalCamera(Camera camera)
        {
            if (camera == null)
                return false;

            InternalCamera internalCamera = InternalCamera.Instance;
            if (internalCamera == null)
                return false;

            Camera internalCameraComponent = internalCamera.GetComponent<Camera>();
            return internalCameraComponent != null && internalCameraComponent == camera;
        }
    }
}
