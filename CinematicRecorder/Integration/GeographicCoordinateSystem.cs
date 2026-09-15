using UnityEngine;
using System;
using System.Linq;

namespace CinematicRecorder.Integration
{
    /// <summary>
    /// Handles lat/lon/alt <-> world position conversions.
    /// Isolated from CameraTools dependency for reusability and testability.
    /// </summary>
    public static class GeographicCoordinateSystem
    {
        /// <summary>
        /// Converts geographic coordinates to world position using PQS (full precision).
        /// </summary>
        public static Vector3 GetWorldPosition(CelestialBody body, double latitude, double longitude, double altitude)
        {
            if (body == null) return Vector3.zero;
            return body.GetWorldSurfacePosition(latitude, longitude, altitude);
        }

        /// <summary>
        /// Extracts geographic coordinates from a world position relative to a body.
        /// </summary>
        public static GeographicCoords GetCoordinates(CelestialBody body, Vector3 worldPosition)
        {
            if (body == null) return new GeographicCoords();

            return new GeographicCoords
            {
                Latitude = body.GetLatitude(worldPosition),
                Longitude = body.GetLongitude(worldPosition),
                Altitude = body.GetAltitude(worldPosition),
                BodyName = body.name
            };
        }

        /// <summary>
        /// Resolves a body by name from FlightGlobals.
        /// </summary>
        public static CelestialBody ResolveBody(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return FlightGlobals.currentMainBody;

            var body = FlightGlobals.Bodies.FirstOrDefault(b => b.name == bodyName);
            return body ?? FlightGlobals.currentMainBody;
        }

        /// <summary>
        /// Calculates the offset from vessel CoM to a world position.
        /// Used for CameraTools manualPosition relative coordinates.
        /// </summary>
        public static Vector3 CalculateOffsetFromVessel(Vessel vessel, Vector3 worldPosition)
        {
            if (vessel == null) return Vector3.zero;
            return worldPosition - vessel.CoM;
        }

        /// <summary>
        /// Creates a body-fixed East-North-Up reference frame at a geodetic
        /// coordinate on a celestial body (parent spec §5.3 "Body anchor
        /// frame"). Every world-space value of the frame (origin, axes, origin
        /// velocity) is re-derived from the body's current state on each
        /// access, so the frame never holds a world-space pose and is immune
        /// to floating-origin shifts (build-program invariant 2).
        /// </summary>
        /// <param name="body">The celestial body the frame is fixed to.</param>
        /// <param name="latitude">Latitude of the anchor point in degrees.</param>
        /// <param name="longitude">Longitude of the anchor point in degrees.</param>
        /// <param name="altitude">Altitude of the anchor point in meters above the body's mean radius.</param>
        public static BodyFixedEnuFrame CreateEnuFrame(CelestialBody body, double latitude, double longitude, double altitude)
        {
            return new BodyFixedEnuFrame(body, latitude, longitude, altitude);
        }
    }
    public struct GeographicCoords
    {
        public double Latitude;
        public double Longitude;
        public double Altitude;
        public string BodyName;

        public bool IsValid => !string.IsNullOrEmpty(BodyName);
    }

    /// <summary>
    /// A body-fixed East-North-Up reference frame anchored at a geodetic
    /// coordinate on a celestial body (parent spec §5.3 "Body anchor frame").
    /// The frame rotates with the body: every world-space value (origin, axes,
    /// origin velocity) is re-derived from the body's current state on each
    /// access. The struct stores only the anchor parameters (body reference
    /// and doubles), never a world-space pose, so it is immune to
    /// floating-origin shifts (build-program invariant 2, Krakensbane
    /// discipline). Read-only KSP access: writes nothing to the scene.
    /// </summary>
    public struct BodyFixedEnuFrame
    {
        // Squared-below this, a projected axis is treated as degenerate (poles).
        private const double AxisEpsilonSqr = 1e-12;
        // Squared-below this, the anchor radial is treated as degenerate.
        private const double RadialEpsilonSqr = 1e-6;

        private readonly CelestialBody _body;
        private readonly double _latitude;
        private readonly double _longitude;
        private readonly double _altitude;

        /// <summary>
        /// Creates the frame at a geodetic coordinate on a celestial body.
        /// </summary>
        /// <param name="body">The celestial body the frame is fixed to.</param>
        /// <param name="latitude">Latitude of the anchor point in degrees.</param>
        /// <param name="longitude">Longitude of the anchor point in degrees.</param>
        /// <param name="altitude">Altitude of the anchor point in meters above the body's mean radius.</param>
        public BodyFixedEnuFrame(CelestialBody body, double latitude, double longitude, double altitude)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            _body = body;
            _latitude = latitude;
            _longitude = longitude;
            _altitude = altitude;
        }

        /// <summary>The celestial body the frame is fixed to.</summary>
        public CelestialBody Body { get { return _body; } }

        /// <summary>Latitude of the anchor point in degrees.</summary>
        public double Latitude { get { return _latitude; } }

        /// <summary>Longitude of the anchor point in degrees.</summary>
        public double Longitude { get { return _longitude; } }

        /// <summary>Altitude of the anchor point in meters above the body's mean radius.</summary>
        public double Altitude { get { return _altitude; } }

        /// <summary>
        /// World position of the anchor point, re-derived on each access.
        /// </summary>
        public Vector3d Origin
        {
            get { return _body.GetWorldSurfacePosition(_latitude, _longitude, _altitude); }
        }

        /// <summary>
        /// Unit Up axis of the frame (radially outward from the body's center
        /// through the anchor point), re-derived on each access.
        /// </summary>
        public Vector3d Up
        {
            get
            {
                Vector3d radial = Origin - _body.position;
                if (radial.sqrMagnitude < RadialEpsilonSqr) return Vector3d.zero;
                return radial.normalized;
            }
        }

        /// <summary>
        /// Unit North axis of the frame (the body's pole axis projected onto
        /// the local horizon at the anchor point), re-derived on each access.
        /// At the poles, where the pole axis has no horizontal component, the
        /// body's forward axis is projected instead so the frame never
        /// degenerates.
        /// </summary>
        public Vector3d North
        {
            get
            {
                Vector3d up = Up;
                Vector3d north = ProjectOnPlane(_body.bodyTransform.up, up);
                if (north.sqrMagnitude < AxisEpsilonSqr)
                    north = ProjectOnPlane(_body.bodyTransform.forward, up);
                if (north.sqrMagnitude < AxisEpsilonSqr)
                    return Vector3d.zero;
                return north.normalized;
            }
        }

        /// <summary>
        /// Unit East axis of the frame (the direction the rotating surface
        /// moves under the anchor point), re-derived on each access.
        /// </summary>
        public Vector3d East
        {
            get
            {
                Vector3d north = North;
                if (north == Vector3d.zero) return Vector3d.zero;
                return Vector3d.Cross(Up, north);
            }
        }

        /// <summary>
        /// The frame's orientation: level with the local horizon, forward
        /// along North, up along Up. This is the canonical orientation of a
        /// camera anchored in this frame with no target and zero roll (parent
        /// spec §5.3 hold behavior).
        /// </summary>
        public Quaternion Rotation
        {
            get
            {
                Vector3d north = North;
                if (north == Vector3d.zero) return Quaternion.identity;
                return Quaternion.LookRotation(north, Up);
            }
        }

        /// <summary>
        /// Inertial (non-rotating) velocity of the ground point under the
        /// anchor, due to the body's rotation. Exposed for the velocity model
        /// of P1-C4.
        /// </summary>
        public Vector3d OriginVelocity
        {
            get { return _body.getRFrmVel(Origin); }
        }

        private static Vector3d ProjectOnPlane(Vector3d vector, Vector3d planeNormal)
        {
            return vector - planeNormal * Vector3d.Dot(vector, planeNormal);
        }
    }
}