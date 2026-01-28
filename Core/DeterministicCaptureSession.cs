using System.IO;
using System.Collections;
using UnityEngine;
using CinematicRecorder.Capture;

namespace CinematicRecorder.Core
{
    public static class DeterministicCaptureSession
    {
        public static bool IsRunning { get; private set; }

        // UI Fields
        public static float SimSpeedPercent { get; internal set; }
        public static float CapturedSeconds { get; internal set; }
        public static int CapturedFrames { get; internal set; }
        public static float TargetSeconds { get; internal set; }
        public static int TargetFrames { get; internal set; }

        // Rate Control
        public static int SimulationFPS { get; internal set; }
        public static int PlaybackFPS { get; internal set; }
        public static float PlaybackSpeed { get; internal set; }

        public static void Run(
           int simulationFps,
           int playbackFps,
           float durationSeconds,
           bool forceSoftwareEncoding,
           bool useGpuZeroCopy = false)  // NEW PARAMETER
        {
            if (IsRunning)
                return;

            IsRunning = true;

            // Initialize progress tracking
            TargetSeconds = durationSeconds;
            TargetFrames = Mathf.RoundToInt(durationSeconds * simulationFps);

            SimulationFPS = simulationFps;
            PlaybackFPS = playbackFps;
            PlaybackSpeed = playbackFps / (float)simulationFps;

            CapturedSeconds = 0f;
            CapturedFrames = 0;
            SimSpeedPercent = 100f;

            Camera cam = Camera.main;
            if (cam == null)
                throw new System.Exception("No camera available for capture");

            int width = Screen.width;
            int height = Screen.height;

            string outputDir = Path.Combine(
                KSPUtil.ApplicationRootPath,
                "GameData",
                "CinematicRecorder",
                "Videos");

            Directory.CreateDirectory(outputDir);

            string outputFile =
                Path.Combine(outputDir, $"Cinematic_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mkv");

            var controller = new OfflineCaptureController(
                            cam,
                            width,
                            height,
                            simulationFps,
                            playbackFps,
                            durationSeconds,
                            outputFile,
                            forceSoftwareEncoding,
                            useGpuZeroCopy);

            var runner = new GameObject("DeterministicCaptureRunner");
            UnityEngine.Object.DontDestroyOnLoad(runner);

            var captureRunner = runner.AddComponent<CaptureRunner>();

            captureRunner.StartCoroutine(RunAndCleanup(controller, runner));
        }

        private static IEnumerator RunAndCleanup(
    OfflineCaptureController controller,
    GameObject runner)
        {
            yield return controller.RunCoroutine();

            DeterministicCaptureSession.EndSession();

            UnityEngine.Object.Destroy(runner);

            Debug.Log("[DeterministicCaptureSession] Capture session fully cleaned up.");
        }

        private static System.Collections.IEnumerator RunWrapped(
            OfflineCaptureController controller,
            GameObject runner)
        {
            yield return controller.RunCoroutine();

            EndSession();

            if (runner != null)
                UnityEngine.Object.Destroy(runner);
        }

        public static void EndSession()
        {
            IsRunning = false;

            // Reset UI-facing state
            CapturedSeconds = 0f;
            CapturedFrames = 0;
            SimSpeedPercent = 100f;
            TargetSeconds = 0f;
            TargetFrames = 0;

            Debug.Log("[DeterministicCaptureSession] Capture session ended.");
        }
    }
}
