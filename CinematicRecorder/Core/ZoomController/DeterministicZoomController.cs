using System.Collections.Generic;
using UnityEngine;
using CinematicRecorder.Capture;
using CinematicRecorder.Integration;

namespace CinematicRecorder.Core
{
    public class DeterministicZoomController : MonoBehaviour, IZoomController
    {
        private Queue<IZoomStrategy> _pendingStrategies = new Queue<IZoomStrategy>();
        private IZoomStrategy _activeStrategy;
        private float _cachedRateInput;
        private bool _hasCachedInput;
        private bool _enableConsistentOnComplete;

        public bool UseConsistentAutoZoom { get; set; }
        public float ConsistentZoomPadding { get; set; } = 1.5f;
        public bool HasActiveStrategy => _activeStrategy != null || _pendingStrategies.Count > 0;
        public float CurrentFoV => CinematicCameraManager.Instance.GetCurrentFOV();

        private void OnEnable()
        {
            DeterministicCaptureSession.OnPhysicsStepped += OnPhysicsStepped;
        }

        private void OnDisable()
        {
            DeterministicCaptureSession.OnPhysicsStepped -= OnPhysicsStepped;
            Clear();
        }

        private void OnPhysicsStepped(float physicsDeltaTime)
        {
            if (!CinematicCameraManager.Instance.HasActiveCamera) return;

            // Handle consistent framing transition completion
            if (_activeStrategy is ConsistentFramingTransitionStrategy && _activeStrategy.IsComplete)
            {
                if (_enableConsistentOnComplete)
                {
                    UseConsistentAutoZoom = true;
                    _enableConsistentOnComplete = false;
                }
                _activeStrategy = null;
            }

            if (UseConsistentAutoZoom)
            {
                ApplyConsistentFraming();
                return;
            }

            // Advance strategy queue
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

        private void ProcessActiveStrategy(float physicsDeltaTime)
        {
            if (CaptureCameraResolver.IsIvaMode()) return;

            float currentFOV = CurrentFoV;
            float targetFOV = _activeStrategy.GetTargetFOV(currentFOV, physicsDeltaTime);

            float minFOV = CinematicCameraManager.Instance.GetMinFOV();
            float maxFOV = CinematicCameraManager.Instance.GetMaxFOV();
            targetFOV = Mathf.Clamp(targetFOV, minFOV, maxFOV);

            CinematicCameraManager.Instance.ApplyZoom(targetFOV);
        }

        public void SetRateInput(float input)
        {
            _cachedRateInput = Mathf.Clamp(input, -1f, 1f);
            _hasCachedInput = true;

            if (_activeStrategy == null && Mathf.Abs(_cachedRateInput) > 0.001f)
            {
                _activeStrategy = new RateBasedZoomStrategy(60f); // Deterministic uses fixed max rate
                ((RateBasedZoomStrategy)_activeStrategy).SetInput(_cachedRateInput);
            }
            else if (_activeStrategy is RateBasedZoomStrategy rateStrategy)
            {
                rateStrategy.SetInput(_cachedRateInput);
            }
        }

        public void DecayZoomIntent(float deltaTime)
        {
            // Deterministic mode doesn't decay - input is held until changed
        }

        void IZoomController.Update(float deltaTime) { /* No-op - physics driven */ }

        public void ApplyConsistentFraming()
        {
            if (CaptureCameraResolver.IsIvaMode()) return;

            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || FlightCamera.fetch == null) return;

            Vector3 camPos = FlightCamera.fetch.transform.position;
            float targetFOV = ZoomMathUtility.CalculateConsistentFramingFOV(vessel, camPos, ConsistentZoomPadding);

            float minFOV = CinematicCameraManager.Instance.GetMinFOV();
            float maxFOV = CinematicCameraManager.Instance.GetMaxFOV();
            targetFOV = Mathf.Clamp(targetFOV, minFOV, maxFOV);

            CinematicCameraManager.Instance.ApplyZoom(targetFOV);
        }

        public void QueueTargetZoom(float targetFOV, float duration, ZoomCurve curve)
        {
            if (duration < 0.001f)
                Interrupt(new InstantZoomStrategy(targetFOV));
            else
                AddStrategy(new TargetBasedZoomStrategy(targetFOV, duration, curve));
        }

        public void QueueConsistentTransition(float duration, ZoomCurve curve)
        {
            float startFOV = CurrentFoV;
            var strategy = new ConsistentFramingTransitionStrategy(startFOV, duration, curve, ConsistentZoomPadding);
            strategy.EnableConsistentFramingOnComplete = true;
            _enableConsistentOnComplete = true;
            Interrupt(strategy);
        }

        public void AddStrategy(IZoomStrategy strategy)
        {
            if (strategy == null) return;
            _pendingStrategies.Enqueue(strategy);
        }

        public void Interrupt(IZoomStrategy strategy)
        {
            _pendingStrategies.Clear();
            _activeStrategy = strategy;
            _hasCachedInput = false;
        }

        public void CancelActiveZoom()
        {
            Clear();
        }

        public void Clear()
        {
            _pendingStrategies.Clear();
            _activeStrategy = null;
            _hasCachedInput = false;
        }

        public void ResetZoom(float maxFov)
        {
            Clear();
            Interrupt(new InstantZoomStrategy(maxFov));
        }
    }
}