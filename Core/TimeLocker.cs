using System;
using UnityEngine;
using KSP.IO;

namespace CinematicRecorder.Core
{
    /// <summary>
    /// Manages time decoupling for cinematic recording.
    /// Forces Unity to simulate fixed timesteps regardless of real-world performance.
    /// </summary>
    public class TimeLocker : MonoBehaviour
    {
        // Configuration
        [KSPField(isPersistant = true)]
        public int targetCaptureFramerate = 60;

        [KSPField(isPersistant = true)]
        public bool lockPhysicsDeltaTime = true;

        // State
        private bool isRecording = false;
        private float originalFixedDeltaTime;
        private int originalCaptureFramerate;
        private float originalTimeScale;

        // Events
        public static event Action OnRecordingStarted;
        public static event Action OnRecordingStopped;
        public static event Action<int, float> OnFrameRendered; // frameIndex, effectiveTime

        // Internal tracking
        private int frameIndex = 0;
        private double sessionStartTime;

        // Diagnostic tracking
        private double lastGameTime;
        private float lastRealTime;
        private int diagnosticFrameCounter = 0;

        // Physics Rate
        private double originalPlanetariumDelta = 0.02;

        void Awake()
        {
            // Safety: Ensure we survive scene transitions if needed, 
            // but usually we want to stop recording on scene changes
            DontDestroyOnLoad(this.gameObject);
        }

        void Start()
        {
            // Subscribe to safety interlocks
            GameEvents.onGamePause.Add(OnGamePause);
            GameEvents.onLevelWasLoaded.Add(OnLevelWasLoaded);
            GameEvents.onGameStateLoad.Add(OnGameStateLoad);
        }

        void OnDestroy()
        {
            // Critical: Always cleanup events
            GameEvents.onGamePause.Remove(OnGamePause);
            GameEvents.onLevelWasLoaded.Remove(OnLevelWasLoaded);
            GameEvents.onGameStateLoad.Remove(OnGameStateLoad);

            // Emergency stop if still recording
            if (isRecording)
                StopRecording();
        }

        /// <summary>
        /// Starts the time-lock recording session.
        /// </summary>
        public void StartRecording()
        {
            if (isRecording)
            {
                UnityEngine.Debug.LogWarning("[TimeLocker] Already recording, ignoring start request.");
                return;
            }

            // Store original values
            originalFixedDeltaTime = Time.fixedDeltaTime;
            originalCaptureFramerate = Time.captureFramerate;
            originalTimeScale = Time.timeScale;

            // CRITICAL: Store KSP's internal physics timestep values
            if (Planetarium.fetch != null)
                originalPlanetariumDelta = Planetarium.fetch.fixedDeltaTime;
            // Note: TimeWarp.fixedDeltaTime is read-only, we patch it via Harmony

            // Force warp to 1x
            ForceWarpToNormal();

            // Calculate target timestep
            float targetDelta = 1.0f / targetCaptureFramerate;

            // Set Unity's time
            Time.captureFramerate = targetCaptureFramerate;
            Time.fixedDeltaTime = targetDelta;

            // CRITICAL: Set KSP's internal timestep values
            if (Planetarium.fetch != null)
                Planetarium.fetch.fixedDeltaTime = targetDelta; // Planetarium is assignable (double)

            // TimeWarp is read-only, use Harmony patch override
            TimeWarp_FixedDeltaTime_Patch.OverrideValue = targetDelta;
            TimeWarp_FixedDeltaTime_Patch.IsOverridden = true;

            // Init tracking
            frameIndex = 0;
            diagnosticFrameCounter = 0;
            sessionStartTime = Planetarium.GetUniversalTime();
            lastGameTime = Planetarium.GetUniversalTime();
            lastRealTime = Time.realtimeSinceStartup;
            isRecording = true;

            UnityEngine.Debug.Log($"[TimeLocker] Recording started. Target: {targetCaptureFramerate} FPS\n" +
                $"  Unity fixedDeltaTime: {Time.fixedDeltaTime:F6}\n" +
                $"  Planetarium fixedDeltaTime: {Planetarium.fetch?.fixedDeltaTime:F6}\n" +
                $"  TimeWarp fixedDeltaTime (patched): {TimeWarp_FixedDeltaTime_Patch.OverrideValue:F6}");

            OnRecordingStarted?.Invoke();
        }

        /// <summary>
        /// Stops recording and restores normal time flow.
        /// </summary>
        public void StopRecording()
        {
            if (!isRecording)
                return;

            // Restore Unity time settings
            Time.captureFramerate = originalCaptureFramerate;
            Time.fixedDeltaTime = originalFixedDeltaTime;
            Time.timeScale = originalTimeScale;

            // CRITICAL: Restore KSP's internal timestep values
            if (Planetarium.fetch != null)
                Planetarium.fetch.fixedDeltaTime = originalPlanetariumDelta;

            // Disable Harmony patch override for TimeWarp
            TimeWarp_FixedDeltaTime_Patch.IsOverridden = false;

            isRecording = false;

            double gameDuration = Planetarium.GetUniversalTime() - sessionStartTime;
            float expectedDuration = frameIndex / (float)targetCaptureFramerate;

            UnityEngine.Debug.Log($"[TimeLocker] Recording stopped. Captured {frameIndex} frames " +
                $"over {gameDuration:F2}s game time (expected: {expectedDuration:F2}s). " +
                $"Drift: {((gameDuration / expectedDuration) - 1) * 100:F1}%");

            OnRecordingStopped?.Invoke();
        }

        void Update()
        {
            if (!isRecording)
                return;

            frameIndex++;
            diagnosticFrameCounter++;

            // Log every 60 frames (1 second of video time at 60fps) to avoid spam
            if (diagnosticFrameCounter >= targetCaptureFramerate)
            {
                diagnosticFrameCounter = 0;

                double currentGameTime = Planetarium.GetUniversalTime();
                float currentRealTime = Time.realtimeSinceStartup;

                // FIX: Calculate expected time for the entire interval (e.g., 60 frames = 1.0s)
                float expectedIntervalTime = targetCaptureFramerate * (1.0f / targetCaptureFramerate); // = 1.0s

                // Calculate actual advances
                double gameTimeAdvanced = currentGameTime - lastGameTime;
                float realTimeAdvanced = currentRealTime - lastRealTime;

                UnityEngine.Debug.Log($"[TimeLocker DIAGNOSTIC] Frame {frameIndex}\n" +
                    $"  Game Time Advanced: {gameTimeAdvanced:F6}s (Expected: {expectedIntervalTime:F6}s)\n" +
                    $"  Real Time Advanced: {realTimeAdvanced:F6}s\n" +
                    $"  Ratio (Game/Real): {gameTimeAdvanced / realTimeAdvanced:F2}x\n" +
                    $"  Time.captureFramerate: {Time.captureFramerate}\n" +
                    $"  Time.fixedDeltaTime: {Time.fixedDeltaTime:F6}");

                lastGameTime = currentGameTime;
                lastRealTime = currentRealTime;
            }

            OnFrameRendered?.Invoke(frameIndex, frameIndex / (float)targetCaptureFramerate);
        }

        #region Safety Interlocks

        /// <summary>
        /// Auto-stop when game is paused (prevents frozen frames in output)
        /// </summary>
        private void OnGamePause()
        {
            if (isRecording)
            {
                UnityEngine.Debug.Log("[TimeLocker] Game paused - auto-stopping recording to prevent corruption.");
                StopRecording();
            }
        }

        /// <summary>
        /// Auto-stop on scene transition (loading screen, VAB, etc.)
        /// </summary>
        private void OnLevelWasLoaded(GameScenes scene)
        {
            if (isRecording)
            {
                UnityEngine.Debug.Log($"[TimeLocker] Scene loaded ({scene}) - auto-stopping recording.");
                StopRecording();
            }
        }

        /// <summary>
        /// Auto-stop on game state load (quickload, revert)
        /// </summary>
        private void OnGameStateLoad(ConfigNode node)
        {
            if (isRecording)
            {
                UnityEngine.Debug.Log("[TimeLocker] Game state loaded - auto-stopping recording.");
                StopRecording();
            }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Forces KSP time warp to 1x speed immediately.
        /// </summary>
        private void ForceWarpToNormal()
        {
            // Method 1: Direct TimeWarp access - index 0 is 1x speed
            if (TimeWarp.fetch != null)
            {
                TimeWarp.SetRate(0, true); // 0 = 1x speed, true = instant (no ramp)
            }

            // Method 2: Ensure time scale is normal (backup)
            Time.timeScale = 1.0f;

            // Wait a frame to ensure it takes effect before locking
            StartCoroutine(WaitForWarpStabilization());
        }

        private System.Collections.IEnumerator WaitForWarpStabilization()
        {
            yield return new WaitForFixedUpdate();
            yield return new WaitForEndOfFrame();

            // Double-check we're at 1x after the frame delay
            if (TimeWarp.fetch != null && TimeWarp.CurrentRateIndex != 0)
            {
                UnityEngine.Debug.LogWarning("[TimeLocker] Warp not at 1x after forced reset, retrying...");
                TimeWarp.SetRate(0, true);
            }
        }

        /// <summary>
        /// Gets current recording statistics for UI display.
        /// </summary>
        public RecordingStats GetStats()
        {
            return new RecordingStats
            {
                IsRecording = isRecording,
                FrameIndex = frameIndex,
                TargetFramerate = targetCaptureFramerate,
                EffectiveDuration = frameIndex / (float)targetCaptureFramerate,
                CurrentRealtimeFPS = 1.0f / Time.unscaledDeltaTime,
                PhysicsDeltaTime = Time.fixedDeltaTime
            };
        }

        #endregion

        public struct RecordingStats
        {
            public bool IsRecording;
            public int FrameIndex;
            public int TargetFramerate;
            public float EffectiveDuration;
            public float CurrentRealtimeFPS;
            public float PhysicsDeltaTime;
        }
    }
}