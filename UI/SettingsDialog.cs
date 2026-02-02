using CinematicRecorder.Capture;
using CinematicRecorder.Core;
using System;
using UnityEngine;

namespace CinematicRecorder.UI
{
    public class SettingsDialog : MonoBehaviour
    {
        // ===================================================================
        // UI CONSTANTS - All dimensions defined here for easy tweaking
        // ===================================================================

        // Window
        private const float WINDOW_DEFAULT_X = 300f;
        private const float WINDOW_DEFAULT_Y = 60f;
        private const float WINDOW_COLLAPSED_HEIGHT = 380f;
        private const float WINDOW_EXPANDED_HEIGHT = 620f;

        // Layout
        private const float MAIN_PANEL_WIDTH = 320f;
        private const float ADVANCED_PANEL_WIDTH = 260f;
        private const float ADVANCED_MARGIN = 20f;
        private const float TEXT_COLUMN_PADDING = 10f;
        private const float SEPARATOR_LINE_WIDTH = 2f;
        private const float ADVANCED_TOGGLE_WIDTH = 110f;
        private const float ADVANCED_TOGGLE_HEIGHT = 28f;

        // Spacing
        private const float SPACING_MINIMAL = 2f;
        private const float SPACING_TIGHT = 4f;
        private const float SPACING_NORMAL = 10f;
        private const float SPACING_LARGE = 15f;
        private const float SPACING_STATUS_TOP = 10f;

        // Encoder UI
        private const float ENCODER_BTN_WIDTH_AMD = 55f;
        private const float ENCODER_BTN_WIDTH_NVIDIA = 70f;
        private const float ENCODER_BTN_WIDTH_CPU = 55f;
        private const float RATECONTROL_WIDTH_QUALITY = 110f;
        private const float RATECONTROL_WIDTH_VBR = 55f;
        private const float SPEED_WIDTH_SPEED = 80f;
        private const float SPEED_WIDTH_BALANCED = 100f;
        private const float SPEED_WIDTH_QUALITY = 90f;

        // Timing/Input
        private const float DURATION_BTN_WIDTH = 50f;
        private const float DURATION_FIELD_WIDTH = 80f;
        private const float DURATION_STEP = 5f;
        private const float PLAYBACK_LABEL_WIDTH = 90f;
        private const float LOCK_TOGGLE_WIDTH = 60f;
        private const float FPS_SELECTOR_WIDTH = 25f;
        private const float FPS_LABEL_WIDTH = 75f;

        // Record
        private const float BTN_HEIGHT_RECORD = 40f;

        // Typography
        private const int INFO_FONT_SIZE = 11;
        private const int HEADER_FONT_SIZE = 14;
        public static readonly Color INFO_TEXT_COLOR = new Color(1f, 0.5490196f, 0f);


        private Rect windowRect = new Rect(WINDOW_DEFAULT_X, WINDOW_DEFAULT_Y, MAIN_PANEL_WIDTH, WINDOW_COLLAPSED_HEIGHT);
        private bool renderDisplay;
        private bool showEncodingSettings;
        private bool stopRequested;

        private GUIStyle windowStyle;
        private bool stylesInitialized;
        public bool IsVisible => renderDisplay;

        public event Action OnDialogDismissed;

        private bool showAdvancedPanel = false;


        private readonly int[] frameratePresets = { 24, 30, 60, 120, 240 };
        private readonly string[] encoderTabNames = { "AMD", "NVIDIA", "CPU" };
        private readonly string[] rateControlNames = { "Quality(CQP)", "VBR" };
        private readonly string[] speedPresetNames = { "Speed", "Balanced", "Quality" };

        public void Show()
        {
            renderDisplay = true;
            stopRequested = false;
            InitStyles();
        }

        public void Hide()
        {
            renderDisplay = false;
            OnDialogDismissed?.Invoke();
        }

        private void InitStyles()
        {
            if (stylesInitialized) return;
            windowStyle = new GUIStyle(HighLogic.Skin.window);
            stylesInitialized = true;
        }

        private void OnGUI()
        {
            if (!renderDisplay) return;

            if (Event.current.type == EventType.Layout)
            {
                float targetWidth = showAdvancedPanel ? (MAIN_PANEL_WIDTH + ADVANCED_PANEL_WIDTH + 10) : MAIN_PANEL_WIDTH;
                windowRect.width = Mathf.Lerp(windowRect.width, targetWidth, 0.25f);
                if (Mathf.Abs(windowRect.width - targetWidth) < 1f)
                    windowRect.width = targetWidth;

                float targetHeight = showEncodingSettings ? WINDOW_EXPANDED_HEIGHT : WINDOW_COLLAPSED_HEIGHT;
                windowRect.height = Mathf.Lerp(windowRect.height, targetHeight, 0.25f);
                if (Mathf.Abs(windowRect.height - targetHeight) < 0.5f)
                    windowRect.height = targetHeight;
            }

            windowRect = GUILayout.Window(
                12345,
                windowRect,
                DrawWindow,
                "Cinematic Recorder",
                windowStyle
            );
        }

        private void DrawWindow(int id)
        {
            // TOP ROW: Status left, Advanced button right
            GUILayout.BeginHorizontal();

            // Status takes remaining space
            GUILayout.BeginVertical();
            DrawStatusSection();
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            // Advanced toggle button - fixed width container
            GUILayout.BeginVertical(GUILayout.Width(ADVANCED_TOGGLE_WIDTH));
            GUIStyle advStyle = new GUIStyle(HighLogic.Skin.button);
            if (showAdvancedPanel)
            {
                advStyle.normal.textColor = new Color(0.2f, 0.9f, 0.2f);
                advStyle.fontStyle = FontStyle.Bold;
            }

            string buttonText = showAdvancedPanel ? "▼ Advanced" : "► Advanced";
            if (GUILayout.Button(buttonText, advStyle, GUILayout.Height(ADVANCED_TOGGLE_HEIGHT)))
            {
                showAdvancedPanel = !showAdvancedPanel;
            }
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            // MAIN CONTENT with slide-out panel
            GUILayout.BeginHorizontal();

            // LEFT: Main settings
            GUILayout.BeginVertical(GUILayout.Width(MAIN_PANEL_WIDTH - TEXT_COLUMN_PADDING * 2));

            DrawCaptureTimingSection();
            GUILayout.Space(SPACING_TIGHT);
            DrawDurationSection();
            GUILayout.Space(SPACING_NORMAL);
            DrawEncodingFoldout();
            GUILayout.Space(SPACING_LARGE);
            DrawRecordButton();
            GUILayout.EndVertical();

            // RIGHT: Advanced panel slides in
            if (showAdvancedPanel)
            {
                GUILayout.Space(SPACING_TIGHT / 2);
                GUI.color = new Color(0.9f, 0.9f, 0.9f);
                GUILayout.Box("", GUILayout.Width(SEPARATOR_LINE_WIDTH), GUILayout.ExpandHeight(true));
                GUI.color = Color.white;
                GUILayout.Space(SPACING_NORMAL);

                GUILayout.BeginVertical(GUILayout.Width(ADVANCED_PANEL_WIDTH - ADVANCED_MARGIN));
                DrawAdvancedContent();
                GUILayout.EndVertical();
            }

            GUILayout.EndHorizontal();
            GUI.DragWindow();
        }

        private void DrawAdvancedContent()
        {
            GUIStyle headerStyle = new GUIStyle(HighLogic.Skin.label);
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.fontSize = HEADER_FONT_SIZE;
            GUILayout.Label("Advanced Options", headerStyle);
            GUILayout.Space(SPACING_LARGE);

            if (SessionState.SelectedEncoderTab == 0)
            {
                GUIStyle ditherStyle = new GUIStyle(HighLogic.Skin.toggle);
                if (!SessionState.AmfUseBlueNoiseDither)
                    ditherStyle.normal.textColor = INFO_TEXT_COLOR;

                SessionState.AmfUseBlueNoiseDither = GUILayout.Toggle(
                    SessionState.AmfUseBlueNoiseDither,
                    " Gradient Protection",
                    ditherStyle
                );

                GUIStyle tooltipStyle = new GUIStyle(HighLogic.Skin.label);
                tooltipStyle.fontSize = INFO_FONT_SIZE;
                tooltipStyle.normal.textColor = INFO_TEXT_COLOR;
                tooltipStyle.wordWrap = true;

                if (SessionState.AmfUseBlueNoiseDither)
                    GUILayout.Label("Reduces color banding in dark areas", tooltipStyle);
            }
            else
            {
                GUIStyle infoStyle = new GUIStyle(HighLogic.Skin.label);
                infoStyle.normal.textColor = INFO_TEXT_COLOR;
                infoStyle.wordWrap = true;
                GUILayout.Label("Advanced options require AMD encoder.", infoStyle);
            }

            GUILayout.Space(20);
            GUIStyle placeholderStyle = new GUIStyle(HighLogic.Skin.label);
            placeholderStyle.normal.textColor = INFO_TEXT_COLOR;
            GUILayout.Label("(Post-processing effects will appear here)", placeholderStyle);
        }

        private void DrawStatusSection()
        {
            if (stopRequested && !DeterministicCaptureSession.IsRunning)
            {
                stopRequested = false;
            }

            if (DeterministicCaptureSession.IsRunning && !stopRequested)
            {
                bool unlimited = DeterministicCaptureSession.IsUnlimitedMode;

                GUIStyle style = new GUIStyle(HighLogic.Skin.label);
                style.normal.textColor = Color.yellow;
                style.fontStyle = FontStyle.Bold;

                if (unlimited)
                {
                    GUILayout.Label("● UNLIMITED RECORDING", style);
                    GUILayout.Label($"{DeterministicCaptureSession.AccumulatedSimulatedSeconds:F1}s elapsed");
                    GUILayout.Label($"{DeterministicCaptureSession.CapturedFrames:N0} frames");

                    float captureFps = DeterministicCaptureSession.CaptureFPS;
                    float playbackFps = frameratePresets[SessionState.PlaybackFpsIndex];
                    float ratio = playbackFps > 0.1f ? captureFps / playbackFps : 0f;

                    GUIStyle fpsStyle = new GUIStyle(HighLogic.Skin.label);
                    fpsStyle.fontStyle = FontStyle.Bold;
                    ApplyFpsColorGradient(fpsStyle, ratio);
                    GUILayout.Label($"Capture Rate: {captureFps:F1} FPS", fpsStyle);
                }
                else
                {
                    GUILayout.Label("● RECORDING", style);
                    GUILayout.Label($"{DeterministicCaptureSession.AccumulatedSimulatedSeconds:F1}s / {DeterministicCaptureSession.TargetSeconds:F1}s");
                    GUILayout.Label($"{DeterministicCaptureSession.CapturedFrames:N0} / {DeterministicCaptureSession.TargetFrames:N0} frames");

                    float captureFps = DeterministicCaptureSession.CaptureFPS;
                    int framesRemaining = DeterministicCaptureSession.TargetFrames - DeterministicCaptureSession.CapturedFrames;
                    float secondsRemaining = captureFps > 0.1f ? framesRemaining / captureFps : 0f;
                    TimeSpan remaining = TimeSpan.FromSeconds(secondsRemaining);

                    float playbackFps = frameratePresets[SessionState.PlaybackFpsIndex];
                    float ratio = playbackFps > 0.1f ? captureFps / playbackFps : 0f;

                    GUIStyle fpsStyle = new GUIStyle(HighLogic.Skin.label);
                    fpsStyle.fontStyle = FontStyle.Bold;
                    ApplyFpsColorGradient(fpsStyle, ratio);

                    GUILayout.Label($"Capture Rate: {captureFps:F1} FPS ({ratio * 100:F0}%)", fpsStyle);
                    GUILayout.Label($"Est. Remaining: {remaining:mm\\:ss}");
                }
            }
            else if (stopRequested)
            {
                GUIStyle style = new GUIStyle(HighLogic.Skin.label);
                style.normal.textColor = Color.red;
                GUILayout.Label("■ STOPPING...", style);
            }
            else
            {
                int fps = frameratePresets[SessionState.PlaybackFpsIndex];
                GUILayout.Label($"Ready — {Screen.width}x{Screen.height} @ {fps} FPS");
            }
        }

        private void ApplyFpsColorGradient(GUIStyle style, float ratio)
        {
            if (ratio < 0.10f)
                style.normal.textColor = new Color(0.5f, 0f, 0f);
            else if (ratio < 0.30f)
            {
                float t = Mathf.Pow((ratio - 0.10f) / 0.20f, 0.5f);
                style.normal.textColor = Color.Lerp(Color.red, new Color(1f, 0.5f, 0f), t);
            }
            else if (ratio < 0.60f)
            {
                float t = Mathf.Pow((ratio - 0.30f) / 0.30f, 2.0f);
                style.normal.textColor = Color.Lerp(new Color(1f, 0.5f, 0f), Color.yellow, t);
            }
            else if (ratio < 0.95f)
            {
                float t = (ratio - 0.60f) / 0.35f;
                style.normal.textColor = Color.Lerp(Color.yellow, Color.green, t);
            }
            else if (ratio <= 1.05f)
                style.normal.textColor = Color.green;
            else if (ratio < 1.25f)
            {
                float t = (ratio - 1.05f) / 0.20f;
                style.normal.textColor = Color.Lerp(Color.green, Color.cyan, t);
            }
            else
                style.normal.textColor = Color.cyan;
        }

        private void DrawCaptureTimingSection()
        {
            GUILayout.BeginVertical();
            GUILayout.Label("Capture FPS", HighLogic.Skin.label);
            DrawFpsSelector(SessionState.SimFpsIndex, v => SessionState.SimFpsIndex = v);

            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            GUI.enabled = !SessionState.LockFps;
            GUILayout.Label("Playback FPS", HighLogic.Skin.label, GUILayout.Width(PLAYBACK_LABEL_WIDTH));
            GUI.enabled = true;

            GUIStyle lockStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (SessionState.LockFps)
            {
                lockStyle.normal.textColor = Color.green;
                lockStyle.fontStyle = FontStyle.Bold;
            }

            SessionState.LockFps = GUILayout.Toggle(SessionState.LockFps, "Lock", lockStyle, GUILayout.Width(LOCK_TOGGLE_WIDTH), GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();

            GUI.enabled = !SessionState.LockFps;
            DrawFpsSelector(SessionState.PlaybackFpsIndex, v => SessionState.PlaybackFpsIndex = v);
            GUI.enabled = true;

            if (SessionState.LockFps)
                SessionState.PlaybackFpsIndex = SessionState.SimFpsIndex;

            float sim = frameratePresets[SessionState.SimFpsIndex];
            float play = frameratePresets[SessionState.PlaybackFpsIndex];
            GUILayout.Label($"Playback Speed: {(play / sim):0.##}×");
            GUILayout.EndVertical();
        }

        private void DrawDurationSection()
        {
            GUILayout.Label("Simulated Time (seconds)", HighLogic.Skin.label);
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("-5s", GUILayout.Width(DURATION_BTN_WIDTH)))
                SessionState.DurationSeconds = Mathf.Max(0f, SessionState.DurationSeconds - 5f);

            string displayText = SessionState.DurationSeconds <= 0 ? "∞" : SessionState.DurationSeconds.ToString("0.0");
            string text = GUILayout.TextField(displayText, GUILayout.Width(DURATION_FIELD_WIDTH));

            if (float.TryParse(text, out float parsed))
                SessionState.DurationSeconds = Mathf.Clamp(parsed, 0f, 3600f);

            if (GUILayout.Button("+5s", GUILayout.Width(DURATION_BTN_WIDTH)))
            {
                SessionState.DurationSeconds += 5f;
                if (DeterministicCaptureSession.IsRunning)
                    DeterministicCaptureSession.ExtendDuration(5f);
            }

            GUILayout.EndHorizontal();
        }

        private void DrawEncodingFoldout()
        {
            string label = showEncodingSettings ? "▼ Hide Encoding" : "► Show Encoding";
            if (GUILayout.Button(label, HighLogic.Skin.button))
                showEncodingSettings = !showEncodingSettings;

            if (!showEncodingSettings) return;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Encoder", HighLogic.Skin.label);

            int selected = SessionState.SelectedEncoderTab;
            GUILayout.BeginHorizontal();


            if (GUILayout.Toggle(selected == 0, "AMD", HighLogic.Skin.toggle, GUILayout.Width(ENCODER_BTN_WIDTH_AMD)))
                selected = 0;
            GUILayout.Space(SPACING_NORMAL); // Space between AMD and NVIDIA

            if (GUILayout.Toggle(selected == 1, "NVIDIA", HighLogic.Skin.toggle, GUILayout.Width(ENCODER_BTN_WIDTH_NVIDIA)))
                selected = 1;
            GUILayout.Space(SPACING_NORMAL); // Space between NVIDIA and CPU

            if (GUILayout.Toggle(selected == 2, "CPU", HighLogic.Skin.toggle, GUILayout.Width(ENCODER_BTN_WIDTH_CPU)))
                selected = 2;

            GUILayout.EndHorizontal();
            SessionState.SelectedEncoderTab = selected;
            GUILayout.Space(SPACING_NORMAL);

            if (selected == 0)
                DrawAmfSettings();
            else if (selected == 1)
                DrawNvencSettings();
            else
                DrawCpuSettings();

            GUILayout.EndVertical();
        }

        private void DrawAmfSettings()
        {
            GUILayout.Label("AMD (HEVC)", HighLogic.Skin.label);

            int selectedRc = SessionState.AmfRateControlMode;
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(selectedRc == 0, "Quality(CQP)", HighLogic.Skin.toggle, GUILayout.Width(RATECONTROL_WIDTH_QUALITY)))
                selectedRc = 0;
            if (GUILayout.Toggle(selectedRc == 1, "VBR", HighLogic.Skin.toggle, GUILayout.Width(RATECONTROL_WIDTH_VBR)))
                selectedRc = 1;
            GUILayout.EndHorizontal();
            SessionState.AmfRateControlMode = selectedRc;
            GUILayout.Space(SPACING_NORMAL);

            if (SessionState.AmfRateControlMode == 0)
            {
                GUILayout.Label("Quality Level:", HighLogic.Skin.label);
                SessionState.AmfQualitySlider = GUILayout.HorizontalSlider(SessionState.AmfQualitySlider, 0f, 1f);
                GUILayout.Label(SessionState.GetQualityLabel(SessionState.AmfQualitySlider));

                GUIStyle infoStyle = new GUIStyle(HighLogic.Skin.label);
                infoStyle.fontSize = INFO_FONT_SIZE;
                infoStyle.normal.textColor = INFO_TEXT_COLOR;
                GUILayout.Label("File size varies by scene complexity", infoStyle);
            }
            else
            {
                GUILayout.Label("Target Bitrate:", HighLogic.Skin.label);
                int estimatedMB = (SessionState.AmfTargetBitrate * 5) / 4;
                GUILayout.Label(SessionState.AmfTargetBitrate + " Mbps (~" + estimatedMB + " MB per 10s)");

                SessionState.AmfTargetBitrate = Mathf.Clamp((int)GUILayout.HorizontalSlider(SessionState.AmfTargetBitrate, 10, 200), 10, 200);

                GUIStyle infoStyle = new GUIStyle(HighLogic.Skin.label);
                infoStyle.fontSize = INFO_FONT_SIZE;
                infoStyle.normal.textColor = INFO_TEXT_COLOR;
                GUILayout.Label("Quality adjusts automatically to hit target", infoStyle);
            }

            GUILayout.Space(SPACING_NORMAL);
            GUILayout.Label("Encoding Speed:", HighLogic.Skin.label);
            int selectedSpeed = SessionState.AmfEncoderSpeed;
            GUILayout.BeginHorizontal();

            if (GUILayout.Toggle(selectedSpeed == 0, "Speed", HighLogic.Skin.toggle, GUILayout.Width(SPEED_WIDTH_SPEED)))
                selectedSpeed = 0;
            GUILayout.Space(SPACING_NORMAL);

            if (GUILayout.Toggle(selectedSpeed == 1, "Balanced", HighLogic.Skin.toggle, GUILayout.Width(SPEED_WIDTH_BALANCED)))
                selectedSpeed = 1;
            GUILayout.Space(SPACING_NORMAL);

            if (GUILayout.Toggle(selectedSpeed == 2, "Quality", HighLogic.Skin.toggle, GUILayout.Width(SPEED_WIDTH_QUALITY)))
                selectedSpeed = 2;

            GUILayout.EndHorizontal();
            SessionState.AmfEncoderSpeed = selectedSpeed;
        }

        private void DrawNvencSettings()
        {
            GUILayout.Label("NVIDIA (HEVC)", HighLogic.Skin.label);

            int selectedRc = SessionState.NvencRateControlMode;
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(selectedRc == 0, "Quality(CQ)", HighLogic.Skin.toggle, GUILayout.Width(RATECONTROL_WIDTH_QUALITY)))
                selectedRc = 0;
            if (GUILayout.Toggle(selectedRc == 1, "VBR", HighLogic.Skin.toggle, GUILayout.Width(RATECONTROL_WIDTH_VBR)))
                selectedRc = 1;
            GUILayout.EndHorizontal();
            SessionState.NvencRateControlMode = selectedRc;
            GUILayout.Space(SPACING_NORMAL);

            if (SessionState.NvencRateControlMode == 0)
            {
                GUILayout.Label("Quality Level:", HighLogic.Skin.label);
                SessionState.NvencQualitySlider = GUILayout.HorizontalSlider(SessionState.NvencQualitySlider, 0f, 1f);
                int cq = SessionState.NvencCqValue;
                string qualityDesc = cq <= 8 ? "Near Lossless" : cq <= 14 ? "Master Quality" : cq <= 20 ? "High Quality" : "Compressed";
                GUILayout.Label($"CQ {cq} ({qualityDesc})");

                GUIStyle infoStyle = new GUIStyle(HighLogic.Skin.label);
                infoStyle.fontSize = INFO_FONT_SIZE;
                infoStyle.normal.textColor = INFO_TEXT_COLOR;
                GUILayout.Label("File size varies by scene complexity", infoStyle);
            }
            else
            {
                GUILayout.Label("Target Bitrate:", HighLogic.Skin.label);
                int estimatedMB = (SessionState.NvencTargetBitrate * 5) / 4;
                GUILayout.Label(SessionState.NvencTargetBitrate + " Mbps (~" + estimatedMB + " MB per 10s)");
                SessionState.NvencTargetBitrate = Mathf.Clamp((int)GUILayout.HorizontalSlider(SessionState.NvencTargetBitrate, 10, 200), 10, 200);

                GUIStyle infoStyle = new GUIStyle(HighLogic.Skin.label);
                infoStyle.fontSize = INFO_FONT_SIZE;
                infoStyle.normal.textColor = INFO_TEXT_COLOR;
                GUILayout.Label("Quality adjusts automatically to hit target", infoStyle);
            }

            GUILayout.Space(SPACING_NORMAL);
            GUILayout.Label("Encoding Speed:", HighLogic.Skin.label);
            int selectedSpeed = SessionState.NvencPreset;
            GUILayout.BeginHorizontal();

            if (GUILayout.Toggle(selectedSpeed == 0, "Speed", HighLogic.Skin.toggle, GUILayout.Width(SPEED_WIDTH_SPEED)))
                selectedSpeed = 0;
            GUILayout.Space(SPACING_NORMAL);
            if (GUILayout.Toggle(selectedSpeed == 1, "Balanced", HighLogic.Skin.toggle, GUILayout.Width(SPEED_WIDTH_BALANCED)))
                selectedSpeed = 1;
            GUILayout.Space(SPACING_NORMAL);
            if (GUILayout.Toggle(selectedSpeed == 2, "Quality", HighLogic.Skin.toggle, GUILayout.Width(SPEED_WIDTH_QUALITY)))
                selectedSpeed = 2;

            GUILayout.EndHorizontal();
            SessionState.NvencPreset = selectedSpeed;
        }

        private void DrawCpuSettings()
        {
            GUILayout.Label("CPU (x264)", HighLogic.Skin.label);

            int selectedRc = SessionState.CpuRateControlMode;
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(selectedRc == 0, "Quality(CRF)", HighLogic.Skin.toggle, GUILayout.Width(RATECONTROL_WIDTH_QUALITY)))
                selectedRc = 0;
            if (GUILayout.Toggle(selectedRc == 1, "VBR", HighLogic.Skin.toggle, GUILayout.Width(RATECONTROL_WIDTH_VBR)))
                selectedRc = 1;
            GUILayout.EndHorizontal();
            SessionState.CpuRateControlMode = selectedRc;
            GUILayout.Space(SPACING_NORMAL);

            if (SessionState.CpuRateControlMode == 0)
            {
                GUILayout.Label("Quality Level:", HighLogic.Skin.label);
                SessionState.CpuQualitySlider = GUILayout.HorizontalSlider(SessionState.CpuQualitySlider, 0f, 1f);
                int crf = SessionState.CpuCrfValue;
                string qualityDesc = crf <= 8 ? "Near Lossless" : crf <= 14 ? "Master Quality" : crf <= 20 ? "High Quality" : "Compressed";
                GUILayout.Label($"CRF {crf} ({qualityDesc})");

                GUIStyle infoStyle = new GUIStyle(HighLogic.Skin.label);
                infoStyle.fontSize = INFO_FONT_SIZE;
                infoStyle.normal.textColor = INFO_TEXT_COLOR;
                GUILayout.Label("File size varies by scene complexity", infoStyle);
            }
            else
            {
                GUILayout.Label("Target Bitrate:", HighLogic.Skin.label);
                int estimatedMB = (SessionState.CpuTargetBitrate * 5) / 4;
                GUILayout.Label(SessionState.CpuTargetBitrate + " Mbps (~" + estimatedMB + " MB per 10s)");
                SessionState.CpuTargetBitrate = Mathf.Clamp((int)GUILayout.HorizontalSlider(SessionState.CpuTargetBitrate, 10, 200), 10, 200);

                GUIStyle infoStyle = new GUIStyle(HighLogic.Skin.label);
                infoStyle.fontSize = INFO_FONT_SIZE;
                infoStyle.normal.textColor = INFO_TEXT_COLOR;
                GUILayout.Label("Quality adjusts automatically to hit target", infoStyle);
            }

            GUILayout.Space(SPACING_NORMAL);
            GUILayout.Label("Encoding Speed:", HighLogic.Skin.label);
            int selectedSpeed = SessionState.CpuPreset;
            GUILayout.BeginHorizontal();


            if (GUILayout.Toggle(selectedSpeed == 0, "Speed", HighLogic.Skin.toggle, GUILayout.Width(SPEED_WIDTH_SPEED)))
                selectedSpeed = 0;
            GUILayout.Space(SPACING_NORMAL);
            if (GUILayout.Toggle(selectedSpeed == 1, "Balanced", HighLogic.Skin.toggle, GUILayout.Width(SPEED_WIDTH_BALANCED)))
                selectedSpeed = 1;
            GUILayout.Space(SPACING_NORMAL);
            if (GUILayout.Toggle(selectedSpeed == 2, "Quality", HighLogic.Skin.toggle, GUILayout.Width(SPEED_WIDTH_QUALITY)))
                selectedSpeed = 2;

            GUILayout.EndHorizontal();
            SessionState.CpuPreset = selectedSpeed;
        }

        private void DrawFpsSelector(int index, Action<int> setter)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(FPS_SELECTOR_WIDTH)) && index > 0)
                setter(index - 1);

            GUILayout.Label($"{frameratePresets[index]} FPS", GUILayout.Width(FPS_LABEL_WIDTH));

            if (GUILayout.Button(">", GUILayout.Width(FPS_SELECTOR_WIDTH)) && index < frameratePresets.Length - 1)
                setter(index + 1);
            GUILayout.EndHorizontal();
        }

        private void DrawRecordButton()
        {
            bool running = DeterministicCaptureSession.IsRunning;
            GUI.color = running ? Color.red : Color.green;

            if (GUILayout.Button(
                running ? "■ Stop Recording" : "● Start Recording",
                GUILayout.Height(BTN_HEIGHT_RECORD)))
            {
                if (running)
                {
                    stopRequested = true;
                    DeterministicCaptureSession.RequestStop();
                }
                else
                {
                    StartRecording();
                }
            }

            GUI.color = Color.white;
        }

        private void StartRecording()
        {
            stopRequested = false;

            int simFps = frameratePresets[SessionState.SimFpsIndex];
            int playbackFps = frameratePresets[SessionState.PlaybackFpsIndex];

            bool forceSoftware = SessionState.SelectedEncoderTab == 2;
            bool zeroCopy = SessionState.SelectedEncoderTab != 2;

            DeterministicCaptureSession.Run(
                    simFps,
                    playbackFps,
                    SessionState.DurationSeconds,
                    forceSoftware,
                    zeroCopy);
        }
    }

    public class CaptureReport
    {
        public int CapturedFrames;
        public float SimulatedSeconds;
        public float OutputDuration;
        public float RealWorldCaptureTime;
        public string EncodingMode;
        public string OutputFilePath;
    }
}