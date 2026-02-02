using CinematicRecorder.Core;
using CinematicRecorder.Integration;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CinematicRecorder.UI
{
    public class RecordingControlsWindow : MonoBehaviour
    {
        #region Configuration Constants
        // Window Base Configuration
        private const float WINDOW_X_DEFAULT = 300f;
        private const float WINDOW_Y_DEFAULT = 480f;
        private const float WINDOW_WIDTH = 355f;
        private const float WINDOW_HEIGHT_INITIAL = 220f;
        private const float WINDOW_HEIGHT_BASE = 140f;
        private const int WINDOW_ID = 12347;
        private const int DIALOG_DELETE_ID = 99999;
        private const int DIALOG_UNASSIGN_ID = 99998;

        // Layout Spacing & Sizing
        private const float SECTION_SPACING = 8f;
        private const float INNER_PADDING = 5f;
        private const float BOX_PADDING = 8f;
        private const float ELEMENT_GAP = 10f;
        private const float SMALL_GAP = 4f;

        // Speed Control Section
        private const float SPEED_BUTTON_WIDTH = 80f;
        private const float SPEED_BUTTON_HEIGHT = 30f; // implicit from GUILayout
        private const int HEADER_FONT_SIZE = 14;

        // Progress Bar
        private const float PROGRESS_BAR_WIDTH = 200f;
        private const float PROGRESS_BAR_HEIGHT = 16f;
        private const float PROGRESS_PULSE_SPEED = 2f;
        private const float PROGRESS_SEGMENT_WIDTH = 60f;
        private static readonly Color PROGRESS_BAR_COLOR = new Color(0.2f, 0.6f, 0.9f);

        // Speed Ramps Section
        private const float RAMP_DURATION_MIN = 0.1f;
        private const float RAMP_DURATION_MAX = 3.0f;
        private const int HELP_FONT_SIZE = 10;

        // Camera Panel Grid
        private const int CAMERA_GRID_ROWS = 4;
        private const int CAMERA_GRID_COLS = 4;
        private const int TOTAL_CAMERA_SLOTS = 16; // 4x4
        private const float CAMERA_BUTTON_SIZE = 32f;
        private const float CAMERA_BUTTON_HEIGHT = 30f;
        private const float GRID_COLUMN_WIDTH = 140f; // (4x32) + (3x~4px gap)
        private const float GRID_TEXT_COLUMN_WIDTH = 160f;

        // Camera Panel Colors ( reused in styles )
        private static readonly Color COLOR_CAM_ACTIVE = new Color(0.2f, 0.8f, 0.2f);      // Green
        private static readonly Color COLOR_CAM_ASSIGNED = new Color(1f, 0.9f, 0.2f);      // Yellow
        private static readonly Color COLOR_CAM_UNAVAILABLE = new Color(0.8f, 0.2f, 0.2f); // Red
        private static readonly Color COLOR_CAM_UNASSIGNED = new Color(0.3f, 0.3f, 0.3f);  // Gray
        private static readonly Color COLOR_CAM_REMOTE = new Color(0.0f, 0.8f, 0.8f);      // Aqua
        private static readonly Color COLOR_TEXT_DIM = Color.gray;
        public static readonly Color INFO_TEXT_COLOR = new Color(1f, 0.5490196f, 0f);       // Orange
        private static readonly Color COLOR_TEXT_GLOW = new Color(0.2f, 1f, 0.2f);
        private static readonly Color COLOR_AUTO_TRACK = new Color(0.2f, 0.8f, 1f);

        // Crossfade Configuration
        private const float FADE_DURATION_MIN = 0.05f;
        private const float FADE_DURATION_MAX = 2.0f; // Actually 0.05 + 0.95*2, but effectively up to 2s
        private const float FADE_SLIDER_MAX = 1f;

        // Zoom Control
        private const float ZOOM_SMOOTH_TIME = 0.15f;
        private const float ZOOM_MAX_SPEED = 40f;
        private const float ZOOM_RETURN_SPEED = 8f;
        private const float ZOOM_INTENT_THRESHOLD = 0.05f;
        private const float ZOOM_LABEL_WIDTH = 30f;
        private const float ZOOM_RESET_BUTTON_WIDTH = 90f;

        // Dialog Dimensions
        private const float DIALOG_WIDTH = 200f;
        private const float DIALOG_HEIGHT = 100f;
        private const float DIALOG_OFFSET_X = 60f;
        private const float DIALOG_OFFSET_Y = 80f;
        private const float DIALOG_BUTTON_HEIGHT = 30f;

        // UI String Constants (to avoid typos in repeated strings)
        private const string LOG_PREFIX = "[CamPanel]";
        #endregion

        private Rect windowRect = new Rect(WINDOW_X_DEFAULT, WINDOW_Y_DEFAULT, WINDOW_WIDTH, WINDOW_HEIGHT_INITIAL);
        private GUIStyle windowStyle;
        private GUIStyle buttonStyle;
        private GUIStyle activeButtonStyle;
        private GUIStyle labelStyle;
        private bool stylesInitialized = false;
        private bool shouldShow = false;


        // Speed ramps foldout state
        private bool showSpeedRamps = false;
        private float durationSlider;
        private float exponentSlider;

        // Button state tracking for visual feedback
        private enum SpeedMode { Normal, Slow, SuperSlow, KrakenTime }
        private SpeedMode currentSpeedMode = SpeedMode.Normal;

        // Camera Panel Fields
        private bool showCameraPanel = false;
        private List<CameraSlot> cameraSlots = new List<CameraSlot>(); // 16 slots
        private GUIStyle[] cameraButtonStyles = new GUIStyle[5]; // Cache for 4 states
        private float cameraButtonSize; // Uniform size for grid
        private float gridColumnWidth;       // Set to 140f in Init
        private object lastKnownActiveCamera; // Watchdog tracking
        private bool cameraPanelStylesInitialized = false;
        private bool showPresetList = false;

        // Crossfade Controls
        private float screenFadeAlpha = 0f;
        private bool isFading = false;
        private float fadeSpeed = 8f; // ~0.125 seconds to black
        private Action pendingCameraAction; // Callback to execute at peak darkness
        private bool useFadeOnSwap = true;
        private float fadeDurationSlider = 0.5f; // 0-1 mapped to 2.0s - 0.05s duration

        // Preset name input buffer (auto-populates with vessel name or loaded preset)
        private string presetNameBuffer = "";
        private bool showDeleteConfirm = false;
        private int pendingUnassignSlot = -1;

        // Zoom control state
        private float zoomIntentSlider = 0f; // -1 (out) to 1 (in)
        private float zoomSmoothVelocity = 0f; // For SmoothDamp
        private bool autoDistanceTracking = false;
        private float autoZoomDistanceRef = 100f; // Distance that maps to middle FOV
        private float targetFoV = 60f;
        private float currentFoV = 60f;
        private object zoomControlledCamera = null; // Which camera we're currently zooming



        void Start()
        {
            InitStyles();
            SubscribeToEvents();
            LoadFromSessionState();
            InitCameraPanel();

            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
            GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequested);
        }

        #region UI Helpers

        private Texture2D CreateColorTexture(Color color)
        {
            Color[] pixels = new Color[4]; // 2x2
            for (int i = 0; i < 4; i++) pixels[i] = color;
            Texture2D result = new Texture2D(2, 2);
            result.SetPixels(pixels);
            result.Apply();
            return result;
        }

        private GUIStyle CreateColoredButtonStyle(Color background, Color textColor, FontStyle fontStyle = FontStyle.Bold)
        {
            GUIStyle style = new GUIStyle(HighLogic.Skin.button);
            style.fontStyle = fontStyle;
            style.normal.textColor = textColor;
            style.hover.textColor = textColor; // Maintain color on hover
            style.active.textColor = textColor;
            style.normal.background = CreateColorTexture(background);
            style.hover.background = style.normal.background; // Same background
            style.active.background = style.normal.background;
            return style;
        }

        private GUIStyle CreateLabelStyle(Color color, FontStyle fontStyle = FontStyle.Normal, int fontSize = 0, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            GUIStyle style = new GUIStyle(HighLogic.Skin.label);
            style.normal.textColor = color;
            style.fontStyle = fontStyle;
            if (fontSize > 0) style.fontSize = fontSize;
            style.alignment = alignment;
            return style;
        }

        private Rect CenterDialogRect(float width, float height)
        {
            return new Rect(
                windowRect.x + DIALOG_OFFSET_X,
                windowRect.y + DIALOG_OFFSET_Y,
                width,
                height
            );
        }

        #endregion
        void InitCameraPanel()
        {
            // Initialize 16 empty slots
            cameraSlots.Clear();
            for (int i = 0; i < TOTAL_CAMERA_SLOTS; i++)
            {
                cameraSlots.Add(new CameraSlot { buttonID = $"Cam_{i}" });
            }

            // Subscribe to scenario
            if (CameraPanelConfig.Instance != null)
            {
                CameraPanelConfig.Instance.OnPresetLoaded += OnPresetLoaded;
            }

            cameraButtonSize = CAMERA_BUTTON_SIZE;
            // Grid = 4 buttons × 32px + 3 gaps × ~4px = 140px
            gridColumnWidth = 140f;
        }

        void OnDestroy()
        {
            UnsubscribeFromEvents();
        }

        private void InitStyles()
        {
            if (stylesInitialized) return;

            windowStyle = new GUIStyle(HighLogic.Skin.window);
            buttonStyle = new GUIStyle(HighLogic.Skin.button);

            activeButtonStyle = new GUIStyle(HighLogic.Skin.button);
            activeButtonStyle.normal.textColor = Color.green;
            activeButtonStyle.fontStyle = FontStyle.Bold;

            labelStyle = new GUIStyle(HighLogic.Skin.label);
            stylesInitialized = true;
        }

        private void LoadFromSessionState()
        {
            durationSlider = SessionState.RampDurationDefault;
            // Convert actual exponent (0.3-4.0) to 0-1 slider position via inverse log
            exponentSlider = Mathf.Log(SessionState.RampExponent / SessionState.RampExponentMin)
                           / Mathf.Log(SessionState.RampExponentMax / SessionState.RampExponentMin);
        }

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
            if (CameraPanelConfig.Instance != null)
            {
                CameraPanelConfig.Instance.OnPresetLoaded -= OnPresetLoaded;
            }

            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
            GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequested);
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

        private void OnVesselWillDestroy(Vessel v)
        {
            // Only clear if it's our active vessel to avoid clearing when distant vessels unload
            if (v == FlightGlobals.ActiveVessel && HullCamBridge.IsAnyCameraActive())
            {
                HullCamBridge.ClearHullCamStaticState();
            }
        }

        private void OnGameSceneLoadRequested(GameScenes scene)
        {
            // Always clear when changing scenes (revert to VAB, load save, exit to menu)
            HullCamBridge.ClearHullCamStaticState();
        }

        private void OnTimeScaleChanged(float newScale)
        {
            float tolerance = 0.01f;

            // Map scale -> mode using array lookup (C# 7.3 compatible)
            var scaleMappings = new (float scale, SpeedMode mode)[]
            {
        (1.0f, SpeedMode.Normal),
        (DeterministicCaptureSession.SUPER_SLOW_SCALE, SpeedMode.SuperSlow),
        (DeterministicCaptureSession.SLOW_SCALE, SpeedMode.Slow),
        (DeterministicCaptureSession.KRAKEN_TIME_SCALE, SpeedMode.KrakenTime)
            };

            currentSpeedMode = SpeedMode.Normal; // default
            foreach (var mapping in scaleMappings)
            {
                if (Mathf.Abs(newScale - mapping.scale) < tolerance)
                {
                    currentSpeedMode = mapping.mode;
                    break;
                }
            }
        }

        void OnGUI()
        {
            HandleScreenFade();

            if (!shouldShow && !DeterministicCaptureSession.IsRunning) return;

            UpdateWindowHeight();

            windowRect = GUILayout.Window(WINDOW_ID, windowRect, DrawWindow, "Recording Controls", windowStyle);
            DrawConfirmationDialogs();
        }

        private void UpdateWindowHeight()
        {
            if (Event.current.type != EventType.Layout) return;

            float targetHeight = CalculateTargetHeight();
            windowRect.height = Mathf.Lerp(windowRect.height, targetHeight, 0.25f);

            if (Mathf.Abs(windowRect.height - targetHeight) < 0.5f)
                windowRect.height = targetHeight;
        }

        private float CalculateTargetHeight()
        {
            float speedRampHeight = showSpeedRamps ? 210f : 0f; // These could become constants too
            float cameraPanelHeight = showCameraPanel ? 255f : 0f;
            return WINDOW_HEIGHT_BASE + speedRampHeight + cameraPanelHeight;
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.BeginVertical();

            DrawStatusDisplay();
            GUILayout.Space(8);

            DrawSpeedButtons();
            GUILayout.Space(8);

            DrawProgressInfo();
            GUILayout.Space(8);

            DrawSpeedRampsFoldout();

            GUILayout.EndVertical();

            DrawCameraPanelFoldout();

            GUI.DragWindow();
        }

        private void DrawStatusDisplay()
        {
            if (!DeterministicCaptureSession.IsRunning)
            {
                GUILayout.Label("Recording stopped", labelStyle);
                return;
            }

            float multiplier = DeterministicCaptureSession.CurrentTimeScale < 1.0f ?
                1.0f / DeterministicCaptureSession.CurrentTimeScale : 1.0f;

            string speedText = DeterministicCaptureSession.CurrentTimeScale >= 0.999f ?
                "Normal Speed" :
                $"{multiplier:F1}× Slow Motion";

            GUIStyle headerStyle = new GUIStyle(labelStyle);
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.fontSize = HEADER_FONT_SIZE;
            headerStyle.alignment = TextAnchor.MiddleCenter;

            GUILayout.Label(speedText, headerStyle);

            if (DeterministicCaptureSession.IsTransitioning)
            {
                string transitionText = DeterministicCaptureSession.CurrentTransitionDirection ==
                    DeterministicCaptureSession.TransitionDirection.Slowing ?
                    "Slowing..." : "Resuming...";

                GUIStyle transitionStyle = new GUIStyle(labelStyle);
                transitionStyle.normal.textColor = Color.yellow;
                transitionStyle.alignment = TextAnchor.MiddleCenter;
                GUILayout.Label($"Transition: {transitionText}", transitionStyle);
            }
        }

        private void DrawSpeedButtons()
        {
            bool running = DeterministicCaptureSession.IsRunning;
            GUI.enabled = running;

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Kraken-Time", currentSpeedMode == SpeedMode.KrakenTime ? activeButtonStyle : buttonStyle, GUILayout.Width(SPEED_BUTTON_WIDTH)))
                DeterministicCaptureSession.RequestKrakenTime();

            if (GUILayout.Button("Super-Slow", currentSpeedMode == SpeedMode.SuperSlow ? activeButtonStyle : buttonStyle, GUILayout.Width(SPEED_BUTTON_WIDTH)))
                DeterministicCaptureSession.RequestSuperSlow();

            if (GUILayout.Button("Slow", currentSpeedMode == SpeedMode.Slow ? activeButtonStyle : buttonStyle, GUILayout.Width(SPEED_BUTTON_WIDTH)))
                DeterministicCaptureSession.RequestSlow();

            if (GUILayout.Button("Resume", currentSpeedMode == SpeedMode.Normal ? activeButtonStyle : buttonStyle, GUILayout.Width(SPEED_BUTTON_WIDTH)))
                DeterministicCaptureSession.RequestNormalSpeed();

            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }

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

            GUILayout.Label($"Simulated: {simulated:F1}s elapsed", labelStyle);
            GUILayout.Label($"Frames: {frames:N0}", labelStyle);

            DrawIndeterminateProgressBar();
        }

        private void DrawIndeterminateProgressBar()
        {
            GUILayout.BeginHorizontal(GUI.skin.box);

            float pulse = Mathf.PingPong(Time.time * PROGRESS_PULSE_SPEED, 1f);
            float barWidth = PROGRESS_BAR_WIDTH;
            float segmentWidth = PROGRESS_SEGMENT_WIDTH;
            float xPos = pulse * (barWidth - segmentWidth);

            GUIStyle activeBarStyle = new GUIStyle(GUI.skin.box);
            activeBarStyle.normal.background = CreateColorTexture(PROGRESS_BAR_COLOR);

            // Left spacer
            GUILayout.Box("", GUIStyle.none, GUILayout.Width(xPos), GUILayout.Height(PROGRESS_BAR_HEIGHT));
            // Middle segment
            GUILayout.Box("", activeBarStyle, GUILayout.Width(segmentWidth), GUILayout.Height(PROGRESS_BAR_HEIGHT));
            // Right spacer
            GUILayout.Box("", GUIStyle.none, GUILayout.Width(barWidth - xPos - segmentWidth), GUILayout.Height(PROGRESS_BAR_HEIGHT));

            GUILayout.EndHorizontal();
        }

        private void DrawLimitedProgress()
        {
            float current = DeterministicCaptureSession.AccumulatedSimulatedSeconds;
            float target = DeterministicCaptureSession.TargetSeconds;

            string progress = $"Simulated: {current:F1}s / {target:F1}s";
            GUILayout.Label(progress, labelStyle);

            float percent = target > 0 ? Mathf.Clamp01(current / target) : 0f;

            GUILayout.BeginHorizontal(GUI.skin.box);
            GUIStyle barStyle = new GUIStyle(GUI.skin.box);
            barStyle.normal.background = CreateColorTexture(PROGRESS_BAR_COLOR);
            GUILayout.Box("", barStyle, GUILayout.Width(PROGRESS_BAR_WIDTH * percent), GUILayout.Height(PROGRESS_BAR_HEIGHT));
            GUILayout.EndHorizontal();
        }

        // Speed Ramps foldout section
        private void DrawSpeedRampsFoldout()
        {
            string label = showSpeedRamps ? "▼ Speed Ramps" : "► Speed Ramps";
            if (GUILayout.Button(label, HighLogic.Skin.button))
            {
                showSpeedRamps = !showSpeedRamps;
            }

            if (!showSpeedRamps) return;

            GUIStyle helpStyle = CreateLabelStyle(INFO_TEXT_COLOR, fontSize: HELP_FONT_SIZE);

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Space(INNER_PADDING);

            // Ramp Duration
            GUILayout.Label($"Duration: {durationSlider:F2}s", HighLogic.Skin.label);
            float newDuration = GUILayout.HorizontalSlider(durationSlider, RAMP_DURATION_MIN, RAMP_DURATION_MAX);
            if (!Mathf.Approximately(newDuration, durationSlider))
            {
                durationSlider = newDuration;
                SessionState.RampDurationDefault = newDuration;
            }
            GUILayout.Label("Wall-clock time for speed transitions", helpStyle);
            GUILayout.Space(ELEMENT_GAP);

            // Curve Bias
            GUILayout.Label($"Bias: {SessionState.RampExponent:F2}", HighLogic.Skin.label);
            DrawExponentSlider(helpStyle);
            GUILayout.Label(GetCurveDescription(SessionState.RampExponent), helpStyle);

            GUILayout.Space(INNER_PADDING);
            GUILayout.EndVertical();
        }

        private void DrawExponentSlider(GUIStyle helpStyle)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Linger Slow", helpStyle, GUILayout.Width(70));
            float sliderPos = GUILayout.HorizontalSlider(exponentSlider, 0f, 1f);
            GUILayout.Label("Linger Normal", helpStyle, GUILayout.Width(70));
            GUILayout.EndHorizontal();

            if (!Mathf.Approximately(sliderPos, exponentSlider))
            {
                exponentSlider = sliderPos;
                float min = SessionState.RampExponentMin;
                float max = SessionState.RampExponentMax;
                SessionState.RampExponent = min * Mathf.Pow(max / min, sliderPos);
            }
        }

        // Camera Control Panel Methods

        void InitCameraPanelStyles()
        {
            if (cameraPanelStylesInitialized) return;

            // Slot style definitions: Background, TextColor
            var styleDefs = new (Color bg, Color text, FontStyle font)[]
            {
        (COLOR_CAM_ACTIVE, Color.white, FontStyle.Bold),        // 0: Active (Green)
        (COLOR_CAM_ASSIGNED, Color.black, FontStyle.Bold),      // 1: Assigned (Yellow)
        (COLOR_CAM_UNAVAILABLE, Color.white, FontStyle.Bold),   // 2: Unavailable (Red)
        (COLOR_CAM_UNASSIGNED, COLOR_TEXT_DIM, FontStyle.Bold), // 3: Unassigned (Gray)
        (COLOR_CAM_REMOTE, Color.white, FontStyle.Bold)         // 4: Remote (Aqua)
            };

            for (int i = 0; i < styleDefs.Length; i++)
            {
                cameraButtonStyles[i] = CreateColoredButtonStyle(
                    styleDefs[i].bg,
                    styleDefs[i].text,
                    styleDefs[i].font
                );
            }

            cameraPanelStylesInitialized = true;
        }

        void DrawCameraPanelFoldout()
        {
            if (!HullCamBridge.IsAvailable)
            {
                DrawDisabledCameraPanel();
                return;
            }

            InitCameraPanelStyles();

            GUILayout.Space(SECTION_SPACING);
            DrawCameraFoldoutButton();

            if (!showCameraPanel) return;

            DrawFadeControls();
            DrawCameraGridContainer(); // Grid + Text side-by-side
            DrawZoomControlsIfActive();
            GUILayout.Space(BOX_PADDING);
            DrawCameraProfilesInterface();
            UpdateCameraMonitoring();
        }

        private void DrawCameraGridContainer()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();

            // LEFT: Camera Grid + Return Button
            GUILayout.BeginVertical(GUILayout.Width(GRID_COLUMN_WIDTH));
            DrawCameraGrid();

            GUILayout.FlexibleSpace();

            bool hasActiveCam = HullCamBridge.IsAnyCameraActive();
            GUI.enabled = hasActiveCam;
            if (GUILayout.Button("Return to Main", GUILayout.Height(SPEED_BUTTON_HEIGHT)))
            {
                HullCamBridge.RestoreMain();
            }
            GUI.enabled = true;
            GUILayout.EndVertical();

            GUILayout.Space(ELEMENT_GAP);

            // RIGHT: Instructions + Assign Current (now at bottom)
            GUILayout.BeginVertical(GUILayout.Width(GRID_TEXT_COLUMN_WIDTH));
            DrawCameraInstructions();

            GUILayout.FlexibleSpace();

            GUI.enabled = HullCamBridge.GetCurrentCamera() != null;
            if (GUILayout.Button("Assign Current", GUILayout.Height(SPEED_BUTTON_HEIGHT)))
            {
                for (int i = 0; i < TOTAL_CAMERA_SLOTS; i++)
                {
                    if (cameraSlots[i].GetStatus() == CameraSlot.SlotStatus.Unassigned)
                    {
                        AssignCurrentCameraToSlot(i);
                        break;
                    }
                }
            }
            GUI.enabled = true;
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawCameraInstructions()
        {
            GUIStyle header = new GUIStyle(HighLogic.Skin.label) { fontStyle = FontStyle.Bold };
            GUILayout.Label("Controls:", header);
            GUILayout.Space(SMALL_GAP);

            GUIStyle small = new GUIStyle(HighLogic.Skin.label) { fontSize = 11, wordWrap = true };
            GUILayout.Label("• Left-click camera to view", small);
            GUILayout.Label("• Right-click to unassign", small);
            GUILayout.Label("• 'Assign Current' binds active cam to first open slot", small);
        }

        private void DrawZoomControlsIfActive()
        {
            if (!HullCamBridge.IsAnyCameraActive()) return;

            GUILayout.Space(ELEMENT_GAP);
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label("Zoom Control (Velocity)", HighLogic.Skin.label);

            // Slider row
            GUILayout.BeginHorizontal();
            GUILayout.Label("Out", GUILayout.Width(ZOOM_LABEL_WIDTH));

            GUIStyle intentStyle = new GUIStyle(HighLogic.Skin.horizontalSlider);
            GUIStyle thumbStyle = new GUIStyle(HighLogic.Skin.horizontalSliderThumb);
            zoomIntentSlider = GUILayout.HorizontalSlider(zoomIntentSlider, -1f, 1f, intentStyle, thumbStyle);

            GUILayout.Label("In", GUILayout.Width(ZOOM_LABEL_WIDTH));
            GUILayout.EndHorizontal();

            // FOV Display
            float maxFov = HullCamBridge.GetCameraFoVMax(HullCamBridge.GetCurrentCamera());
            GUILayout.Label($"FOV: {currentFoV:F1}° / {maxFov:F0}°", HighLogic.Skin.label);

            GUILayout.BeginHorizontal();
            DrawZoomResetButton(maxFov);
            GUILayout.FlexibleSpace();
            DrawAutoDistanceToggle();
            GUILayout.EndHorizontal();

            if (autoDistanceTracking)
            {
                GUIStyle distStyle = CreateLabelStyle(INFO_TEXT_COLOR, fontSize: 10);
                GUILayout.Label("Automatically adjusts zoom based on vessel distance", distStyle);
            }

            GUILayout.EndVertical();
        }

        private void DrawZoomResetButton(float maxFov)
        {
            if (GUILayout.Button("Reset Zoom", GUILayout.Width(ZOOM_RESET_BUTTON_WIDTH)))
            {
                targetFoV = maxFov;
                zoomIntentSlider = 0f;
            }
        }

        private void DrawAutoDistanceToggle()
        {
            GUIStyle autoStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (autoDistanceTracking)
            {
                autoStyle.normal.textColor = COLOR_AUTO_TRACK;
                autoStyle.fontStyle = FontStyle.Bold;
            }
            autoDistanceTracking = GUILayout.Toggle(autoDistanceTracking, " Auto-Distance", autoStyle);
        }

        private void DrawDisabledCameraPanel()
        {
            GUIStyle disabledStyle = CreateLabelStyle(COLOR_TEXT_DIM, alignment: TextAnchor.MiddleCenter);
            GUILayout.Label("Camera Panel requires HullCam VDS", disabledStyle);
        }

        private void DrawCameraFoldoutButton()
        {
            string label = showCameraPanel ? "▼ Camera Panel" : "► Camera Panel";
            if (GUILayout.Button(label, HighLogic.Skin.button))
            {
                showCameraPanel = !showCameraPanel;
            }
        }

        private void DrawFadeControls()
        {
            GUILayout.BeginHorizontal();
            GUIStyle toggleStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (useFadeOnSwap)
            {
                toggleStyle.normal.textColor = COLOR_TEXT_GLOW;
                toggleStyle.onNormal.textColor = COLOR_TEXT_GLOW;
                toggleStyle.fontStyle = FontStyle.Bold;
                toggleStyle.alignment = TextAnchor.MiddleLeft;
            }
            useFadeOnSwap = GUILayout.Toggle(useFadeOnSwap, " Fade-On-Swap", toggleStyle);
            GUILayout.EndHorizontal();

            if (useFadeOnSwap)
            {
                float duration = Mathf.Lerp(FADE_DURATION_MIN, FADE_DURATION_MAX, fadeDurationSlider);
                GUILayout.Label($"Fade Duration: {duration:F2}s", HighLogic.Skin.label);
                fadeDurationSlider = GUILayout.HorizontalSlider(fadeDurationSlider, 0f, FADE_SLIDER_MAX);
                GUILayout.Space(SMALL_GAP);
            }

            GUILayout.Space(SMALL_GAP);
        }



        void DrawCameraGrid()
        {
            Vessel currentVessel = FlightGlobals.ActiveVessel;

            for (int row = 0; row < CAMERA_GRID_ROWS; row++)
            {
                GUILayout.BeginHorizontal();

                for (int col = 0; col < CAMERA_GRID_COLS; col++)
                {
                    int index = row * 4 + col;
                    CameraSlot slot = cameraSlots[index];
                    CameraSlot.SlotStatus status = slot.GetStatus(currentVessel);

                    int styleIndex;
                    switch (status)
                    {
                        case CameraSlot.SlotStatus.Active:
                            styleIndex = 0;
                            break;
                        case CameraSlot.SlotStatus.Assigned:
                            styleIndex = 1;
                            break;
                        case CameraSlot.SlotStatus.Unavailable:
                            styleIndex = 2;
                            break;
                        case CameraSlot.SlotStatus.Remote: 
                            styleIndex = 4;
                            break;
                        default:
                            styleIndex = 3;
                            break;
                    }

                    string buttonLabel = (index + 1).ToString();

                    // Draw button
                    if (GUILayout.Button(buttonLabel, cameraButtonStyles[styleIndex],
                        GUILayout.Width(cameraButtonSize), GUILayout.Height(CAMERA_BUTTON_HEIGHT)))
                    {
                        OnCameraButtonClicked(index);
                    }

                    // Check for right-click (button 1) on this button
                    Rect buttonRect = GUILayoutUtility.GetLastRect();
                    if (Event.current.type == EventType.MouseDown &&
                        Event.current.button == 1 &&
                        buttonRect.Contains(Event.current.mousePosition))
                    {
                        if (status != CameraSlot.SlotStatus.Unassigned)
                        {
                            pendingUnassignSlot = index;
                            Event.current.Use(); // Prevent other GUI from processing this event
                        }
                    }
                }

                GUILayout.EndHorizontal();
            }
        }



        void DrawConfirmationDialogs()
        {
            // Delete confirmation
            if (showDeleteConfirm)
            {
                Rect dialogRect = CenterDialogRect(DIALOG_WIDTH, DIALOG_HEIGHT);

                GUI.ModalWindow(DIALOG_DELETE_ID, dialogRect, (id) =>
                {
                    GUILayout.Label($"Delete preset '{presetNameBuffer}'?");
                    GUILayout.Space(SECTION_SPACING);

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Yes", GUILayout.Height(DIALOG_BUTTON_HEIGHT)))
                    {
                        CameraPanelConfig.Instance?.DeletePreset(presetNameBuffer);
                        presetNameBuffer = GetDefaultPresetName();
                        showDeleteConfirm = false;
                    }

                    if (GUILayout.Button("No", GUILayout.Height(DIALOG_BUTTON_HEIGHT)))
                    {
                        showDeleteConfirm = false;
                    }
                    GUILayout.EndHorizontal();
                }, "Confirm Delete");
            }

            // Unassign confirmation  
            if (pendingUnassignSlot >= 0)
            {
                Rect dialogRect = CenterDialogRect(DIALOG_WIDTH, DIALOG_HEIGHT);
                int slotIndex = pendingUnassignSlot; // Local copy for closure capture

                GUI.ModalWindow(DIALOG_UNASSIGN_ID, dialogRect, (id) =>
                {
                    GUILayout.Label($"Unassign camera from slot {slotIndex + 1}?");
                    GUILayout.Space(SECTION_SPACING);

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Yes", GUILayout.Height(DIALOG_BUTTON_HEIGHT)))
                    {
                        ClearSlot(slotIndex);
                        pendingUnassignSlot = -1;
                    }

                    if (GUILayout.Button("No", GUILayout.Height(DIALOG_BUTTON_HEIGHT)))
                    {
                        pendingUnassignSlot = -1;
                    }
                    GUILayout.EndHorizontal();
                }, "Confirm Unassign");
            }
        }

        void OnCameraButtonClicked(int index)
        {
            CameraSlot slot = cameraSlots[index];
            Vessel currentVessel = FlightGlobals.ActiveVessel;
            CameraSlot.SlotStatus status = slot.GetStatus(currentVessel);

            if (status == CameraSlot.SlotStatus.Unassigned)
            {
                AssignCurrentCameraToSlot(index);
            }
            else if (status == CameraSlot.SlotStatus.Active)
            {
                return; // Already viewing this camera
            }
            else if (status == CameraSlot.SlotStatus.Assigned || status == CameraSlot.SlotStatus.Remote) 
            {
                object cam = HullCamBridge.ResolveCameraSlot(slot, currentVessel);
                if (cam != null && cam != HullCamBridge.GetCurrentCamera())
                {
                    // Reset zoom intent when switching cameras
                    zoomIntentSlider = 0f;
                    TriggerCameraSwitchWithFade(() => {
                        HullCamBridge.Activate(cam);
                    });
                }
            }
            else if (status == CameraSlot.SlotStatus.Unavailable)
            {
                // Don't auto-clear on click anymore - just show message
                ScreenMessages.PostScreenMessage("Camera unavailable (vessel may be unloaded)", 2f);
            }
        }

        void AssignCurrentCameraToSlot(int index)
        {
            Debug.Log("Assigning camera to slot " + (index + 1));

            if (!ValidateAssignmentPrerequisites(out object currentCam, out Vessel vessel)) return;

            Part part = GetPartFromCamera(currentCam);
            string camName = HullCamBridge.GetCameraName(currentCam) ?? "";

            Debug.Log($"Camera name: {camName} Vessel: {vessel.name}");
            if (part == null) Debug.Log("Using vessel-only fallback");

            cameraSlots[index] = new CameraSlot
            {
                buttonID = $"Cam_{index}",
                cameraName = camName,
                partPersistentId = part != null ? part.persistentId : 0u,
                vesselId = vessel.id.ToString(),
                allowAnyVessel = false
            };

            Debug.Log("Assignment complete!");
        }

        private bool ValidateAssignmentPrerequisites(out object currentCam, out Vessel vessel)
        {
            currentCam = null;
            vessel = null;

            if (!HullCamBridge.IsAvailable)
            {
                Debug.Log("HullCam not available!");
                return false;
            }

            currentCam = HullCamBridge.GetCurrentCamera();
            if (currentCam == null)
            {
                Debug.Log("No current camera!");
                ScreenMessages.PostScreenMessage("No active HullCam to assign", 2f);
                return false;
            }

            vessel = FlightGlobals.ActiveVessel;
            if (vessel == null)
            {
                Debug.Log("No active vessel!");
                return false;
            }

            return true;
        }


        // Helper method to find Part from camera object without reflection
        Part GetPartFromCamera(object cameraModule)
        {
            try
            {
                // Cast to Component to access gameObject
                Component comp = cameraModule as Component;
                if (comp == null) return null;

                // Traverse up to find the Part component
                Transform current = comp.transform;
                while (current != null)
                {
                    Part part = current.GetComponent<Part>();
                    if (part != null) return part;
                    current = current.parent;
                }
            }
            catch (Exception ex)
            {
                Debug.Log("[CamPanel] GetPartFromCamera failed: " + ex.Message);
            }
            return null;
        }

        void ClearSlot(int index)
        {
            cameraSlots[index] = new CameraSlot { buttonID = "Cam_" + index };
        }

        void DrawCameraProfilesInterface()
        {
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Space(5);

            // Second row: Name field, Save, Delete, Load
            GUILayout.BeginHorizontal();

            CameraPanelConfig scenario = CameraPanelConfig.Instance;
            bool hasPresets = scenario != null && scenario.GetPresetNames().Count > 0;
            CameraPanelPreset activePreset = scenario?.GetActivePreset();

            // Ensure we have a default name ready
            EnsurePresetNameBuffer();

            // Preset name text field (150px width)
            presetNameBuffer = GUILayout.TextField(presetNameBuffer, GUILayout.Width(150));

            // Save button
            if (GUILayout.Button("Save", GUILayout.Width(50)))
            {
                string nameToSave = string.IsNullOrWhiteSpace(presetNameBuffer)
                    ? GetDefaultPresetName()
                    : presetNameBuffer;

                // Auto-generate [1], [2] suffix if name exists
                nameToSave = GetUniquePresetName(nameToSave);

                scenario.SavePreset(nameToSave, false, cameraSlots, windowRect.x, windowRect.y);

                // Update buffer to show the actual saved name (with suffix if applied)
                presetNameBuffer = nameToSave;
            }

            // Delete button (only enabled if a preset is currently loaded)
            GUI.enabled = activePreset != null;
            if (GUILayout.Button("Delete", GUILayout.Width(50)))
            {
                if (activePreset != null)
                {
                    showDeleteConfirm = true;
                }
            }
            GUI.enabled = true;

            GUILayout.FlexibleSpace();

            // Load dropdown
            if (hasPresets)
            {
                if (GUILayout.Button("Load ▼", GUILayout.Width(60)))
                {
                    showPresetList = !showPresetList;
                }
            }

            GUILayout.EndHorizontal();

            // Preset list dropdown
            if (showPresetList && scenario != null)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                List<string> presetNames = scenario.GetPresetNames();
                foreach (string name in presetNames)
                {
                    if (GUILayout.Button(name))
                    {
                        scenario.LoadPreset(name);
                        showPresetList = false;
                    }
                }
                GUILayout.EndVertical();
            }

            GUILayout.EndVertical();
        }

        void OnPresetLoaded(CameraPanelPreset preset)
        {
            if (preset != null && preset.buttonAssignments != null && preset.buttonAssignments.Count == 16)
            {
                cameraSlots = preset.buttonAssignments;
                windowRect.x = preset.panelX;
                windowRect.y = preset.panelY;

                // Update name field to loaded preset name
                presetNameBuffer = preset.presetName;
            }
        }

        private string GetDefaultPresetName()
        {
            return FlightGlobals.ActiveVessel?.vesselName ?? "Preset";
        }

        private string GetUniquePresetName(string baseName)
        {
            var scenario = CameraPanelConfig.Instance;
            if (scenario == null) return baseName;

            var existing = scenario.GetPresetNames();
            if (!existing.Contains(baseName)) return baseName;

            int counter = 1;
            string candidate;
            do
            {
                candidate = $"{baseName} [{counter}]";
                counter++;
            } while (existing.Contains(candidate));

            return candidate;
        }

        // Call this to ensure buffer is initialized when panel opens
        private void EnsurePresetNameBuffer()
        {
            if (string.IsNullOrEmpty(presetNameBuffer))
            {
                presetNameBuffer = GetDefaultPresetName();
            }
        }

        void UpdateCameraMonitoring()
        {
            if (!HullCamBridge.IsAvailable) return;

            // Just track current camera for UI status colors
            // Don't auto-switch - HullCam handles camera death internally
            lastKnownActiveCamera = HullCamBridge.GetCurrentCamera();
        }

        private void SetExponentWithSliderUpdate(float value)
        {
            SessionState.RampExponent = value;
            exponentSlider = Mathf.Log(value / SessionState.RampExponentMin)
                           / Mathf.Log(SessionState.RampExponentMax / SessionState.RampExponentMin);
        }

        private string GetCurveDescription(float exp)
        {
            if (Mathf.Abs(exp - 1.0f) < 0.1f) return "Linear transition";
            if (exp > 2.5f) return "Linger at normal speed, rush through slow-motion";
            if (exp > 1.5f) return "Gradual entry, fast exit to slow-mo";
            if (exp < 0.5f) return "Snap to slow-mo, linger there";
            if (exp < 0.8f) return "Fast entry, gentle exit";
            return "Moderate curve";
        }

        // Crossfade-to-black to cover up camera flicker on swaps
        void TriggerCameraSwitchWithFade(Action cameraAction)
        {
            if (!useFadeOnSwap)
            {
                // Instant switch if fade disabled
                cameraAction?.Invoke();
                return;
            }

            if (isFading) return;
            isFading = true;
            screenFadeAlpha = 0f;

            // Map slider (0-1) to duration (2s slow - 0.05s fast)
            float duration = 0.05f + fadeDurationSlider * .95f;
            fadeSpeed = 1f / duration;

            pendingCameraAction = cameraAction;
        }

        void LateUpdate()
        {
            if (!shouldShow) return;
            if (!HullCamBridge.IsAvailable) return;

            var activeCam = HullCamBridge.GetCurrentCamera();
            if (activeCam == null)
            {
                zoomControlledCamera = null;
                return;
            }

            InitializeZoomForNewCamera(activeCam);
            ProcessZoomIntent(activeCam);
            ApplyZoomToCamera(activeCam);
            DecayZoomIntent();
        }

        private void InitializeZoomForNewCamera(object activeCam)
        {
            if (activeCam == zoomControlledCamera) return;

            zoomControlledCamera = activeCam;
            currentFoV = HullCamBridge.GetCameraFoV(activeCam);
            targetFoV = currentFoV;
            zoomIntentSlider = 0f;
        }

        private void ProcessZoomIntent(object activeCam)
        {
            float minFoV = HullCamBridge.GetCameraFoVMin(activeCam);
            float maxFoV = HullCamBridge.GetCameraFoVMax(activeCam);

            if (autoDistanceTracking && FlightGlobals.ActiveVessel != null)
            {
                ApplyAutoZoom(activeCam, minFoV, maxFoV);
            }
            else
            {
                ApplyManualZoom(minFoV, maxFoV);
            }

            targetFoV = Mathf.Clamp(targetFoV, minFoV, maxFoV);
            currentFoV = Mathf.SmoothDamp(currentFoV, targetFoV, ref zoomSmoothVelocity, ZOOM_SMOOTH_TIME, Mathf.Infinity, Time.deltaTime);
        }

        private void ApplyAutoZoom(object activeCam, float minFoV, float maxFoV)
        {
            var camTransform = HullCamBridge.GetCameraTransform(activeCam);
            if (camTransform == null || FlightGlobals.ActiveVessel == null) return;

            float distance = Vector3.Distance(camTransform.position, FlightGlobals.ActiveVessel.transform.position);
            float t = Mathf.Clamp01(Mathf.Log(distance / 10f + 1f) / Mathf.Log(autoZoomDistanceRef / 10f + 1f));
            float autoTarget = Mathf.Lerp(maxFoV, minFoV, t);

            // Blend auto with manual intent
            targetFoV = Mathf.Lerp(autoTarget, targetFoV, Mathf.Abs(zoomIntentSlider));
        }

        private void ApplyManualZoom(float minFoV, float maxFoV)
        {
            if (Mathf.Abs(zoomIntentSlider) > ZOOM_INTENT_THRESHOLD)
            {
                // Zoom IN (positive) = decrease FoV, OUT (negative) = increase FoV
                float zoomDelta = -zoomIntentSlider * ZOOM_MAX_SPEED * Time.deltaTime;
                targetFoV += zoomDelta;
            }
            else
            {
                zoomIntentSlider = Mathf.MoveTowards(zoomIntentSlider, 0f, Time.deltaTime * 2f);
            }
        }

        private void ApplyZoomToCamera(object activeCam)
        {
            HullCamBridge.SetCameraFoV(activeCam, currentFoV);
        }

        private void DecayZoomIntent()
        {
            if (!Input.GetMouseButton(0))
            {
                zoomIntentSlider = Mathf.MoveTowards(zoomIntentSlider, 0f, Time.deltaTime * ZOOM_RETURN_SPEED);
            }
        }

        void HandleScreenFade()
        {
            if (!isFading) return;

            screenFadeAlpha += Time.unscaledDeltaTime * fadeSpeed;

            if (screenFadeAlpha >= 1f)
            {
                screenFadeAlpha = 1f;

                if (pendingCameraAction != null)
                {
                    pendingCameraAction();
                    pendingCameraAction = null;
                }

                fadeSpeed = -Mathf.Abs(fadeSpeed);
            }
            else if (screenFadeAlpha <= 0f && fadeSpeed < 0)
            {
                screenFadeAlpha = 0f;
                isFading = false;
                fadeSpeed = Mathf.Abs(fadeSpeed);
            }

            GUI.color = new Color(0, 0, 0, screenFadeAlpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        public void Show() { shouldShow = true; }
        public void Hide() { shouldShow = false; }


    }
}