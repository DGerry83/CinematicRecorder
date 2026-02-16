using UnityEngine;
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
    }
    public struct GeographicCoords
    {
        public double Latitude;
        public double Longitude;
        public double Altitude;
        public string BodyName;

        public bool IsValid => !string.IsNullOrEmpty(BodyName);
    }
}