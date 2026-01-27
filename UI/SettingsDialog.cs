using CinematicRecorder.Capture;
using CinematicRecorder.Core;
using System;
using System.IO;
using UnityEngine;

namespace CinematicRecorder.UI
{
    public class SettingsDialog : MonoBehaviour
    {
        private Rect windowRect = new Rect(300, 60, 400, 400);
        private bool _renderDisplay = false;
        private GUIStyle windowStyle;
        private bool hasInitStyles = false;

        public event Action OnDialogDismissed;

        // Toggles and settings
        private bool forceSoftwareEncoding = false;

        // Default Settings
        private readonly int[] frameratePresets = { 24, 30, 60, 120, 240 };
        private int framerateIndex = 2;
        private string durationSecondsText = "10";

        private int simFpsIndex = 2;     // default 60
        private int playbackFpsIndex = 2;
        private bool lockFps = true;

        // Format
        private bool pngSequence = false;

        void Start()
        {
        }

        void OnDestroy()
        {
            Hide();
        }

        private void InitStyles()
        {
            if (hasInitStyles) return;
            windowStyle = new GUIStyle(HighLogic.Skin.window);
            hasInitStyles = true;
        }

        public void Show()
        {
            if (!_renderDisplay)
            {
                _renderDisplay = true;
                if (!hasInitStyles) InitStyles();
            }
        }

        public void Hide()
        {
            if (_renderDisplay)
            {
                _renderDisplay = false;
                OnDialogDismissed?.Invoke();
            }
        }

        void OnGUI()
        {
            if (!_renderDisplay) return;

            windowRect = GUILayout.Window(12345, windowRect, OnWindow, "Cinematic Recorder", windowStyle);
        }

        private void OnWindow(int windowId)
        {
            GUILayout.BeginVertical();

            // Status
            GUILayout.Label(GetStatusText(), HighLogic.Skin.label);
            GUILayout.Space(15);

            GUILayout.Label("Simulation FPS:", HighLogic.Skin.label);
            DrawFpsSelector(ref simFpsIndex);

            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            lockFps = GUILayout.Toggle(lockFps, " Lock Playback Rate", HighLogic.Skin.toggle);
            GUILayout.EndHorizontal();

            GUILayout.Space(5);

            GUILayout.Label("Playback FPS:", HighLogic.Skin.label);
            GUI.enabled = !lockFps;
            DrawFpsSelector(ref playbackFpsIndex);
            GUI.enabled = true;

            if (lockFps)
            {
                playbackFpsIndex = simFpsIndex;
            }
            float simFps = frameratePresets[simFpsIndex];
            float outFps = frameratePresets[playbackFpsIndex];
            float speed = outFps / simFps;

            GUILayout.Label(
                $"Playback Speed: {speed:0.##}×",
                HighLogic.Skin.label);

            // FORMAT
            pngSequence = GUILayout.Toggle(pngSequence, " PNG Sequence (uncheck for MKV/x264)", HighLogic.Skin.toggle);
            GUILayout.Space(20);

            // SAFE MODE TOGGLE
            forceSoftwareEncoding = GUILayout.Toggle(forceSoftwareEncoding, " Force Software Encoding (Safe Mode)", HighLogic.Skin.toggle);
            GUILayout.Space(10);

            // DURATION
            GUILayout.Label("Duration (seconds):", HighLogic.Skin.label);

            // Draw text box
            durationSecondsText = GUILayout.TextField(
                durationSecondsText,
                HighLogic.Skin.textField,
                GUILayout.Width(100)
            );

            // Sanitize AFTER user input
            durationSecondsText = SanitizeDurationInput(durationSecondsText);

            GUILayout.Space(10);

            // Preview
            float.TryParse(durationSecondsText, out float previewSeconds);
            int previewFrames = Mathf.RoundToInt(previewSeconds * frameratePresets[framerateIndex]);

            GUILayout.Label(
                $"Total Frames: {previewFrames}",
                HighLogic.Skin.label);

            // RECORD BUTTON
            if (GUILayout.Button(GetRecordButtonText(), HighLogic.Skin.button))
            {
                OnRecordButtonClick();
            }

            GUILayout.EndVertical();

            GUI.DragWindow();
        }

        private void DrawFpsSelector(ref int index)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(30)) && index > 0)
                index--;
            GUILayout.Label($"{frameratePresets[index]} FPS", GUILayout.Width(100));
            if (GUILayout.Button(">", GUILayout.Width(30)) && index < frameratePresets.Length - 1)
                index++;
            GUILayout.EndHorizontal();
        }

        private string SanitizeDurationInput(string input)
        {
            bool hasDot = false;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            foreach (char c in input)
            {
                if (char.IsDigit(c))
                {
                    sb.Append(c);
                }
                else if (c == '.' && !hasDot)
                {
                    sb.Append(c);
                    hasDot = true;
                }
            }

            return sb.ToString();
        }

        private string GetStatusText()
        {
            if (DeterministicCaptureSession.IsRunning)
            {
                return
                    $"Recording (Offline Deterministic)\n" +
                    $"Time: {DeterministicCaptureSession.CapturedSeconds:F1}s / {DeterministicCaptureSession.TargetSeconds:F1}s\n" +
                    $"Frames: {DeterministicCaptureSession.CapturedFrames} / {DeterministicCaptureSession.TargetFrames}\n" +
                    $"Sim Speed: {DeterministicCaptureSession.SimSpeedPercent:F0}%";
            }

            int screenWidth = Screen.width;
            int screenHeight = Screen.height;

            string mode = pngSequence
                ? "PNG"
                : (forceSoftwareEncoding ? "MKV (CPU Safe Mode)" : "MKV");

            return $"Ready - {screenWidth}x{screenHeight}, {frameratePresets[framerateIndex]} FPS, {mode}";
        }

        private string GetRecordButtonText()
        {
            return DeterministicCaptureSession.IsRunning
                ? "Recording…"
                : "Start Recording";
        }

        private void OnRecordButtonClick()
        {
            int simFps = frameratePresets[simFpsIndex];
            int playbackFps = frameratePresets[playbackFpsIndex];

            if (!float.TryParse(durationSecondsText, out float durationSeconds))
            {
                Debug.LogWarning("[CinematicRecorder] Invalid duration entered, defaulting to 10 seconds.");
                durationSeconds = 10f;
            }

            DeterministicCaptureSession.Run(
                simFps,
                playbackFps,
                durationSeconds,
                forceSoftwareEncoding);
        }
    }
}