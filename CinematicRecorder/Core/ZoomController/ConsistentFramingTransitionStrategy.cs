using CinematicRecorder.Integration;
using UnityEngine;

namespace CinematicRecorder.Core
{
    /// <summary>
    /// Transitions from current FOV to consistent framing FOV over time.
    /// Target is recalculated each step (moving target) to handle vessel motion.
    /// When complete, hands off to consistent framing mode.
    /// </summary>
    public class ConsistentFramingTransitionStrategy : IZoomStrategy
    {
        private readonly float _startFOV;
        private readonly float _duration;
        private readonly ZoomCurve _curve;
        private readonly float _padding;
        private float _elapsed;
        private bool _isComplete;
        public bool EnableConsistentFramingOnComplete { get; set; }

        /// <summary>
        /// Creates a transition strategy that interpolates from start FOV to calculated consistent framing FOV.
        /// </summary>
        public ConsistentFramingTransitionStrategy(float startFOV, float duration, ZoomCurve curve, float padding)
        {
            _startFOV = startFOV;
            _duration = Mathf.Max(0.0001f, duration);
            _curve = curve;
            _padding = padding;
            _elapsed = 0f;
            _isComplete = false;
        }
        public float GetTargetFOV(float currentFOV, float physicsDeltaTime)
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            float targetConsistentFOV = currentFOV; // fallback

            if (vessel != null && FlightCamera.fetch != null)
            {
                Vector3 camPos = FlightCamera.fetch.transform.position;
                targetConsistentFOV = ZoomMathUtility.CalculateConsistentFramingFOV(vessel, camPos, _padding);

                // Clamp to camera bounds
                var cam = CinematicCameraManager.Instance.ActiveCamera;
                if (cam != null)
                    targetConsistentFOV = Mathf.Clamp(targetConsistentFOV, cam.MinFieldOfView, cam.MaxFieldOfView);
            }

            // If duration is effectively zero, snap immediately
            if (_duration < 0.001f)
            {
                _isComplete = true;
                return targetConsistentFOV;
            }

            _elapsed += physicsDeltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);

            if (t >= 1.0f)
            {
                _isComplete = true;
                return targetConsistentFOV; // Hand off value
            }

            // Interpolate from start toward current consistent FOV (moving target)
            float curvedT = ZoomCurveEvaluator.Evaluate(t, _curve);
            return Mathf.Lerp(_startFOV, targetConsistentFOV, curvedT);
        }
        public bool IsComplete => _isComplete;
        public void SetInput(float input)
        {
            // No manual input in transition mode
        }
    }
}