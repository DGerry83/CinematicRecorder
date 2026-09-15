using System;
using CinematicRecorder.Camera.Frames;

namespace CinematicRecorder.Camera.Model
{
    /// <summary>
    /// A geodetic coordinate on a named celestial body: latitude/longitude in
    /// degrees, altitude in meters above mean sea level. Frame-encoding payload
    /// type: a coordinate is meaningless without its body, so the frame encoding
    /// (body-fixed geodetic on <see cref="BodyName"/>) is carried by the type itself.
    /// </summary>
    public struct GeoCoordinate
    {
        /// <summary>
        /// Name of the celestial body the coordinate refers to (e.g. "Kerbin").
        /// </summary>
        public string BodyName { get; }

        /// <summary>
        /// Latitude in degrees, range [-90, 90].
        /// </summary>
        public double Latitude { get; }

        /// <summary>
        /// Longitude in degrees, range [-180, 180].
        /// </summary>
        public double Longitude { get; }

        /// <summary>
        /// Altitude in meters above the body's mean sea level.
        /// </summary>
        public double Altitude { get; }

        /// <summary>
        /// Creates a geodetic coordinate on a named body.
        /// </summary>
        /// <param name="bodyName">Name of the celestial body the coordinate refers to.</param>
        /// <param name="latitude">Latitude in degrees.</param>
        /// <param name="longitude">Longitude in degrees.</param>
        /// <param name="altitude">Altitude in meters above mean sea level.</param>
        public GeoCoordinate(string bodyName, double latitude, double longitude, double altitude)
        {
            if (string.IsNullOrEmpty(bodyName)) throw new ArgumentException("Body name must not be null or empty.", nameof(bodyName));
            BodyName = bodyName;
            Latitude = latitude;
            Longitude = longitude;
            Altitude = altitude;
        }
    }

    /// <summary>
    /// Fallback policy for a vessel anchor whose stored anchor part cannot be
    /// resolved (destroyed, staged away, or the vessel reference changed).
    /// </summary>
    public enum AnchorFallbackPolicy
    {
        /// <summary>
        /// Fall back to the vessel's root part.
        /// </summary>
        RootPart,

        /// <summary>
        /// Fall back to the vessel's center of mass.
        /// </summary>
        VesselCenterOfMass
    }

    /// <summary>
    /// Pivot point for camera rotation: about the camera itself, or about the
    /// resolved target. Lifted CameraTools FMPivotMode concept, renamed natively.
    /// </summary>
    public enum CameraPivotMode
    {
        /// <summary>
        /// Rotations pivot about the camera position.
        /// </summary>
        Camera,

        /// <summary>
        /// Rotations pivot about the resolved target.
        /// </summary>
        Target
    }

    /// <summary>
    /// Discriminated anchor of a camera: where the camera is anchored.
    /// The discriminant is the runtime type of the derived payload, so an anchor
    /// can only ever carry the payload that matches its kind. Persisted positions
    /// inside the payloads carry their frame encoding explicitly in their types
    /// (frame-discriminator rule).
    /// </summary>
    public abstract class CameraAnchor
    {
        private CameraAnchor()
        {
        }

        /// <summary>
        /// Body anchor: a body-fixed geodetic point on a celestial body. The camera
        /// pose is re-derived from (body, lat, lon, alt) plus local offset every
        /// step; world coordinates are never stored.
        /// </summary>
        public sealed class Body : CameraAnchor
        {
            /// <summary>
            /// Geodetic anchor coordinate on the named body.
            /// </summary>
            public GeoCoordinate Coordinate { get; }

            internal Body(GeoCoordinate coordinate)
            {
                Coordinate = coordinate;
            }
        }

        /// <summary>
        /// Vessel anchor: a part on a vessel, with a stored local offset from the
        /// anchor part. The anchor part defaults to the vessel's active control
        /// point when no specific part is stored.
        /// </summary>
        public sealed class Vessel : CameraAnchor
        {
            /// <summary>
            /// Persistent id of the anchor part. Zero means "no part stored": the
            /// resolver uses the vessel's active control point at runtime.
            /// </summary>
            public uint AnchorPartPersistentId { get; }

            /// <summary>
            /// Fallback used when the stored anchor part cannot be resolved.
            /// </summary>
            public AnchorFallbackPolicy FallbackPolicy { get; }

            /// <summary>
            /// Distance component of the stored local offset from the anchor part, in meters.
            /// </summary>
            public double Distance { get; }

            /// <summary>
            /// Forward component of the stored local offset from the anchor part, in meters.
            /// </summary>
            public double Forward { get; }

            /// <summary>
            /// Right component of the stored local offset from the anchor part, in meters.
            /// </summary>
            public double Right { get; }

            /// <summary>
            /// Up component of the stored local offset from the anchor part, in meters.
            /// </summary>
            public double Up { get; }

            internal Vessel(uint anchorPartPersistentId, AnchorFallbackPolicy fallbackPolicy, double distance, double forward, double right, double up)
            {
                AnchorPartPersistentId = anchorPartPersistentId;
                FallbackPolicy = fallbackPolicy;
                Distance = distance;
                Forward = forward;
                Right = right;
                Up = up;
            }
        }

        /// <summary>
        /// Creates a body-fixed anchor at a geodetic coordinate.
        /// </summary>
        /// <param name="coordinate">Geodetic anchor coordinate on the named body.</param>
        public static CameraAnchor AtBody(GeoCoordinate coordinate)
        {
            if (coordinate.BodyName == null) throw new ArgumentNullException(nameof(coordinate));
            return new Body(coordinate);
        }

        /// <summary>
        /// Creates a vessel-part anchor with a stored local offset.
        /// </summary>
        /// <param name="anchorPartPersistentId">Persistent id of the anchor part, or 0 for the runtime default (active control point).</param>
        /// <param name="fallbackPolicy">Fallback used when the stored anchor part cannot be resolved.</param>
        /// <param name="distance">Distance component of the stored local offset, in meters.</param>
        /// <param name="forward">Forward component of the stored local offset, in meters.</param>
        /// <param name="right">Right component of the stored local offset, in meters.</param>
        /// <param name="up">Up component of the stored local offset, in meters.</param>
        public static CameraAnchor OnVessel(uint anchorPartPersistentId, AnchorFallbackPolicy fallbackPolicy, double distance, double forward, double right, double up)
        {
            if (distance < 0) throw new ArgumentOutOfRangeException(nameof(distance), "Distance must not be negative.");
            return new Vessel(anchorPartPersistentId, fallbackPolicy, distance, forward, right, up);
        }
    }

    /// <summary>
    /// Discriminated target of a camera: what the camera looks at. The
    /// discriminant is the runtime type of the derived payload; targets without
    /// payload (none, vessel center of mass, path direction) carry no positional
    /// data at all, and the geo-point payload carries its frame encoding
    /// explicitly in its type (frame-discriminator rule).
    /// </summary>
    public abstract class CameraTarget
    {
        private CameraTarget()
        {
        }

        /// <summary>
        /// Shared immutable instance of the no-target variant.
        /// </summary>
        public static CameraTarget None { get; } = new NoTarget();

        /// <summary>
        /// No target: the camera holds a fixed local orientation.
        /// </summary>
        public sealed class NoTarget : CameraTarget
        {
            internal NoTarget()
            {
            }
        }

        /// <summary>
        /// Target the reference vessel's center of mass.
        /// </summary>
        public sealed class VesselComTarget : CameraTarget
        {
            internal VesselComTarget()
            {
            }
        }

        /// <summary>
        /// Target a specific part (of the reference vessel, resolved at runtime).
        /// </summary>
        public sealed class PartTarget : CameraTarget
        {
            /// <summary>
            /// Persistent id of the target part.
            /// </summary>
            public uint PersistentId { get; }

            internal PartTarget(uint persistentId)
            {
                PersistentId = persistentId;
            }
        }

        /// <summary>
        /// Target a fixed geodetic point on a celestial body.
        /// </summary>
        public sealed class GeoPointTarget : CameraTarget
        {
            /// <summary>
            /// Geodetic target coordinate on the named body.
            /// </summary>
            public GeoCoordinate Coordinate { get; }

            internal GeoPointTarget(GeoCoordinate coordinate)
            {
                Coordinate = coordinate;
            }
        }

        /// <summary>
        /// Target the travel direction of the camera's own path (while a path plays).
        /// </summary>
        public sealed class PathDirectionTarget : CameraTarget
        {
            internal PathDirectionTarget()
            {
            }
        }

        /// <summary>
        /// Creates a vessel-center-of-mass target.
        /// </summary>
        public static CameraTarget ForVesselCenterOfMass()
        {
            return new VesselComTarget();
        }

        /// <summary>
        /// Creates a part target.
        /// </summary>
        /// <param name="persistentId">Persistent id of the target part.</param>
        public static CameraTarget ForPart(uint persistentId)
        {
            return new PartTarget(persistentId);
        }

        /// <summary>
        /// Creates a geodetic point target.
        /// </summary>
        /// <param name="coordinate">Geodetic target coordinate on the named body.</param>
        public static CameraTarget ForGeoPoint(GeoCoordinate coordinate)
        {
            if (coordinate.BodyName == null) throw new ArgumentNullException(nameof(coordinate));
            return new GeoPointTarget(coordinate);
        }

        /// <summary>
        /// Creates a path-direction target.
        /// </summary>
        public static CameraTarget ForPathDirection()
        {
            return new PathDirectionTarget();
        }
    }

    /// <summary>
    /// Per-camera inertial velocity model settings (parent spec §5.3): how
    /// rigidly the camera's velocity is bound to its reference frame.
    /// </summary>
    public class VelocityModelSettings
    {
        /// <summary>
        /// Velocity binding rigidity k in [0, 1]: 0 = free (fully inertial,
        /// velocity integrated per sim step), 1 = locked to the reference
        /// frame's velocity.
        /// </summary>
        public double Rigidity { get; set; }

        /// <summary>
        /// When true, the camera's velocity is sampled from the reference frame
        /// at recording start; when false, the stored/initial velocity is used.
        /// </summary>
        public bool MatchOnStart { get; set; }

        /// <summary>
        /// When true, the free (inertial) component of the camera's velocity is
        /// affected by the current main body's gravity.
        /// </summary>
        public bool Gravity { get; set; }

        /// <summary>
        /// Creates velocity model settings with the deterministic-hold defaults:
        /// fully rigid (k = 1), no match-on-start, no gravity.
        /// </summary>
        public VelocityModelSettings()
        {
            Rigidity = 1.0;
            MatchOnStart = false;
            Gravity = false;
        }
    }

    /// <summary>
    /// Per-camera zoom settings: manual field of view plus the consistent-framing
    /// auto-zoom parameters.
    /// </summary>
    public class ZoomSettings
    {
        /// <summary>
        /// Manual field of view in degrees, used when auto-zoom is disabled.
        /// </summary>
        public double ManualFov { get; set; }

        /// <summary>
        /// When true, the field of view is driven by consistent-framing math
        /// (vessel bounding radius, distance, and padding) instead of the manual FOV.
        /// </summary>
        public bool AutoZoomEnabled { get; set; }

        /// <summary>
        /// Consistent-framing padding multiplier applied to the vessel bounding
        /// radius (1.0 = tight fit, larger values add margin around the vessel).
        /// </summary>
        public double PaddingMultiplier { get; set; }

        /// <summary>
        /// Duration of zoom/FOV transitions in playback seconds. Tween progress is
        /// evaluated on the playback clock during deterministic capture.
        /// </summary>
        public double TransitionDurationSeconds { get; set; }

        /// <summary>
        /// Creates zoom settings with manual FOV 60 degrees, auto-zoom disabled,
        /// tight-fit padding, and a one-second transition.
        /// </summary>
        public ZoomSettings()
        {
            ManualFov = 60.0;
            AutoZoomEnabled = false;
            PaddingMultiplier = 1.0;
            TransitionDurationSeconds = 1.0;
        }
    }

    /// <summary>
    /// The camera definition data transfer object: the complete persisted state
    /// of one named library camera (parent spec §4.4, path keys deferred to
    /// P2-C2). Pure data plus trivial value semantics; uniqueness of <see cref="Name"/>
    /// within the library is enforced by the library state, not by this DTO.
    /// Every persisted position carries its frame encoding explicitly in its
    /// type (frame-discriminator rule); no bare UnityEngine.Vector3 with an
    /// implied frame appears anywhere in this schema.
    /// </summary>
    public sealed class CameraDefinition
    {
        /// <summary>
        /// Unique name of the camera within its library. The library state
        /// enforces uniqueness; the DTO itself does not validate it.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Discriminated anchor: where the camera is anchored (body-fixed
        /// geodetic point, or vessel part with stored local offset).
        /// </summary>
        public CameraAnchor Anchor { get; set; }

        /// <summary>
        /// Discriminated target: what the camera looks at.
        /// </summary>
        public CameraTarget Target { get; set; }

        /// <summary>
        /// Inertial velocity model settings (rigidity, match-on-start, gravity).
        /// </summary>
        public VelocityModelSettings Velocity { get; set; }

        /// <summary>
        /// Reference frame mode (surface or orbit frame of the anchor). Only
        /// consulted for vessel anchors; body anchors are always body-fixed.
        /// </summary>
        public ReferenceFrameMode FrameMode { get; set; }

        /// <summary>
        /// Manual offset applied on top of the anchor's stored offset, expressed
        /// in the camera's anchor-local frame.
        /// </summary>
        public AnchorLocalOffset ManualOffset { get; set; }

        /// <summary>
        /// Camera roll in degrees, applied around the camera's forward axis.
        /// </summary>
        public double RollDegrees { get; set; }

        /// <summary>
        /// Pivot mode for camera rotation: about the camera or about the target.
        /// </summary>
        public CameraPivotMode PivotMode { get; set; }

        /// <summary>
        /// Zoom settings: manual field of view plus consistent-framing auto-zoom.
        /// </summary>
        public ZoomSettings Zoom { get; set; }

        // Extension point (P2-C2): spline path keys (playback-second times,
        // anchor-local positions/rotations, per-key FOV, duplicate-timestamp
        // guard) will extend this schema as a path property. Intentionally
        // absent here — no path type is stubbed in this chunk.

        /// <summary>
        /// Creates a camera definition with neutral defaults: unnamed, anchored
        /// to the runtime-default vessel part (active control point) with zero
        /// offset, no target, fully rigid velocity binding, surface frame, zero
        /// roll, camera pivot, and default zoom settings.
        /// </summary>
        public CameraDefinition()
        {
            Name = string.Empty;
            Anchor = CameraAnchor.OnVessel(0, AnchorFallbackPolicy.RootPart, 0.0, 0.0, 0.0, 0.0);
            Target = CameraTarget.None;
            Velocity = new VelocityModelSettings();
            FrameMode = ReferenceFrameMode.Surface;
            ManualOffset = new AnchorLocalOffset(0.0, 0.0, 0.0);
            RollDegrees = 0.0;
            PivotMode = CameraPivotMode.Camera;
            Zoom = new ZoomSettings();
        }
    }
}
