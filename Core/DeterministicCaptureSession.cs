using CinematicRecorder.Audio;
using CinematicRecorder.Capture;
using CinematicRecorder.Integration;
using CinematicRecorder.UI;
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace CinematicRecorder.Core
{
    public static class DeterministicCaptureSession
    {
        #region Fields
        private static string _ffmpegPath;
        #endregion
        #region Session State
        private static volatile bool _isRunning;
        public static bool IsRunning
        {
            get => _isRunning;
            private set => _isRunning = value;
        }
        private static volatile bool _stopRequested;
        public static bool StopRequested
        {
            get => _stopRequested;
            private set => _stopRequested = value;
        }
        private static volatile bool _isUnlimitedMode;
        public static bool IsUnlimitedMode
        {
            get => _isUnlimitedMode;
            private set => _isUnlimitedMode = value;
        }
        /// <summary>Fired every physics step during deterministic capture. Parameter = physics delta time for this step.</summary>
        public static event Action<float> OnPhysicsStepped;

        /// <summary>Active deterministic zoom controller during capture. Null when not running.</summary>
        public static DeterministicZoomController ActiveZoomController { get; private set; }
        #endregion
        #region Progress Tracking  
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

                if (IsUnlimitedMode) return CapturedFrames;

                // Dynamic: current capture + (remaining time at current sim rate)
                float remainingSeconds = Mathf.Max(0f, TargetSeconds - AccumulatedSimulatedSeconds);
                float currentSimFps = GetCurrentSimulationFps();
                return CapturedFrames + Mathf.RoundToInt(remainingSeconds * currentSimFps);
            }
            internal set { _targetFramesBacking = value; } // Backing field for non-running state
        }
        #endregion
        #region Rate Control
        public static int SimulationFPS { get; internal set; }
        public static int PlaybackFPS { get; internal set; }
        public static float PlaybackSpeed { get; internal set; }
        #endregion
        #region Time Scale Control
        /// <summary>Current time scale (1.0 = normal, 0.1 = 10% speed)</summary>
        public static float CurrentTimeScale { get; private set; } = 1.0f;

        /// <summary>Target time scale during transitions</summary>
        public static float TargetTimeScale { get; private set; } = 1.0f;

        /// <summary>True when ramping between time scales</summary>
        public static bool IsTransitioning { get; private set; } = false;
        public enum TransitionDirection { None, Slowing, Resuming }
        /// <summary>Direction of current transition for UI feedback</summary>
        public static TransitionDirection CurrentTransitionDirection { get; private set; } = TransitionDirection.None;

        /// <summary>Original simulation FPS set at start of recording</summary>
        public static int OriginalSimulationFps { get; private set; } = 60;

        /// <summary>Accumulated simulated seconds (replaces frame count for progress)</summary>
        public static float AccumulatedSimulatedSeconds { get; internal set; } = 0f;

        public const float KRAKEN_TIME_FPS = 10000f;
        public const float KRAKEN_TIME_THRESHOLD = 0.015f;
        public const float SUPER_SLOW_SCALE = 0.1f;
        public const float SLOW_SCALE = 0.35f;
        public const float KRAKEN_TIME_SCALE = 0.01f;

        private static float rampStartScale;
        private static float rampDuration;
        private static float rampElapsed;
        #endregion
        #region Events
        /// <summary>Fired when recording begins</summary>
        public static event Action OnRecordingStarted;

        /// <summary>Fired when recording ends (before cleanup)</summary>
        public static event Action OnRecordingStopped;

        /// <summary>Fired whenever time scale changes (parameter = new scale value)</summary>
        public static event Action<float> OnTimeScaleChanged;
        private static Stopwatch realWorldTimer;
        #endregion
        #region Public API
        public static void InvokeOnPhysicsStepped(float physicsDeltaTime)
        {
            OnPhysicsStepped?.Invoke(physicsDeltaTime);

            // Drive CameraTools deterministic camera updates if available
            // CameraTools uses the physicsDeltaTime or playbackDeltaTime based on LockPathingToPlaybackRate setting
            if (CameraToolsAPIManager.IsAvailable)
            {
                float playbackDt = 1.0f / PlaybackFPS;
                CameraToolsAPIManager.PhysicsStepUpdate(physicsDeltaTime, playbackDt);
            }
        }
        /// <summary>
        /// Begins deterministic capture with specified simulation and playback parameters.
        /// Creates capture runner GameObject and initializes zoom controller.
        /// </summary>
        public static void Run(
            int simulationFps,
            int playbackFps,
            float durationSeconds,
            bool forceSoftwareEncoding,
            bool useGpuZeroCopy = false)
        {
            if (IsRunning)
                return;

            _ffmpegPath = Path.Combine(Path.GetDirectoryName(typeof(DeterministicCaptureSession).Assembly.Location),"..", "PluginData", "FFMpeg", "ffmpeg.exe");

            if (!File.Exists(_ffmpegPath))
            {
                _ffmpegPath = null; // Will check in UI
            }

            IsRunning = true;
            StopRequested = false;

            // Determine unlimited mode and set targets accordingly
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

            // Initialize Time Scale State
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

            string baseName = $"Cinematic_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
            string outputPath;
            string audioPath = null;

            if (SessionState.PngSequence)
            {
                // PNG Sequence: Create a directory named after what would have been the video file
                outputPath = Path.Combine(outputDir, baseName);
                Directory.CreateDirectory(outputPath);
                UnityEngine.Debug.Log($"[DeterministicCaptureSession] PNG Sequence mode - output directory: {outputPath}");
            }
            else
            {
                // Video mode: Standard MKV file path
                outputPath = Path.Combine(outputDir, $"{baseName}.mkv");
                if (SessionState.EnableAudioCapture)
                    audioPath = Path.ChangeExtension(outputPath, ".wav");
            }

            // Force software encoding and disable zero-copy for PNG mode 
            // (hardware encoders can't output PNGs, and we need the CPU readback pathway)
            bool effectiveForceSoftware = forceSoftwareEncoding || SessionState.PngSequence;
            bool effectiveZeroCopy = useGpuZeroCopy && !SessionState.PngSequence;

            AudioCaptureController audioController = null;
            if (SessionState.EnableAudioCapture && !string.IsNullOrEmpty(audioPath))
            {
                // Audio capture only works reliably at 30fps or lower
                if (simulationFps > 30)
                {
                    UnityEngine.Debug.LogWarning($"[DeterministicCaptureSession] Audio capture disabled: capture rate {simulationFps}fps exceeds 30fps limit");
                    ScreenMessages.PostScreenMessage(CinematicUIStrings.Settings.AudioDisabledScreenMsg, 5f, ScreenMessageStyle.UPPER_CENTER);
                }
                else
                {
                    audioController = new AudioCaptureController(audioPath, playbackFps);
                    UnityEngine.Debug.Log($"[DeterministicCaptureSession] Audio capture enabled: {audioPath}");
                }
            }

            var controller = new OfflineCaptureController(
                cam,
                width,
                height,
                simulationFps,
                playbackFps,
                durationSeconds,
                outputPath,
                forceSoftwareEncoding,
                useGpuZeroCopy,
                audioController);

            var runner = new GameObject("DeterministicCaptureRunner");
            UnityEngine.Object.DontDestroyOnLoad(runner);

            var captureRunner = runner.AddComponent<CaptureRunner>();

            ActiveZoomController = runner.AddComponent<DeterministicZoomController>();

            TakeControlOfActivePathingCamera(playbackFps);
            OnRecordingStarted?.Invoke();

            captureRunner.StartCoroutine(RunAndCleanup(controller, runner));
        }
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
        /// <summary>
        /// Increases recording duration mid-capture. Silently ignored in unlimited mode.
        /// </summary>
        public static void ExtendDuration(float additionalSeconds)
        {
            if (!IsRunning)
                return;

            // Silently ignore extension requests in unlimited mode
            if (IsUnlimitedMode)
                return;

            TargetSeconds += additionalSeconds;
            TargetFrames = Mathf.RoundToInt(TargetSeconds * SimulationFPS);

            UnityEngine.Debug.Log(
                $"[DeterministicCaptureSession] Duration extended by {additionalSeconds}s → {TargetSeconds}s");
        }
        /// <summary>
        /// Signals the capture loop to finish after current frame. Idempotent.
        /// </summary>
        public static void RequestStop()
        {
            if (!IsRunning || StopRequested)
                return;

            StopRequested = true;
            UnityEngine.Debug.Log("[DeterministicCaptureSession] Stop requested");
        }
        public static void EndSession()
        {
            IsRunning = false;
            StopRequested = false;
            IsUnlimitedMode = false; // Reset unlimited flag

            CapturedSeconds = 0f;
            CapturedFrames = 0;
            CaptureFPS = 0f;
            TargetSeconds = 0f;
            TargetFrames = 0;

            realWorldTimer?.Stop();
            realWorldTimer = null;

            ActiveZoomController = null;

            // Reset time scale state
            CurrentTimeScale = 1.0f;
            TargetTimeScale = 1.0f;
            IsTransitioning = false;
            CurrentTransitionDirection = TransitionDirection.None;
            AccumulatedSimulatedSeconds = 0f;
        }
        public static void UpdateProgress(int frames, float seconds, float fps)
        {
            CapturedFrames = frames;
            CapturedSeconds = seconds;
            CaptureFPS = fps;
        }
        #endregion
        #region Internal Implementation
        /// <summary>
        /// If a CameraTools pathing camera is already active when recording starts,
        /// enable deterministic control and capture current path progress without jumping.
        /// </summary>
        private static void TakeControlOfActivePathingCamera(int playbackFps)
        {
            if (!CameraToolsAPIManager.IsAvailable)
                return;

            // Check if CT is active and in pathing mode
            if (!CameraToolsAPIManager.IsCameraActive())
                return;

            var currentMode = CameraToolsAPIManager.GetToolMode();
            if (currentMode != ToolModes.Pathing)
                return;

            // Get current state - this populates internal _lastState for helper methods
            var state = CameraToolsAPIManager.GetCurrentState();
            if (state == null)
                return;

            // Get state values via API helper methods (state is object, not typed)
            bool isPlayingPath = CameraToolsAPIManager.GetIsPlayingPathFromState();
            float currentPathTime = CameraToolsAPIManager.GetCurrentPathTime();

            UnityEngine.Debug.Log($"[DeterministicCaptureSession] Taking control of active pathing camera. " +
                $"IsPlaying: {isPlayingPath}, CurrentTime: {currentPathTime}s");

            // Configure timing mode based on slot settings
            bool usePlaybackTiming = false;
            var activeSlot = CinematicCameraManager.Instance.ActiveSlot;
            if (activeSlot?.isCameraToolsSlot == true && activeSlot.ctSettings != null)
            {
                usePlaybackTiming = activeSlot.ctSettings.LockPathingToPlaybackRate;
            }

            CameraToolsAPIManager.SetLockPathingToPlaybackRate(usePlaybackTiming);

            //  Enable deterministic control - captures current elapsed time if playing
            CameraToolsAPIManager.SetCinematicRecorderControl(enabled: true, deterministicMode: true);

            UnityEngine.Debug.Log("[DeterministicCaptureSession] Deterministic control enabled for pathing camera");

            // Only start playback if not already playing
            if (!isPlayingPath)
            {
                CameraToolsAPIManager.StartPathPlayback();
            }
        }
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
        private static IEnumerator RunAndCleanup(
            OfflineCaptureController controller,
            GameObject runner)
        {
            yield return controller.RunCoroutine();

            // Capture final stats BEFORE reset
            int finalFrames = CapturedFrames;
            float finalSimSeconds = AccumulatedSimulatedSeconds; // Use accumulated instead of CapturedSeconds
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
            string audioPath = controller.AudioController?.OutputPath;

            // Show report
            ShowFinalReport(
                finalFrames,
                finalSimSeconds,
                outputDuration,
                finalRealSeconds,
                encodingMode,
                outputPath,
                audioPath,
                IsUnlimitedMode,
                _ffmpegPath); // Pass unlimited flag

            // Fire stopped event before cleanup
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
            string audioPath,
            bool wasUnlimited,
            string ffmpegPath) // Parameter to indicate unlimited recording
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
                audioPath,
                wasUnlimited,
                ffmpegPath); // Pass flag
        }
        #endregion
    }
}