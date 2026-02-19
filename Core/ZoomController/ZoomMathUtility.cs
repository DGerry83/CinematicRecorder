using UnityEngine;

namespace CinematicRecorder.Core
{
    /// <summary>
    /// Shared mathematical calculations for camera zoom/FOV operations.
    /// Camera-agnostic utility used by both CameraTools and HullCam controllers.
    /// </summary>
    public static class ZoomMathUtility
    {
        /// <summary>
        /// Calculates FOV using angular size formula for consistent vessel framing.
        /// </summary>
        public static float CalculateConsistentFramingFOV(Vessel vessel, Vector3 cameraPosition, float paddingMultiplier)
        {
            if (vessel == null) return 60f;

            float distance = Vector3.Distance(cameraPosition, vessel.CoM);
            if (distance < 0.01f) distance = 0.01f;

            float radius = CalculateVesselBoundingRadius(vessel);
            float fov = 2f * Mathf.Rad2Deg * Mathf.Atan((radius * paddingMultiplier) / distance);

            return Mathf.Clamp(fov, 2f, 120f);
        }

        /// <summary>
        /// Calculates the bounding radius of a vessel from its center of mass.
        /// Returns 5f default if vessel has no parts.
        /// </summary>
        public static float CalculateVesselBoundingRadius(Vessel vessel)
        {
            if (vessel?.Parts == null || vessel.Parts.Count == 0) return 5f;

            float maxDistSq = 0f;
            Vector3 com = vessel.CoM;

            foreach (Part p in vessel.Parts)
            {
                if (p?.transform == null) continue;
                float distSq = (p.transform.position - com).sqrMagnitude;
                if (distSq > maxDistSq) maxDistSq = distSq;
            }

            return Mathf.Sqrt(maxDistSq);
        }
    }
}