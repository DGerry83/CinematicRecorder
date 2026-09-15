using System;
using CinematicRecorder.Camera.Frames;
using CinematicRecorder.Camera.Model;
using CinematicRecorder.Camera.Motion;
using CinematicRecorder.Core;
using CinematicRecorder.Integration;
using UnityEngine;

namespace CinematicRecorder.Camera.Control
{
    /// <summary>
    /// Per-camera state of a native camera (parent spec §5.2; build-program
    /// chunk P1-C5): Inactive → Previewing → Armed → RecordingDriven →
    /// Releasing → Inactive, plus Unavailable for unresolvable anchors or
    /// targets. Unavailable always carries a reason string and never throws.
    /// </summary>
    public enum NativeCameraState
    {
        /// <summary>Not activated, or fully released after activation.</summary>
        Inactive,

        /// <summary>Seizure acquired; the camera is evaluated live (non-deterministic preview).</summary>
        Previewing,

        /// <summary>Recording start armed the camera; waiting for the first captured-frame evaluation.</summary>
        Armed,

        /// <summary>At least one captured-frame evaluation has run since arming.</summary>
        RecordingDriven,

        /// <summary>Transient state inside Deactivate while the full release path runs.</summary>
        Releasing,

        /// <summary>The anchor or target is unresolvable; the reason is exposed and no evaluation writes occur.</summary>
        Unavailable
    }

    /// <summary>
    /// Native library camera: implements <see cref="ICamera"/> over the
    /// bespoke camera stack (build-program chunk P1-C5; parent spec §5.1-§5.3).
    /// Composes, per evaluation, the pinned hold/preview position —
    /// anchor ∘ (payload offset + ManualOffset + integrated displacement) —
    /// drives the seized flight camera rig through
    /// <see cref="FlightCameraSeizure.SetRigWorldPose"/>, resolves its target
    /// and look-at orientation through <see cref="NativeTargetResolver"/>, and
    /// writes the manual FOV through <see cref="NativeZoomController"/> (the
    /// only per-frame FoV writer for native cameras, P1-C3 ruling D-1(a)).
    ///
    /// Evaluation cadence (pinned): a single evaluation method parameterized
    /// by the step delta. The capture path supplies the sim-step duration; the
    /// preview path supplies the caller-measured preview step. The method
    /// itself never reads any engine clock (build-program invariant 1), and
    /// no engine clock API appears anywhere in this file. The ICamera
    /// Update(float) hook forwards here with the caller-supplied step delta.
    ///
    /// Arm path (pinned, P1-C4 follow-up): on recording-start arming — if the
    /// definition's MatchOnStart is set, the velocity model samples the
    /// reference frame via MatchReferenceVelocity(anchorFrame); otherwise it
    /// is Reset(). Previewing→Armed on arming; Armed→RecordingDriven on the
    /// first captured-frame evaluation. The velocity model is owned by this
    /// camera: created on activation, Dispose()d on every release path
    /// (P1-C4 ruling D-C4-7). Gravity for the free velocity component comes
    /// from FlightGlobals.currentMainBody (P1-C4 ruling follow-up).
    ///
    /// Offset composition (pinned, D-C4-2): the vessel payload offset is
    /// (Right, Up, Forward) meters along the anchor part's local axes plus
    /// Distance meters along the anchor's local −Z (backward, flight-cam-like;
    /// Distance positive = behind). The body payload offset is none — the
    /// altitude already encodes it and BodyAnchorFrame.ResolvePosition owns
    /// the ENU composition. The payload's Distance fold (Forward − Distance)
    /// is applied in exactly one place — ComposeHoldPosition, which maps the
    /// DTO payload onto anchor-local components — while the composition of
    /// payload + ManualOffset + integrated displacement into a world position
    /// lives solely in VelocityModel (impediment D-C5-1 ruling MODIFY fixed
    /// the axis order of VelocityModel's vessel composition, so this class
    /// consumes EvaluatePosition instead of duplicating it). The velocity
    /// model is consumed for integration, arming, runtime state, and hold
    /// composition.
    ///
    /// Parent-steal discipline (pinned): every evaluation while active calls
    /// FlightCameraSeizure.AssertControl(). Unavailable states set a reason
    /// string and never throw (parent spec §5.4); entering Unavailable runs
    /// the full release (seizure restore plus velocity-model disposal), and a
    /// later evaluation that resolves again re-acquires and resumes at
    /// Previewing — the velocity state restarts from the model defaults.
    ///
    /// Krakensbane discipline (build-program invariant 2): no world-space pose
    /// is stored in this class; every evaluation re-derives position and
    /// orientation from the anchor frames and the anchor-local velocity
    /// state. The ICamera Position getter reads the live seized transform for
    /// display only and never feeds evaluation.
    /// </summary>
    public sealed class NativeCamera : ICamera
    {
        private const string CameraIdPrefix = "native:";

        // Unavailable reason strings (pre-UI; centralized with the library UI in P3).
        private const string ReasonNoActiveVessel = "no active vessel to anchor to";
        private const string ReasonAnchorUnresolved = "anchor reference is unresolvable";

        private readonly CameraDefinition _definition;
        private readonly NativeTargetResolver _targetResolver;
        private readonly NativeZoomController _zoomController;

        private VelocityModel _velocityModel;
        private NativeCameraState _state = NativeCameraState.Inactive;
        private string _unavailableReason;

        /// <summary>
        /// Creates the native camera for a library definition. The definition
        /// is read live on each evaluation: edits written through while the
        /// camera is active take effect on the next evaluation (same
        /// live-read contract as VelocityModel).
        /// </summary>
        /// <param name="definition">The camera definition. Must not be null and
        /// must carry non-null Velocity and Zoom settings (the DTO defaults do).</param>
        public NativeCamera(CameraDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (definition.Velocity == null) throw new ArgumentNullException(nameof(definition), "The camera definition must carry velocity model settings.");
            if (definition.Zoom == null) throw new ArgumentNullException(nameof(definition), "The camera definition must carry zoom settings.");
            _definition = definition;
            _targetResolver = new NativeTargetResolver(definition);
            _zoomController = new NativeZoomController(definition.Zoom);
        }

        #region ICamera
        /// <summary>
        /// True in the live camera states: Previewing, Armed, or
        /// RecordingDriven. Unavailable and Inactive are not active.
        /// </summary>
        public bool IsActive
        {
            get
            {
                return _state == NativeCameraState.Previewing
                    || _state == NativeCameraState.Armed
                    || _state == NativeCameraState.RecordingDriven;
            }
        }

        /// <summary>Camera identifier for UI display: the library camera name.</summary>
        public string DisplayName
        {
            get { return _definition.Name; }
        }

        /// <summary>
        /// Unique identifier for persistence: the "native:" prefix plus the
        /// library camera name (the name is the uniqueness key of the library).
        /// </summary>
        public string CameraId
        {
            get { return CameraIdPrefix + _definition.Name; }
        }

        /// <summary>
        /// World position of the camera. Display-only: reads the live seized
        /// flight camera transform when holding, zero otherwise. Never used in
        /// evaluation math, and the setter is a deliberate no-op — the native
        /// pose is fully definition-driven and nothing may push a world
        /// position into the frame-discriminated model (build-program
        /// invariant 2).
        /// </summary>
        public Vector3 Position
        {
            get
            {
                if (!FlightCameraSeizure.Instance.IsHolding) return Vector3.zero;
                FlightCamera flightCamera = FlightCamera.fetch;
                return flightCamera != null ? flightCamera.transform.position : Vector3.zero;
            }
            set { }
        }

        /// <summary>
        /// Current manual FOV target in degrees (clamped). Setting it writes
        /// through to the definition's zoom settings and applies from the next
        /// evaluation on; the actual write to the flight camera stays the
        /// zoom controller's job (ruling D-1(a)).
        /// </summary>
        public float FieldOfView
        {
            get { return _zoomController.CurrentTargetFov; }
            set { _zoomController.SetTargetFov(value, false); }
        }

        /// <summary>Maximum FOV supported by a native camera.</summary>
        public float MaxFieldOfView
        {
            get { return NativeZoomController.MaxFieldOfView; }
        }

        /// <summary>Minimum FOV supported by a native camera.</summary>
        public float MinFieldOfView
        {
            get { return NativeZoomController.MinFieldOfView; }
        }

        /// <summary>
        /// Applies an immediate FOV without smoothing: writes through to the
        /// definition's zoom settings and applies it in this call. The actual
        /// write to the flight camera stays the zoom controller's job and
        /// remains IVA-gated inside it (ruling D-1(a)).
        /// </summary>
        /// <param name="fov">Field of view to apply, in degrees.</param>
        public void SetFieldOfViewImmediate(float fov)
        {
            _zoomController.SetTargetFov(fov, true);
        }

        /// <summary>Raised when the camera activates (seizure acquired, Previewing).</summary>
        public event Action OnActivated;

        /// <summary>Raised when an active camera fully releases back to Inactive.</summary>
        public event Action OnDeactivated;

        /// <summary>
        /// ICamera per-frame hook: forwards to <see cref="Evaluate"/> with the
        /// caller-supplied step delta. The parameter is named independently of
        /// the interface so no engine-clock token appears in this file
        /// (build-program invariant 1).
        /// </summary>
        public void Update(float stepSeconds)
        {
            Evaluate(stepSeconds);
        }

        /// <summary>
        /// Deactivate without reverting to the main camera, for switching
        /// between cameras. For a native camera the physical release IS the
        /// revert (the seizure restores the stock camera snapshot, and there
        /// is no non-reverting hold), so this runs the same physical release
        /// as <see cref="Deactivate"/> but does not raise OnDeactivated — the
        /// switching owner activates the next camera immediately after.
        /// </summary>
        public void ReleaseControl()
        {
            if (_state == NativeCameraState.Inactive) return;
            _state = NativeCameraState.Releasing;
            FlightCameraSeizure.Instance.Release();
            DisposeVelocityModel();
            _state = NativeCameraState.Inactive;
        }
        #endregion

        #region Native API
        /// <summary>The camera's current state in the parent-spec §5.2 state machine.</summary>
        public NativeCameraState CurrentState
        {
            get { return _state; }
        }

        /// <summary>
        /// Why the camera is Unavailable; null in every other state. Never
        /// throws: unresolvable states are reported here, not thrown.
        /// </summary>
        public string UnavailableReason
        {
            get { return _state == NativeCameraState.Unavailable ? _unavailableReason : null; }
        }

        /// <summary>True when the camera is not in the Unavailable state.</summary>
        public bool IsAvailable
        {
            get { return _state != NativeCameraState.Unavailable; }
        }

        /// <summary>
        /// Activates the camera (Inactive→Previewing): resolves the anchor as
        /// a preflight (an unresolvable anchor surfaces Unavailable with a
        /// reason instead of throwing, parent spec §5.4), acquires the seizure
        /// (no-op when already holding, when the flight camera is unavailable,
        /// or while IVA is active — in those cases the camera simply stays
        /// Inactive), creates the velocity model, and raises OnActivated.
        /// Calling Activate while Unavailable retries the preflight.
        /// </summary>
        public void Activate()
        {
            if (_state != NativeCameraState.Inactive && _state != NativeCameraState.Unavailable) return;

            AnchorFrame anchor;
            string reason;
            if (!ResolveAnchorFrame(out anchor, out reason))
            {
                EnterUnavailable(reason);
                return;
            }

            if (!FlightCameraSeizure.Instance.Acquire()) return;

            EnsureVelocityModel();
            _state = NativeCameraState.Previewing;
            OnActivated?.Invoke();
        }

        /// <summary>
        /// Deactivates the camera from any state (Any→Releasing→Inactive):
        /// releases the seizure (full snapshot restore, idempotent), disposes
        /// the velocity model, and raises OnDeactivated when the camera was
        /// active. Deactivation always runs the full release path (parent
        /// spec §5.2).
        /// </summary>
        public void Deactivate()
        {
            if (_state == NativeCameraState.Inactive) return;

            bool wasActive = IsActive;
            _state = NativeCameraState.Releasing;

            FlightCameraSeizure.Instance.Release();
            DisposeVelocityModel();

            _state = NativeCameraState.Inactive;
            if (wasActive) OnDeactivated?.Invoke();
        }

        /// <summary>
        /// Arms the camera for recording start (Previewing→Armed): resolves
        /// the anchor and initializes the velocity state per the pinned arm
        /// path — MatchReferenceVelocity(anchorFrame) when the definition's
        /// MatchOnStart is set, Reset() otherwise. Returns false (and surfaces
        /// Unavailable when the anchor is unresolvable) from any state other
        /// than Previewing.
        /// </summary>
        public bool ArmForRecording()
        {
            if (_state != NativeCameraState.Previewing) return false;

            AnchorFrame anchor;
            string reason;
            if (!ResolveAnchorFrame(out anchor, out reason))
            {
                EnterUnavailable(reason);
                return false;
            }

            EnsureVelocityModel();
            if (_definition.Velocity.MatchOnStart)
                MatchReferenceVelocity(anchor);
            else
                _velocityModel.Reset();

            _state = NativeCameraState.Armed;
            return true;
        }

        /// <summary>
        /// The single pose+orientation evaluation, parameterized by the step
        /// delta the caller supplies (sim-step duration on the capture path,
        /// caller-measured step on the preview path). This method never reads
        /// any engine clock. Per evaluation while active: re-asserts seizure
        /// control (parent-steal guard), re-resolves the anchor, advances the
        /// velocity model by the step (an invalid — negative or non-finite —
        /// step is skipped rather than thrown, keeping a caller bug from
        /// killing a capture), composes the pinned hold position, resolves the
        /// target and orientation, drives the seized rig, and writes the FOV.
        /// The first evaluation while Armed flips the state to
        /// RecordingDriven.
        ///
        /// Evaluation in the Unavailable state retries the anchor: when it
        /// resolves again the camera re-acquires the seizure and resumes at
        /// Previewing (the velocity state restarts from the model defaults).
        /// In Inactive and Releasing this is a no-op.
        /// </summary>
        /// <param name="stepSeconds">Duration of the step being evaluated, in
        /// seconds; must be non-negative and finite for velocity integration
        /// to advance.</param>
        public void Evaluate(double stepSeconds)
        {
            if (_state == NativeCameraState.Inactive || _state == NativeCameraState.Releasing) return;

            FlightCameraSeizure.Instance.AssertControl();

            AnchorFrame anchor;
            string reason;
            if (!ResolveAnchorFrame(out anchor, out reason))
            {
                EnterUnavailable(reason);
                return;
            }

            if (_state == NativeCameraState.Unavailable)
            {
                if (!FlightCameraSeizure.Instance.Acquire()) return;
                EnsureVelocityModel();
                _state = NativeCameraState.Previewing;
            }

            if (stepSeconds >= 0.0 && !double.IsNaN(stepSeconds) && !double.IsInfinity(stepSeconds))
                StepVelocity(anchor, stepSeconds);

            Vector3d position = ComposeHoldPosition(anchor);

            Vessel referenceVessel = anchor.IsBody
                ? FlightGlobals.ActiveVessel
                : anchor.VesselAnchor.Vessel;
            TargetResolution resolution = _targetResolver.ResolveTarget(referenceVessel);
            if (!resolution.IsResolvable)
            {
                EnterUnavailable(resolution.UnavailableReason);
                return;
            }

            position = _targetResolver.OrbitPositionAboutTarget(
                position, resolution, Quaternion.identity, _definition.PivotMode);

            Quaternion anchorOrientation = anchor.IsBody ? anchor.BodyAnchor.Rotation : anchor.VesselAnchor.Rotation;
            Quaternion orientation = _targetResolver.ComputeOrientation(position, anchorOrientation, Quaternion.identity, resolution);

            Vector3 worldPosition = new Vector3((float)position.x, (float)position.y, (float)position.z);
            FlightCameraSeizure.Instance.SetRigWorldPose(worldPosition, orientation);

            _zoomController.Evaluate();

            if (_state == NativeCameraState.Armed)
                _state = NativeCameraState.RecordingDriven;
        }
        #endregion

        #region Private Helpers
        private struct AnchorFrame
        {
            public readonly BodyAnchorFrame BodyAnchor;
            public readonly VesselAnchorFrame VesselAnchor;

            private AnchorFrame(BodyAnchorFrame bodyAnchor, VesselAnchorFrame vesselAnchor)
            {
                BodyAnchor = bodyAnchor;
                VesselAnchor = vesselAnchor;
            }

            public bool IsBody
            {
                get { return VesselAnchor == null; }
            }

            public static AnchorFrame ForBody(BodyAnchorFrame frame)
            {
                return new AnchorFrame(frame, null);
            }

            public static AnchorFrame ForVessel(VesselAnchorFrame frame)
            {
                return new AnchorFrame(null, frame);
            }
        }

        private bool ResolveAnchorFrame(out AnchorFrame anchor, out string reason)
        {
            anchor = default(AnchorFrame);
            reason = null;

            CameraAnchor.Body body = _definition.Anchor as CameraAnchor.Body;
            if (body != null)
            {
                // ResolveBody never returns null: unknown or empty body names
                // fall back to the current main body, so a body anchor always
                // resolves in P1 (impediment D-C5-4).
                CelestialBody celestialBody = GeographicCoordinateSystem.ResolveBody(body.Coordinate.BodyName);
                anchor = AnchorFrame.ForBody(new BodyAnchorFrame(
                    celestialBody, body.Coordinate.Latitude, body.Coordinate.Longitude, body.Coordinate.Altitude));
                return true;
            }

            CameraAnchor.Vessel vessel = _definition.Anchor as CameraAnchor.Vessel;
            if (vessel != null)
            {
                // The DTO carries no vessel identity (spec §4.4); anchors
                // resolve against the active vessel (D-C5-5).
                Vessel activeVessel = FlightGlobals.ActiveVessel;
                if (activeVessel == null)
                {
                    reason = ReasonNoActiveVessel;
                    return false;
                }

                VesselAnchorFrame frame = new VesselAnchorFrame(
                    activeVessel,
                    vessel.AnchorPartPersistentId,
                    vessel.FallbackPolicy,
                    _definition.FrameMode);
                if (!frame.IsResolved)
                {
                    reason = ReasonAnchorUnresolved;
                    return false;
                }

                anchor = AnchorFrame.ForVessel(frame);
                return true;
            }

            reason = ReasonAnchorUnresolved;
            return false;
        }

        private void EnsureVelocityModel()
        {
            if (_velocityModel == null)
                _velocityModel = new VelocityModel(_definition);
        }

        private void DisposeVelocityModel()
        {
            if (_velocityModel != null)
            {
                _velocityModel.Dispose();
                _velocityModel = null;
            }
        }

        private void MatchReferenceVelocity(AnchorFrame anchor)
        {
            if (anchor.IsBody)
                _velocityModel.MatchReferenceVelocity(anchor.BodyAnchor);
            else
                _velocityModel.MatchReferenceVelocity(anchor.VesselAnchor);
        }

        private void StepVelocity(AnchorFrame anchor, double stepSeconds)
        {
            // Gravity source pinned per the P1-C4 ruling follow-up: the
            // current main body; null when there is none (no gravity then).
            CelestialBody gravitySource = FlightGlobals.currentMainBody;
            if (anchor.IsBody)
                _velocityModel.Step(anchor.BodyAnchor, stepSeconds, gravitySource);
            else
                _velocityModel.Step(anchor.VesselAnchor, stepSeconds, gravitySource);
        }

        private Vector3d ComposeHoldPosition(AnchorFrame anchor)
        {
            // Body payload = none (pinned D-C4-2): the altitude already encodes
            // it and BodyAnchorFrame.ResolvePosition owns the ENU composition;
            // VelocityModel composes the manual offset plus the integrated
            // displacement there.
            if (anchor.IsBody)
                return _velocityModel.EvaluatePosition(anchor.BodyAnchor);

            // Vessel payload mapping (pinned D-C4-2): (Right, Up, Forward)
            // meters along the anchor part's local axes plus Distance meters
            // along the anchor's local -Z (Distance positive = behind). In the
            // anchor-local component basis (x = forward along part +Z, y =
            // right, z = up) that fold is Forward - Distance on the forward
            // axis — applied here, in this one place; the offset-to-world
            // composition itself is VelocityModel's single source of truth
            // (impediment D-C5-1 ruling MODIFY fixed its axis order).
            CameraAnchor.Vessel vesselAnchor = (CameraAnchor.Vessel)_definition.Anchor;
            AnchorLocalOffset payloadOffset = new AnchorLocalOffset(
                vesselAnchor.Forward - vesselAnchor.Distance,
                vesselAnchor.Right,
                vesselAnchor.Up);
            return _velocityModel.EvaluatePosition(anchor.VesselAnchor, payloadOffset);
        }

        private void EnterUnavailable(string reason)
        {
            _unavailableReason = reason ?? ReasonAnchorUnresolved;
            FlightCameraSeizure.Instance.Release();
            DisposeVelocityModel();
            _state = NativeCameraState.Unavailable;
        }
        #endregion
    }
}
