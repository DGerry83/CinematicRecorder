using CinematicRecorder.UI;
using KSP;
using System;
using UnityEngine;
using static CinematicRecorder.UI.CinematicUIStrings;

namespace CinematicRecorder.Core
{
    /// <summary>
    /// Monitors for critical game state changes during recording and forces time scale reset
    /// to prevent KSP from remaining in "slow-mo" if a crash or scene change occurs.
    /// </summary>
    public class SafetyMonitor : MonoBehaviour
    {
        #region Stored Time Values
        private float originalFixedDeltaTime;
        private float originalMaximumDeltaTime;
        private int originalCaptureFramerate;
        private double originalPlanetariumFixedDeltaTime;
        private bool hasStoredValues = false;
        #endregion
        #region Unity Lifecycle
        void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }
        void OnEnable()
        {
            DeterministicCaptureSession.OnRecordingStarted += OnRecordingStarted;
            DeterministicCaptureSession.OnRecordingStopped += OnRecordingStopped;
            GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequested);

            if (DeterministicCaptureSession.IsRunning && !hasStoredValues)
            {
                StoreOriginalValues();
            }
        }
        void OnDisable()
        {
            DeterministicCaptureSession.OnRecordingStarted -= OnRecordingStarted;
            DeterministicCaptureSession.OnRecordingStopped -= OnRecordingStopped;

            GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequested);
        }
        #endregion
        #region Event Handlers
        private void OnRecordingStarted()
        {
            StoreOriginalValues();
        }
        private void OnRecordingStopped()
        {
            hasStoredValues = false;
        }
        private void OnGameSceneLoadRequested(GameScenes scene)
        {
            // Trigger on any scene change: revert, editor, main menu, tracking station, etc.
            CheckAndForceReset("scene change to " + scene);
        }
        #endregion
        #region Value Storage
        private void StoreOriginalValues()
        {
            originalFixedDeltaTime = Time.fixedDeltaTime;
            originalMaximumDeltaTime = Time.maximumDeltaTime;
            originalCaptureFramerate = Time.captureFramerate;

            if (Planetarium.fetch != null)
            {
                originalPlanetariumFixedDeltaTime = Planetarium.fetch.fixedDeltaTime;
            }

            hasStoredValues = true;
        }
        #endregion
        #region Safety Logic
        private void CheckAndForceReset(string reason)
        {
            if (DeterministicCaptureSession.IsRunning)
            {
                UnityEngine.Debug.LogWarning(
                    string.Format("[CinematicRecorder.SafetyMonitor] Recording detected during {0}! Executing emergency reset.", reason));
                ForceEmergencyReset();
            }
        }

        /// <summary>
        /// Forces immediate restoration of time settings and terminates recording session.
        /// Called when scene changes are detected during recording.
        /// </summary>
        private void ForceEmergencyReset()
        {
            // Restore Unity time settings
            if (hasStoredValues)
            {
                Time.fixedDeltaTime = originalFixedDeltaTime;
                Time.maximumDeltaTime = originalMaximumDeltaTime;
                Time.captureFramerate = originalCaptureFramerate;

                if (Planetarium.fetch != null)
                {
                    Planetarium.fetch.fixedDeltaTime = originalPlanetariumFixedDeltaTime;
                }

                UnityEngine.Debug.Log("[CinematicRecorder.SafetyMonitor] Restored original time values.");
            }
            else
            {
                // Fallback to safe defaults if we don't have stored values
                Time.fixedDeltaTime = 0.02f;
                Time.maximumDeltaTime = 0.3333333f;
                Time.captureFramerate = 0;

                if (Planetarium.fetch != null)
                {
                    Planetarium.fetch.fixedDeltaTime = 0.02;
                }

                UnityEngine.Debug.LogWarning("[CinematicRecorder.SafetyMonitor] Using fallback time defaults.");
            }

            if (DeterministicCaptureSession.IsRunning)
            {
                DeterministicCaptureSession.EndSession();
                UnityEngine.Debug.Log("[CinematicRecorder.SafetyMonitor] Recording session forcefully terminated.");
            }

            ScreenMessages.PostScreenMessage(
                            CinematicUIStrings.ScreenMessages.EmergencyResetSceneChange,
                            5f,
                            ScreenMessageStyle.UPPER_CENTER);
        }
        #endregion
        #region Public API
        /// <summary>
        /// Public API to check if safety monitor has stored time values.
        /// </summary>
        public bool HasStoredValues => hasStoredValues;
        #endregion
    }
}