using CinematicRecorder.Capture;
using CinematicRecorder.Core;
using System;
using UnityEngine;

namespace CinematicRecorder.UI
{
    public class SettingsDialog : MonoBehaviour
    {
        private Rect windowRect = new Rect(300, 60, 320, 500);
        private bool renderDisplay;
        private bool showEncodingSettings;
        private bool stopRequested;

        //Window size cache
        private float animWidth;
        private float animHeight;

        private GUIStyle windowStyle;
        private bool stylesInitialized;
        public bool IsVisible => renderDisplay;

        public event Action OnDialogDismissed;


        private enum SettingsTab { Main, Advanced }
        private SettingsTab currentTab = SettingsTab.Main;

        // Constants
        private readonly int[] frameratePresets = { 24, 30, 60, 120, 240 };
        private readonly string[] encoderTabNames = { "AMD", "NVIDIA", "CPU (x264)" };
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

            // Fixed width for both tabs - no more horizontal animation jitter
            float targetHeight = showEncodingSettings ? 620f : 440f;

            // Gentle height animate for encoder expansion only (no width fighting)
            windowRect.height = Mathf.Lerp(windowRect.height, targetHeight, 0.25f);
            // Snap height when close to avoid micro-lerp
            if (Mathf.Abs(windowRect.height - targetHeight) < 0.5f) windowRect.height = targetHeight;

            // Fixed width - no more horizontal fighting
            windowRect.width = 420f;

            windowRect = GUILayout.Window(
                12345,
                windowRect,
                DrawWindow,
                "Cinematic Recorder",
                windowStyle);
        }

        private float CalculateTargetHeight() => showEncodingSettings ? 620f : 360f;

        private void AnimateDimensions(float targetW, float targetH)
        {
            // Slower lerp for symmetry (adjust 0.15f to taste)
            animWidth = Mathf.Lerp(animWidth, targetW, 0.15f);
            animHeight = Mathf.Lerp(animHeight, targetH, 0.15f);

            // Snap to target when close to avoid micro-jitter
            if (Mathf.Abs(animWidth - targetW) < 0.5f) animWidth = targetW;
            if (Mathf.Abs(animHeight - targetH) < 0.5f) animHeight = targetH;
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            // Tab buttons row
            GUILayout.BeginHorizontal();
            GUIStyle tabStyle = new GUIStyle(HighLogic.Skin.button);
            GUIStyle activeTabStyle = new GUIStyle(HighLogic.Skin.button);
            activeTabStyle.normal.textColor = Color.green;
            activeTabStyle.fontStyle = FontStyle.Bold;

            if (GUILayout.Button("Main", currentTab == SettingsTab.Main ? activeTabStyle : tabStyle, GUILayout.Height(25)))
                currentTab = SettingsTab.Main;

            if (GUILayout.Button("Advanced", currentTab == SettingsTab.Advanced ? activeTabStyle : tabStyle, GUILayout.Height(25)))
                currentTab = SettingsTab.Advanced;

            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Content based on selected tab
            if (currentTab == SettingsTab.Main)
            {
                DrawMainTab();
            }
            else
            {
                DrawAdvancedTab();
            }

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void DrawMainTab()
        {
            DrawStatusSection();
            GUILayout.Space(10);

            DrawCaptureTimingSection();
            GUILayout.Space(4);

            DrawDurationSection();
            GUILayout.Space(10);

            DrawEncodingFoldout();
            GUILayout.Space(15);

            DrawRecordButton();
        }

        private void DrawAdvancedTab()
        {
            GUIStyle headerStyle = new GUIStyle(HighLogic.Skin.label);
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.fontSize = 14;
            GUILayout.Label("Advanced Options", headerStyle);
            GUILayout.Space(20);

            // Blue Noise Dithering - AMD Zero-Copy only
            if (SessionState.SelectedEncoderTab == 0) // AMD selected
            {
                GUIStyle ditherStyle = new GUIStyle(HighLogic.Skin.toggle);
                if (!SessionState.AmfUseBlueNoiseDither)
                {
                    ditherStyle.normal.textColor = Color.gray;
                }

                SessionState.AmfUseBlueNoiseDither = GUILayout.Toggle(
                    SessionState.AmfUseBlueNoiseDither,
                    " Blue Noise Dithering (AMD only)",
                    ditherStyle
                );

                GUIStyle tooltipStyle = new GUIStyle(HighLogic.Skin.label);
                tooltipStyle.fontSize = 11;
                tooltipStyle.normal.textColor = Color.gray;
                tooltipStyle.wordWrap = true;

                if (SessionState.AmfUseBlueNoiseDither)
                    GUILayout.Label("Reduces color banding in dark areas via GPU compute", tooltipStyle);
                else
                    GUILayout.Label("Uses fast GPU copy (may show banding in gradients)", tooltipStyle);
            }
            else
            {
                GUIStyle infoStyle = new GUIStyle(HighLogic.Skin.label);
                infoStyle.normal.textColor = Color.gray;
                infoStyle.wordWrap = true;
                GUILayout.Label("Advanced options require AMD encoder to be selected.", infoStyle);
            }

            GUILayout.Space(20);

            // Placeholder for future AO settings
            GUIStyle placeholderStyle = new GUIStyle(HighLogic.Skin.label);
            placeholderStyle.normal.textColor = Color.gray;
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
                // Check for unlimited mode
                bool unlimited = DeterministicCaptureSession.IsUnlimitedMode;

                GUIStyle style = new GUIStyle(HighLogic.Skin.label);
                style.normal.textColor = Color.yellow;
                style.fontStyle = FontStyle.Bold;

                if (unlimited)
                {
                    GUILayout.Label("● UNLIMITED RECORDING", style);

                    // Time progress - elapsed only
                    GUILayout.Label($"{DeterministicCaptureSession.AccumulatedSimulatedSeconds:F1}s elapsed");

                    // Frame progress - captured only
                    GUILayout.Label($"{DeterministicCaptureSession.CapturedFrames:N0} frames");

                    float captureFps = DeterministicCaptureSession.CaptureFPS;
                    float playbackFps = frameratePresets[SessionState.PlaybackFpsIndex];
                    float ratio = playbackFps > 0.1f ? captureFps / playbackFps : 0f;

                    GUIStyle fpsStyle = new GUIStyle(HighLogic.Skin.label);
                    fpsStyle.fontStyle = FontStyle.Bold;

                    // Apply gradient logic even for unlimited
                    ApplyFpsColorGradient(fpsStyle, ratio);

                    GUILayout.Label($"Capture Rate: {captureFps:F1} FPS", fpsStyle);
                }
                else
                {
                    GUILayout.Label("● RECORDING", style);

                    // Time progress
                    GUILayout.Label(
                        $"{DeterministicCaptureSession.AccumulatedSimulatedSeconds:F1}s / " +
                        $"{DeterministicCaptureSession.TargetSeconds:F1}s");

                    // Frame progress
                    GUILayout.Label(
                        $"{DeterministicCaptureSession.CapturedFrames:N0} / " +
                        $"{DeterministicCaptureSession.TargetFrames:N0} frames");

                    // Calculate and display FPS + Time remaining
                    float captureFps = DeterministicCaptureSession.CaptureFPS;
                    int framesRemaining = DeterministicCaptureSession.TargetFrames -
                        DeterministicCaptureSession.CapturedFrames;
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

        // Helper method for consistent FPS coloring
        private void ApplyFpsColorGradient(GUIStyle style, float ratio)
        {
            // Blood red for catastrophic/overnight encode scenarios (< 10% of target FPS)
            if (ratio < 0.10f)
            {
                // Dark blood red (RGB: 0.5, 0, 0) - distinct from bright Color.red (1, 0, 0)
                style.normal.textColor = new Color(0.5f, 0f, 0f);
            }
            else if (ratio < 0.30f) // 10% to 30%: Red to Orange (with bias)
            {
                float t = (ratio - 0.10f) / 0.20f;

                // Power curve 0.5: rush from red to orange quickly
                // At 15% (midpoint), you're already 70% toward orange (sqrt(0.5) ≈ 0.7)
                t = Mathf.Pow(t, 0.5f);

                style.normal.textColor = Color.Lerp(Color.red, new Color(1f, 0.5f, 0f), t);
            }
            else if (ratio < 0.60f) // 30% to 60%: Orange to Yellow (linger here)
            {
                float t = (ratio - 0.30f) / 0.30f;

                // Power 2.0: Linger in orange, rush to yellow only at the end
                t = Mathf.Pow(t, 2.0f);

                style.normal.textColor = Color.Lerp(new Color(1f, 0.5f, 0f), Color.yellow, t);
            }
            else if (ratio < 0.95f) // 60% to 95%: Yellow to Green
            {
                float t = (ratio - 0.60f) / 0.35f;
                style.normal.textColor = Color.Lerp(Color.yellow, Color.green, t);
            }
            else if (ratio <= 1.05f) // Sweet spot (95% - 105%)
            {
                style.normal.textColor = Color.green;
            }
            else if (ratio < 1.25f) // 105% - 125%: Green to Cyan
            {
                float t = (ratio - 1.05f) / 0.20f;
                style.normal.textColor = Color.Lerp(Color.green, Color.cyan, t);
            }
            else // > 125%
            {
                style.normal.textColor = Color.cyan;
            }
        }

        private void DrawCaptureTimingSection()
        {
            GUILayout.BeginVertical();

            // Capture FPS
            GUILayout.Label("Capture FPS", HighLogic.Skin.label);
            DrawFpsSelector(SessionState.SimFpsIndex, v => SessionState.SimFpsIndex = v);

            GUILayout.Space(2);

            // Playback FPS label with Lock toggle on same line, closer to arrows
            GUILayout.BeginHorizontal();
            GUI.enabled = !SessionState.LockFps;
            GUILayout.Label("Playback FPS", HighLogic.Skin.label, GUILayout.Width(90));
            GUI.enabled = true;

            // Green glowing style when active (mimicking radio button active state)
            GUIStyle lockStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (SessionState.LockFps)
            {
                lockStyle.normal.textColor = Color.green;
                lockStyle.fontStyle = FontStyle.Bold;
            }

            // ExpandedWidth false keeps it tight to the text
            SessionState.LockFps = GUILayout.Toggle(SessionState.LockFps, "Locked", lockStyle, GUILayout.Width(70), GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();

            // Playback FPS selector (disable interaction when locked)
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

            if (GUILayout.Button("-5s", GUILayout.Width(50)))
                SessionState.DurationSeconds = Mathf.Max(0f, SessionState.DurationSeconds - 5f); // MODIFIED: Allow 0

            // NEW: Display infinity symbol when duration is 0
            string displayText;
            if (SessionState.DurationSeconds <= 0)
                displayText = "∞";
            else
                displayText = SessionState.DurationSeconds.ToString("0.0");

            string text = GUILayout.TextField(displayText, GUILayout.Width(80));

            float parsed;
            if (float.TryParse(text, out parsed))
                SessionState.DurationSeconds = Mathf.Clamp(parsed, 0f, 3600f); // MODIFIED: Allow 0

            if (GUILayout.Button("+5s", GUILayout.Width(50)))
            {
                SessionState.DurationSeconds += 5f;
                if (DeterministicCaptureSession.IsRunning)
                    DeterministicCaptureSession.ExtendDuration(5f);
            }

            GUILayout.EndHorizontal();
        }

        private void DrawEncodingFoldout()
        {
            string label = showEncodingSettings
                ? "▼ Hide Encoding Settings"
                : "► Show Encoding Settings";

            if (GUILayout.Button(label, HighLogic.Skin.button))
                showEncodingSettings = !showEncodingSettings;

            if (!showEncodingSettings) return;

            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label("Encoder", HighLogic.Skin.label);

            int selected = SessionState.SelectedEncoderTab;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < encoderTabNames.Length; i++)
            {
                if (GUILayout.Toggle(selected == i, encoderTabNames[i], HighLogic.Skin.toggle, GUILayout.Width(120)))
                    selected = i;
            }
            GUILayout.EndHorizontal();

            SessionState.SelectedEncoderTab = selected;
            GUILayout.Space(8);

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

            // Rate control selection (only 2 options now)
            int selectedRc = SessionState.AmfRateControlMode;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < rateControlNames.Length; i++)
            {
                if (GUILayout.Toggle(selectedRc == i, rateControlNames[i], HighLogic.Skin.toggle, GUILayout.Width(160)))
                    selectedRc = i;
            }
            GUILayout.EndHorizontal();
            SessionState.AmfRateControlMode = selectedRc;

            GUILayout.Space(10);

            if (SessionState.AmfRateControlMode == 0) // CQP Mode
            {
                // Quality slider (now 0-24 range)
                GUILayout.Label("Quality Level:", HighLogic.Skin.label);

                SessionState.AmfQualitySlider = GUILayout.HorizontalSlider(
                    SessionState.AmfQualitySlider, 0f, 1f);

                // Show descriptive label
                string label = SessionState.GetQualityLabel(SessionState.AmfQualitySlider);
                GUILayout.Label(label);

                GUIStyle infoStyle = new GUIStyle(HighLogic.Skin.label);
                infoStyle.fontSize = 11;
                infoStyle.normal.textColor = Color.gray;
                GUILayout.Label("File size varies by scene complexity", infoStyle);
            }
            else // VBR Mode
            {
                // Bitrate slider
                GUILayout.Label("Target Bitrate:", HighLogic.Skin.label);

                // Display current value
                int estimatedMB = (SessionState.AmfTargetBitrate * 5) / 4;
                GUILayout.Label(SessionState.AmfTargetBitrate + " Mbps (~" + estimatedMB + " MB per 10s)");

                SessionState.AmfTargetBitrate = Mathf.Clamp(
                    (int)GUILayout.HorizontalSlider(SessionState.AmfTargetBitrate, 10, 200),
                    10, 200);

                GUIStyle infoStyle = new GUIStyle(HighLogic.Skin.label);
                infoStyle.fontSize = 11;
                infoStyle.normal.textColor = Color.gray;
                GUILayout.Label("Quality adjusts automatically to hit target", infoStyle);
            }

            GUILayout.Space(10);

            // REMOVED: Blue Noise Dithering section (moved to Advanced panel)

            // Speed preset (always visible)
            GUILayout.Label("Encoding Speed:", HighLogic.Skin.label);
            int selectedSpeed = SessionState.AmfEncoderSpeed;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < speedPresetNames.Length; i++)
            {
                if (GUILayout.Toggle(selectedSpeed == i, speedPresetNames[i], HighLogic.Skin.toggle, GUILayout.Width(100)))
                    selectedSpeed = i;
            }
            GUILayout.EndHorizontal();
            SessionState.AmfEncoderSpeed = selectedSpeed;
        }

        private void DrawNvencSettings()
        {
            GUILayout.Label("NVIDIA (HEVC)", HighLogic.Skin.label);

            // Rate control selection (only 2 options now)
            int selectedRc = SessionState.NvencRateControlMode;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < rateControlNames.Length; i++)
            {
                if (GUILayout.Toggle(selectedRc == i, rateControlNames[i], HighLogic.Skin.toggle, GUILayout.Width(160)))
                    selectedRc = i;
            }
            GUILayout.EndHorizontal();
            SessionState.NvencRateControlMode = selectedRc;

            GUILayout.Space(10);

            if (SessionState.NvencRateControlMode == 0) // CQ Mode (Quality)
            {
                GUILayout.Label("Quality Level:", HighLogic.Skin.label);

                SessionState.NvencQualitySlider = GUILayout.HorizontalSlider(
                    SessionState.NvencQualitySlider, 0f, 1f);

                // Show CQ value (0-24 range from SessionState)
                int cq = SessionState.NvencCqValue;
                string qualityDesc = cq <= 8 ? "Near Lossless" : cq <= 14 ? "Master Quality" : cq <= 20 ? "High Quality" : "Compressed";
                GUILayout.Label($"CQ {cq} ({qualityDesc})");

                GUIStyle infoStyle = new GUIStyle(HighLogic.Skin.label);
                infoStyle.fontSize = 11;
                infoStyle.normal.textColor = Color.gray;
                GUILayout.Label("File size varies by scene complexity", infoStyle);
            }
            else // VBR Mode (File Size)
            {
                GUILayout.Label("Target Bitrate:", HighLogic.Skin.label);

                // Display current value with file size estimate
                int estimatedMB = (SessionState.NvencTargetBitrate * 5) / 4;
                GUILayout.Label(SessionState.NvencTargetBitrate + " Mbps (~" + estimatedMB + " MB per 10s)");

                SessionState.NvencTargetBitrate = Mathf.Clamp(
                    (int)GUILayout.HorizontalSlider(SessionState.NvencTargetBitrate, 10, 200),
                    10, 200);

                GUIStyle infoStyle = new GUIStyle(HighLogic.Skin.label);
                infoStyle.fontSize = 11;
                infoStyle.normal.textColor = Color.gray;
                GUILayout.Label("Quality adjusts automatically to hit target", infoStyle);
            }

            GUILayout.Space(10);

            // Speed preset (always visible)
            int selectedSpeed = SessionState.NvencPreset;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < speedPresetNames.Length; i++)
            {
                if (GUILayout.Toggle(selectedSpeed == i, speedPresetNames[i], HighLogic.Skin.toggle, GUILayout.Width(100)))
                    selectedSpeed = i;
            }
            GUILayout.EndHorizontal();
            SessionState.NvencPreset = selectedSpeed;
        }

        private void DrawCpuSettings()
        {
            GUILayout.Label("CPU (x264)", HighLogic.Skin.label);

            // Rate control selection (only 2 options)
            int selectedRc = SessionState.CpuRateControlMode;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < rateControlNames.Length; i++)
            {
                if (GUILayout.Toggle(selectedRc == i, rateControlNames[i], HighLogic.Skin.toggle, GUILayout.Width(160)))
                    selectedRc = i;
            }
            GUILayout.EndHorizontal();
            SessionState.CpuRateControlMode = selectedRc;

            GUILayout.Space(10);

            if (SessionState.CpuRateControlMode == 0) // CRF Mode (Quality)
            {
                GUILayout.Label("Quality Level:", HighLogic.Skin.label);

                SessionState.CpuQualitySlider = GUILayout.HorizontalSlider(
                    SessionState.CpuQualitySlider, 0f, 1f);

                // Show CRF value (0-24 range)
                int crf = SessionState.CpuCrfValue;
                string qualityDesc = crf <= 8 ? "Near Lossless" : crf <= 14 ? "Master Quality" : crf <= 20 ? "High Quality" : "Compressed";
                GUILayout.Label($"CRF {crf} ({qualityDesc})");

                GUIStyle infoStyle = new GUIStyle(HighLogic.Skin.label);
                infoStyle.fontSize = 11;
                infoStyle.normal.textColor = Color.gray;
                GUILayout.Label("File size varies by scene complexity", infoStyle);
            }
            else // VBR Mode
            {
                GUILayout.Label("Target Bitrate:", HighLogic.Skin.label);
                int estimatedMB = (SessionState.CpuTargetBitrate * 5) / 4;
                GUILayout.Label(SessionState.CpuTargetBitrate + " Mbps (~" + estimatedMB + " MB per 10s)");

                SessionState.CpuTargetBitrate = Mathf.Clamp(
                    (int)GUILayout.HorizontalSlider(SessionState.CpuTargetBitrate, 10, 200),
                    10, 200);

                GUIStyle infoStyle = new GUIStyle(HighLogic.Skin.label);
                infoStyle.fontSize = 11;
                infoStyle.normal.textColor = Color.gray;
                GUILayout.Label("Quality adjusts automatically to hit target", infoStyle);
            }

            GUILayout.Space(10);

            // Encoding preset (Speed/Balanced/Quality)
            int selectedSpeed = SessionState.CpuPreset;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < speedPresetNames.Length; i++)
            {
                if (GUILayout.Toggle(selectedSpeed == i, speedPresetNames[i], HighLogic.Skin.toggle, GUILayout.Width(100)))
                    selectedSpeed = i;
            }
            GUILayout.EndHorizontal();
            SessionState.CpuPreset = selectedSpeed;
        }

        private void DrawFpsSelector(int index, Action<int> setter)
        {
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("<", GUILayout.Width(30)) && index > 0)
                setter(index - 1);

            GUILayout.Label($"{frameratePresets[index]} FPS", GUILayout.Width(100));

            if (GUILayout.Button(">", GUILayout.Width(30)) && index < frameratePresets.Length - 1)
                setter(index + 1);

            GUILayout.EndHorizontal();
        }

        private void DrawRecordButton()
        {
            bool running = DeterministicCaptureSession.IsRunning;
            GUI.color = running ? Color.red : Color.green;

            if (GUILayout.Button(
                running ? "■ Stop Recording" : "● Start Recording",
                GUILayout.Height(40)))
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