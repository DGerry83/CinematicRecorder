using System;
using UnityEngine;

namespace CinematicRecorder.UI
{
    public class FinalReportWindow : MonoBehaviour
    {
        private Rect windowRect = new Rect(320, 480, 400, 240);
        private GUIStyle windowStyle;
        private bool hasInitStyles = false;
        private bool shouldShow = false;

        // Report data
        private int capturedFrames;
        private float simulatedSeconds;
        private float outputDuration;
        private float realWorldCaptureTime;
        private string encodingModeUsed;
        private string outputFilePath;
        private bool wasUnlimitedRecording; // NEW: Track if this was an unlimited recording

        public bool IsVisible => shouldShow;

        void Start()
        {
            InitStyles();
        }

        private void InitStyles()
        {
            if (hasInitStyles) return;
            windowStyle = new GUIStyle(HighLogic.Skin.window);
            hasInitStyles = true;
        }

        // NEW: Added unlimited parameter
        public void ShowReport(
            int frames,
            float simSeconds,
            float outDuration,
            float realTimeSeconds,
            string encodingMode,
            string filePath,
            bool unlimited = false)
        {
            capturedFrames = frames;
            simulatedSeconds = simSeconds;
            outputDuration = outDuration;
            realWorldCaptureTime = realTimeSeconds;
            encodingModeUsed = encodingMode;
            outputFilePath = filePath;
            wasUnlimitedRecording = unlimited;

            shouldShow = true;

            Debug.Log($"[CinematicRecorder] Final Report - Frames: {frames}, " +
                     $"SimTime: {simSeconds:F1}s, RealTime: {realTimeSeconds:F1}s, " +
                     $"Mode: {encodingMode}, Unlimited: {unlimited}, File: {filePath}");
        }

        public void HideReport()
        {
            shouldShow = false;
        }

        void OnGUI()
        {
            if (!shouldShow) return;

            windowRect = GUILayout.Window(12346, windowRect, OnWindow,
                "Recording Complete", windowStyle);
        }

        private void OnWindow(int windowId)
        {
            GUILayout.BeginVertical();

            // Summary Stats
            GUIStyle headerStyle = new GUIStyle(HighLogic.Skin.label);
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.fontSize = 14;

            GUILayout.Label("Capture Summary", headerStyle);
            GUILayout.Space(10);

            // Stats grid
            GUILayout.BeginHorizontal();
            GUILayout.Label("Frames Captured:", GUILayout.Width(130));
            GUILayout.Label(capturedFrames.ToString("N0"), GUILayout.Width(100));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Simulated Time:", GUILayout.Width(130));
            GUILayout.Label($"{simulatedSeconds:F2} sec", GUILayout.Width(100));
            GUILayout.EndHorizontal();

            // MODIFIED: Indicate if this was an unlimited recording
            GUILayout.BeginHorizontal();
            GUILayout.Label(wasUnlimitedRecording ? "Output Duration (Unlimited):" : "Output Duration:", GUILayout.Width(130));
            GUILayout.Label($"{outputDuration:F2} sec", GUILayout.Width(100));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Real Capture Time:", GUILayout.Width(130));
            GUILayout.Label($"{FormatTimeSpan(TimeSpan.FromSeconds(realWorldCaptureTime))}", GUILayout.Width(150));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Encoding Mode:", GUILayout.Width(130));
            GUILayout.Label(encodingModeUsed);
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            // File path (clickable to open folder)
            GUILayout.Label("Output File:", HighLogic.Skin.label);
            string displayPath = outputFilePath;
            if (displayPath.Length > 45)
                displayPath = "..." + displayPath.Substring(displayPath.Length - 42);
            GUI.enabled = false;
            GUILayout.TextField(displayPath, HighLogic.Skin.textField);
            GUI.enabled = true;

            GUILayout.Space(20);

            // Centered Okay button
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Okay", HighLogic.Skin.button, GUILayout.Width(100), GUILayout.Height(30)))
            {
                shouldShow = false;
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            GUI.DragWindow();
        }

        private string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 100:D1}";
        }
    }
}