using UnityEngine;

namespace CinematicRecorder.Core
{
    /// <summary>
    /// Continuous rate-based zoom that accumulates FOV change based on input (-1 to 1).
    /// Never completes naturally - must be interrupted to exit.
    /// Matches the elastic slider behavior in CameraPanelController.
    /// </summary>
    public class RateBasedZoomStrategy : IZoomStrategy
    {
        private float _currentInput;
        private readonly float _maxRate; // Degrees per second at full input

        public RateBasedZoomStrategy(float maxRateDegreesPerSecond)
        {
            _maxRate = maxRateDegreesPerSecond;
            _currentInput = 0f;
        }
        /// <summary>
        /// Gets the target FOV by applying rate-based delta from input.
        /// Negative input zooms in (decreases FOV), positive zooms out.
        /// </summary>
        public float GetTargetFOV(float currentFOV, float physicsDeltaTime)
        {
            // Negative sign: Right (+1) → FOV decreases (zoom in), Left (-1) → FOV increases (zoom out)
            float deltaFOV = -_currentInput * _maxRate * physicsDeltaTime;
            return currentFOV + deltaFOV;
        }

        /// <summary>
        /// Rate-based zoom never completes naturally.
        /// </summary>
        public bool IsComplete => false;

        /// <summary>
        /// Sets the zoom rate input (-1 = max zoom in, 1 = max zoom out, 0 = stop).
        /// </summary>
        public void SetInput(float input)
        {
            _currentInput = Mathf.Clamp(input, -1f, 1f);
        }
    }
}