using UnityEngine;

namespace CinematicRecorder.Core
{
    /// <summary>
    /// Interpolates from current FOV to target FOV over a specified physics duration.
    /// Captures start FOV on first execution to prevent snapping.
    /// </summary>
    public class TargetBasedZoomStrategy : IZoomStrategy
    {
        private readonly float _targetFOV;
        private readonly float _duration;
        private readonly ZoomCurve _curve;
        private float _startFOV;
        private float _elapsedTime;
        private bool _hasCapturedStart;
        private bool _isComplete;

        /// <summary>
        /// Creates a target-based zoom that interpolates from current FOV to target over specified duration.
        /// Start FOV is captured on first execution to ensure smooth transitions.
        /// </summary>
        public TargetBasedZoomStrategy(float targetFOV, float durationSeconds, ZoomCurve curve)
        {
            _targetFOV = targetFOV;
            _duration = Mathf.Max(0.0001f, durationSeconds); // Prevent division by zero
            _curve = curve;
            _hasCapturedStart = false;
            _isComplete = false;
            _elapsedTime = 0f;
        }
        public float GetTargetFOV(float currentFOV, float physicsDeltaTime)
        {
            // Capture start FOV on first execution (not construction) to ensure smooth transition
            if (!_hasCapturedStart)
            {
                _startFOV = currentFOV;
                _hasCapturedStart = true;
            }

            _elapsedTime += physicsDeltaTime;

            if (_elapsedTime >= _duration)
            {
                _isComplete = true;
                return _targetFOV; // Return exact target to avoid floating point drift
            }

            float t = Mathf.Clamp01(_elapsedTime / _duration);
            float curvedT = ZoomCurveEvaluator.Evaluate(t, _curve);

            return Mathf.Lerp(_startFOV, _targetFOV, curvedT);
        }
        public bool IsComplete => _isComplete;
        public void SetInput(float input)
        {
        }
    }
}