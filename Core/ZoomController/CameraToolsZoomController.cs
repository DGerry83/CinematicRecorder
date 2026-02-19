using CinematicRecorder.Integration;
using CinematicRecorder.UI;
using UnityEngine;

namespace CinematicRecorder.Core
{
    public class CameraToolsZoomController : IZoomController
    {
        private IZoomStrategy _currentStrategy;
        private float _rateInput;
        private float _rateControlCurrentFOV;
        private bool _enableConsistentOnComplete;

        public bool UseConsistentAutoZoom { get; set; }
        public float ConsistentZoomPadding { get; set; } = 1.5f;
        public bool HasActiveStrategy => _currentStrategy != null;
        public float CurrentFoV => CameraToolsAPIManager.GetActualFOV();

        public void SetRateInput(float input)
        {
            _rateInput = Mathf.Clamp(input, -1f, 1f);
            if (_currentStrategy == null && Mathf.Abs(_rateInput) > 0.001f)
            {
                _currentStrategy = new RateBasedZoomStrategy(CinematicUIResources.Layout.Zoom.MAX_SPEED);
                _rateControlCurrentFOV = CurrentFoV;
            }
            if (_currentStrategy is RateBasedZoomStrategy rateStrategy)
            {
                rateStrategy.SetInput(_rateInput);
            }
        }

        public void DecayZoomIntent(float deltaTime)
        {
            if (!Input.GetMouseButton(0))
            {
                _rateInput = Mathf.MoveTowards(_rateInput, 0f, deltaTime * CinematicUIResources.Layout.Zoom.RETURN_SPEED);
            }
        }

        public void Update(float deltaTime)
        {
            if (!CameraToolsAPIManager.IsCameraActive()) return;

            if (_currentStrategy != null)
            {
                float newFOV = _currentStrategy.GetTargetFOV(CurrentFoV, deltaTime);
                CameraToolsAPIManager.SetExternalFOV(newFOV);
                _rateControlCurrentFOV = newFOV;

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
            else if (UseConsistentAutoZoom)
            {
                ApplyConsistentFraming();
            }
        }

        public void ApplyConsistentFraming()
        {
            if (!CameraToolsAPIManager.IsAvailable || !CameraToolsAPIManager.IsCameraActive()) return;

            CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.AutoZoomStationaryField, false);

            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || FlightCamera.fetch == null) return;

            Vector3 camPos = FlightCamera.fetch.transform.position;
            float targetFov = ZoomMathUtility.CalculateConsistentFramingFOV(vessel, camPos, ConsistentZoomPadding);
            targetFov = Mathf.Clamp(targetFov, 2f, 120f);

            CameraToolsAPIManager.SetExternalFOV(targetFov);
        }

        public void QueueTargetZoom(float targetFOV, float duration, ZoomCurve curve)
        {
            CancelActiveZoom();
            if (duration < 0.001f)
            {
                CameraToolsAPIManager.SetExternalFOV(targetFOV);
            }
            else
            {
                _currentStrategy = new TargetBasedZoomStrategy(targetFOV, duration, curve);
            }
        }

        public void QueueConsistentTransition(float duration, ZoomCurve curve)
        {
            CancelActiveZoom();
            if (duration < 0.001f)
            {
                UseConsistentAutoZoom = true;
                ApplyConsistentFraming();
                return;
            }

            _currentStrategy = new ConsistentFramingTransitionStrategy(CurrentFoV, duration, curve, ConsistentZoomPadding);
            _enableConsistentOnComplete = true;
        }

        public void Interrupt(IZoomStrategy strategy)
        {
            CancelActiveZoom();
            _currentStrategy = strategy;
            if (strategy is InstantZoomStrategy instant)
            {
                CameraToolsAPIManager.SetExternalFOV(instant.TargetFOV);
            }
            else if (strategy is RateBasedZoomStrategy rate)
            {
                rate.SetInput(_rateInput);
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
            CameraToolsAPIManager.SetExternalFOV(maxFov);
        }
    }
}