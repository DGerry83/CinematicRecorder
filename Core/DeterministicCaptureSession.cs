// File: Core/DeterministicCaptureSession.cs
using System;
using System.IO;
using System.Collections;
using System.Diagnostics;
using UnityEngine;
using CinematicRecorder.Capture;
using CinematicRecorder.UI;

namespace CinematicRecorder.Core
{
    public static class DeterministicCaptureSession
    {
        public static bool IsRunning { get; private set; }
        public static bool StopRequested { get; private set; }

        // UI Fields
        public static float CaptureFPS { get; internal set; }
        public static float CapturedSeconds { get; internal set; }
        public static int CapturedFrames { get; internal set; }
        public static float TargetSeconds { get; internal set; }
        public static int TargetFrames { get; internal set; }

        // Rate Control
        public static int SimulationFPS { get; internal set; }
        public static int PlaybackFPS { get; internal set; }
        public static float PlaybackSpeed { get; internal set; }

        // Internal state
        private static Stopwatch realWorldTimer;

        public static void Run(
            int simulationFps,
            int playbackFps,
            float durationSeconds,
            bool forceSoftwareEncoding,
            bool useGpuZeroCopy = false)
        {
            if (IsRunning)
                return;

            IsRunning = true;
            StopRequested = false;

            TargetSeconds = durationSeconds;
            TargetFrames = Mathf.RoundToInt(durationSeconds * simulationFps);

            SimulationFPS = simulationFps;
            PlaybackFPS = playbackFps;
            PlaybackSpeed = playbackFps / (float)simulationFps;

            CapturedSeconds = 0f;
            CapturedFrames = 0;
            CaptureFPS = 0f;

            realWorldTimer = new Stopwatch();
            realWorldTimer.Start();

            Camera cam = Camera.main;
            if (cam == null)
                throw new Exception("No camera available for capture");

            int width = Screen.width;
            int height = Screen.height;

            string outputDir = Path.Combine(
                KSPUtil.ApplicationRootPath,
                "GameData",
                "CinematicRecorder",
                "Videos");

            Directory.CreateDirectory(outputDir);

            string outputPath = Path.Combine(
                outputDir,
                $"Cinematic_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mkv");

            var controller = new OfflineCaptureController(
                cam,
                width,
                height,
                simulationFps,
                playbackFps,
                durationSeconds,
                outputPath,
                forceSoftwareEncoding,
                useGpuZeroCopy);

            var runner = new GameObject("DeterministicCaptureRunner");
            UnityEngine.Object.DontDestroyOnLoad(runner);

            var captureRunner = runner.AddComponent<CaptureRunner>();
            captureRunner.StartCoroutine(RunAndCleanup(controller, runner));
        }

        public static void ExtendDuration(float additionalSeconds)
        {
            if (!IsRunning)
                return;

            TargetSeconds += additionalSeconds;
            TargetFrames = Mathf.RoundToInt(TargetSeconds * SimulationFPS);

            UnityEngine.Debug.Log(
                $"[DeterministicCaptureSession] Duration extended by {additionalSeconds}s → {TargetSeconds}s");
        }

        public static void RequestStop()
        {
            if (!IsRunning || StopRequested)
                return;

            StopRequested = true;
            UnityEngine.Debug.Log("[DeterministicCaptureSession] Stop requested");
        }

        private static IEnumerator RunAndCleanup(
            OfflineCaptureController controller,
            GameObject runner)
        {
            yield return controller.RunCoroutine();

            // Capture final stats BEFORE reset
            int finalFrames = CapturedFrames;
            float finalSimSeconds = CapturedSeconds;
            float finalRealSeconds = (float)realWorldTimer.Elapsed.TotalSeconds;

            // Output duration is based on playback FPS
            float outputDuration = finalFrames / (float)PlaybackFPS;

            // Encoding mode string (simple + honest)
            string encodingMode =
                SessionState.SelectedEncoderTab == 0 ? "AMF (AMD HEVC)" :
                SessionState.SelectedEncoderTab == 1 ? "NVENC (NVIDIA HEVC)" :
                "CPU (x264)";

            // Pull output path from controller
            string outputPath = controller.OutputPath;

            // Show report
            ShowFinalReport(
                finalFrames,
                finalSimSeconds,
                outputDuration,
                finalRealSeconds,
                encodingMode,
                outputPath);

            EndSession();

            UnityEngine.Object.Destroy(runner);
            UnityEngine.Debug.Log("[DeterministicCaptureSession] Capture completed");
        }

        private static void ShowFinalReport(
    int frames,
    float simulatedSeconds,
    float outputDuration,
    float realWorldSeconds,
    string encodingMode,
    string outputPath)
        {
            FinalReportWindow report = UnityEngine.Object.FindObjectOfType<FinalReportWindow>();

            if (report == null)
            {
                GameObject go = new GameObject("FinalReportWindow");
                UnityEngine.Object.DontDestroyOnLoad(go);
                report = go.AddComponent<FinalReportWindow>();
            }

            report.ShowReport(
                frames,
                simulatedSeconds,
                outputDuration,
                realWorldSeconds,
                encodingMode,
                outputPath);
        }

        public static void EndSession()
        {
            IsRunning = false;
            StopRequested = false;

            CapturedSeconds = 0f;
            CapturedFrames = 0;
            CaptureFPS = 0f;
            TargetSeconds = 0f;
            TargetFrames = 0;

            realWorldTimer?.Stop();
            realWorldTimer = null;
        }

        // Called by controller
        public static void UpdateProgress(int frames, float seconds, float fps)
        {
            CapturedFrames = frames;
            CapturedSeconds = seconds;
            CaptureFPS = fps;
        }
    }
}
