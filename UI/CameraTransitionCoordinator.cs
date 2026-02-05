using System;
using UnityEngine;

namespace CinematicRecorder.UI
{
    /// <summary>
    /// Coordinates crossfade transitions between camera switches.
    /// Manages fade state (alpha, timing) but not the actual GUI rendering.
    /// </summary>
    public class CameraTransitionCoordinator
    {
        private float screenFadeAlpha = 0f;
        private bool isFading = false;
        private float fadeSpeed = 8f;
        private Action pendingCameraAction;
        private bool useFadeOnSwap = true;
        private float fadeDurationSlider = 0.5f;
        private bool cameraSwitchPending = false;

        public bool IsFading => isFading;
        public float FadeAlpha => screenFadeAlpha;
        public bool UseFadeOnSwap { get => useFadeOnSwap; set => useFadeOnSwap = value; }
        public float FadeDurationSlider { get => fadeDurationSlider; set => fadeDurationSlider = value; }

        /// <summary>
        /// True if an action was triggered at peak fade and we're now fading back out
        /// </summary>
        public bool IsCompletingSwitch => cameraSwitchPending;

        public event Action OnFadeStarted;
        public event Action OnFadePeakReached; // Action is invoked here
        public event Action OnFadeComplete;

        /// <summary>
        /// Initiates a camera switch with optional fade.
        /// </summary>
        /// <param name="cameraAction">Action to execute at fade peak (e.g., activate camera)</param>
        public void BeginTransition(Action cameraAction)
        {
            if (!useFadeOnSwap)
            {
                cameraAction?.Invoke();
                return;
            }

            if (isFading) return; // Already fading

            isFading = true;
            screenFadeAlpha = 0f;
            cameraSwitchPending = false;

            float duration = CinematicUIResources.Layout.Crossfade.DURATION_MIN +
                fadeDurationSlider * (CinematicUIResources.Layout.Crossfade.DURATION_MAX - CinematicUIResources.Layout.Crossfade.DURATION_MIN);
            fadeSpeed = 1f / duration;

            pendingCameraAction = cameraAction;
            OnFadeStarted?.Invoke();
        }

        /// <summary>
        /// Updates fade state. Call from OnGUI before drawing fade overlay.
        /// </summary>
        public void UpdateFade()
        {
            if (!isFading) return;

            screenFadeAlpha += Time.unscaledDeltaTime * fadeSpeed;

            if (screenFadeAlpha >= 1f && pendingCameraAction != null)
            {
                // Peak reached - execute pending action
                screenFadeAlpha = 1f;
                pendingCameraAction?.Invoke();
                pendingCameraAction = null;
                cameraSwitchPending = true;
                OnFadePeakReached?.Invoke();
            }
            else if (cameraSwitchPending)
            {
                // Fading back out
                cameraSwitchPending = false;
                fadeSpeed = -Mathf.Abs(fadeSpeed);
            }
            else if (screenFadeAlpha <= 0f && fadeSpeed < 0)
            {
                // Fade complete
                screenFadeAlpha = 0f;
                isFading = false;
                fadeSpeed = Mathf.Abs(fadeSpeed);
                OnFadeComplete?.Invoke();
            }
        }

        /// <summary>
        /// Immediately cancels any active fade and resets state.
        /// </summary>
        public void CancelFade()
        {
            isFading = false;
            screenFadeAlpha = 0f;
            pendingCameraAction = null;
            cameraSwitchPending = false;
            fadeSpeed = Mathf.Abs(fadeSpeed);
        }

        /// <summary>
        /// Calculates current fade color alpha for GUI rendering.
        /// </summary>
        public Color GetFadeColor()
        {
            return new Color(0, 0, 0, screenFadeAlpha);
        }
    }
}