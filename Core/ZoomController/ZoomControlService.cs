using CinematicRecorder.Integration;
using CinematicRecorder.UI;
using UnityEngine;

namespace CinematicRecorder.Core
{
    /// <summary>
    /// Manages zoom/FOV calculations and application for HullCam cameras.
    /// Handles rate-based, target-based, and consistent framing zoom modes.
    /// </summary>
    public class ZoomControlService
    {
        #region State
        // Strategy state
        private IZoomStrategy _currentStrategy;
        private float _rateInput;
        private float _currentFOV = 60f;
        private object _activeCameraModule;

        // Consistent framing state
        private bool _useConsistentAutoZoom = false;
        private float _consistentZoomPadding = 1.5f;

        // Target zoom state
        private bool _enableConsistentOnComplete = false;
        #endregion

        #region Public Properties
        public float CurrentFoV => _currentFOV;

        public float ZoomIntent
        {
            get => _rateInput;
            set => _rateInput = Mathf.Clamp(value, -1f, 1f);
        }

        public bool UseConsistentAutoZoom
        {
            get => _useConsistentAutoZoom;
            set
            {
                _useConsistentAutoZoom = value;
                if (value) CancelActiveZoom();
            }
        }

        public float ConsistentZoomPadding
        {
            get => _consistentZoomPadding;
            set => _consistentZoomPadding = value;
        }

        public bool IsConsistentTransitionActive =>
            _currentStrategy is ConsistentFramingTransitionStrategy && !_currentStrategy.IsComplete;

        public bool HasActiveStrategy => _currentStrategy != null;
        #endregion

        #region Core Update Loop
        /// <summary>
        /// Primary update method - call this from LateUpdate every frame.
        /// Executes current strategy or applies consistent framing.
        /// </summary>
        public void Update()
        {
            if (!HullCamBridge.IsAvailable) return;

            object hullCamModule = HullCamBridge.GetCurrentCamera();
            if (hullCamModule == null)
            {
                _currentStrategy = null;
                _activeCameraModule = null;
                return;
            }

            // Initialize if camera changed
            if (hullCamModule != _activeCameraModule)
            {
                InitializeForCamera(hullCamModule);
            }

            // Execute strategy if active
            if (_currentStrategy != null)
            {
                ExecuteStrategy(Time.deltaTime, hullCamModule);
            }
            else if (_useConsistentAutoZoom)
            {
                ApplyConsistentFramingToHullCam();
            }
            else
            {
                // No strategy and no consistent framing - ensure we track current FOV
                _currentFOV = HullCamBridge.GetCameraFoV(hullCamModule);
            }
        }

        private void ExecuteStrategy(float deltaTime, object hullCamModule)
        {
            float currentFOV = HullCamBridge.GetCameraFoV(hullCamModule);
            float newFOV = _currentStrategy.GetTargetFOV(currentFOV, deltaTime);

            // Clamp to HullCam limits
            float minFoV = HullCamBridge.GetCameraFoVMin(hullCamModule);
            float maxFoV = HullCamBridge.GetCameraFoVMax(hullCamModule);
            newFOV = Mathf.Clamp(newFOV, minFoV, maxFoV);

            // Apply
            HullCamBridge.SetCameraFoV(hullCamModule, newFOV);
            _currentFOV = newFOV;

            // Check completion
            if (_currentStrategy.IsComplete)
            {
                // Handoff to consistent framing if applicable
                if (_currentStrategy is ConsistentFramingTransitionStrategy && _enableConsistentOnComplete)
                {
                    _useConsistentAutoZoom = true;
                    _enableConsistentOnComplete = false;
                }
                _currentStrategy = null;
            }
        }

        private void InitializeForCamera(object hullCamModule)
        {
            _activeCameraModule = hullCamModule;
            _currentFOV = HullCamBridge.GetCameraFoV(hullCamModule);
            _currentStrategy = null;
            _rateInput = 0f;
        }
        #endregion

        #region Rate-Based Control
        /// <summary>
        /// Sets rate input (-1 to 1). Creates RateBasedZoomStrategy automatically if needed.
        /// </summary>
        public void SetRateInput(float input)
        {
            _rateInput = Mathf.Clamp(input, -1f, 1f);

            // Auto-activate rate-based strategy if we have input but nothing active
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

        /// <summary>
        /// Decays zoom intent for elastic slider behavior.
        /// </summary>
        public void DecayZoomIntent(float deltaTime)
        {
            if (!Input.GetMouseButton(0))
            {
                _rateInput = Mathf.MoveTowards(_rateInput, 0f,
                    deltaTime * CinematicUIResources.Layout.Zoom.RETURN_SPEED);
            }
        }
        #endregion

        #region Target-Based Control
        /// <summary>
        /// Queues a target zoom for real-time execution.
        /// Zero duration = instant application.
        /// </summary>
        public void QueueTargetZoom(float targetFOV, float duration, ZoomCurve curve)
        {
            CancelActiveZoom();

            if (duration < 0.001f)
            {
                _currentStrategy = new InstantZoomStrategy(targetFOV);
            }
            else
            {
                _currentStrategy = new TargetBasedZoomStrategy(targetFOV, duration, curve);
            }
        }

        /// <summary>
        /// Queues a transition to consistent framing FOV.
        /// Upon completion, automatically enables consistent framing mode.
        /// </summary>
        public void QueueConsistentTransition(float duration, ZoomCurve curve)
        {
            CancelActiveZoom();

            object hullCamModule = HullCamBridge.GetCurrentCamera();
            if (hullCamModule == null) return;

            float startFOV = HullCamBridge.GetCameraFoV(hullCamModule);

            if (duration < 0.001f)
            {
                // Instant - just enable immediately
                _useConsistentAutoZoom = true;
                return;
            }

            _currentStrategy = new ConsistentFramingTransitionStrategy(
                startFOV, duration, curve, _consistentZoomPadding);
            _enableConsistentOnComplete = true;
        }

        /// <summary>
        /// Cancels any active zoom strategy (rate or target).
        /// Does not affect consistent framing mode.
        /// </summary>
        public void CancelActiveZoom()
        {
            _currentStrategy = null;
            _enableConsistentOnComplete = false;
        }
        #endregion

        #region Consistent Framing
        /// <summary>
        /// Applies consistent framing to HullCam immediately.
        /// Call this every frame when UseConsistentAutoZoom is enabled.
        /// </summary>
        public void ApplyConsistentFramingToHullCam()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || FlightCamera.fetch == null) return;

            Vector3 camPos = FlightCamera.fetch.transform.position;
            float targetFOV = ZoomMathUtility.CalculateConsistentFramingFOV(vessel, camPos, _consistentZoomPadding);

            object hullCamModule = HullCamBridge.GetCurrentCamera();
            if (hullCamModule == null) return;

            // Clamp to HullCam limits
            float minFoV = HullCamBridge.GetCameraFoVMin(hullCamModule);
            float maxFoV = HullCamBridge.GetCameraFoVMax(hullCamModule);
            targetFOV = Mathf.Clamp(targetFOV, minFoV, maxFoV);

            HullCamBridge.SetCameraFoV(hullCamModule, targetFOV);
            _currentFOV = targetFOV;
        }
        #endregion

        #region Utility
        /// <summary>
        /// Resets zoom to maximum FOV and clears all strategies.
        /// </summary>
        public void ResetZoom(float maxFov)
        {
            CancelActiveZoom();
            _useConsistentAutoZoom = false;

            object hullCamModule = HullCamBridge.GetCurrentCamera();
            if (hullCamModule != null)
            {
                HullCamBridge.SetCameraFoV(hullCamModule, maxFov);
                _currentFOV = maxFov;
            }
        }
        #endregion
    }
}