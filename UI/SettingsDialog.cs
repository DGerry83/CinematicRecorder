using CinematicRecorder.Capture;
using CinematicRecorder.Core;
using System;
using UnityEngine;

namespace CinematicRecorder.UI
{
    public class SettingsDialog : MonoBehaviour
    {
        private Rect windowRect = new Rect(300, 60, 420, 500);
        private bool renderDisplay;
        private bool showEncodingSettings;
        private bool stopRequested;

        private GUIStyle windowStyle;
        private bool stylesInitialized;

        public event Action OnDialogDismissed;

        private FinalReportWindow finalReportWindow;

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

            float targetHeight = showEncodingSettings ? 620f : 440f;
            windowRect.height = Mathf.Lerp(windowRect.height, targetHeight, 0.25f);

            windowRect = GUILayout.Window(
                12345,
                windowRect,
                DrawWindow,
                "Cinematic Recorder",
                windowStyle);
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            DrawStatusSection();
            GUILayout.Space(10);

            DrawCaptureTimingSection();
            GUILayout.Space(10);

            DrawDurationSection();
            GUILayout.Space(10);

            DrawEncodingFoldout();
            GUILayout.Space(15);

            DrawRecordButton();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void DrawStatusSection()
        {
            if (DeterministicCaptureSession.IsRunning && !stopRequested)
            {
                GUIStyle style = new GUIStyle(HighLogic.Skin.label);
                style.normal.textColor = Color.yellow;
                style.fontStyle = FontStyle.Bold;

                GUILayout.Label("● RECORDING", style);

                // Time progress
                GUILayout.Label(
                    $"{DeterministicCaptureSession.CapturedSeconds:F1}s / " +
                    $"{DeterministicCaptureSession.TargetSeconds:F1}s");

                // Frame progress
                GUILayout.Label(
                    $"{DeterministicCaptureSession.CapturedFrames:N0} / " +
                    $"{DeterministicCaptureSession.TargetFrames:N0} frames");

                // Calculate and display FPS + Time remaining
                float fps = DeterministicCaptureSession.CaptureFPS;
                int framesRemaining = DeterministicCaptureSession.TargetFrames -
                    DeterministicCaptureSession.CapturedFrames;
                float secondsRemaining = fps > 0.1f ? framesRemaining / fps : 0f;
                TimeSpan remaining = TimeSpan.FromSeconds(secondsRemaining);

                GUILayout.Label($"Capture Rate: {fps:F1} FPS");
                GUILayout.Label($"Est. Remaining: {remaining:mm\\:ss}");
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

        private void DrawCaptureTimingSection()
        {
            GUILayout.Label("Capture FPS", HighLogic.Skin.label);
            DrawFpsSelector(
                SessionState.SimFpsIndex,
                v => SessionState.SimFpsIndex = v);

            GUILayout.Space(4);

            SessionState.LockFps = GUILayout.Toggle(
                SessionState.LockFps,
                " Lock output FPS to capture FPS",
                HighLogic.Skin.toggle);

            GUILayout.Label("Playback FPS", HighLogic.Skin.label);
            GUI.enabled = !SessionState.LockFps;

            DrawFpsSelector(
                SessionState.PlaybackFpsIndex,
                v => SessionState.PlaybackFpsIndex = v);

            GUI.enabled = true;

            if (SessionState.LockFps)
                SessionState.PlaybackFpsIndex = SessionState.SimFpsIndex;

            float sim = frameratePresets[SessionState.SimFpsIndex];
            float play = frameratePresets[SessionState.PlaybackFpsIndex];
            GUILayout.Label($"Playback Speed: {(play / sim):0.##}×");
        }

        private void DrawDurationSection()
        {
            GUILayout.Label("Simulated Time (seconds)", HighLogic.Skin.label);

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("-5s", GUILayout.Width(50)))
                SessionState.DurationSeconds = Mathf.Max(1f, SessionState.DurationSeconds - 5f);

            string text = GUILayout.TextField(
                SessionState.DurationSeconds.ToString("0.0"),
                GUILayout.Width(80));

            float parsed;
            if (float.TryParse(text, out parsed))
                SessionState.DurationSeconds = Mathf.Clamp(parsed, 1f, 3600f);

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
