namespace CinematicRecorder.Core
{
    /// <summary>
    /// Immediately snaps to target FOV. Completes after single execution.
    /// </summary>
    public class InstantZoomStrategy : IZoomStrategy
    {
        private readonly float _targetFOV;
        private bool _isComplete;

        public InstantZoomStrategy(float targetFOV)
        {
            _targetFOV = targetFOV;
            _isComplete = false;
        }

        public float GetTargetFOV(float currentFOV, float physicsDeltaTime)
        {
            _isComplete = true;
            return _targetFOV;
        }

        public bool IsComplete => _isComplete;

        public void SetInput(float input)
        {
            // Instant zoom does not use continuous input
        }
    }
}