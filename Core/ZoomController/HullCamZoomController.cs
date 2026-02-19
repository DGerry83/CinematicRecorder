using CinematicRecorder.Integration;
using CinematicRecorder.UI;
using UnityEngine;

namespace CinematicRecorder.Core
{
    public class HullCamZoomController : IZoomController
    {
        private IZoomStrategy _currentStrategy;
        private float _rateInput;
        private float _currentFOV = 60f;
        private object _activeCameraModule;
        private bool _enableConsistentOnComplete;

        public bool UseConsistentAutoZoom { get; set; }
        public float ConsistentZoomPadding { get; set; } = 1.5f;
        public bool HasActiveStrategy => _currentStrategy != null;
        public float CurrentFoV => _currentFOV;

        public void SetRateInput(float input)
        {
            _rateInput = Mathf.Clamp(input, -1f, 1f);
            if (_currentStrategy == null && Mathf.Abs(_rateInput) > 0.001f)
            {
                _currentStrategy = new RateBasedZoomStrategy(CinematicUIResources.Layout.Zoom.MAX_SPEED);
                ((RateBasedZoomStrategy)_currentStrategy).SetInput(_rateInput);
            }
            else if (_currentStrategy is RateBasedZoomStrategy rateStrategy)
            {
                rateStrategy.SetInput(_rateInput);
            }
        }

        public void DecayZoomIntent(float deltaTime)
        {
            if (!Input.GetMouseButton(0))
            {
                _rateInput = Mathf.MoveTowards(_rateInput, 0f, deltaTime * CinematicUIResources.Layout.Zoom.RETURN_SPEED);
                SetRateInput(_rateInput);
            }
        }

        public void Update(float deltaTime)
        {
            if (!HullCamBridge.IsAvailable) return;

            object hullCamModule = HullCamBridge.GetCurrentCamera();
            if (hullCamModule == null)
            {
                _currentStrategy = null;
                _activeCameraModule = null;
                return;
            }

            if (hullCamModule != _activeCameraModule)
            {
                _activeCameraModule = hullCamModule;
                _currentFOV = HullCamBridge.GetCameraFoV(hullCamModule);
                _currentStrategy = null;
                _rateInput = 0f;
            }

            if (_currentStrategy != null)
            {
                ExecuteStrategy(deltaTime, hullCamModule);
            }
            else if (UseConsistentAutoZoom)
            {
                ApplyConsistentFraming();
            }
            else
            {
                _currentFOV = HullCamBridge.GetCameraFoV(hullCamModule);
            }
        }

        private void ExecuteStrategy(float deltaTime, object hullCamModule)
        {
            float newFOV = _currentStrategy.GetTargetFOV(_currentFOV, deltaTime);
            float minFoV = HullCamBridge.GetCameraFoVMin(hullCamModule);
            float maxFoV = HullCamBridge.GetCameraFoVMax(hullCamModule);
            newFOV = Mathf.Clamp(newFOV, minFoV, maxFoV);

            HullCamBridge.SetCameraFoV(hullCamModule, newFOV);
            _currentFOV = newFOV;

            if (_currentStrategy.IsComplete)
            {
                if (_currentStrategy is ConsistentFramingTransitionStrategy && _enableConsistentOnComplete)
                {
                    UseConsistentAutoZoom = true;
                    _enableConsistentOnComplete = false;
                }
                _currentStrategy = null;
            }
        }

        public void ApplyConsistentFraming()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || FlightCamera.fetch == null) return;

            Vector3 camPos = FlightCamera.fetch.transform.position;
            float targetFOV = ZoomMathUtility.CalculateConsistentFramingFOV(vessel, camPos, ConsistentZoomPadding);

            object hullCamModule = HullCamBridge.GetCurrentCamera();
            if (hullCamModule == null) return;

            float minFoV = HullCamBridge.GetCameraFoVMin(hullCamModule);
            float maxFoV = HullCamBridge.GetCameraFoVMax(hullCamModule);
            targetFOV = Mathf.Clamp(targetFOV, minFoV, maxFoV);

            HullCamBridge.SetCameraFoV(hullCamModule, targetFOV);
            _currentFOV = targetFOV;
        }

        public void QueueTargetZoom(float targetFOV, float duration, ZoomCurve curve)
        {
            CancelActiveZoom();
            if (duration < 0.001f)
                _currentStrategy = new InstantZoomStrategy(targetFOV);
            else
                _currentStrategy = new TargetBasedZoomStrategy(targetFOV, duration, curve);
        }

        public void QueueConsistentTransition(float duration, ZoomCurve curve)
        {
            CancelActiveZoom();
            object hullCamModule = HullCamBridge.GetCurrentCamera();
            if (hullCamModule == null) return;

            float startFOV = HullCamBridge.GetCameraFoV(hullCamModule);

            if (duration < 0.001f)
            {
                UseConsistentAutoZoom = true;
                return;
            }

            _currentStrategy = new ConsistentFramingTransitionStrategy(startFOV, duration, curve, ConsistentZoomPadding);
            _enableConsistentOnComplete = true;
        }

        public void Interrupt(IZoomStrategy strategy)
        {
            CancelActiveZoom();
            _currentStrategy = strategy;
            if (strategy is InstantZoomStrategy instant)
            {
                object hullCamModule = HullCamBridge.GetCurrentCamera();
                if (hullCamModule != null)
                {
                    HullCamBridge.SetCameraFoV(hullCamModule, instant.TargetFOV);
                    _currentFOV = instant.TargetFOV;
                }
            }
        }

        public void CancelActiveZoom()
        {
            _currentStrategy = null;
            _enableConsistentOnComplete = false;
        }

        public void ResetZoom(float maxFov)
        {
            CancelActiveZoom();
            UseConsistentAutoZoom = false;
            object hullCamModule = HullCamBridge.GetCurrentCamera();
            if (hullCamModule != null)
            {
                HullCamBridge.SetCameraFoV(hullCamModule, maxFov);
                _currentFOV = maxFov;
            }
        }
    }
}