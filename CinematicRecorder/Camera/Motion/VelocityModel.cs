using System;
using CinematicRecorder.Camera.Frames;
using CinematicRecorder.Camera.Model;
using UnityEngine;

namespace CinematicRecorder.Camera.Motion
{
    /// <summary>
    /// Per-camera inertial velocity model and hold evaluation (parent spec §5.3
    /// "Inertial velocity model" + "Hold behavior"; build-program chunk P1-C4,
    /// decisions D-03/D-09). Owns the runtime velocity state the DTO deliberately
    /// does not carry (ruling P1-C1 D5): the camera's current velocity and its
    /// integrated displacement from the anchor. Both are stored as components in
    /// the camera's anchor-local frame (x = forward, y = right, z = up along the
    /// anchor axes), so a floating-origin shift — a pure translation of world
    /// positions — cannot corrupt them (build-program invariant 2, Krakensbane
    /// discipline). Every step is driven by the physics step duration the caller
    /// passes in; no engine clock API is read anywhere in this class
    /// (build-program invariant 1).
    ///
    /// Per physics step (parent spec §5.3): the free component adds the current
    /// main body's gravitational acceleration (GM/r^2 toward the body center,
    /// computed here from the body the caller passes in) to the velocity; the
    /// bound component forces the velocity to the reference frame's velocity the
    /// anchor frame exposes (P1-C2; the Surface/Orbit selection lives there).
    /// The blend v = (1-k)*v_integrated + k*v_reference then advances the
    /// integrated displacement. Rigidity k = 1 re-pins by construction: the
    /// velocity becomes the reference velocity and the displacement is forced to
    /// zero every step, so the pose is the anchor frame composed with the
    /// camera's offset alone — drift is impossible by construction (the
    /// #003-class killer).
    ///
    /// Krakensbane discipline: the only world-space values this model holds are
    /// locals re-derived per step from the anchor frame; nothing world-space
    /// persists across steps, so a shift leaves nothing stale. The model still
    /// subscribes KSP's floating-origin shift event — GameEvents
    /// .onFloatingOriginShift, EventData&lt;Vector3d, Vector3d&gt; (decompiled
    /// GameEvents.cs:463) — which FloatingOrigin.setOffset fires only after KSP
    /// has corrected every body position (FloatingOrigin.cs:467-471), vessel
    /// position (FloatingOrigin.cs:660) and vessel center of mass
    /// (FloatingOrigin.cs:687-688) itself (FloatingOrigin.cs:821). That
    /// after-KSP-pass ordering is the mandatory BetterLateThanNever discipline
    /// (synthesis #1 risk); the handler is the seam where any future world-space
    /// working state must be corrected, and documents why today it corrects
    /// nothing.
    /// </summary>
    public sealed class VelocityModel : IDisposable
    {
        private readonly CameraDefinition _definition;
        private Vector3d _velocity;
        private Vector3d _displacement;
        private AnchorLocalOffset _initialRelativeVelocity;

        /// <summary>
        /// Creates the velocity model for a camera definition. The definition is
        /// read live on each step: rigidity/gravity edits written through while
        /// the camera is active take effect on the next step, and the manual
        /// offset is re-read on each pose evaluation. The model subscribes the
        /// floating-origin shift event here; the owner (the camera controller of
        /// P1-C5) must call <see cref="Dispose"/> when the camera is released.
        /// </summary>
        /// <param name="definition">The camera definition supplying the velocity
        /// model settings and the manual offset. Must not be null and must carry
        /// a non-null <see cref="CameraDefinition.Velocity"/>.</param>
        public VelocityModel(CameraDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (definition.Velocity == null) throw new ArgumentNullException(nameof(definition), "The camera definition must carry velocity model settings.");
            _definition = definition;
            _velocity = Vector3d.zero;
            _displacement = Vector3d.zero;
            _initialRelativeVelocity = new AnchorLocalOffset(0.0, 0.0, 0.0);
            GameEvents.onFloatingOriginShift.Add(OnFloatingOriginShift);
        }

        /// <summary>
        /// Relative initial velocity in the camera's anchor-local frame, added on
        /// top of the sampled reference velocity by
        /// <see cref="MatchReferenceVelocity"/> and used on its own by
        /// <see cref="Reset"/> (parent spec §5.3 "plus any stored relative
        /// initial velocity" — runtime state per ruling P1-C1 D5, not DTO data).
        /// Set this before arming the camera. Defaults to zero.
        /// </summary>
        public AnchorLocalOffset InitialRelativeVelocity
        {
            get { return _initialRelativeVelocity; }
            set { _initialRelativeVelocity = value; }
        }

        /// <summary>
        /// The camera's current velocity, stored as components in the
        /// anchor-local frame (x = forward, y = right, z = up), in m/s. Runtime
        /// state owned by this model; never persisted.
        /// </summary>
        public Vector3d Velocity
        {
            get { return _velocity; }
        }

        /// <summary>
        /// The integrated displacement from the anchor, stored as components in
        /// the anchor-local frame (x = forward, y = right, z = up), in meters.
        /// Runtime state owned by this model; never persisted (build-program
        /// invariant 2). Forced to zero every step while rigidity is 1.
        /// </summary>
        public Vector3d Displacement
        {
            get { return _displacement; }
        }

        /// <summary>
        /// Initializes the velocity state for an arm-without-match start: the
        /// velocity becomes <see cref="InitialRelativeVelocity"/> and the
        /// displacement is cleared (parent spec §5.1: when match-on-start is off,
        /// "the stored initial velocity is used").
        /// </summary>
        public void Reset()
        {
            _velocity = ToComponents(_initialRelativeVelocity);
            _displacement = Vector3d.zero;
        }

        /// <summary>
        /// Match-on-start for a body-anchored camera: samples the reference
        /// frame's velocity (the inertial velocity of the ground point under the
        /// anchor, from the anchor frame's <see cref="BodyAnchorFrame.Velocity"/>),
        /// converts it into the anchor-local frame, adds
        /// <see cref="InitialRelativeVelocity"/> and stores the result as the
        /// camera's velocity; the displacement is cleared so the run starts from
        /// the anchor. Called by the arming path (P1-C5/C7) when
        /// <c>MatchOnStart</c> is set; this model never self-triggers.
        /// </summary>
        /// <param name="anchor">The resolved body anchor frame to sample.</param>
        public void MatchReferenceVelocity(BodyAnchorFrame anchor)
        {
            if (anchor == null) throw new ArgumentNullException(nameof(anchor));
            _velocity = ToAnchor(anchor, anchor.Velocity) + ToComponents(_initialRelativeVelocity);
            _displacement = Vector3d.zero;
        }

        /// <summary>
        /// Match-on-start for a vessel-anchored camera: samples the reference
        /// frame's velocity per the anchor frame's Surface/Orbit mode (from
        /// <see cref="VesselAnchorFrame.Velocity"/>), converts it into the
        /// anchor-local frame via the anchor rotation, adds
        /// <see cref="InitialRelativeVelocity"/> and stores the result as the
        /// camera's velocity; the displacement is cleared so the run starts from
        /// the anchor. Called by the arming path (P1-C5/C7) when
        /// <c>MatchOnStart</c> is set; this model never self-triggers.
        /// </summary>
        /// <param name="anchor">The resolved vessel anchor frame to sample. When
        /// the anchor is unresolved the frame reports a zero velocity; callers
        /// must surface such cameras as Unavailable instead of arming them
        /// (parent spec §5.4).</param>
        public void MatchReferenceVelocity(VesselAnchorFrame anchor)
        {
            if (anchor == null) throw new ArgumentNullException(nameof(anchor));
            _velocity = RotateInverse(anchor.Rotation, anchor.Velocity) + ToComponents(_initialRelativeVelocity);
            _displacement = Vector3d.zero;
        }

        /// <summary>
        /// Advances the model by one physics step for a body-anchored camera.
        /// The step duration is supplied by the caller (the physics step length
        /// from the physics-stepped hook wired in P1-C7); no engine clock is
        /// read. Evaluation order per step: gravity is added to the free
        /// velocity when the camera's gravity toggle is on and a body is
        /// supplied; the bound component is the anchor frame's reference
        /// velocity; the blend v = (1-k)*v_integrated + k*v_reference updates
        /// the velocity and advances the displacement. Rigidity k = 1 re-pins:
        /// the velocity becomes the reference velocity and the displacement is
        /// forced to zero, so the pose is the anchor composed with the camera's
        /// offset alone every step.
        /// </summary>
        /// <param name="anchor">The resolved body anchor frame.</param>
        /// <param name="stepSeconds">Duration of the physics step in seconds;
        /// must be non-negative.</param>
        /// <param name="gravitySource">The body whose gravity acts on the free
        /// component this step (the current main body), or null for no gravity
        /// even when the toggle is on.</param>
        public void Step(BodyAnchorFrame anchor, double stepSeconds, CelestialBody gravitySource)
        {
            if (anchor == null) throw new ArgumentNullException(nameof(anchor));
            ValidateStepSeconds(stepSeconds);
            AnchorLocalOffset local = Add(_definition.ManualOffset, _displacement);
            Vector3d worldPosition = anchor.ResolvePosition(local);
            StepCore(anchor.North, anchor.East, anchor.Up, worldPosition, anchor.Velocity, stepSeconds, gravitySource);
        }

        /// <summary>
        /// Advances the model by one physics step for a vessel-anchored camera.
        /// The step duration is supplied by the caller (the physics step length
        /// from the physics-stepped hook wired in P1-C7); no engine clock is
        /// read. Evaluation order per step: gravity is added to the free
        /// velocity when the camera's gravity toggle is on and a body is
        /// supplied; the bound component is the anchor frame's reference
        /// velocity (Surface or Orbit per the frame's mode); the blend
        /// v = (1-k)*v_integrated + k*v_reference updates the velocity and
        /// advances the displacement. Rigidity k = 1 re-pins: the velocity
        /// becomes the reference velocity and the displacement is forced to
        /// zero, so the pose is the anchor composed with the camera's offset
        /// alone every step.
        /// </summary>
        /// <param name="anchor">The resolved vessel anchor frame. When the anchor
        /// is unresolved the frame reports zero position and velocity; callers
        /// must surface such cameras as Unavailable instead of evaluating them
        /// (parent spec §5.4).</param>
        /// <param name="stepSeconds">Duration of the physics step in seconds;
        /// must be non-negative.</param>
        /// <param name="gravitySource">The body whose gravity acts on the free
        /// component this step (the current main body), or null for no gravity
        /// even when the toggle is on.</param>
        public void Step(VesselAnchorFrame anchor, double stepSeconds, CelestialBody gravitySource)
        {
            if (anchor == null) throw new ArgumentNullException(nameof(anchor));
            ValidateStepSeconds(stepSeconds);
            AnchorLocalOffset local = Add(_definition.ManualOffset, _displacement);
            Vector3d worldPosition = ComposePosition(anchor, local);
            Vector3d forwardAxis = anchor.Rotation * Vector3.forward;
            Vector3d rightAxis = anchor.Rotation * Vector3.right;
            Vector3d upAxis = anchor.Rotation * Vector3.up;
            StepCore(forwardAxis, rightAxis, upAxis, worldPosition, anchor.Velocity, stepSeconds, gravitySource);
        }

        /// <summary>
        /// Hold/inertial camera position for a body-anchored camera: the anchor
        /// frame composed with the camera's manual offset plus the integrated
        /// displacement (parent spec §5.3 hold behavior). The composition runs
        /// through the anchor frame's own resolver so the East-North-Up axis
        /// handedness stays with P1-C2; the returned world position is
        /// re-derived from the anchor on every call and is never stored
        /// (build-program invariant 2).
        /// </summary>
        /// <param name="anchor">The resolved body anchor frame.</param>
        public Vector3d EvaluatePosition(BodyAnchorFrame anchor)
        {
            if (anchor == null) throw new ArgumentNullException(nameof(anchor));
            return anchor.ResolvePosition(Add(_definition.ManualOffset, _displacement));
        }

        /// <summary>
        /// Hold/inertial camera position for a vessel-anchored camera: the
        /// anchor frame composed with the camera's manual offset plus the
        /// integrated displacement (parent spec §5.3 hold behavior). The
        /// stored anchor-payload offset (Distance/Forward/Right/Up) is hold
        /// composition owned by the camera controller (P1-C5) per the P1-C2
        /// ruling: the controller maps the DTO payload onto anchor-local
        /// components (Distance along anchor -Z folds into Forward) and hands
        /// it to <see cref="EvaluatePosition(VesselAnchorFrame, AnchorLocalOffset)"/>;
        /// this overload composes the manual offset and the displacement only.
        /// The returned world position is re-derived from the anchor on every
        /// call and is never stored (build-program invariant 2).
        /// </summary>
        /// <param name="anchor">The resolved vessel anchor frame. When the anchor
        /// is unresolved the frame reports a zero position; callers must surface
        /// such cameras as Unavailable instead of evaluating them (parent spec
        /// §5.4).</param>
        public Vector3d EvaluatePosition(VesselAnchorFrame anchor)
        {
            if (anchor == null) throw new ArgumentNullException(nameof(anchor));
            return ComposePosition(anchor, Add(_definition.ManualOffset, _displacement));
        }

        /// <summary>
        /// Hold/inertial camera position for a vessel-anchored camera carrying
        /// its stored anchor payload: the anchor frame composed with
        /// <paramref name="payloadOffset"/> plus the manual offset plus the
        /// integrated displacement (parent spec §5.3 hold behavior; pinned
        /// offset composition of CHUNK P1-C5). The payload must already be
        /// expressed in anchor-local components — the Distance-along-anchor
        /// -Z fold (Forward - Distance) is applied by the caller, in exactly
        /// one place (NativeCamera.ComposeHoldPosition), so this class is the
        /// single source of truth for composing an anchor-local offset into a
        /// world position. The returned world position is re-derived from the
        /// anchor on every call and is never stored (build-program invariant 2).
        /// </summary>
        /// <param name="anchor">The resolved vessel anchor frame. When the anchor
        /// is unresolved the frame reports a zero position; callers must surface
        /// such cameras as Unavailable instead of evaluating them (parent spec
        /// §5.4).</param>
        /// <param name="payloadOffset">The stored anchor payload already mapped
        /// to anchor-local components (x = forward, y = right, z = up), with
        /// any Distance fold applied by the caller.</param>
        public Vector3d EvaluatePosition(VesselAnchorFrame anchor, AnchorLocalOffset payloadOffset)
        {
            if (anchor == null) throw new ArgumentNullException(nameof(anchor));
            return ComposePosition(anchor, Sum(payloadOffset, Add(_definition.ManualOffset, _displacement)));
        }

        /// <summary>
        /// Stops listening to the floating-origin shift event. Idempotent; the
        /// owner (the camera controller of P1-C5) calls this when the camera is
        /// released. After disposal the model's math API keeps working (the
        /// event carried no state for it), but a disposed model must not be
        /// reused for a new activation.
        /// </summary>
        public void Dispose()
        {
            GameEvents.onFloatingOriginShift.Remove(OnFloatingOriginShift);
        }

        /// <summary>
        /// Krakensbane-discipline correction hook, fired by
        /// FloatingOrigin.setOffset only after KSP has shifted and corrected
        /// every celestial body position (decompiled FloatingOrigin.cs:467-471),
        /// every vessel position (FloatingOrigin.cs:660) and every vessel center
        /// of mass (FloatingOrigin.cs:687-688) itself (event fired at
        /// FloatingOrigin.cs:821, GameEvents.cs:463). Subscribing with this
        /// after-KSP-pass ordering is the mandatory BetterLateThanNever
        /// discipline (synthesis #1 risk). This model persists no world-space
        /// state — the velocity and displacement live in anchor-local
        /// components and every world-space value is re-derived per step from
        /// the anchor frame — and a floating-origin shift is a pure translation,
        /// so there is nothing here to correct. Any future world-space working
        /// state added to this class MUST be corrected in this handler, after
        /// KSP's own pass.
        /// </summary>
        private void OnFloatingOriginShift(Vector3d offset, Vector3d nonKrakensbaneOffset)
        {
        }

        /// <summary>
        /// Single per-step evaluation shared by both anchor kinds. The frame
        /// type-specific conversions (ENU axes vs anchor rotation) happen in the
        /// public Step overloads; everything here is pure anchor-frame math.
        /// Order: re-pin short-circuit for k = 1, then gravity on the free
        /// component, then the rigidity blend, then displacement integration.
        /// </summary>
        private void StepCore(Vector3d forwardAxis, Vector3d rightAxis, Vector3d upAxis, Vector3d cameraWorldPosition, Vector3d referenceVelocityWorld, double stepSeconds, CelestialBody gravitySource)
        {
            Vector3d referenceVelocity = WorldToAnchor(forwardAxis, rightAxis, upAxis, referenceVelocityWorld);
            double k = ClampRigidity(_definition.Velocity.Rigidity);

            if (k >= 1.0)
            {
                _velocity = referenceVelocity;
                _displacement = Vector3d.zero;
                return;
            }

            Vector3d gravity = Vector3d.zero;
            if (_definition.Velocity.Gravity && gravitySource != null)
            {
                Vector3d toBodyCenter = gravitySource.position - cameraWorldPosition;
                double rSqr = toBodyCenter.sqrMagnitude;
                if (rSqr > 0.0)
                {
                    gravity = WorldToAnchor(forwardAxis, rightAxis, upAxis, toBodyCenter * (gravitySource.gravParameter / (rSqr * Math.Sqrt(rSqr))));
                }
            }

            Vector3d integrated = _velocity + gravity * stepSeconds;
            _velocity = integrated * (1.0 - k) + referenceVelocity * k;
            _displacement = _displacement + _velocity * stepSeconds;
        }

        /// <summary>
        /// Converts a world-space vector into anchor-local components by
        /// projecting onto the anchor's orthonormal basis (x = forward, y =
        /// right, z = up).
        /// </summary>
        private static Vector3d WorldToAnchor(Vector3d forwardAxis, Vector3d rightAxis, Vector3d upAxis, Vector3d world)
        {
            return new Vector3d(
                Vector3d.Dot(world, forwardAxis),
                Vector3d.Dot(world, rightAxis),
                Vector3d.Dot(world, upAxis));
        }

        /// <summary>
        /// Converts a world-space vector into a body frame's anchor-local
        /// components: forward along North, right along East, up along Up —
        /// the same mapping <see cref="BodyAnchorFrame.ResolvePosition"/>
        /// applies in the opposite direction.
        /// </summary>
        private static Vector3d ToAnchor(BodyAnchorFrame anchor, Vector3d world)
        {
            return new Vector3d(
                Vector3d.Dot(world, anchor.North),
                Vector3d.Dot(world, anchor.East),
                Vector3d.Dot(world, anchor.Up));
        }

        /// <summary>
        /// Applies the inverse of the anchor rotation to a world-space vector
        /// with double arithmetic (the quaternion components are the anchor
        /// transform's own, re-derived by the caller on every step).
        /// </summary>
        private static Vector3d RotateInverse(Quaternion rotation, Vector3d world)
        {
            double x = -rotation.x;
            double y = -rotation.y;
            double z = -rotation.z;
            double w = rotation.w;
            double vx = world.x;
            double vy = world.y;
            double vz = world.z;
            double tx = 2.0 * (y * vz - z * vy);
            double ty = 2.0 * (z * vx - x * vz);
            double tz = 2.0 * (x * vy - y * vx);
            return new Vector3d(
                vx + w * tx + (y * tz - z * ty),
                vy + w * ty + (z * tx - x * tz),
                vz + w * tz + (x * ty - y * tx));
        }

        /// <summary>
        /// Composes a vessel anchor frame with a local offset: the resolved
        /// anchor position plus the offset rotated into world space by the
        /// anchor's reference transform rotation. The offset components map
        /// onto part-local space as x = Right, y = Up, z = Forward — the
        /// anchor basis this class documents and integrates in (forward along
        /// part +Z, right along +X, up along +Y; impediment D-C5-1 ruling
        /// MODIFY fixed an x/z-transposed construction here).
        /// </summary>
        private static Vector3d ComposePosition(VesselAnchorFrame anchor, AnchorLocalOffset local)
        {
            Vector3 worldOffset = anchor.Rotation * new Vector3((float)local.Right, (float)local.Up, (float)local.Forward);
            return anchor.Position + worldOffset;
        }

        /// <summary>
        /// Adds a stored anchor-local offset and an anchor-local displacement
        /// component-wise.
        /// </summary>
        private static AnchorLocalOffset Add(AnchorLocalOffset offset, Vector3d displacement)
        {
            return new AnchorLocalOffset(
                offset.Forward + displacement.x,
                offset.Right + displacement.y,
                offset.Up + displacement.z);
        }

        /// <summary>
        /// Adds two stored anchor-local offsets component-wise (payload plus
        /// manual offset; the displacement is added separately via
        /// <see cref="Add(AnchorLocalOffset, Vector3d)"/>).
        /// </summary>
        private static AnchorLocalOffset Sum(AnchorLocalOffset a, AnchorLocalOffset b)
        {
            return new AnchorLocalOffset(
                a.Forward + b.Forward,
                a.Right + b.Right,
                a.Up + b.Up);
        }

        /// <summary>
        /// Maps an anchor-local offset onto component form (x = forward, y =
        /// right, z = up).
        /// </summary>
        private static Vector3d ToComponents(AnchorLocalOffset offset)
        {
            return new Vector3d(offset.Forward, offset.Right, offset.Up);
        }

        /// <summary>
        /// Clamps the rigidity into [0, 1]. Non-finite input falls back to 1,
        /// the deterministic-hold default: a re-pinned hold is the safe failure
        /// mode, never a runaway integration.
        /// </summary>
        private static double ClampRigidity(double rigidity)
        {
            if (double.IsNaN(rigidity)) return 1.0;
            if (rigidity < 0.0) return 0.0;
            if (rigidity > 1.0) return 1.0;
            return rigidity;
        }

        /// <summary>
        /// Rejects negative or non-finite step durations; a physics step length
        /// is never negative, and failing here keeps a caller bug from becoming
        /// silent state corruption.
        /// </summary>
        private static void ValidateStepSeconds(double stepSeconds)
        {
            if (double.IsNaN(stepSeconds) || stepSeconds < 0.0)
                throw new ArgumentOutOfRangeException(nameof(stepSeconds), stepSeconds, "The physics step duration must be a non-negative, finite number of seconds.");
        }
    }
}
