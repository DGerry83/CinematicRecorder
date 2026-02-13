using CinematicRecorder.Core;
using static CinematicRecorder.UI.CinematicUIStrings;
using System;
using UnityEngine;

namespace CinematicRecorder.UI
{
    public class RecordingControlsWindow : MonoBehaviour
    {
        #region Fields & State
        private Rect windowRect = new Rect(
            CinematicUIResources.Windows.Recording.DEFAULT_X,
            CinematicUIResources.Windows.Recording.DEFAULT_Y,
            CinematicUIResources.Windows.Recording.WIDTH,
            CinematicUIResources.Windows.Recording.HEIGHT_INITIAL
        );

        private GUIStyle windowStyle;
        private GUIStyle buttonStyle;
        private GUIStyle activeButtonStyle;
        private GUIStyle labelStyle;
        private bool stylesInitialized = false;
        private bool shouldShow = false;

        // Speed ramps state
        private bool showSpeedRamps = false;
        private float durationSlider;
        private float exponentSlider;
        private enum SpeedMode { Normal, Slow, SuperSlow, KrakenTime }
        private SpeedMode currentSpeedMode = SpeedMode.Normal;

        // NEW: Advanced Camera Settings panel state
        private bool showAdvancedCameraPanel = false;

        // Sub-controllers
        private CameraPanelController cameraPanel;
        #endregion

        #region Unity Lifecycle
        void Start()
        {
            InitStyles();
            SubscribeToEvents();
            LoadFromSessionState();

            cameraPanel = new CameraPanelController(this);
        }

        void OnDestroy()
        {
            UnsubscribeFromEvents();
            cameraPanel?.Shutdown();
        }

        void OnGUI()
        {
            cameraPanel.DrawFadeOverlay();

            if (!shouldShow && !DeterministicCaptureSession.IsRunning) return;

            UpdateWindowDimensions();

            windowRect = GUILayout.Window(
                CinematicUIResources.Windows.IDs.RecordingControls,
                windowRect,
                DrawWindow,
                Recording.WindowTitle,
                windowStyle
            );

            cameraPanel.DrawConfirmationDialogs();
        }

        void LateUpdate()
        {
            if (!shouldShow) return;
            cameraPanel.ProcessZoomLateUpdate();
        }
        #endregion

        #region Initialization
        private void InitStyles()
        {
            if (stylesInitialized) return;

            windowStyle = CinematicUIResources.Styles.Window();
            buttonStyle = CinematicUIResources.Styles.Button();
            activeButtonStyle = CinematicUIResources.Styles.ActiveButton();
            labelStyle = new GUIStyle(HighLogic.Skin.label);

            stylesInitialized = true;
        }

        private void LoadFromSessionState()
        {
            durationSlider = SessionState.RampDurationDefault;
            exponentSlider = Mathf.Log(SessionState.RampExponent / SessionState.RampExponentMin)
                           / Mathf.Log(SessionState.RampExponentMax / SessionState.RampExponentMin);
        }
        #endregion

        #region Window Dimensions & Animation
        private void UpdateWindowDimensions()
        {
            if (Event.current.type != EventType.Layout) return;

            // Calculate target width based on advanced panel visibility
            float targetWidth = showAdvancedCameraPanel
                ? (CinematicUIResources.Windows.Recording.WIDTH + CinematicUIResources.Windows.Recording.ADVANCED_PANEL_WIDTH + 10)
                : CinematicUIResources.Windows.Recording.WIDTH;

            windowRect.width = Mathf.Lerp(windowRect.width, targetWidth, 0.25f);
            if (Mathf.Abs(windowRect.width - targetWidth) < 1f)
                windowRect.width = targetWidth;

            float targetHeight = CalculateTargetHeight();
            windowRect.height = Mathf.Lerp(windowRect.height, targetHeight, 0.25f);
            if (Mathf.Abs(windowRect.height - targetHeight) < 0.5f)
                windowRect.height = targetHeight;
        }

        private float CalculateTargetHeight()
        {
            float speedRampHeight = showSpeedRamps ? 210f : 0f;
            float cameraPanelHeight = cameraPanel.IsVisible ? 255f : 0f;
            return CinematicUIResources.Windows.Recording.HEIGHT_BASE + speedRampHeight + cameraPanelHeight;
        }
        #endregion

        #region Window Layout
        private void DrawWindow(int id)
        {
            // Use Horizontal layout for slide-out panel support
            GUILayout.BeginHorizontal();

            // LEFT COLUMN: Main content
            GUILayout.BeginVertical(GUILayout.Width(CinematicUIResources.Windows.Recording.WIDTH - CinematicUIResources.Spacing.NORMAL * 2));

            DrawHeaderWithAdvancedToggle();
            GUILayout.Space(CinematicUIResources.Spacing.SECTION);

            DrawStatusDisplay();
            GUILayout.Space(CinematicUIResources.Spacing.SECTION);

            DrawSpeedButtons();
            GUILayout.Space(CinematicUIResources.Spacing.SECTION);

            DrawProgressInfo();
            GUILayout.Space(CinematicUIResources.Spacing.SECTION);

            DrawSpeedRampsFoldout();

            GUILayout.EndVertical();

            // RIGHT COLUMN: Advanced Camera Settings panel (slides in)
            if (showAdvancedCameraPanel)
            {
                GUILayout.Space(CinematicUIResources.Spacing.TIGHT / 2);
                GUI.color = CinematicUIResources.Colors.SEPARATOR_GRAY;
                GUILayout.Box("", GUILayout.Width(CinematicUIResources.Layout.SEPARATOR_LINE_WIDTH), GUILayout.ExpandHeight(true));
                GUI.color = Color.white;
                GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

                GUILayout.BeginVertical(GUILayout.Width(CinematicUIResources.Windows.Recording.ADVANCED_PANEL_WIDTH - CinematicUIResources.Windows.Recording.ADVANCED_MARGIN));
                DrawAdvancedCameraContent();
                GUILayout.EndVertical();
            }

            GUILayout.EndHorizontal();

            // Delegate camera panel rendering (draws below main content)
            // Note: Camera panel draws inside the left column width automatically
            cameraPanel.Draw(windowRect);

            GUI.DragWindow();
        }

        private void DrawHeaderWithAdvancedToggle()
        {
            GUILayout.BeginHorizontal();

            // Status takes remaining space
            GUILayout.BeginVertical();
            DrawStatusDisplay();
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            // Advanced Camera Settings toggle button
            GUILayout.BeginVertical(GUILayout.Width(CinematicUIResources.Windows.Recording.ADVANCED_TOGGLE_WIDTH));
            GUIStyle advStyle = CinematicUIResources.Styles.Button();
            if (showAdvancedCameraPanel)
            {
                advStyle.normal.textColor = CinematicUIResources.Colors.TOGGLE_ACTIVE_GREEN;
                advStyle.fontStyle = FontStyle.Bold;
            }

            string arrow = showAdvancedCameraPanel ? Common.arrowL : Common.arrowR;
            string buttonText = arrow + Recording.AdvancedCameraButton;
            if (GUILayout.Button(buttonText, advStyle, GUILayout.Height(CinematicUIResources.Windows.Recording.ADVANCED_TOGGLE_HEIGHT)))
            {
                showAdvancedCameraPanel = !showAdvancedCameraPanel;
            }
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }
        #endregion

        #region Advanced Camera Settings Panel
        private void DrawAdvancedCameraContent()
        {
            GUIStyle headerStyle = CinematicUIResources.Styles.Header();
            GUILayout.Label(Recording.AdvancedCameraHeader, headerStyle);
            GUILayout.Space(CinematicUIResources.Spacing.LARGE);

            // Check if we have an active CameraTools slot to configure
            var activeSlot = cameraPanel.SlotManager.ActiveSlot;
            bool hasCTSlot = activeSlot?.isCameraToolsSlot == true && activeSlot.ctSettings != null;

            GUI.enabled = hasCTSlot;

            if (!hasCTSlot)
            {
                GUIStyle infoStyle = CinematicUIResources.Styles.Info();
                infoStyle.wordWrap = true;
                GUILayout.Label("Select a CameraTools slot to configure advanced options", infoStyle);
                GUI.enabled = true;
                return;
            }

            // Path Playback Timing Toggle
            GUIStyle toggleStyle = CinematicUIResources.Styles.Toggle();
            if (activeSlot.ctSettings.LockPathingToPlaybackRate)
            {
                toggleStyle.normal.textColor = CinematicUIResources.Colors.TOGGLE_ACTIVE_GREEN;
                toggleStyle.fontStyle = FontStyle.Bold;
            }

            bool newTiming = GUILayout.Toggle(
                activeSlot.ctSettings.LockPathingToPlaybackRate,
                Recording.PathPlaybackTimingToggle,
                toggleStyle
            );

            if (newTiming != activeSlot.ctSettings.LockPathingToPlaybackRate)
            {
                activeSlot.ctSettings.LockPathingToPlaybackRate = newTiming;
                // Also update the global default for future assignments
                SessionState.CameraPathPlaybackTiming = newTiming;
            }

            GUIStyle tooltipStyle = CinematicUIResources.Styles.Help();
            tooltipStyle.wordWrap = true;
            GUILayout.Label(Recording.PathPlaybackTimingTooltip, tooltipStyle);

            GUI.enabled = true;
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

        #region Status Display
        private void DrawStatusDisplay()
        {
            if (!DeterministicCaptureSession.IsRunning)
            {
                GUILayout.Label(Recording.RecordingStopped, labelStyle);
                return;
            }

            float multiplier = DeterministicCaptureSession.CurrentTimeScale < 1.0f ?
                1.0f / DeterministicCaptureSession.CurrentTimeScale : 1.0f;

            string speedText = DeterministicCaptureSession.CurrentTimeScale >= 0.999f ?
                Recording.NormalSpeed :
                string.Format(Recording.SlowMotionFormat, multiplier);

            GUIStyle headerStyle = CinematicUIResources.Styles.Header(centered: true);
            GUILayout.Label(speedText, headerStyle);

            if (DeterministicCaptureSession.IsTransitioning)
            {
                string transitionText = DeterministicCaptureSession.CurrentTransitionDirection ==
                    DeterministicCaptureSession.TransitionDirection.Slowing ?
                    Recording.TransitionSlowing : Recording.TransitionResuming;

                GUIStyle transitionStyle = CinematicUIResources.Styles.Status(Color.yellow);
                transitionStyle.alignment = TextAnchor.MiddleCenter;
                GUILayout.Label(string.Format(Recording.TransitionLabelFormat, transitionText), transitionStyle);
            }
        }
        #endregion

        #region Speed Control Section
        private void DrawSpeedButtons()
        {
            bool running = DeterministicCaptureSession.IsRunning;
            GUI.enabled = running;

            GUILayout.BeginHorizontal();

            if (GUILayout.Button(Recording.KrakenTime, currentSpeedMode == SpeedMode.KrakenTime ? activeButtonStyle : buttonStyle, GUILayout.Width(CinematicUIResources.Layout.SpeedControl.BUTTON_WIDTH)))
                DeterministicCaptureSession.RequestKrakenTime();

            if (GUILayout.Button(Recording.SuperSlow, currentSpeedMode == SpeedMode.SuperSlow ? activeButtonStyle : buttonStyle, GUILayout.Width(CinematicUIResources.Layout.SpeedControl.BUTTON_WIDTH)))
                DeterministicCaptureSession.RequestSuperSlow();

            if (GUILayout.Button(Recording.Slow, currentSpeedMode == SpeedMode.Slow ? activeButtonStyle : buttonStyle, GUILayout.Width(CinematicUIResources.Layout.SpeedControl.BUTTON_WIDTH)))
                DeterministicCaptureSession.RequestSlow();

            if (GUILayout.Button(Recording.Resume, currentSpeedMode == SpeedMode.Normal ? activeButtonStyle : buttonStyle, GUILayout.Width(CinematicUIResources.Layout.SpeedControl.BUTTON_WIDTH)))
                DeterministicCaptureSession.RequestNormalSpeed();

            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }

        private void DrawSpeedRampsFoldout()
        {
            string label = showSpeedRamps ? Recording.SpeedRampsCollapse : Recording.SpeedRampsExpand;
            if (GUILayout.Button(label, HighLogic.Skin.button))
            {
                showSpeedRamps = !showSpeedRamps;
            }

            if (!showSpeedRamps) return;

            GUIStyle helpStyle = CinematicUIResources.Styles.Help();

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Space(CinematicUIResources.Spacing.INNER);

            GUILayout.Label(string.Format(Recording.DurationFormat, durationSlider), HighLogic.Skin.label);
            float newDuration = GUILayout.HorizontalSlider(durationSlider, CinematicUIResources.Layout.Ramp.DURATION_MIN, CinematicUIResources.Layout.Ramp.DURATION_MAX);
            if (!Mathf.Approximately(newDuration, durationSlider))
            {
                durationSlider = newDuration;
                SessionState.RampDurationDefault = newDuration;
            }
            GUILayout.Label(Recording.DurationHelper, helpStyle);
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            GUILayout.Label(string.Format(Recording.BiasFormat, SessionState.RampExponent), HighLogic.Skin.label);
            DrawExponentSlider(helpStyle);
            GUILayout.Label(GetCurveDescription(SessionState.RampExponent), helpStyle);

            GUILayout.Space(CinematicUIResources.Spacing.INNER);
            GUILayout.EndVertical();
        }

        private void DrawExponentSlider(GUIStyle helpStyle)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Recording.LingerSlow, helpStyle, GUILayout.Width(70));
            float sliderPos = GUILayout.HorizontalSlider(exponentSlider, 0f, 1f);
            GUILayout.Label(Recording.LingerNormal, helpStyle, GUILayout.Width(70));
            GUILayout.EndHorizontal();

            if (!Mathf.Approximately(sliderPos, exponentSlider))
            {
                exponentSlider = sliderPos;
                float min = SessionState.RampExponentMin;
                float max = SessionState.RampExponentMax;
                SessionState.RampExponent = min * Mathf.Pow(max / min, sliderPos);
            }
        }

        private string GetCurveDescription(float exp)
        {
            if (Mathf.Abs(exp - 1.0f) < 0.1f) return CurveDescriptions.Linear;
            if (exp > 2.5f) return CurveDescriptions.LingerNormalRushSlow;
            if (exp > 1.5f) return CurveDescriptions.GradualEntryFastExit;
            if (exp < 0.5f) return CurveDescriptions.SnapToSlow;
            if (exp < 0.8f) return CurveDescriptions.FastEntryGentleExit;
            return CurveDescriptions.Moderate;
        }
        #endregion

        #region Progress Display
        private void DrawProgressInfo()
        {
            if (!DeterministicCaptureSession.IsRunning) return;

            bool unlimited = DeterministicCaptureSession.IsUnlimitedMode;

            if (unlimited)
            {
                DrawUnlimitedProgress();
            }
            else
            {
                DrawLimitedProgress();
            }
        }

        private void DrawUnlimitedProgress()
        {
            float simulated = DeterministicCaptureSession.AccumulatedSimulatedSeconds;
            int frames = DeterministicCaptureSession.CapturedFrames;

            GUILayout.Label(string.Format(Recording.SimulatedUnlimitedFormat, simulated), labelStyle);
            GUILayout.Label(string.Format(Recording.FramesUnlimitedFormat, frames), labelStyle);

            DrawIndeterminateProgressBar();
        }

        private void DrawIndeterminateProgressBar()
        {
            Rect barRect = GUILayoutUtility.GetRect(
                0f,
                CinematicUIResources.Layout.Progress.BAR_HEIGHT,
                GUILayout.ExpandWidth(true)
            );

            float pulse = Mathf.PingPong(Time.time * CinematicUIResources.Layout.Progress.PULSE_SPEED, 1f);
            float segmentWidth = CinematicUIResources.Layout.Progress.SEGMENT_WIDTH;
            float xPos = pulse * (barRect.width - segmentWidth);

            GUI.Box(barRect, "");

            Rect segmentRect = new Rect(
                barRect.x + xPos,
                barRect.y,
                segmentWidth,
                barRect.height
            );
            GUI.DrawTexture(segmentRect, CinematicUIResources.Styles.ProgressFill().normal.background, ScaleMode.StretchToFill);
        }

        private void DrawLimitedProgress()
        {
            float current = DeterministicCaptureSession.AccumulatedSimulatedSeconds;
            float target = DeterministicCaptureSession.TargetSeconds;

            string progress = string.Format(Recording.SimulatedFormat, current, target);
            GUILayout.Label(progress, labelStyle);

            float percent = target > 0 ? Mathf.Clamp01(current / target) : 0f;

            Rect barRect = GUILayoutUtility.GetRect(
                0f,
                CinematicUIResources.Layout.Progress.BAR_HEIGHT,
                GUILayout.ExpandWidth(true)
            );

            GUI.Box(barRect, "");

            if (percent > 0f)
            {
                Rect fillRect = new Rect(
                    barRect.x,
                    barRect.y,
                    barRect.width * percent,
                    barRect.height
                );
                GUI.DrawTexture(fillRect, CinematicUIResources.Styles.ProgressFill().normal.background, ScaleMode.StretchToFill);
            }
        }
        #endregion

        #region Public API
        public void Show() { shouldShow = true; }
        public void Hide() { shouldShow = false; }
        #endregion
    }
}