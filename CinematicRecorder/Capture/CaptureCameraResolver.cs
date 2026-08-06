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
        /// The <see cref="Camera"/> component on <see cref="InternalCamera.Instance"/> when in
        /// <see cref="CameraManager.CameraMode.IVA"/> or <see cref="CameraManager.CameraMode.Internal"/>;
        /// <see cref="FlightCamera.fetch.mainCamera"/> for Flight, Map, and External modes;
        /// or <see cref="Camera.main"/> as a last-resort fallback with a one-time warning.
        /// </returns>
        public static Camera ResolveForCurrentMode()
        {
            CameraManager cameraManager = CameraManager.Instance;
            if (cameraManager != null)
            {
                CameraManager.CameraMode mode = cameraManager.currentCameraMode;

                if (mode == CameraManager.CameraMode.IVA || mode == CameraManager.CameraMode.Internal)
                {
                    InternalCamera internalCamera = InternalCamera.Instance;
                    if (internalCamera != null)
                    {
                        Camera internalCameraComponent = internalCamera.GetComponent<Camera>();
                        if (internalCameraComponent != null)
                            return internalCameraComponent;
                    }
                }
                else
                {
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
