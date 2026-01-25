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

        // Framerate presets
        private readonly int[] frameratePresets = { 24, 30, 60, 120, 240 };
        private int framerateIndex = 2;

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

            // FRAMERATE
            GUILayout.Label("Target Framerate:", HighLogic.Skin.label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", HighLogic.Skin.button, GUILayout.Width(30)) && framerateIndex > 0)
            {
                framerateIndex--;
            }
            GUILayout.Label($"{frameratePresets[framerateIndex]} FPS", HighLogic.Skin.label);
            if (GUILayout.Button(">", HighLogic.Skin.button, GUILayout.Width(30)) && framerateIndex < frameratePresets.Length - 1)
            {
                framerateIndex++;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            // FORMAT
            pngSequence = GUILayout.Toggle(pngSequence, " PNG Sequence (uncheck for MKV/x264)", HighLogic.Skin.toggle);
            GUILayout.Space(20);

            // SAFE MODE TOGGLE
            forceSoftwareEncoding = GUILayout.Toggle(forceSoftwareEncoding, " Force Software Encoding (Safe Mode)", HighLogic.Skin.toggle);
            GUILayout.Space(10);

            // RECORD BUTTON
            if (GUILayout.Button(GetRecordButtonText(), HighLogic.Skin.button))
            {
                OnRecordButtonClick();
            }

            GUILayout.EndVertical();

            GUI.DragWindow();
        }

        private string GetStatusText()
        {
            if (CinematicRecorderAddon.TimeLockerInstance == null)
                return "System Offline";

            var stats = CinematicRecorderAddon.TimeLockerInstance.GetStats();

            if (stats.IsRecording)
            {
                if (CinematicRecorderAddon.FrameCaptureInstance != null &&
                    CinematicRecorderAddon.FrameCaptureInstance.IsRecording)
                {
                    string encoderType = CinematicRecorderAddon.FrameCaptureInstance.ActiveEncoderType.ToString();
                    return $"Recording ({encoderType}) - Frame: {stats.FrameIndex}, Duration: {stats.EffectiveDuration:F1}s";
                }
                return $"Recording - Frame: {stats.FrameIndex}, Duration: {stats.EffectiveDuration:F1}s";
            }
            else
            {
                // Get screen resolution for status display
                int screenWidth = Screen.width;
                int screenHeight = Screen.height;

                string mode = pngSequence ? "PNG" : (forceSoftwareEncoding ? "MKV (CPU Safe Mode)" : "MKV");
                return $"Ready - {screenWidth}x{screenHeight}, {frameratePresets[framerateIndex]} FPS, {mode}";
            }
        }

        private string GetRecordButtonText()
        {
            if (CinematicRecorderAddon.TimeLockerInstance?.GetStats().IsRecording ?? false)
                return "Stop Recording";
            return "Start Recording";
        }

        private void OnRecordButtonClick()
        {
            if (CinematicRecorderAddon.TimeLockerInstance == null || CinematicRecorderAddon.FrameCaptureInstance == null)
                return;

            if (CinematicRecorderAddon.TimeLockerInstance.GetStats().IsRecording)
            {
                // Stop recording
                CinematicRecorderAddon.TimeLockerInstance.StopRecording();
                CinematicRecorderAddon.FrameCaptureInstance.StopRecording();
            }
            else
            {
                // Start recording
                int fps = frameratePresets[framerateIndex];

                // Determine output path based on format
                string baseDir = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "CinematicRecorder", "Videos");
                string outputDir;

                if (pngSequence)
                {
                    // PNG: Create subfolder with timestamp (frames aren't timestamped)
                    string sessionFolder = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    outputDir = Path.Combine(baseDir, sessionFolder);
                }
                else
                {
                    // MKV: Filename has timestamp, no subfolder needed
                    outputDir = baseDir;
                }

                // SET THE SAFE MODE FLAG
                CinematicRecorderAddon.FrameCaptureInstance.ForceSoftwareEncoding = forceSoftwareEncoding;

                // Configure and start capture - NO RESOLUTION PARAMETERS
                CinematicRecorderAddon.TimeLockerInstance.targetCaptureFramerate = fps;
                CinematicRecorderAddon.FrameCaptureInstance.Initialize(fps, outputDir, pngSequence);
                CinematicRecorderAddon.FrameCaptureInstance.StartRecording();
                CinematicRecorderAddon.TimeLockerInstance.StartRecording();
            }
        }
    }
}