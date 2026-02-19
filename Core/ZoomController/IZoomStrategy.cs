using System;

namespace CinematicRecorder.Core
{
    /// <summary>
    /// Defines a deterministic zoom operation that processes FOV changes per physics step.
    /// </summary>
    public interface IZoomStrategy
    {
        /// <summary>
        /// Calculates the target FOV for this physics step.
        /// </summary>
        /// <param name="currentFOV">The current camera FOV at the start of this physics step</param>
        /// <param name="physicsDeltaTime">Duration of this physics step in seconds</param>
        /// <returns>The desired FOV at the end of this step</returns>
        float GetTargetFOV(float currentFOV, float physicsDeltaTime);

        /// <summary>
        /// True when this strategy has completed its operation (for queue advancement).
        /// Rate-based strategies never complete and must be interrupted.
        /// </summary>
        bool IsComplete { get; }

        /// <summary>
        /// Sets continuous input for rate-based strategies (-1 to 1).
        /// Ignored by instantaneous and target-based strategies.
        /// </summary>
        void SetInput(float input);
    }
}