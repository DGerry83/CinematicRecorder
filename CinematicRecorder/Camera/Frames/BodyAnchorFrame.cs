using System;
using CinematicRecorder.Camera.Model;
using CinematicRecorder.Integration;
using UnityEngine;

namespace CinematicRecorder.Camera.Frames
{
    /// <summary>
    /// Body-fixed East-North-Up anchor frame for a camera (parent spec §5.3
    /// "Body anchor frame"; build-program chunk P1-C2). Constructed from a
    /// celestial body and a geodetic coordinate; provides the frame's axes and
    /// resolves <see cref="CameraAnchor.Body"/> plus an <see cref="AnchorLocalOffset"/>
    /// into a world position/rotation on demand, every step. Stores only the
    /// anchor parameters (body reference and doubles); every world-space value
    /// is re-derived per access, so the frame never persists a world-space
    /// pose across steps and is immune to floating-origin shifts
    /// (build-program invariant 2, Krakensbane discipline). Read-only KSP
    /// access: writes nothing to the scene.
    /// </summary>
    public sealed class BodyAnchorFrame
    {
        private readonly BodyFixedEnuFrame _frame;

        /// <summary>
        /// Creates the anchor frame at a geodetic coordinate on a celestial
        /// body. Callers resolve the body name stored in the anchor payload to
        /// a body via <see cref="GeographicCoordinateSystem.ResolveBody"/>.
        /// </summary>
        /// <param name="body">The celestial body the frame is fixed to.</param>
        /// <param name="latitude">Latitude of the anchor point in degrees.</param>
        /// <param name="longitude">Longitude of the anchor point in degrees.</param>
        /// <param name="altitude">Altitude of the anchor point in meters above the body's mean radius.</param>
        public BodyAnchorFrame(CelestialBody body, double latitude, double longitude, double altitude)
        {
            _frame = new BodyFixedEnuFrame(body, latitude, longitude, altitude);
        }

        /// <summary>The celestial body the frame is fixed to.</summary>
        public CelestialBody Body { get { return _frame.Body; } }

        /// <summary>Latitude of the anchor point in degrees.</summary>
        public double Latitude { get { return _frame.Latitude; } }

        /// <summary>Longitude of the anchor point in degrees.</summary>
        public double Longitude { get { return _frame.Longitude; } }

        /// <summary>Altitude of the anchor point in meters above the body's mean radius.</summary>
        public double Altitude { get { return _frame.Altitude; } }

        /// <summary>
        /// World position of the anchor point, re-derived on each access.
        /// </summary>
        public Vector3d Origin { get { return _frame.Origin; } }

        /// <summary>
        /// Unit East axis of the frame (the direction the rotating surface
        /// moves under the anchor point), re-derived on each access.
        /// </summary>
        public Vector3d East { get { return _frame.East; } }

        /// <summary>
        /// Unit North axis of the frame (the body's pole axis projected onto
        /// the local horizon at the anchor point), re-derived on each access.
        /// </summary>
        public Vector3d North { get { return _frame.North; } }

        /// <summary>
        /// Unit Up axis of the frame (radially outward from the body's center
        /// through the anchor point), re-derived on each access.
        /// </summary>
        public Vector3d Up { get { return _frame.Up; } }

        /// <summary>
        /// The frame's orientation: level with the local horizon, forward
        /// along North, up along Up. This is the canonical orientation of a
        /// camera anchored in this frame with no target and zero roll (parent
        /// spec §5.3 hold behavior).
        /// </summary>
        public Quaternion Rotation { get { return _frame.Rotation; } }

        /// <summary>
        /// Inertial (non-rotating) velocity of the ground point under the
        /// anchor, due to the body's rotation. This is the reference-frame
        /// velocity consumed by the velocity model of P1-C4.
        /// </summary>
        public Vector3d Velocity { get { return _frame.OriginVelocity; } }

        /// <summary>
        /// Resolves a camera position from the anchor plus a local offset on
        /// demand: the anchor point plus the offset composed in the frame's
        /// East-North-Up axes (Forward along North, Right along East as with
        /// a right hand facing north, Up along Up).
        /// </summary>
        /// <param name="localOffset">Anchor-local offset to apply on top of the anchor point.</param>
        public Vector3d ResolvePosition(AnchorLocalOffset localOffset)
        {
            return Origin
                + North * localOffset.Forward
                + East * localOffset.Right
                + Up * localOffset.Up;
        }

        /// <summary>
        /// Resolves the camera rotation for a camera with no target and zero
        /// roll: the frame's canonical level north-facing orientation. Target
        /// look-at composition is applied on top of this orientation by the
        /// camera controller (P1-C5).
        /// </summary>
        public Quaternion ResolveRotation()
        {
            return Rotation;
        }
    }
}
