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

        // NEW: Unlimited mode flag
        public static bool IsUnlimitedMode { get; private set; }

        // UI Fields
        public static float CaptureFPS { get; internal set; }
        public static float CapturedSeconds { get; internal set; }
        public static int CapturedFrames { get; internal set; }
        public static float TargetSeconds { get; internal set; }
        private static int _targetFramesBacking; // Stores pre-calc value when not running
        public static int TargetFrames
        {
            get
            {
                if (!IsRunning) return _targetFramesBacking;

                // NEW: If unlimited, just return captured frames (no target)
                if (IsUnlimitedMode) return CapturedFrames;

                // Dynamic: current capture + (remaining time at current sim rate)
                float remainingSeconds = Mathf.Max(0f, TargetSeconds - AccumulatedSimulatedSeconds);
                float currentSimFps = GetCurrentSimulationFps();
                return CapturedFrames + Mathf.RoundToInt(remainingSeconds * currentSimFps);
            }
            internal set { _targetFramesBacking = value; } // Backing field for non-running state
        }

        // Rate Control
        public static int SimulationFPS { get; internal set; }
        public static int PlaybackFPS { get; internal set; }
        public static float PlaybackSpeed { get; internal set; }

        // ============================================================
        // NEW: Time Scale Management (Kraken Time API)
        // ============================================================

        public enum TransitionDirection { None, Slowing, Resuming }

        /// <summary>Current time scale (1.0 = normal, 0.1 = 10% speed)</summary>
        public static float CurrentTimeScale { get; private set; } = 1.0f;

        /// <summary>Target time scale during transitions</summary>
        public static float TargetTimeScale { get; private set; } = 1.0f;

        /// <summary>True when ramping between time scales</summary>
        public static bool IsTransitioning { get; private set; } = false;

        /// <summary>Direction of current transition for UI feedback</summary>
        public static TransitionDirection CurrentTransitionDirection { get; private set; } = TransitionDirection.None;

        /// <summary>Original simulation FPS set at start of recording</summary>
        public static int OriginalSimulationFps { get; private set; } = 60;

        /// <summary>Accumulated simulated seconds (replaces frame count for progress)</summary>
        public static float AccumulatedSimulatedSeconds { get; internal set; } = 0f;

        // Time Scale Constants
        public const float KRAKEN_TIME_FPS = 10000f;
        public const float KRAKEN_TIME_THRESHOLD = 0.015f;
        public const float SUPER_SLOW_SCALE = 0.1f;
        public const float SLOW_SCALE = 0.35f;
        public const float KRAKEN_TIME_SCALE = 0.01f;

        // Ramp state
        private static float rampStartScale;
        private static float rampDuration;
        private static float rampElapsed;

        // ============================================================
        // NEW: Public Events for UI Subscription
        // ============================================================

        /// <summary>Fired when recording begins</summary>
        public static event Action OnRecordingStarted;

        /// <summary>Fired when recording ends (before cleanup)</summary>
        public static event Action OnRecordingStopped;

        /// <summary>Fired whenever time scale changes (parameter = new scale value)</summary>
        public static event Action<float> OnTimeScaleChanged;

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

            // NEW: Determine unlimited mode and set targets accordingly
            IsUnlimitedMode = durationSeconds <= 0;
            if (IsUnlimitedMode)
            {
                TargetSeconds = 0f;
                TargetFrames = 0;
            }
            else
            {
                TargetSeconds = durationSeconds;
                TargetFrames = Mathf.RoundToInt(durationSeconds * simulationFps);
            }

            SimulationFPS = simulationFps;
            PlaybackFPS = playbackFps;
            PlaybackSpeed = playbackFps / (float)simulationFps;

            CapturedSeconds = 0f;
            CapturedFrames = 0;
            CaptureFPS = 0f;

            // NEW: Initialize Time Scale State
            OriginalSimulationFps = simulationFps;
            CurrentTimeScale = 1.0f;
            TargetTimeScale = 1.0f;
            IsTransitioning = false;
            CurrentTransitionDirection = TransitionDirection.None;
            AccumulatedSimulatedSeconds = 0f;
            rampElapsed = 0f;

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

            // NEW: Fire recording started event
            OnRecordingStarted?.Invoke();

            captureRunner.StartCoroutine(RunAndCleanup(controller, runner));
        }

        // ============================================================
        // NEW: Public API Methods for Mod Access
        // ============================================================

        /// <summary>Request 10,000 FPS Kraken time (1% speed)</summary>
        public static void RequestKrakenTime() =>
            SetTargetTimeScale(KRAKEN_TIME_SCALE);

        /// <summary>Request 10% playback speed</summary>
        public static void RequestSuperSlow() =>
            SetTargetTimeScale(SUPER_SLOW_SCALE);

        /// <summary>Request 35% playback speed</summary>
        public static void RequestSlow() =>
            SetTargetTimeScale(SLOW_SCALE);

        /// <summary>Request normal 100% speed</summary>
        public static void RequestNormalSpeed() =>
            SetTargetTimeScale(1.0f);

        /// <summary>Set custom target scale with ramp duration</summary>
        public static void SetTargetTimeScale(float targetScale, float? customDuration = null)
        {
            TargetTimeScale = Mathf.Clamp(targetScale, 0.0001f, 1.0f);
            rampDuration = customDuration ?? SessionState.RampDurationDefault;
            rampDuration = Mathf.Max(0.01f, rampDuration);

            rampStartScale = CurrentTimeScale;
            rampElapsed = 0f;

            if (Mathf.Abs(TargetTimeScale - CurrentTimeScale) > 0.001f)
            {
                IsTransitioning = true;
                CurrentTransitionDirection = TargetTimeScale < CurrentTimeScale ?
                    TransitionDirection.Slowing : TransitionDirection.Resuming;
            }
        }

        // ============================================================
        // NEW: Internal Time Scale Update Logic
        // ============================================================

        /// <summary>Call each physics frame to interpolate time scale</summary>
        internal static void UpdateTimeScale()
        {
            if (!IsTransitioning) return;

            rampElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(rampElapsed / rampDuration);

            // Map aggressive UI range (0.3-4.0) to gentler actual range (0.8-2.0)
            // This gives control without being too extreme
            float uiExponent = SessionState.RampExponent;
            float range = SessionState.RampExponentMax - SessionState.RampExponentMin;
            float actualExponent = Mathf.Lerp(0.8f, 2.0f, (uiExponent - SessionState.RampExponentMin) / range);

            if (CurrentTransitionDirection == TransitionDirection.Slowing)
            {
                // Power curve but softer: spend moderate time at normal speed
                t = Mathf.Pow(t, actualExponent);
            }
            else // Resuming
            {
                // Mirror: invert the exponent so we "rewind" through the curve
                float resumeExp = 1.0f / actualExponent;
                t = Mathf.Pow(t, resumeExp);
            }

            CurrentTimeScale = Mathf.Lerp(rampStartScale, TargetTimeScale, t);

            if (Mathf.Abs(CurrentTimeScale - TargetTimeScale) < 0.0005f || t >= 1.0f)
            {
                CurrentTimeScale = TargetTimeScale;
                IsTransitioning = false;
                CurrentTransitionDirection = TransitionDirection.None;
            }

            OnTimeScaleChanged?.Invoke(CurrentTimeScale);
        }

        /// <summary>Calculate simulation FPS based on current time scale</summary>
        internal static float GetCurrentSimulationFps()
        {
            if (CurrentTimeScale <= KRAKEN_TIME_THRESHOLD)
                return KRAKEN_TIME_FPS;

            // Use original requested FPS as the base, not PlaybackFPS
            return OriginalSimulationFps / CurrentTimeScale;
        }

        public static void ExtendDuration(float additionalSeconds)
        {
            if (!IsRunning)
                return;

            // NEW: Silently ignore extension requests in unlimited mode
            if (IsUnlimitedMode)
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
            float finalSimSeconds = AccumulatedSimulatedSeconds; // MODIFIED: Use accumulated instead of CapturedSeconds
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
                outputPath,
                IsUnlimitedMode); // NEW: Pass unlimited flag

            // NEW: Fire stopped event before cleanup
            OnRecordingStopped?.Invoke();

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
            string outputPath,
            bool wasUnlimited) // NEW: Parameter to indicate unlimited recording
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
                outputPath,
                wasUnlimited); // NEW: Pass flag
        }

        public static void EndSession()
        {
            IsRunning = false;
            StopRequested = false;
            IsUnlimitedMode = false; // NEW: Reset unlimited flag

            CapturedSeconds = 0f;
            CapturedFrames = 0;
            CaptureFPS = 0f;
            TargetSeconds = 0f;
            TargetFrames = 0;

            realWorldTimer?.Stop();
            realWorldTimer = null;

            // NEW: Reset time scale state
            CurrentTimeScale = 1.0f;
            TargetTimeScale = 1.0f;
            IsTransitioning = false;
            CurrentTransitionDirection = TransitionDirection.None;
            AccumulatedSimulatedSeconds = 0f;
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