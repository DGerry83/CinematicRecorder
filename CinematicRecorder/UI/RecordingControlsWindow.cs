using CinematicRecorder.Core;
using CinematicRecorder.Integration;
using DearImGuiKSP;
using DearImGuiKSP.Application;
using System;
using UnityEngine;
using static CinematicRecorder.UI.CinematicUIStrings;

namespace CinematicRecorder.UI
{
    /// <summary>
    /// Recording controls view — speed selection, capture progress, and speed-ramp
    /// parameters. DearImGui-KSP VIEW-1 port (chunk C5) of the IMGUI MonoBehaviour;
    /// follows the SettingsDialog pattern (chunk C2). Drawn inside the
    /// "Recording Controls" window scope from CinematicUiHost. The camera panel the
    /// old window hosted is excluded here (PANEL-1) and returns in chunk C8.
    /// </summary>
    public class RecordingControlsWindow
    {
        #region Constants & Static State
        // Progress bar geometry: the old GUI.Box + GUI.DrawTexture bar is redrawn on
        // the ImGuiDraw canvas; autoResize windows need an explicit canvas width.
        private const float ProgressBarWidth = 320f;
        private const float ProgressBarRounding = 2f;

        // Old CinematicUIResources.Colors.PROGRESS_BLUE (0.2, 0.6, 0.9) as bytes.
        private static readonly Color32 ProgressFillColor = new Color32(51, 153, 230, 255);
        private static readonly Color32 ProgressBgColor = KspPalette.FrameBg;

        // Grey for the idle speed-button stand-ins (no disabled-widget state in the
        // library — C2 D-U8 #1).
        private static readonly Color32 GreyedColor = KspPalette.TextLightGrey;
        #endregion

        #region Fields & State
        private bool shouldShow;
        private float durationSlider;
        private float exponentSlider;

        private enum SpeedMode { Normal, Slow, SuperSlow, KrakenTime }
        private SpeedMode currentSpeedMode = SpeedMode.Normal;
        #endregion

        #region Public API
        /// <summary>True while the window should be drawn.</summary>
        public bool IsVisible => shouldShow || DeterministicCaptureSession.IsRunning;

        /// <summary>Subscribes to session events and seeds ramp state from SessionState.</summary>
        public RecordingControlsWindow()
        {
            SubscribeToEvents();
            LoadFromSessionState();
        }

        /// <summary>Shows the window.</summary>
        public void Show() { shouldShow = true; }

        /// <summary>Hides the window. As in the old UI, the window stays drawn while a recording is running.</summary>
        public void Hide() { shouldShow = false; }

        /// <summary>
        /// Unsubscribes from session events and runs the CameraTools shutdown preserved
        /// from the MonoBehaviour version. Called from CinematicUiHost.OnDestroy.
        /// </summary>
        internal void Shutdown()
        {
            UnsubscribeFromEvents();
            CameraToolsAPIManager.Shutdown();
        }
        #endregion

        #region Draw
        /// <summary>
        /// Per-frame widget declarations for the whole window. Called only from
        /// CinematicUiHost, inside the "Recording Controls" window scope.
        /// Layout per LAYOUT_PROPOSAL §3: status line → speed button Row → progress
        /// (while recording) → Speed Ramps collapsing header (L8).
        /// </summary>
        internal void Draw()
        {
            DrawStatusLine();
            DrawSpeedButtons();

            if (DeterministicCaptureSession.IsRunning)
            {
                DrawProgressInfo();
            }

            // Default closed, matching the old foldout's initial state.
            if (DearImGuiKSP.DearImGuiKSP.CollapsingHeader(Recording.SpeedRampsHeader))
            {
                DrawSpeedRamps();
            }
        }
        #endregion

        #region Status Line
        private static void DrawStatusLine()
        {
            if (!DeterministicCaptureSession.IsRunning)
            {
                DearImGuiKSP.DearImGuiKSP.Text(Recording.RecordingStopped);
                return;
            }

            float multiplier = DeterministicCaptureSession.CurrentTimeScale < 1.0f ?
                1.0f / DeterministicCaptureSession.CurrentTimeScale : 1.0f;

            string speedText = DeterministicCaptureSession.CurrentTimeScale >= 0.999f ?
                Recording.NormalSpeed :
                string.Format(Recording.SlowMotionFormat, multiplier);

            // Old UI drew a separate yellow transition line; the port appends it to
            // the status line (single-line status). The "Transition:" wording carries
            // the meaning without the color.
            if (DeterministicCaptureSession.IsTransitioning)
            {
                string transitionText = DeterministicCaptureSession.CurrentTransitionDirection ==
                    DeterministicCaptureSession.TransitionDirection.Slowing ?
                    Recording.TransitionSlowing : Recording.TransitionResuming;

                DearImGuiKSP.DearImGuiKSP.Text(
                    string.Format(Recording.StatusTransitionFormat, speedText, transitionText));
                return;
            }

            DearImGuiKSP.DearImGuiKSP.Text(speedText);
        }
        #endregion

        #region Speed Control Section
        private void DrawSpeedButtons()
        {
            using (ImGuiEx.Row())
            {
                if (!DeterministicCaptureSession.IsRunning)
                {
                    // Old semantics: the whole row was GUI.enabled = false while idle.
                    // Greyed stand-ins replace the disabled buttons (uniform idiom,
                    // C3 ruling D-2).
                    DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor, Recording.KrakenTime);
                    DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor, Recording.SuperSlow);
                    DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor, Recording.Slow);
                    DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor, Recording.Resume);
                    return;
                }

                DrawSpeedButton(Recording.KrakenTime, SpeedMode.KrakenTime,
                    DeterministicCaptureSession.RequestKrakenTime);
                DrawSpeedButton(Recording.SuperSlow, SpeedMode.SuperSlow,
                    DeterministicCaptureSession.RequestSuperSlow);
                DrawSpeedButton(Recording.Slow, SpeedMode.Slow,
                    DeterministicCaptureSession.RequestSlow);
                DrawSpeedButton(Recording.Resume, SpeedMode.Normal,
                    DeterministicCaptureSession.RequestNormalSpeed);
            }
        }

        private void DrawSpeedButton(string label, SpeedMode mode, Action request)
        {
            // Old active state was green bold button text; bold is not expressible in
            // the library (C2 D-U8 #3), so the active button gets a green text scope.
            if (currentSpeedMode == mode)
            {
                using (ImGuiEx.StyleColor(ImGuiCol.Text, KspPalette.GreenLight))
                {
                    if (DearImGuiKSP.DearImGuiKSP.Button(label))
                    {
                        request();
                    }
                }
            }
            else if (DearImGuiKSP.DearImGuiKSP.Button(label))
            {
                request();
            }
        }
        #endregion

        #region Progress Display
        private static void DrawProgressInfo()
        {
            if (DeterministicCaptureSession.IsUnlimitedMode)
            {
                DrawUnlimitedProgress();
            }
            else
            {
                DrawLimitedProgress();
            }
        }

        private static void DrawUnlimitedProgress()
        {
            float simulated = DeterministicCaptureSession.AccumulatedSimulatedSeconds;
            int frames = DeterministicCaptureSession.CapturedFrames;

            DearImGuiKSP.DearImGuiKSP.Text(string.Format(Recording.SimulatedUnlimitedFormat, simulated));
            DearImGuiKSP.DearImGuiKSP.Text(string.Format(Recording.FramesUnlimitedFormat, frames));

            DrawPulseBar();
        }

        private static void DrawPulseBar()
        {
            Vector2 origin = ImGuiDraw.GetCursorScreenPos();
            float barHeight = CinematicUIResources.Layout.Progress.BAR_HEIGHT;
            ImGuiDraw.Dummy(ProgressBarWidth, barHeight);
            ImGuiDraw.AddRectFilled(origin, origin + new Vector2(ProgressBarWidth, barHeight),
                ProgressBgColor, ProgressBarRounding);

            // Real-time ping-pong, exactly the old math (outside footage, so the
            // wall-clock Time.time drive is fine).
            float pulse = Mathf.PingPong(Time.time * CinematicUIResources.Layout.Progress.PULSE_SPEED, 1f);
            float segmentWidth = CinematicUIResources.Layout.Progress.SEGMENT_WIDTH;
            float xPos = pulse * (ProgressBarWidth - segmentWidth);

            Vector2 segmentOrigin = origin + new Vector2(xPos, 0f);
            ImGuiDraw.AddRectFilled(segmentOrigin, segmentOrigin + new Vector2(segmentWidth, barHeight),
                ProgressFillColor, ProgressBarRounding);
        }

        private static void DrawLimitedProgress()
        {
            float current = DeterministicCaptureSession.AccumulatedSimulatedSeconds;
            float target = DeterministicCaptureSession.TargetSeconds;

            DearImGuiKSP.DearImGuiKSP.Text(string.Format(Recording.SimulatedFormat, current, target));

            float percent = target > 0 ? Mathf.Clamp01(current / target) : 0f;

            Vector2 origin = ImGuiDraw.GetCursorScreenPos();
            float barHeight = CinematicUIResources.Layout.Progress.BAR_HEIGHT;
            ImGuiDraw.Dummy(ProgressBarWidth, barHeight);
            ImGuiDraw.AddRectFilled(origin, origin + new Vector2(ProgressBarWidth, barHeight),
                ProgressBgColor, ProgressBarRounding);

            if (percent > 0f)
            {
                ImGuiDraw.AddRectFilled(origin, origin + new Vector2(ProgressBarWidth * percent, barHeight),
                    ProgressFillColor, ProgressBarRounding);
            }
        }
        #endregion

        #region Speed Ramps
        private void DrawSpeedRamps()
        {
            using (ImGuiEx.Row())
            {
                DearImGuiKSP.DearImGuiKSP.Text(Recording.RampDurationLabel);

                float duration = durationSlider;
                bool durationChanged = DearImGuiKSP.DearImGuiKSP.SliderFloat("##rampDuration", ref duration,
                    CinematicUIResources.Layout.Ramp.DURATION_MIN, CinematicUIResources.Layout.Ramp.DURATION_MAX);
                // The old permanent help sentence, now a tooltip on the slider.
                DearImGuiKSP.DearImGuiKSP.Tooltip(Recording.DurationHelper);
                DearImGuiKSP.DearImGuiKSP.Text(string.Format(Recording.RampDurationValueFormat, duration));

                if (durationChanged)
                {
                    durationSlider = duration;
                    SessionState.RampDurationDefault = duration;
                }
            }

            using (ImGuiEx.Row())
            {
                DearImGuiKSP.DearImGuiKSP.Text(Recording.RampBiasLabel);

                float bias = exponentSlider;
                bool biasChanged = DearImGuiKSP.DearImGuiKSP.SliderFloat("##rampBias", ref bias, 0f, 1f);
                // The old Linger Slow / Linger Normal slider-end labels, now a tooltip.
                DearImGuiKSP.DearImGuiKSP.Tooltip(Recording.RampBiasTooltip);
                DearImGuiKSP.DearImGuiKSP.Text(string.Format(Recording.RampBiasValueFormat, SessionState.RampExponent));

                if (biasChanged)
                {
                    // Log interpolation into RampExponentMin..Max — bit-identical to the
                    // old slider math.
                    exponentSlider = bias;
                    float min = SessionState.RampExponentMin;
                    float max = SessionState.RampExponentMax;
                    SessionState.RampExponent = min * Mathf.Pow(max / min, bias);
                }
            }

            // Dynamic curve description, kept as one text line (thresholds unchanged).
            DearImGuiKSP.DearImGuiKSP.Text(GetCurveDescription(SessionState.RampExponent));
        }

        private static string GetCurveDescription(float exp)
        {
            if (Mathf.Abs(exp - 1.0f) < 0.1f) return CurveDescriptions.Linear;
            if (exp > 2.5f) return CurveDescriptions.LingerNormalRushSlow;
            if (exp > 1.5f) return CurveDescriptions.GradualEntryFastExit;
            if (exp < 0.5f) return CurveDescriptions.SnapToSlow;
            if (exp < 0.8f) return CurveDescriptions.FastEntryGentleExit;
            return CurveDescriptions.Moderate;
        }
        #endregion

        #region Event Subscription & Handlers
        private void SubscribeToEvents()
        {
            DeterministicCaptureSession.OnRecordingStarted += OnRecordingStarted;
            DeterministicCaptureSession.OnRecordingStopped += OnRecordingStopped;
            DeterministicCaptureSession.OnTimeScaleChanged += OnTimeScaleChanged;
        }

        private void UnsubscribeFromEvents()
        {
            DeterministicCaptureSession.OnRecordingStarted -= OnRecordingStarted;
            DeterministicCaptureSession.OnRecordingStopped -= OnRecordingStopped;
            DeterministicCaptureSession.OnTimeScaleChanged -= OnTimeScaleChanged;
        }

        private void OnRecordingStarted()
        {
            shouldShow = true;
            currentSpeedMode = SpeedMode.Normal;
        }

        private void OnRecordingStopped()
        {
            currentSpeedMode = SpeedMode.Normal;
        }

        private void OnTimeScaleChanged(float newScale)
        {
            float tolerance = 0.01f;
            var scaleMappings = new (float scale, SpeedMode mode)[]
            {
                (1.0f, SpeedMode.Normal),
                (DeterministicCaptureSession.SUPER_SLOW_SCALE, SpeedMode.SuperSlow),
                (DeterministicCaptureSession.SLOW_SCALE, SpeedMode.Slow),
                (DeterministicCaptureSession.KRAKEN_TIME_SCALE, SpeedMode.KrakenTime)
            };

            currentSpeedMode = SpeedMode.Normal;
            foreach (var mapping in scaleMappings)
            {
                if (Mathf.Abs(newScale - mapping.scale) < tolerance)
                {
                    currentSpeedMode = mapping.mode;
                    break;
                }
            }
        }
        #endregion

        #region Initialization
        private void LoadFromSessionState()
        {
            durationSlider = SessionState.RampDurationDefault;
            exponentSlider = Mathf.Log(SessionState.RampExponent / SessionState.RampExponentMin)
                           / Mathf.Log(SessionState.RampExponentMax / SessionState.RampExponentMin);
        }
        #endregion
    }
}
