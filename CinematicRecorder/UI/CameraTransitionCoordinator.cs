using System;
using UnityEngine;
using CinematicRecorder.Core;

namespace CinematicRecorder.UI
{
    /// <summary>
    /// Manages fade-to-black transitions for camera swaps.
    /// Supports both real-time (Time.deltaTime) and deterministic (frame-based) modes.
    /// </summary>
    public class CameraTransitionCoordinator
    {
        #region Configuration
        public bool UseFadeOnSwap { get; set; } = true;

        /// <summary>
        /// Slider value 0-1 representing fade duration between MIN and MAX
        /// </summary>
        public float FadeDurationSlider { get; set; } = 0.5f;

        public float FadeAlpha { get; private set; } = 0f;
        public bool IsFading { get; private set; } = false;

        /// <summary>
        /// True when fade has reached midpoint (full black) and camera switch has triggered
        /// </summary>
        public bool IsCompletingSwitch { get; private set; } = false;
        #endregion
        #region State
        private float _fadeDurationSeconds;
        private float _elapsedTime;
        private Action _onFadeMidpoint;
        private bool _midpointTriggered;

        private bool _isDeterministicMode;
        private int _currentFadeFrame;
        private int _totalFadeFrames;
        private int _playbackFps;
        #endregion
        #region Initialization
        public CameraTransitionCoordinator()
        {
            // Default duration calculation (will be recalculated on BeginTransition)
            UpdateDurationFromSlider();
        }

        private void UpdateDurationFromSlider()
        {
            _fadeDurationSeconds = Mathf.Lerp(
                CinematicUIResources.Layout.Crossfade.DURATION_MIN,
                CinematicUIResources.Layout.Crossfade.DURATION_MAX,
                FadeDurationSlider
            );
        }
        #endregion
        #region Public API
        /// <summary>
        /// Begins a fade transition. Automatically selects deterministic or real-time mode.
        /// </summary>
        public void BeginTransition(Action cameraSwitchAction, bool useDeterministic = false)
        {
            if (!UseFadeOnSwap)
            {
                // Fading disabled, execute immediately
                cameraSwitchAction?.Invoke();
                return;
            }

            _onFadeMidpoint = cameraSwitchAction;
            IsFading = true;
            IsCompletingSwitch = false;
            _midpointTriggered = false;
            FadeAlpha = 0f;

            // Recalculate duration in case slider changed
            UpdateDurationFromSlider();

            _isDeterministicMode = useDeterministic && DeterministicCaptureSession.IsRunning;

            if (_isDeterministicMode)
            {
                _playbackFps = DeterministicCaptureSession.PlaybackFPS > 0 ?
                    DeterministicCaptureSession.PlaybackFPS : 60;

                // Calculate total frames for the fade duration
                _totalFadeFrames = Mathf.Max(2, Mathf.RoundToInt(_fadeDurationSeconds * _playbackFps));
                _currentFadeFrame = 0;
                _elapsedTime = 0f; // Not used in deterministic mode
            }
            else
            {
                _elapsedTime = 0f;
                _totalFadeFrames = 0;
                _currentFadeFrame = 0;
            }
        }

        /// <summary>
        /// Updates fade progress for real-time mode. Call from OnGUI or LateUpdate.
        /// </summary>
        public void UpdateFade()
        {
            if (!IsFading || _isDeterministicMode) return;

            _elapsedTime += Time.deltaTime;
            float progress = Mathf.Clamp01(_elapsedTime / _fadeDurationSeconds);

            CalculateAlpha(progress);

            if (progress >= 1f)
            {
                CompleteFade();
            }
        }

        /// <summary>
        /// Updates fade progress for deterministic mode. Call once per physics step from ProcessZoomLateUpdate.
        /// </summary>
        public void UpdateDeterministicFade()
        {
            if (!IsFading || !_isDeterministicMode) return;

            _currentFadeFrame++;
            float progress = _currentFadeFrame / (float)_totalFadeFrames;

            CalculateAlpha(progress);

            if (_currentFadeFrame >= _totalFadeFrames)
            {
                CompleteFade();
            }
        }

        /// <summary>
        /// Gets the current fade color (black with alpha)
        /// </summary>
        public Color GetFadeColor()
        {
            return new Color(0f, 0f, 0f, FadeAlpha);
        }
        #endregion
        #region Private Methods

        private void CalculateAlpha(float progress)
        {
            progress = Mathf.Clamp01(progress);

            // First half: fade to black (0 -> 0.5), Second half: fade from black (0.5 -> 1)
            if (progress < 0.5f)
            {
                FadeAlpha = Mathf.Lerp(0f, 1f, progress * 2f);
                IsCompletingSwitch = false;
            }
            else
            {
                FadeAlpha = Mathf.Lerp(1f, 0f, (progress - 0.5f) * 2f);

                // Trigger midpoint action exactly once at 50%
                if (!_midpointTriggered)
                {
                    _onFadeMidpoint?.Invoke();
                    _midpointTriggered = true;
                    IsCompletingSwitch = true;
                }
            }
        }

        private void CompleteFade()
        {
            IsFading = false;
            FadeAlpha = 0f;
            IsCompletingSwitch = false;
            _isDeterministicMode = false;
            _onFadeMidpoint = null;
        }

        #endregion
    }
}