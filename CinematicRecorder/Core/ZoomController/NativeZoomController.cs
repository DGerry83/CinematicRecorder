using CinematicRecorder.Camera.Model;
using CinematicRecorder.Capture;
using UnityEngine;

namespace CinematicRecorder.Core
{
    /// <summary>
    /// Per-frame field-of-view writer for native cameras (build-program chunk
    /// P1-C5; parent spec §5.3 "Zoom"). Writes FlightCamera.fetch.SetFoV
    /// directly — the ONLY per-frame FoV writer for native cameras (P1-C3
    /// ruling D-1(a): the seizure keeps FoV in its snapshot/restore set but
    /// never drives it per frame). Writes are gated on
    /// CaptureCameraResolver.IsIvaMode(), mirroring the
    /// DeterministicZoomController precedent (G-REG IVA no-write rule).
    ///
    /// P1 scope is the manual FOV of the camera definition's ZoomSettings,
    /// read live on each evaluation so write-through edits take effect on the
    /// next evaluation (same live-read contract as VelocityModel). The
    /// consistent-framing auto-zoom port is P3-C4: this class is the additive
    /// seam — ZoomSettings.AutoZoomEnabled/PaddingMultiplier and
    /// ZoomMathUtility.CalculateConsistentFramingFOV plug in here, and no other
    /// call site needs to change.
    ///
    /// No engine clock API is read anywhere in this class; the write decision
    /// is state-based (write when the clamped target differs from the last
    /// written value), which keeps the writer deterministic under capture and
    /// quiet when nothing changed (build-program invariants 1 and 4).
    /// </summary>
    public sealed class NativeZoomController
    {
        /// <summary>Minimum field of view in degrees a native camera supports.</summary>
        public const float MinFieldOfView = 10f;

        /// <summary>Maximum field of view in degrees a native camera supports.</summary>
        public const float MaxFieldOfView = 120f;

        // Below this delta (degrees) a target change is not worth a SetFoV write.
        private const float FovWriteEpsilon = 0.01f;

        private readonly ZoomSettings _settings;
        private float _lastWrittenFov = float.NaN;

        /// <summary>
        /// Creates the zoom controller bound to a camera definition's zoom
        /// settings. The settings object is read live on each evaluation, so
        /// write-through edits (including the ICamera FieldOfView setter)
        /// apply from the next evaluation on.
        /// </summary>
        /// <param name="settings">The zoom settings carrying the manual FOV. Must not be null.</param>
        public NativeZoomController(ZoomSettings settings)
        {
            if (settings == null) throw new System.ArgumentNullException(nameof(settings));
            _settings = settings;
        }

        /// <summary>
        /// The clamped manual FOV this controller currently targets, in degrees.
        /// </summary>
        public float CurrentTargetFov
        {
            get { return Mathf.Clamp((float)_settings.ManualFov, MinFieldOfView, MaxFieldOfView); }
        }

        /// <summary>
        /// Writes the current manual FOV to the flight camera when it differs
        /// from the last written value. No-op while IVA is active or the
        /// flight camera is unavailable; never throws on those states.
        /// </summary>
        public void Evaluate()
        {
            if (CaptureCameraResolver.IsIvaMode()) return;

            FlightCamera flightCamera = FlightCamera.fetch;
            if (flightCamera == null) return;

            float targetFov = CurrentTargetFov;
            if (float.IsNaN(_lastWrittenFov) || Mathf.Abs(targetFov - _lastWrittenFov) >= FovWriteEpsilon)
            {
                flightCamera.SetFoV(targetFov);
                _lastWrittenFov = targetFov;
            }
        }

        /// <summary>
        /// Sets the manual FOV target (write-through to the bound settings,
        /// clamped to the supported range) and optionally applies it
        /// immediately through <see cref="Evaluate"/>.
        /// </summary>
        /// <param name="fov">Target field of view in degrees.</param>
        /// <param name="applyImmediately">True to write the new target to the
        /// flight camera in this call (still IVA-gated inside Evaluate);
        /// false to let the next evaluation pick it up.</param>
        public void SetTargetFov(float fov, bool applyImmediately)
        {
            _settings.ManualFov = Mathf.Clamp(fov, MinFieldOfView, MaxFieldOfView);
            if (applyImmediately) Evaluate();
        }
    }
}
