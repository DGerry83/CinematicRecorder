using System.Collections.Generic;
using UnityEngine;
using CinematicRecorder.Integration;

namespace CinematicRecorder.Core
{
    /// <summary>
    /// Deterministic zoom controller that processes zoom strategies per physics step during capture.
    /// Attaches to the CaptureRunner GameObject and subscribes to OnPhysicsStepped events.
    /// </summary>
    public class SimulationTimeZoomController : MonoBehaviour
    {
        #region Fields
        private Queue<IZoomStrategy> _pendingStrategies = new Queue<IZoomStrategy>();
        private IZoomStrategy _activeStrategy;
        private float _cachedRateInput;
        private bool _hasCachedInput;
        private bool _enableConsistentFramingOnComplete = false;
        #endregion
        #region Public Properties
        public float ConsistentFramingPadding { get; set; } = 1.5f;
        public bool UseConsistentAutoZoom { get; set; }
        public float ConsistentZoomPadding { get; set; } = 1.5f;
        /// <summary>
        /// When true, new strategies interrupt the current queue. When false, they append.
        /// </summary>
        public bool InterruptMode { get; set; } = true;
        /// <summary>
        /// Maximum FOV change rate in degrees per second for rate-based zoom (matches elastic slider behavior).
        /// </summary>
        public float MaxZoomRateDegreesPerSecond = 60f;
        /// <summary>
        /// Returns true if a zoom strategy is currently active or queued.
        /// </summary>
        public bool IsProcessing => _activeStrategy != null || _pendingStrategies.Count > 0;
        #endregion
        #region Unity Lifecycle
        private void OnEnable()
        {
            DeterministicCaptureSession.OnPhysicsStepped += OnPhysicsStepped;
        }

        private void OnDisable()
        {
            DeterministicCaptureSession.OnPhysicsStepped -= OnPhysicsStepped;
            _pendingStrategies.Clear();
            _activeStrategy = null;
        }
        #endregion
        #region Public Control Methods
        /// <summary>
        /// Queues a transition to consistent framing FOV that automatically enables consistent framing upon completion.
        /// </summary>
        public void QueueConsistentFramingTransition(float duration, ZoomCurve curve, float padding)
        {
            float startFOV = CinematicCameraManager.Instance.GetCurrentFOV();
            var strategy = new ConsistentFramingTransitionStrategy(startFOV, duration, curve, padding);
            strategy.EnableConsistentFramingOnComplete = true;
            _enableConsistentFramingOnComplete = true;
            Interrupt(strategy);
        }
        /// <summary>
        /// Adds a zoom strategy. Behavior depends on InterruptMode setting.
        /// Use for Instant or Target-based zooms.
        /// </summary>
        public void AddStrategy(IZoomStrategy strategy)
        {
            if (strategy == null) return;

            if (InterruptMode)
            {
                Interrupt(strategy);
            }
            else
            {
                _pendingStrategies.Enqueue(strategy);
            }
        }
        /// <summary>
        /// Immediately interrupts current strategy and queue, switching to the new strategy.
        /// </summary>
        public void Interrupt(IZoomStrategy strategy)
        {
            if (strategy == null) return;

            _pendingStrategies.Clear();
            _activeStrategy = strategy;
            _hasCachedInput = false;
        }
        /// <summary>
        /// Sets continuous rate input (-1 to 1) for elastic slider behavior.
        /// Input is cached and applied to active RateBasedZoomStrategy, or held for next rate-based strategy.
        /// </summary>
        public void SetRateInput(float input)
        {
            _cachedRateInput = Mathf.Clamp(input, -1f, 1f);
            _hasCachedInput = true;

            // Auto-activate rate-based strategy if we have input but nothing active
            if (_activeStrategy == null && Mathf.Abs(_cachedRateInput) > 0.001f)
            {
                _activeStrategy = new RateBasedZoomStrategy(MaxZoomRateDegreesPerSecond);
                ((RateBasedZoomStrategy)_activeStrategy).SetInput(_cachedRateInput);
            }
            // Apply immediately if currently executing a rate-based strategy
            else if (_activeStrategy is RateBasedZoomStrategy rateStrategy)
            {
                rateStrategy.SetInput(_cachedRateInput);
            }
        }
        /// <summary>
        /// Convenience method to create and activate a rate-based zoom strategy.
        /// Interrupts current queue if InterruptMode is true.
        /// </summary>
        public void BeginRateBasedZoom()
        {
            var strategy = new RateBasedZoomStrategy(MaxZoomRateDegreesPerSecond);
            AddStrategy(strategy);

            if (_hasCachedInput && _activeStrategy == strategy)
            {
                strategy.SetInput(_cachedRateInput);
            }
        }
        /// <summary>
        /// Clears all pending strategies and stops current zoom.
        /// </summary>
        public void Clear()
        {
            _pendingStrategies.Clear();
            _activeStrategy = null;
            _hasCachedInput = false;
        }
        #endregion
        #region Private Implementation
        /// <summary>
        /// Primary physics step handler - processes one zoom iteration per physics frame.
        /// </summary>
        private void OnPhysicsStepped(float physicsDeltaTime)
        {
            if (!CinematicCameraManager.Instance.HasActiveCamera)
                return;

            var activeCam = CinematicCameraManager.Instance.ActiveCamera;
            bool isCT = activeCam is CameraToolsCamera;

            // Check if consistent framing transition completed and hand off
            if (_activeStrategy is ConsistentFramingTransitionStrategy && _activeStrategy.IsComplete)
            {
                if (_enableConsistentFramingOnComplete)
                {
                    UseConsistentAutoZoom = true;
                    _enableConsistentFramingOnComplete = false;
                }
                _activeStrategy = null;
            }
            if (UseConsistentAutoZoom)
            {
                ApplyConsistentFraming();
                return;
            }

            // Handle CT rate mode separately (accumulates in controller, not here)
            if (isCT && _activeStrategy is RateBasedZoomStrategy && _hasCachedInput)
            {
                var ctController = new CameraToolsCameraController();
                ctController.ApplyRateStep(_cachedRateInput, physicsDeltaTime);
                return;
            }
            if (_activeStrategy != null && _activeStrategy.IsComplete)
                _activeStrategy = null;

            if (_activeStrategy == null && _pendingStrategies.Count > 0)
            {
                _activeStrategy = _pendingStrategies.Dequeue();
                if (_activeStrategy is RateBasedZoomStrategy rateStrategy && _hasCachedInput)
                    rateStrategy.SetInput(_cachedRateInput);
            }

            if (_activeStrategy != null)
                ProcessActiveStrategy(physicsDeltaTime);
        }
        private void ApplyConsistentFraming()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || FlightCamera.fetch == null) return;

            Vector3 camPos = FlightCamera.fetch.transform.position;
            float targetFOV = ZoomMathUtility.CalculateConsistentFramingFOV(vessel, camPos, ConsistentZoomPadding);

            // Clamp to camera hardware limits
            float minFOV = CinematicCameraManager.Instance.GetMinFOV();
            float maxFOV = CinematicCameraManager.Instance.GetMaxFOV();
            targetFOV = Mathf.Clamp(targetFOV, minFOV, maxFOV);

            // Apply via manager (handles both HullCam and CT)
            CinematicCameraManager.Instance.ApplyZoom(targetFOV);
        }
        private void ProcessActiveStrategy(float physicsDeltaTime)
        {
            float currentFOV = CinematicCameraManager.Instance.GetCurrentFOV();
            float targetFOV = _activeStrategy.GetTargetFOV(currentFOV, physicsDeltaTime);

            // Clamp to camera hardware limits
            float minFOV = CinematicCameraManager.Instance.GetMinFOV();
            float maxFOV = CinematicCameraManager.Instance.GetMaxFOV();
            targetFOV = Mathf.Clamp(targetFOV, minFOV, maxFOV);

            // Apply to active camera
            CinematicCameraManager.Instance.ApplyZoom(targetFOV);
        }
        #endregion
    }
}