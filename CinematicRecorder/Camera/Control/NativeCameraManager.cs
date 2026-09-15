using System;
using CinematicRecorder.Camera.Model;

namespace CinematicRecorder.Camera.Control
{
    /// <summary>
    /// Factory and lifecycle owner for native cameras (build-program chunk
    /// P1-C5; parent spec §4.5 anti-pattern watch): creates NativeCamera
    /// instances from camera definitions, tracks the one active camera, and
    /// routes activation and deactivation. This class deliberately contains
    /// NO evaluation logic — no pose math, no orientation math, no velocity
    /// stepping, no FOV decisions; every evaluation concern lives in
    /// NativeCamera, NativeTargetResolver, and NativeZoomController. The
    /// phase audit re-verifies this boundary every phase (build-program
    /// §4.5).
    ///
    /// Wiring into the capture session, CinematicCameraManager, and the panel
    /// is explicitly out of scope for P1 (P1-C7 and P3 own it): nothing in
    /// the 0.3.0 P1 code constructs this manager yet; the owner instantiates
    /// it when wiring lands.
    /// </summary>
    public sealed class NativeCameraManager
    {
        private NativeCamera _activeCamera;

        /// <summary>The currently active native camera, or null when none is active.</summary>
        public NativeCamera ActiveCamera
        {
            get { return _activeCamera; }
        }

        /// <summary>True when a native camera is the tracked active camera.</summary>
        public bool HasActiveCamera
        {
            get { return _activeCamera != null; }
        }

        /// <summary>
        /// Creates a native camera from a library definition. The created
        /// camera is not activated; route it through
        /// <see cref="ActivateCamera(NativeCamera)"/> or deactivate it with
        /// <see cref="DeactivateActiveCamera"/>.
        /// </summary>
        /// <param name="definition">The camera definition to create a camera
        /// for. Must not be null.</param>
        public NativeCamera CreateCamera(CameraDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return new NativeCamera(definition);
        }

        /// <summary>
        /// Routes activation to the given camera: any previously active
        /// camera is fully deactivated first (one active camera at a time),
        /// then the camera is activated. A camera whose activation is refused
        /// (IVA active, flight camera unavailable, or an unresolvable anchor
        /// surfacing Unavailable) is not tracked.
        /// </summary>
        /// <param name="camera">The camera to activate. Must not be null.</param>
        /// <returns>True when the camera is now the tracked active camera.</returns>
        public bool ActivateCamera(NativeCamera camera)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (ReferenceEquals(_activeCamera, camera)) return camera.IsActive;

            if (_activeCamera != null)
            {
                NativeCamera previous = _activeCamera;
                _activeCamera = null;
                previous.Deactivate();
            }

            camera.Activate();
            if (camera.CurrentState == NativeCameraState.Previewing)
            {
                _activeCamera = camera;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Convenience factory+activation route: creates a camera from the
        /// definition and activates it (see
        /// <see cref="ActivateCamera(NativeCamera)"/>).
        /// </summary>
        /// <param name="definition">The camera definition to activate. Must
        /// not be null.</param>
        /// <returns>True when the created camera is now the tracked active camera.</returns>
        public bool ActivateCamera(CameraDefinition definition)
        {
            return ActivateCamera(CreateCamera(definition));
        }

        /// <summary>
        /// Routes deactivation to the tracked active camera, if any: the full
        /// release path runs and the camera stops being tracked. No-op when
        /// nothing is active.
        /// </summary>
        public void DeactivateActiveCamera()
        {
            if (_activeCamera == null) return;

            NativeCamera active = _activeCamera;
            _activeCamera = null;
            active.Deactivate();
        }
    }
}
